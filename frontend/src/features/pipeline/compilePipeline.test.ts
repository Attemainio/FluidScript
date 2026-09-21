import { beforeEach, describe, expect, it } from 'vitest';

import { ApiError } from '../../api/client.ts';
import type { ModelContract } from '../../api/types.ts';
import { draftOf, useDraftStore } from '../../state/draftStore.ts';
import {
  FakeClient,
  FakeClock,
  answer,
  diagnostic,
  finish,
  model,
  settle,
} from '../../test/fakes.ts';
import { CompilePipeline } from './compilePipeline.ts';
import { LatencyTracker } from './latency.ts';

function circuitName(documentId: string): string | undefined {
  return (
    draftOf(useDraftStore.getState(), documentId).model?.circuits[0] as
      { name?: string } | undefined
  )?.name;
}

describe('the debounce pipeline', () => {
  let clock: FakeClock;
  let client: FakeClient;
  let pipeline: CompilePipeline;
  const doc = 'doc-1';

  beforeEach(() => {
    useDraftStore.setState({ drafts: {} });
    clock = new FakeClock();
    client = new FakeClient();
    pipeline = new CompilePipeline(client, useDraftStore.getState(), {
      clock,
      debounceMs: 300,
      sessionId: (id) => `s-${id}`,
    });
  });

  it('sends nothing while typing continues, and one compile after the idle gap', async () => {
    // 51 acceptance: typing continuously for 10 s produces at most one in-flight request at any moment.
    let revision = 0;
    for (let t = 0; t < 10_000; t += 150) {
      revision++;
      pipeline.edit(doc, `script ${revision}`, revision);
      await clock.advance(150);
      expect(client.inFlight.length).toBeLessThanOrEqual(1);
    }
    expect(client.calls).toHaveLength(0);

    await clock.advance(300);

    expect(client.calls).toHaveLength(1);
    expect(client.calls[0]?.request).toEqual({
      sessionId: `s-${doc}`,
      script: `script ${revision}`,
    });
  });

  it('keeps at most one request in flight by aborting the older one', async () => {
    pipeline.edit(doc, 'a', 1);
    await clock.advance(300);
    pipeline.edit(doc, 'b', 2);
    await clock.advance(300);

    expect(client.calls).toHaveLength(2);
    expect(client.calls[0]?.signal.aborted).toBe(true);
    expect(client.inFlight).toHaveLength(1);
  });

  it('never lets a delayed response overwrite a newer model', async () => {
    // 51 acceptance: an artificially delayed response never overwrites a newer model. The first
    // request is aborted client-side when the second fires; even if its body arrived, it is ignored.
    pipeline.edit(doc, 'a', 1);
    await clock.advance(300);
    const first = client.calls[0]!;
    pipeline.edit(doc, 'b', 2);
    await clock.advance(300);
    const second = client.calls[1]!;

    finish(second, answer('b'));
    await settle();
    expect(circuitName(doc)).toBe('b');

    finish(first, answer('a'));
    await settle();
    expect(circuitName(doc)).toBe('b');
  });

  it('drops an older revision even when its response arrives without an abort', () => {
    // The store's own guard (invariant 4), independent of the abort.
    const store = useDraftStore.getState();
    store.applyCompile(doc, 2, answer('newer'));
    store.applyCompile(doc, 1, answer('older'));

    expect(circuitName(doc)).toBe('newer');
  });

  it('keeps the previous model when a compile returns errors and no model', async () => {
    // 51 invariant 2 / acceptance: a syntax error leaves the canvas showing the previous model.
    pipeline.edit(doc, 'good', 1);
    await clock.advance(300);
    finish(client.calls[0]!, answer('good'));
    await settle();

    pipeline.edit(doc, 'fluidscript 99', 2);
    await clock.advance(300);
    finish(client.calls[1]!, {
      model: null,
      diagnostics: [diagnostic('FS1801')],
      timings: { parseMs: 1, bindMs: 0, sizeMs: 0, solveMs: 0, totalMs: 1 },
    });
    await settle();

    const draft = draftOf(useDraftStore.getState(), doc);
    expect(circuitName(doc)).toBe('good');
    expect(draft.diagnostics.map((d) => d.code)).toEqual(['FS1801']);
    expect(draft.diagnosticsRevision).toBe(2);
    expect(draft.modelRevision).toBe(1);
  });

  it('keeps the last model on a 500 and records the correlation id', async () => {
    pipeline.edit(doc, 'good', 1);
    await clock.advance(300);
    finish(client.calls[0]!, answer('good'));
    await settle();

    pipeline.edit(doc, 'boom', 2);
    await clock.advance(300);
    client.calls[1]!.reject(
      new ApiError(500, { status: 500, code: 'FS9001', correlationId: 'abc' }),
    );
    await settle();

    const draft = draftOf(useDraftStore.getState(), doc);
    expect(circuitName(doc)).toBe('good');
    expect(draft.fault).toEqual({ status: 500, correlationId: 'abc' });
    expect(draft.compiling).toBeNull();
  });

  it('cancels the outgoing document on a tab switch and starts nothing for it afterwards', async () => {
    // 51 invariant 8b.
    pipeline.edit(doc, 'a', 1);
    await clock.advance(300);
    expect(client.inFlight).toHaveLength(1);

    pipeline.cancel(doc);
    await clock.advance(1000);

    expect(client.calls[0]?.signal.aborted).toBe(true);
    expect(client.calls).toHaveLength(1);
    expect(draftOf(useDraftStore.getState(), doc).compiling).toBeNull();
  });

  it('answers a superseded request (499) silently', async () => {
    pipeline.edit(doc, 'a', 1);
    await clock.advance(300);
    client.calls[0]!.reject(new ApiError(499, { status: 499 }));
    await settle();

    expect(draftOf(useDraftStore.getState(), doc).fault).toBeNull();
  });
});

