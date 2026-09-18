// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import App from '../../App.tsx';
import { FakeBackend, MemoryRecoveryStore } from '../../test/fileFakes.ts';
import { useFileStore } from '../../files/fileStore.ts';
import { hashOf } from '../../files/hash.ts';
import { useDraftStore } from '../../state/draftStore.ts';
import { useRunStore } from '../../state/runStore.ts';
import { useUiStore } from '../../state/uiStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { FakeClient, FakeClock, settle, workspaceDocument } from '../../test/fakes.ts';
import { activeEditorView } from '../editor/activeView.ts';
import { forgetAll } from '../editor/documents.ts';
import { CompilePipeline } from '../pipeline/compilePipeline.ts';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const versioned = 'fluidscript 1\n\ncircuit plant\n\nPU1 pump\n';

describe('the file commands in the shell (58)', () => {
  let container: HTMLDivElement;
  let root: Root;
  let backend: FakeBackend;
  let recovery: MemoryRecoveryStore;
  let clock: FakeClock;
  let client: FakeClient;

  const mount = (canOverwrite: boolean, existing?: MemoryRecoveryStore): void => {
    backend = new FakeBackend(canOverwrite);
    recovery = existing ?? new MemoryRecoveryStore();
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
    act(() =>
      root.render(<App pipeline={pipeline} backend={backend} recovery={recovery} clock={clock} />),
    );
  };

  const type = (text: string): void => {
    const view = activeEditorView()!;
    act(() => {
      view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text } });
    });
  };

  /** Lets the actions run: a validate they asked for is answered clean, then the promises settle. */
  const flush = async (): Promise<void> => {
    await act(async () => {
      await settle();
      for (const v of client.validations.splice(0)) {
        v.resolve({
          contractVersion: '1.0',
          languageMajor: 1,
          diagnostics: [],
          timings: { parseMs: 0, bindMs: 0, sizeMs: 0, solveMs: 0, totalMs: 0 },
        });
      }
      await settle();
      await settle();
    });
  };

  const click = (element: Element | null): void => {
    act(() => (element as HTMLButtonElement).click());
  };

  const menuItem = (label: string): Element | null => {
    const button = container.querySelector('.file-menu button')!;
    if (button.getAttribute('aria-expanded') !== 'true') {
      click(button);
    }
    return (
      [...container.querySelectorAll('[role="menuitem"]')].find((e) =>
        e.textContent?.startsWith(label),
      ) ?? null
    );
  };

  beforeEach(() => {
    localStorage.clear();
    forgetAll();
    useDraftStore.setState({ drafts: {} });
    useRunStore.setState({ runs: {} });
    useFileStore.setState({
      notices: {},
      dialog: null,
      storeUnavailable: false,
      canOverwrite: true,
    });
    useUiStore.setState({
      theme: { kind: 'system' },
      splitRatio: 0.45,
      logOpen: true,
      logFilter: 'warnings',
    });
    useWorkspaceStore.setState({
      documents: [workspaceDocument('d1', 'plant_01')],
      activeDocumentId: 'd1',
    });
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('offers Save where the browser can overwrite and Download .fluid where it cannot', () => {
    mount(true);
    expect(menuItem('Save')).not.toBeNull();
    expect(menuItem('Download .fluid')).not.toBeNull();
    act(() => root.unmount());
    container.remove();

    mount(false);
    expect(menuItem('Save')).toBeNull();
    expect(menuItem('Save As')).toBeNull();
    expect(menuItem('Download .fluid')?.textContent).toContain('Ctrl+S');
  });

  it('Save As from the menu names the file, clears the dot, and the tab takes the file name', async () => {
    mount(true);
    type(versioned);
    expect(container.querySelector('.fs-tab__dirty')).not.toBeNull();
    backend.saveAsName = 'plant.fluid';
    click(menuItem('Save As'));
    await flush();
    expect(backend.disk.get('plant.fluid')?.text).toBe(versioned);
    expect(container.querySelector('.fs-tab__dirty')).toBeNull();
    expect(container.querySelector('[role="tab"]')?.textContent).toBe('plant.fluid');
    expect(useWorkspaceStore.getState().documents[0]).toMatchObject({
      savedHash: hashOf(versioned),
      hasHandle: true,
    });
  });

  it('a failed save shows the notice with its actions, and the text stays', async () => {
    mount(true);
    type(versioned);
    backend.saveAsName = 'plant.fluid';
    click(menuItem('Save As'));
    await flush();
    type(`${versioned}PU2 pump\n`);
    backend.failWrite = new Error('disk full');
    click(menuItem('Save'));
    await flush();
    const notice = container.querySelector('.file-notice[data-kind="FILE002"]');
    expect(notice?.textContent).toContain('disk full');
    expect([...notice!.querySelectorAll('button')].map((b) => b.textContent)).toEqual([
      'Save As…',
      'Download .fluid',
      '×',
    ]);
    expect(activeEditorView()!.state.doc.toString()).toContain('PU2 pump');
    expect(container.querySelector('.fs-tab__dirty')).not.toBeNull();
  });

  it('an unversioned draft is offered its version line and the quick fix inserts it', async () => {
    mount(true);
    type('circuit plant\n\nPU1 pump\n');
    // The draft's diagnostics carry FS1701 as the compile would have delivered them.
    act(() =>
      useDraftStore.getState().applyValidate('d1', 99, [
        {
          code: 'FS1701',
          severity: 'info',
          message: 'no version',
          range: {
            start: { line: 0, character: 0 },
            end: { line: 0, character: 0 },
            offset: 0,
            length: 0,
          },
          component: null,
          suggestion: {
            title: "Add 'fluidscript 1'",
            range: {
              start: { line: 0, character: 0 },
              end: { line: 0, character: 0 },
              offset: 0,
              length: 0,
            },
            newText: 'fluidscript 1\n',
          },
          related: [],
        },
      ]),
    );
    click(menuItem('Save'));
    await flush();
    const notice = container.querySelector('.file-notice[data-kind="unversioned"]');
    expect(notice).not.toBeNull();
    click(
      [...notice!.querySelectorAll('button')].find((b) => b.textContent?.startsWith('Add')) ?? null,
    );
    expect(activeEditorView()!.state.doc.toString()).toBe(
      'fluidscript 1\ncircuit plant\n\nPU1 pump\n',
    );
    expect(container.querySelector('.file-notice[data-kind="unversioned"]')).toBeNull();
  });

  it('closing a dirty document asks in a dialog that traps focus; Escape cancels and Discard closes', async () => {
    mount(true);
    type(versioned);
    click(container.querySelector('[aria-label="Close document"]'));
    await flush();
    const dialog = container.querySelector('[role="dialog"]')!;
    expect(dialog.textContent).toContain('unsaved changes');
    expect(document.activeElement?.textContent).toBe('Save');
    act(() => {
      dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    });
    await flush();
    expect(container.querySelector('[role="dialog"]')).toBeNull();
    expect(useWorkspaceStore.getState().documents).toHaveLength(1);

    click(container.querySelector('[aria-label="Close document"]'));
    await flush();
    click(
      [...container.querySelectorAll('[role="dialog"] button')].find(
        (b) => b.textContent === 'Discard',
      ) ?? null,
    );
    await flush();
    expect(useWorkspaceStore.getState().documents[0]?.documentId).not.toBe('d1');
    expect(activeEditorView()!.state.doc.toString()).toBe('fluidscript 1\n\ncircuit plant\n\n');
  });

  it('a draft written to recovery comes back after a reload, dirty, into the same tab', async () => {
    mount(true);
    type(versioned);
    await act(async () => {
      await clock.advance(1000);
    });
    await flush();
    expect(recovery.entries.get('d1')?.text).toBe(versioned);

    // The reload: a new mount with the workspace remembering the tab and the editor empty.
    act(() => root.unmount());
    container.remove();
    forgetAll();
    useWorkspaceStore.setState({
      documents: useWorkspaceStore.getState().documents.map((d) => ({ ...d, loaded: false })),
    });
    mount(true, recovery);
    await flush();
    expect(activeEditorView()!.state.doc.toString()).toBe(versioned);
    expect(useWorkspaceStore.getState().documents[0]).toMatchObject({
      documentId: 'd1',
      loaded: true,
      dirty: true,
    });
    expect(container.querySelector('.fs-tab__dirty')).not.toBeNull();
  });
});
