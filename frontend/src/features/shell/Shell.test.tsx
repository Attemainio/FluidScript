// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import App from '../../App.tsx';
import { useDraftStore } from '../../state/draftStore.ts';
import { useUiStore } from '../../state/uiStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { hashOf } from '../../files/hash.ts';
import { templateText } from '../../files/template.ts';
import {
  FakeClient,
  FakeClock,
  answer,
  diagnostic,
  workspaceDocument,
  finish,
  settle,
} from '../../test/fakes.ts';
import { activeEditorView } from '../editor/activeView.ts';
import { forgetAll } from '../editor/documents.ts';
import { CompilePipeline } from '../pipeline/compilePipeline.ts';
import { statusText } from './statusText.ts';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('the shell', () => {
  let container: HTMLDivElement;
  let root: Root;
  let clock: FakeClock;
  let client: FakeClient;
  let pipeline: CompilePipeline;

  const type = (text: string): void => {
    const view = activeEditorView()!;
    act(() => {
      view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text } });
    });
  };

  const editorText = (): string => activeEditorView()?.state.doc.toString() ?? '';

  const tick = async (ms: number): Promise<void> => {
    await act(async () => {
      await clock.advance(ms);
    });
  };

  const status = (): string => container.querySelector('.status-line')!.textContent ?? '';

  beforeEach(() => {
    localStorage.clear();
    forgetAll();
    useDraftStore.setState({ drafts: {} });
    useUiStore.setState({ theme: { kind: 'system' }, splitRatio: 0.45, logOpen: true });
    const first = workspaceDocument('d1', 'plant_01', {
      savedHash: hashOf(templateText),
      dirty: false,
      status: 'clean',
    });
    useWorkspaceStore.setState({ documents: [first], activeDocumentId: 'd1' });
    clock = new FakeClock();
    client = new FakeClient();
    pipeline = new CompilePipeline(client, useDraftStore.getState(), {
      clock,
      debounceMs: 300,
      sessionId: (id) => `s-${id}`,
    });
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);
    act(() => root.render(<App pipeline={pipeline} />));
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('compiles the open document once after the idle gap and shows what came back', async () => {
    // Mounting compiles the document as it stands (revision 0); the edit restarts the debounce.
    type('fluidscript 1\ncircuit loop\n');
    await tick(299);
    expect(client.calls).toHaveLength(0);
    await tick(1);
    expect(client.calls).toHaveLength(1);
    expect(client.calls[0]?.request.script).toBe('fluidscript 1\ncircuit loop\n');
    expect(status()).toContain(statusText.converging.word);

    await act(async () => {
      finish(client.calls[0]!, answer('loop'));
      await settle();
    });

    expect(container.querySelector('.canvas-pane .scene')).not.toBeNull();
    expect(status()).toContain(
      `${statusText.converged.glyph} ${statusText.converged.word} · steady solve · plant_01`,
    );
    expect(useWorkspaceStore.getState().documents[0]?.dirty).toBe(true);
  });

  it('keeps the model and lists the diagnostics when the next compile fails', async () => {
    type('good');
    await tick(300);
    await act(async () => {
      finish(client.calls[0]!, answer('good'));
      await settle();
    });

    type('bad');
    await tick(300);
    await act(async () => {
      finish(client.calls[1]!, {
        model: null,
        diagnostics: [diagnostic('FS1801')],
        timings: { parseMs: 1, bindMs: 0, sizeMs: 0, solveMs: 0, totalMs: 1 },
      });
      await settle();
    });

    expect(container.querySelector('.canvas-pane .scene')).not.toBeNull();
    expect(container.querySelector('.log-pane')?.textContent).toContain('FS1801');
    expect(container.querySelector('.log-pane')?.textContent).toContain('1 error');
  });

  it('switching tabs cancels the outgoing compile and mounts the incoming text', async () => {
    type('one');
    await tick(300);
    expect(client.inFlight).toHaveLength(1);

    act(() => {
      useWorkspaceStore.getState().open('plant_02');
    });

    expect(client.calls[0]?.signal.aborted).toBe(true);
    expect(editorText()).toContain('fluidscript 1');
    expect(status()).toContain('plant_02');

    act(() => {
      useWorkspaceStore.getState().activate('d1');
    });
    expect(editorText()).toBe('one');
  });

  it('names every state with a distinct glyph and a distinct word, so colour is never the only cue', () => {
    // 51 invariant 8c, R-42: distinguishable in greyscale.
    const glyphs = Object.values(statusText).map((s) => s.glyph);
    const words = Object.values(statusText).map((s) => s.word);
    expect(new Set(glyphs).size).toBe(glyphs.length);
    expect(new Set(words).size).toBe(words.length);
    expect(status()).toContain(`${statusText.idle.glyph} ${statusText.idle.word}`);
  });

  it('keeps the split position across a reload', () => {
    // 51 acceptance: theme and split position survive a reload.
    act(() => useUiStore.getState().setSplitRatio(0.6));
    const saved = localStorage.getItem('fluidscript.ui')!;
    expect(saved).toContain('0.6');

    useUiStore.setState({ splitRatio: 0.45 });
    localStorage.setItem('fluidscript.ui', saved);
    act(() => {
      void useUiStore.persist.rehydrate();
    });

    expect(useUiStore.getState().splitRatio).toBe(0.6);
    expect(
      container.querySelector<HTMLDivElement>('.fs-split')?.style.gridTemplateColumns,
    ).toContain('0.6fr');
  });

  it('keeps the tabs, and only the tabs, across a reload', () => {
    act(() => {
      useWorkspaceStore.getState().open('substation');
    });
    const saved = localStorage.getItem('fluidscript.workspace')!;
    expect(saved).toContain('substation');
    expect(saved).not.toContain('fluidscript 1'); // ids and names only (51)
  });
});