describe('the validate phase', () => {
  let clock: FakeClock;
  let client: FakeClient;
  const doc = 'doc-1';

  beforeEach(() => {
    useDraftStore.setState({ drafts: {} });
    clock = new FakeClock();
    client = new FakeClient();
  });

  it('switches on above a 100 ms p95 and off after fifty quick compiles', () => {
    // 51 acceptance: a latency fixture above 100 ms p95 enables the phase; 50 requests below 75 ms disable it.
    const tracker = new LatencyTracker();
    for (let i = 0; i < 20; i++) {
      tracker.record(i < 18 ? 40 : 140);
    }
    expect(tracker.validatePhase).toBe(true);

    for (let i = 0; i < 49; i++) {
      tracker.record(50);
    }
    expect(tracker.validatePhase).toBe(true);
    tracker.record(50);
    expect(tracker.validatePhase).toBe(false);
  });

  it('a slow compile does not switch it on by itself, and one quick compile does not switch it off', () => {
    const tracker = new LatencyTracker();
    tracker.record(500);
    expect(tracker.validatePhase).toBe(true); // one sample: the p95 is that sample
    tracker.record(10);
    expect(tracker.validatePhase).toBe(true);
  });

  it('fires validate at 100 ms when on, and its diagnostics never replace newer ones', async () => {
    const tracker = new LatencyTracker();
    for (let i = 0; i < 20; i++) {
      tracker.record(200);
    }
    const pipeline = new CompilePipeline(client, useDraftStore.getState(), {
      clock,
      debounceMs: 300,
      latency: tracker,
      sessionId: (id) => id,
    });

    pipeline.edit(doc, 'a', 1);
    await clock.advance(100);
    expect(client.validations).toHaveLength(1);
    expect(client.calls).toHaveLength(0);

    // The compile for revision 1 lands first; then the slow validate for the same revision arrives.
    await clock.advance(200);
    finish(client.calls[0]!, {
      model: { ...model('a'), diagnostics: [diagnostic('FS1507')] } as ModelContract,
      timings: answer('a').timings,
    });
    await settle();
    client.validations[0]!.resolve({
      contractVersion: '2.1',
      languageMajor: 1,
      diagnostics: [diagnostic('FS1302')],
      timings: answer('a').timings,
    });
    await settle();

    expect(draftOf(useDraftStore.getState(), doc).diagnostics.map((d) => d.code)).toEqual([
      'FS1507',
    ]);
  });

  it('shows validate diagnostics until the compile for that revision arrives', async () => {
    const tracker = new LatencyTracker();
    tracker.record(500);
    const pipeline = new CompilePipeline(client, useDraftStore.getState(), {
      clock,
      debounceMs: 300,
      latency: tracker,
      sessionId: (id) => id,
    });

    pipeline.edit(doc, 'a', 1);
    await clock.advance(100);
    client.validations[0]!.resolve({
      contractVersion: '2.1',
      languageMajor: 1,
      diagnostics: [diagnostic('FS1302')],
      timings: answer('a').timings,
    });
    await settle();

    expect(draftOf(useDraftStore.getState(), doc).diagnostics.map((d) => d.code)).toEqual([
      'FS1302',
    ]);
  });
});
