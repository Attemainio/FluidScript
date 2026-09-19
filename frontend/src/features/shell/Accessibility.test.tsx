// @vitest-environment jsdom
import { readFileSync } from 'node:fs';

import axe from 'axe-core';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import App from '../../App.tsx';
import type { CompileResponse } from '../../api/types.ts';
import { useDraftStore } from '../../state/draftStore.ts';
import { useSelectionStore } from '../../state/selectionStore.ts';
import { useUiStore } from '../../state/uiStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { FakeClient, FakeClock, finish, settle, workspaceDocument } from '../../test/fakes.ts';
import { samplesDir, solvedGoldens } from '../../test/goldens.ts';
import { activeEditorView } from '../editor/activeView.ts';
import { forgetAll } from '../editor/documents.ts';
import { CompilePipeline } from '../pipeline/compilePipeline.ts';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const loop = solvedGoldens().find((g) => g.name === 'm2-cooling-loop')!.model;
const loopScript = readFileSync(`${samplesDir}m2-cooling-loop.fluid`, 'utf8');

/**
 * The accessibility pass (`07`, `53`, `62`, P5.11). What runs here is what jsdom can judge: axe's
 * structural rules over the mounted app with a solved plant, the structured table, the keyboard
 * controls and the export dialog. Colour contrast is judged by `themes.test.ts` from the theme
 * files, since jsdom lays nothing out; 200 % zoom and a screen reader's reading order need a
 * browser and are recorded as open in `50-frontend/defects.md`.
 */
describe('accessibility', () => {
  let container: HTMLDivElement;
  let root: Root;
  let clock: FakeClock;
  let client: FakeClient;

  const tick = async (ms: number): Promise<void> => {
    await act(async () => {
      await clock.advance(ms);
    });
  };

  beforeEach(async () => {
    localStorage.clear();
    forgetAll();
    useDraftStore.setState({ drafts: {} });
    useSelectionStore.setState({ selected: {}, origin: {} });
    useUiStore.setState({ theme: { kind: 'system' }, splitRatio: 0.45, logOpen: true });
    useWorkspaceStore.setState({
      documents: [workspaceDocument('d1', 'plant_01')],
      activeDocumentId: 'd1',
    });
    clock = new FakeClock();
    client = new FakeClient();
    const pipeline = new CompilePipeline(client, useDraftStore.getState(), {
      clock,
      debounceMs: 300,
      sessionId: (id) => `s-${id}`,
    });
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);
    act(() => root.render(<App pipeline={pipeline} />));
    const view = activeEditorView()!;
    act(() => {
      view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: loopScript } });
    });
    await tick(300);
    await act(async () => {
      finish(client.calls[client.calls.length - 1]!, {
        model: loop,
        diagnostics: [],
        timings: { parseMs: 1, bindMs: 1, sizeMs: 1, solveMs: 10, totalMs: 14 },
      } as CompileResponse);
      await settle();
    });
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('passes axe over the whole app with a solved plant on the canvas (07: WCAG 2.2 AA automated checks)', async () => {
    const results = await axe.run(container, {
      // Contrast needs layout and computed colours jsdom does not have; 55's theme test covers it
      // from the token values. Region landmarks are the shell's, not each pane's.
      rules: { 'color-contrast': { enabled: false }, region: { enabled: false } },
    });
    const violations = results.violations.map(
      (v) => `${v.id}: ${v.help} — ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`,
    );
    expect(violations).toEqual([]);
  });

  it('exposes the diagram as a table with the same state, provenance and diagnostics as the hover card (53, 62)', () => {
    const table = container.querySelector('.scene-table')!;
    expect(table).not.toBeNull();
    expect(table.querySelector('summary')!.textContent).toBe('Diagram as a table');
    const he1 = table.querySelector('tr[data-id="HE1"]')!;
    expect(he1.textContent).toContain('100HE01');
    expect(he1.textContent).toContain('heat_exchanger');
    expect(he1.textContent).toContain('power 30 kW (stated)');
    expect(he1.textContent).toContain('dp 20 kPa (default)');
    expect(he1.textContent).toContain('out 50.01 °C');
    // Rows come in drawing order: the first symbol first.
    const first = table.querySelector('tbody tr')!.getAttribute('data-id');
    expect(first).toBe(container.querySelector('.scene__symbol')!.getAttribute('data-id'));
    // Every connection has a row with its flow.
    expect(table.querySelectorAll('tr[data-id^="c"]').length).toBe(loop.connections.length);
    expect(table.querySelector('tr[data-id="c1"]')!.textContent).toContain('flow 0.2392 kg/s');
  });

  it('gives the keyboard every control: Tab to a symbol shows its card, Enter selects, arrows pan, +/- zoom (53, R-42)', () => {
    const pane = container.querySelector('.canvas-pane') as HTMLElement;
    const svg = container.querySelector('.canvas-pane__svg')!;
    const symbol = container.querySelector('.scene__symbol[data-id="HE1"]') as SVGGElement;
    expect(symbol.getAttribute('tabindex')).toBe('0');

    act(() => {
      symbol.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    });
    expect(container.querySelector('.hover-card')?.textContent).toContain('100HE01');

    act(() => {
      symbol.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    });
    expect(useSelectionStore.getState().selected.d1).toEqual(['HE1']);

    const zoomOf = (): number =>
      Number(container.querySelector('.canvas-pane__zoom')!.textContent!.replace('%', ''));
    const before = zoomOf();
    act(() => {
      pane.dispatchEvent(new KeyboardEvent('keydown', { key: '+', bubbles: true }));
    });
    expect(zoomOf()).toBeGreaterThan(before);
    act(() => {
      pane.dispatchEvent(new KeyboardEvent('keydown', { key: '-', bubbles: true }));
    });
    expect(zoomOf()).toBe(before);

    const transformOf = (): string =>
      svg.querySelector(':scope > g')!.getAttribute('transform') ?? '';
    const start = transformOf();
    act(() => {
      pane.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    });
    expect(transformOf()).not.toBe(start);

    act(() => {
      svg.dispatchEvent(new FocusEvent('focusout', { bubbles: true }));
    });
    expect(container.querySelector('.hover-card')).toBeNull();
  });

  it('opens the export dialog from the toolbar and Ctrl+E, with a labelled form, and closes on Escape (59)', () => {
    const button = [...container.querySelectorAll('button')].find(
      (b) => b.textContent === 'Export',
    ) as HTMLButtonElement;
    expect(button.disabled).toBe(false);
    act(() => button.click());
    const dialog = container.querySelector('[role="dialog"]')!;
    expect(dialog.textContent).toContain('Export diagram');
    for (const input of dialog.querySelectorAll('input, select')) {
      const id = input.getAttribute('id');
      const labelled =
        input.closest('label') !== null ||
        (id !== null && dialog.querySelector(`label[for="${id}"]`) !== null);
      expect(labelled, `${input.outerHTML} has no label`).toBe(true);
    }
    act(() => {
      dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    });
    expect(container.querySelector('[role="dialog"]')).toBeNull();

    act(() => {
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'e', ctrlKey: true }));
    });
    expect(container.querySelector('[role="dialog"]')).not.toBeNull();
  });
});
