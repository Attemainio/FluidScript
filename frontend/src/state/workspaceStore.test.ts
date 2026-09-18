// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest';

import { hashOf } from '../files/hash.ts';
import { templateText } from '../files/template.ts';
import { useWorkspaceStore } from './workspaceStore.ts';

beforeEach(() => {
  localStorage.clear();
  useWorkspaceStore.setState({ documents: [], activeDocumentId: '' });
  useWorkspaceStore.getState().open('plant_01');
});

describe('the workspace (58, D-39)', () => {
  it('derives dirty from the hashes: an edit dirties, a save cleans, an undo back to the saved text cleans again', () => {
    const store = useWorkspaceStore.getState();
    const id = store.activeDocumentId;
    store.markSaved(id, hashOf(templateText), 5, 'plant.fluid');
    expect(useWorkspaceStore.getState().documents[0]).toMatchObject({
      dirty: false,
      status: 'clean',
      displayName: 'plant.fluid',
      hasHandle: true,
    });
    store.setText(id, hashOf('edited'));
    expect(useWorkspaceStore.getState().documents[0]).toMatchObject({
      dirty: true,
      status: 'dirty',
    });
    store.setText(id, hashOf(templateText));
    expect(useWorkspaceStore.getState().documents[0]).toMatchObject({
      dirty: false,
      status: 'clean',
    });
  });

  it("an action's status stands until the hashes change it: conflict is not undone by typing", () => {
    const store = useWorkspaceStore.getState();
    const id = store.activeDocumentId;
    store.setStatus(id, 'conflict');
    store.setText(id, hashOf('more'));
    expect(useWorkspaceStore.getState().documents[0]).toMatchObject({
      status: 'conflict',
      dirty: true,
    });
    store.markSaved(id, hashOf('more'), 6);
    expect(useWorkspaceStore.getState().documents[0]).toMatchObject({
      status: 'clean',
      dirty: false,
    });
  });

  it('switching tabs changes no document field (invariant 7), compared field by field across a switch and back', () => {
    const store = useWorkspaceStore.getState();
    const a = store.activeDocumentId;
    const b = store.open('plant_02')!;
    store.setText(a, hashOf('a'));
    store.markSaved(b, hashOf('b'), 7, 'b.fluid');
    store.setRecoveryStatus(a, 'written');
    const before = useWorkspaceStore.getState().documents;
    store.activate(a);
    store.activate(b);
    store.activate(a);
    const after = useWorkspaceStore.getState().documents;
    expect(after).toHaveLength(before.length);
    for (let i = 0; i < before.length; i++) {
      for (const key of Object.keys(before[i]!) as (keyof (typeof before)[number])[]) {
        expect(after[i]![key]).toEqual(before[i]![key]);
      }
    }
    expect(useWorkspaceStore.getState().activeDocumentId).toBe(a);
  });

  it("a reload keeps every document and its file state but marks none loaded (the text is the recovery store's)", async () => {
    const store = useWorkspaceStore.getState();
    const id = store.activeDocumentId;
    store.markSaved(id, hashOf('x'), 9, 'x.fluid');
    const stored = localStorage.getItem('fluidscript.workspace')!;
    expect(stored).not.toContain('"loaded"');
    // Clearing the store persists the empty workspace; put the stored one back, as a new session finds it.
    useWorkspaceStore.setState({ documents: [], activeDocumentId: '' });
    localStorage.setItem('fluidscript.workspace', stored);
    await useWorkspaceStore.persist.rehydrate();
    const again = useWorkspaceStore.getState().documents[0]!;
    expect(again).toMatchObject({
      documentId: id,
      displayName: 'x.fluid',
      savedHash: hashOf('x'),
      fileModified: 9,
      hasHandle: true,
      loaded: false,
    });
  });
});
