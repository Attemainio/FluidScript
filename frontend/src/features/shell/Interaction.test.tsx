// @vitest-environment jsdom
import { readFileSync } from 'node:fs';

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import App from '../../App.tsx';
import type { CompileResponse, Diagnostic } from '../../api/types.ts';
import { useDraftStore } from '../../state/draftStore.ts';
import { useSelectionStore } from '../../state/selectionStore.ts';
import { useUiStore } from '../../state/uiStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { FakeClient, FakeClock, finish, settle } from '../../test/fakes.ts';
import { samplesDir, solvedGoldens } from '../../test/goldens.ts';
import { activeEditorView } from '../editor/activeView.ts';
import { forgetAll } from '../editor/documents.ts';
import { CompilePipeline } from '../pipeline/compilePipeline.ts';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const loop = solvedGoldens().find((g) => g.name === 'm2-cooling-loop')!.model;
const loopScript = readFileSync(`${samplesDir}m2-cooling-loop.fluid`, 'utf8');

function d(code: string, severity: string, component: string | null, message: string): Diagnostic {
  return { code, severity, message, range: null, component, suggestion: null, related: [] };
}

function answer(diagnostics: Diagnostic[]): CompileResponse {
  return {
    model: { ...loop, diagnostics },
    diagnostics,
    timings: { parseMs: 1, bindMs: 1, sizeMs: 1, solveMs: 10, totalMs: 14 },
  } as CompileResponse;
}

describe('hover, selection and the log (P5.8)', () => {
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

  const tick = async (ms: number): Promise<void> => {
    await act(async () => {
      await clock.advance(ms);
    });
  };

  const compileWith = async (diagnostics: Diagnostic[]): Promise<void> => {
    await tick(300);
    await act(async () => {
      finish(client.calls[client.calls.length - 1]!, answer(diagnostics));
      await settle();
    });
  };

  beforeEach(() => {
    localStorage.clear();
    forgetAll();
    useDraftStore.setState({ drafts: {} });
    useSelectionStore.setState({ selected: {}, origin: {} });
    useUiStore.setState({
      theme: { kind: 'system' },
      splitRatio: 0.45,
      logOpen: true,
      logFilter: 'warnings',
    });
    useWorkspaceStore.setState({
      documents: [{ documentId: 'd1', displayName: 'plant_01', dirty: false }],
      activeDocumentId: 'd1',
    });
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
    type(loopScript);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
    vi.useRealTimers();
  });

  const logLines = (): string[] =>
    [...container.querySelectorAll('.log-pane__entry')].map((e) => e.textContent?.trim() ?? '');

  it('keeps an unchanged entry in place across compiles and shows the success line (56)', async () => {
    const warning = d('FS2107', 'warning', 'N1', "'N1' is a dead end.");
    await compileWith([warning, d('FS1510', 'info', null, 'Added node.')]);
    const before = container.querySelector('[data-key="FS2107:N1:"]');
    expect(before).not.toBeNull();
    expect(logLines().some((l) => /Solved · \d+ iterations · 14 ms/.test(l))).toBe(true);
    // The info is hidden by the default filter and counted.
    expect(container.querySelector('.log-pane__hidden')?.textContent).toBe('1 info');

    for (let i = 0; i < 3; i++) {
      type(`${loopScript}\n# edit ${i}`);
      await compileWith([warning, d('FS1510', 'info', null, 'Added node.')]);
    }
    const after = container.querySelector('[data-key="FS2107:N1:"]');
    expect(after).toBe(before);
  });

  it('folds three of one code into one line that opens to its members', async () => {
    await compileWith([
      d('FS4001', 'warning', 'N1', 'Approaching freezing point: 2.1 °C.'),
      d('FS4001', 'warning', 'N2', 'Approaching freezing point: 2.4 °C.'),
      d('FS4001', 'warning', 'N3', 'Approaching freezing point: 2.0 °C.'),
    ]);
    const group = container.querySelector('[data-key="group:warning:FS4001"]')!;
    expect(group.textContent).toContain('3 components');
    expect(group.querySelector('.log-pane__members')).toBeNull();
    act(() => (group.querySelector('button') as HTMLButtonElement).click());
    expect(group.querySelectorAll('.log-pane__members li')).toHaveLength(3);
  });

  it('selects on canvas and highlights the declaration from a log click (54, 56)', async () => {
    await compileWith([d('FS1507', 'warning', 'PU1', "'PU1' is not connected to anything.")]);
    const link = container.querySelector('[data-key="FS1507:PU1:"] button') as HTMLButtonElement;
    act(() => link.click());
    expect(useSelectionStore.getState().selected.d1).toEqual(['PU1']);
    expect(container.querySelector('.scene__symbol--selected')?.getAttribute('data-id')).toBe(
      'PU1',
    );
    expect(container.querySelector('.cm-selected-declaration')?.textContent).toContain('PU1 pump');
  });

  it('selects from the canvas with click and Shift+click, and clears with Escape', async () => {
    await compileWith([]);
    const symbol = (id: string): SVGElement =>
      container.querySelector(`.scene__symbol[data-id="${id}"]`)!;
    act(() => symbol('HE1').dispatchEvent(new MouseEvent('click', { bubbles: true })));
    act(() =>
      symbol('PU1').dispatchEvent(new MouseEvent('click', { bubbles: true, shiftKey: true })),
    );
    expect(useSelectionStore.getState().selected.d1).toEqual(['HE1', 'PU1']);
    expect(container.querySelectorAll('.scene__symbol--selected')).toHaveLength(2);
    const pane = container.querySelector('.canvas-pane') as HTMLElement;
    act(() => pane.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })));
    expect(useSelectionStore.getState().selected.d1).toEqual([]);
  });

  it('selects on canvas when the caret lands on a declaration (54: bidirectional)', async () => {
    await compileWith([]);
    const view = activeEditorView()!;
    const at = view.state.doc.toString().indexOf('3WV three_way_valve');
    act(() => view.dispatch({ selection: { anchor: at + 2 } }));
    expect(useSelectionStore.getState().selected.d1).toEqual(['3WV']);
    expect(useSelectionStore.getState().origin.d1).toBe('editor');
  });

  it('shows the hover card after 150 ms with stated, sized and default, and no request (54)', async () => {
    await compileWith([]);
    vi.useFakeTimers();
    const calls = client.calls.length;
    const svg = container.querySelector('.canvas-pane__svg')!;
    const symbol = container.querySelector('.scene__symbol[data-id="3WV"]')!;
    act(() => {
      symbol.dispatchEvent(
        new PointerEvent('pointerover', { bubbles: true, clientX: 10, clientY: 10 }),
      );
    });
    expect(container.querySelector('.hover-card')).toBeNull();
    act(() => {
      vi.advanceTimersByTime(150);
    });
    const card = container.querySelector('.hover-card')!;
    expect(card.textContent).toContain('100TV01');
    expect(
      card.querySelectorAll('.hover-card__source--sized, .hover-card__source--default').length,
    ).toBeGreaterThan(0);
    expect(client.calls.length).toBe(calls);
    act(() => {
      // React's onPointerLeave is derived from pointerout leaving the element.
      svg.dispatchEvent(
        new PointerEvent('pointerout', { bubbles: true, relatedTarget: document.body }),
      );
    });
    expect(container.querySelector('.hover-card')).toBeNull();
  });
});
