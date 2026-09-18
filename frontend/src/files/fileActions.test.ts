// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest';

import { useRunStore } from '../state/runStore.ts';
import { useWorkspaceStore, type WorkspaceDocument } from '../state/workspaceStore.ts';
import { FakeClock } from '../test/fakes.ts';
import { FileActions, staleAfterMs } from './fileActions.ts';
import { useFileStore } from './fileStore.ts';
import {
  FakeBackend,
  FakeText,
  MemoryRecoveryStore,
  unsupportedMajor,
  unversioned,
  validateClient,
} from '../test/fileFakes.ts';
import { hashOf } from './hash.ts';
import { RecoveryScheduler } from './scheduler.ts';
import { templateText } from './template.ts';

const versioned = 'fluidscript 1\n\ncircuit plant\n\nPU1 pump\n';

function docs(): readonly WorkspaceDocument[] {
  return useWorkspaceStore.getState().documents;
}

function doc(id: string): WorkspaceDocument {
  return docs().find((d) => d.documentId === id)!;
}

function active(): WorkspaceDocument {
  return doc(useWorkspaceStore.getState().activeDocumentId);
}

/** Answers the next dialog the store shows with `choice`. */
function answerNext(choice: string): void {
  const unsubscribe = useFileStore.subscribe((state) => {
    if (state.dialog !== null) {
      unsubscribe();
      state.dialog.choose(choice);
    }
  });
}

function setup(canOverwrite: boolean): {
  actions: FileActions;
  backend: FakeBackend;
  recovery: MemoryRecoveryStore;
  text: FakeText;
  clock: FakeClock;
  scheduler: RecoveryScheduler;
} {
  const backend = new FakeBackend(canOverwrite);
  const recovery = new MemoryRecoveryStore();
  const text = new FakeText();
  const clock = new FakeClock();
  let actions: FileActions | null = null;
  const scheduler = new RecoveryScheduler(clock, (id) => void actions!.writeRecovery(id));
  actions = new FileActions({
    backend,
    recovery,
    scheduler,
    client: validateClient((script) =>
      script.startsWith('fluidscript 7')
        ? [unsupportedMajor()]
        : script.startsWith('fluidscript')
          ? []
          : [unversioned()],
    ),
    text,
    workspace: useWorkspaceStore,
    files: useFileStore,
    runs: useRunStore,
    now: () => clock.now(),
  });
  useFileStore.getState().setCanOverwrite(canOverwrite);
  return { actions, backend, recovery, text, clock, scheduler };
}

/** Opens `name` from the fake disk and returns the new document's id. */
async function open(s: ReturnType<typeof setup>, name: string): Promise<string> {
  s.backend.pick = [name];
  await s.actions.openFiles();
  return useWorkspaceStore.getState().activeDocumentId;
}

beforeEach(() => {
  localStorage.clear();
  useWorkspaceStore.setState({ documents: [], activeDocumentId: '' });
  useWorkspaceStore.getState().open('plant_01');
  useFileStore.setState({ notices: {}, dialog: null, storeUnavailable: false, canOverwrite: true });
  useRunStore.setState({ runs: {} });
});

describe.each([
  ['File System Access', true],
  ['upload and download', false],
])('the file lifecycle over %s (58)', (_, canOverwrite) => {
  it('New is template text, no handle, dirty', () => {
    const s = setup(canOverwrite);
    const id = s.actions.newDocument()!;
    expect(s.text.textOf(id)).toBe(templateText);
    expect(doc(id)).toMatchObject({
      dirty: true,
      status: 'dirty',
      hasHandle: false,
      savedHash: null,
    });
  });

  it('Open reads the bytes, marks the document clean at their hash, and keeps the handle where there is one', async () => {
    const s = setup(canOverwrite);
    s.backend.put('loop.fluid', versioned);
    const id = await open(s, 'loop.fluid');
    expect(s.text.textOf(id)).toBe(versioned);
    expect(doc(id)).toMatchObject({
      displayName: 'loop.fluid',
      dirty: false,
      status: 'clean',
      savedHash: hashOf(versioned),
      hasHandle: canOverwrite,
    });
    expect(s.recovery.handles.has(id)).toBe(canOverwrite);
  });

  it('a cancelled picker changes nothing (FILE006)', async () => {
    const s = setup(canOverwrite);
    const before = docs();
    s.backend.pick = [];
    await s.actions.openFiles();
    expect(docs()).toEqual(before);
    expect(useFileStore.getState().notices).toEqual({});
  });

  it('an unsupported file opens read-only with its bytes preserved (FILE005)', async () => {
    const s = setup(canOverwrite);
    const bytes = 'fluidscript 7\n\ncircuit future\n';
    s.backend.put('future.fluid', bytes);
    const id = await open(s, 'future.fluid');
    expect(doc(id).readOnly).toBe(true);
    expect(s.text.readOnly.get(id)).toBe(true);
    expect(useFileStore.getState().notices[id]?.kind).toBe('FILE005');
    await s.actions.save(id);
    expect(s.backend.disk.get('future.fluid')!.text).toBe(bytes);
    expect(doc(id).status).toBe('clean');
  });

  it('an unversioned draft cannot become a file; the notice offers the version line and applying it lets Save through', async () => {
    const s = setup(canOverwrite);
    const id = active().documentId;
    s.text.loadText(id, 'circuit plant\n\nPU1 pump\n', false);
    s.text.diagnostics.set(id, [unversioned()]);
    useWorkspaceStore.getState().setText(id, hashOf(s.text.textOf(id)));
    s.backend.saveAsName = 'plant.fluid';
    await s.actions.save(id);
    expect(useFileStore.getState().notices[id]?.kind).toBe('unversioned');
    expect(s.backend.disk.has('plant.fluid')).toBe(false);
    expect(s.backend.downloads).toEqual([]);

    s.actions.addVersionLine(id);
    expect(s.text.textOf(id)).toBe('fluidscript 1\ncircuit plant\n\nPU1 pump\n');
    s.text.diagnostics.set(id, []);
    await s.actions.save(id);
    if (canOverwrite) {
      expect(s.backend.disk.get('plant.fluid')!.text).toBe(s.text.textOf(id));
      expect(doc(id)).toMatchObject({ status: 'clean', dirty: false, displayName: 'plant.fluid' });
    } else {
      expect(s.backend.downloads).toEqual([{ name: 'plant_01.fluid', text: s.text.textOf(id) }]);
      expect(doc(id).dirty).toBe(true);
    }
  });

  it('stale diagnostics are refreshed before Save decides', async () => {
    const s = setup(canOverwrite);
    const id = active().documentId;
    s.text.loadText(id, versioned, false);
    s.text.diagnostics.set(id, [unversioned()]);
    s.text.stale = true;
    s.backend.saveAsName = 'plant.fluid';
    await s.actions.save(id);
    expect(useFileStore.getState().notices[id]?.kind).not.toBe('unversioned');
  });

  it('Download is never described as saving: the document stays dirty', async () => {
    const s = setup(canOverwrite);
    const id = active().documentId;
    s.text.loadText(id, versioned, false);
    useWorkspaceStore.getState().setText(id, hashOf(versioned));
    s.actions.download(id);
    expect(s.backend.downloads).toEqual([{ name: 'plant_01.fluid', text: versioned }]);
    expect(doc(id).dirty).toBe(true);
    expect(doc(id).savedHash).toBeNull();
  });

  it('recovery is written after the idle gap, per document, and never into the file', async () => {
    const s = setup(canOverwrite);
    const a = active().documentId;
    const b = useWorkspaceStore.getState().open('plant_02')!;
    for (const id of [a, b]) {
      s.text.loadText(id, `${versioned}# ${id}\n`, false);
      useWorkspaceStore.getState().setText(id, hashOf(s.text.textOf(id)));
      s.scheduler.noteEdit(id);
    }
    await s.clock.advance(999);
    expect(s.recovery.entries.size).toBe(0);
    await s.clock.advance(1);
    await Promise.resolve();
    expect([...s.recovery.entries.keys()].sort()).toEqual([a, b].sort());
    expect(s.recovery.entries.get(a)!.text).toContain(`# ${a}`);
    expect(doc(a).recoveryStatus).toBe('written');
    expect(doc(a).savedHash).toBeNull();
    expect(s.backend.disk.size).toBe(0);
  });

  it('a broken recovery store is reported once and editing continues (FILE004)', async () => {
    const s = setup(canOverwrite);
    const id = active().documentId;
    s.recovery.broken = true;
    s.text.loadText(id, versioned, false);
    useWorkspaceStore.getState().setText(id, hashOf(versioned));
    await s.actions.writeRecovery(id);
    expect(doc(id).recoveryStatus).toBe('failed');
    expect(useFileStore.getState().storeUnavailable).toBe(true);
    expect(s.text.textOf(id)).toBe(versioned);
  });

  it('a ninth document is refused with a message naming the limit and no tab is closed (FILE007)', async () => {
    const s = setup(canOverwrite);
    for (let n = 2; n <= 8; n++) {
      expect(s.actions.newDocument()).not.toBeNull();
    }
    expect(docs()).toHaveLength(8);
    answerNext('ok');
    expect(s.actions.newDocument()).toBeNull();
    expect(docs()).toHaveLength(8);
    s.backend.put('ninth.fluid', versioned);
    s.backend.pick = ['ninth.fluid'];
    answerNext('ok');
    await s.actions.openFiles();
    expect(docs()).toHaveLength(8);
  });

  it('closing a dirty document asks; Cancel keeps it, Discard closes it and drops its recovery entry', async () => {
    const s = setup(canOverwrite);
    const id = active().documentId;
    s.text.loadText(id, versioned, false);
    useWorkspaceStore.getState().setText(id, hashOf(versioned));
    await s.actions.writeRecovery(id);
    expect(s.recovery.entries.has(id)).toBe(true);

    answerNext('cancel');
    expect(await s.actions.closeDocument(id)).toBe(false);
    expect(doc(id)).toBeDefined();

    answerNext('discard');
    expect(await s.actions.closeDocument(id)).toBe(true);
    expect(docs().some((d) => d.documentId === id)).toBe(false);
    expect(s.recovery.entries.has(id)).toBe(false);
    expect(s.text.forgotten).toEqual([id]);
  });

  it('an untouched template closes without a question', async () => {
    const s = setup(canOverwrite);
    const id = s.actions.newDocument()!;
    expect(await s.actions.closeDocument(id)).toBe(true);
    expect(useFileStore.getState().dialog).toBeNull();
  });

  it('closing a document with a running simulation asks separately about the run (FILE009): Cancel leaves both, Stop ends it', async () => {
    const s = setup(canOverwrite);
    const id = s.actions.newDocument()!;
    useRunStore.getState().start(id, { runId: 'r1', sourceHash: 'h', endTimeS: 600 });
    answerNext('cancel');
    expect(await s.actions.closeDocument(id)).toBe(false);
    expect(useRunStore.getState().runs[id]?.status).toBe('running');
    expect(doc(id)).toBeDefined();
    answerNext('stop');
    expect(await s.actions.closeDocument(id)).toBe(true);
    expect(docs().some((d) => d.documentId === id)).toBe(false);
  });

  it('after a reload, a document that was never a file comes back from its draft, dirty', async () => {
    const s = setup(canOverwrite);
    const id = active().documentId;
    await s.recovery.put({
      documentId: id,
      displayName: 'plant_01',
      text: versioned,
      currentHash: hashOf(versioned),
      savedHash: null,
      readOnly: false,
      savedAt: 5,
      fingerprint: null,
    });
    useWorkspaceStore.setState({
      documents: docs().map((d) => ({ ...d, loaded: false, currentHash: hashOf(versioned) })),
    });
    await s.actions.start();
    expect(s.text.textOf(id)).toBe(versioned);
    expect(doc(id)).toMatchObject({ loaded: true, dirty: true, recoveryStatus: 'written' });
  });

  it('after a reload, a document whose text is lost says so and starts from the template', async () => {
    const s = setup(canOverwrite);
    const id = active().documentId;
    useWorkspaceStore.setState({
      documents: docs().map((d) => ({ ...d, loaded: false, currentHash: 'gone' })),
    });
    await s.actions.start();
    expect(s.text.textOf(id)).toBe(templateText);
    expect(useFileStore.getState().notices[id]?.kind).toBe('lost');
  });

  it('drafts that belong to no open tab are listed, stale ones marked, and none is deleted unasked', async () => {
    const s = setup(canOverwrite);
    const entry = {
      documentId: 'old',
      displayName: 'old.fluid',
      text: versioned,
      currentHash: hashOf(versioned),
      savedHash: null,
      readOnly: false,
      savedAt: 0,
      fingerprint: null,
    };
    await s.recovery.put(entry);
    await s.recovery.put({
      ...entry,
      documentId: 'new',
      displayName: 'new.fluid',
      savedAt: staleAfterMs,
    });
    await s.clock.advance(staleAfterMs + 1);
    const listed = await s.actions.listDrafts();
    expect(listed.map((d) => [d.documentId, d.stale])).toEqual([
      ['new', false],
      ['old', true],
    ]);
    expect(s.recovery.entries.size).toBe(2);

    await s.actions.restoreDraftToTab(listed[1]!);
    const restored = active();
    expect(restored.displayName).toBe('old.fluid');
    expect(restored.dirty).toBe(true);
    expect(s.text.textOf(restored.documentId)).toBe(versioned);
    expect(s.recovery.entries.has('old')).toBe(false);
    expect(s.recovery.entries.has('new')).toBe(true);
  });
});

describe('saving in place (58, File System Access)', () => {
  it('Save writes through the handle, then marks clean; the recovery entry goes', async () => {
    const s = setup(true);
    s.backend.put('loop.fluid', versioned);
    const id = await open(s, 'loop.fluid');
    const edited = `${versioned}PU2 pump\n`;
    s.text.loadText(id, edited, false);
    useWorkspaceStore.getState().setText(id, hashOf(edited));
    expect(doc(id).dirty).toBe(true);
    await s.actions.writeRecovery(id);

    await s.actions.save(id);
    expect(s.backend.disk.get('loop.fluid')!.text).toBe(edited);
    expect(doc(id)).toMatchObject({
      status: 'clean',
      dirty: false,
      savedHash: hashOf(edited),
      recoveryStatus: 'none',
    });
    expect(s.recovery.entries.has(id)).toBe(false);
  });

  it('a failed write leaves the document dirty with its text, and says which action failed (FILE002)', async () => {
    const s = setup(true);
    s.backend.put('loop.fluid', versioned);
    const id = await open(s, 'loop.fluid');
    const edited = `${versioned}PU2 pump\n`;
    s.text.loadText(id, edited, false);
    useWorkspaceStore.getState().setText(id, hashOf(edited));
    s.backend.failWrite = new Error('disk full');

    await s.actions.save(id);
    expect(doc(id)).toMatchObject({ status: 'error', dirty: true, savedHash: hashOf(versioned) });
    expect(s.text.textOf(id)).toBe(edited);
    expect(s.backend.disk.get('loop.fluid')!.text).toBe(versioned);
    expect(useFileStore.getState().notices[id]).toMatchObject({ kind: 'FILE002' });
    expect(useFileStore.getState().notices[id]!.message).toContain('disk full');
  });

  it('a revoked permission keeps the document dirty and offers Save As (FILE001)', async () => {
    const s = setup(true);
    s.backend.put('loop.fluid', versioned);
    const id = await open(s, 'loop.fluid');
    s.text.loadText(id, `${versioned}\n`, false);
    useWorkspaceStore.getState().setText(id, hashOf(`${versioned}\n`));
    s.backend.permitted = false;
    await s.actions.save(id);
    expect(doc(id)).toMatchObject({ status: 'error', dirty: true });
    expect(useFileStore.getState().notices[id]?.kind).toBe('FILE001');
  });

  it("58's worked example: an external change enters conflict; Save As writes elsewhere and leaves the file and the run alone", async () => {
    const s = setup(true);
    s.backend.put('loop.fluid', versioned);
    const id = await open(s, 'loop.fluid');
    const hashA = hashOf(versioned);
    useRunStore.getState().start(id, { runId: 'r1', sourceHash: hashA, endTimeS: 600 });
    const textB = `${versioned}PU2 pump\n`;
    s.text.loadText(id, textB, false);
    useWorkspaceStore.getState().setText(id, hashOf(textB));
    await s.actions.writeRecovery(id);
    expect(s.recovery.entries.get(id)!.text).toBe(textB);

    // Another program writes C.
    const textC = `${versioned}V1 valve\n`;
    const onDisk = s.backend.disk.get('loop.fluid')!;
    onDisk.text = textC;
    onDisk.modified = 9999;

    await s.actions.save(id);
    expect(doc(id).status).toBe('conflict');
    expect(useFileStore.getState().notices[id]).toMatchObject({
      kind: 'FILE003',
      other: { text: textC },
    });
    expect(s.backend.disk.get('loop.fluid')!.text).toBe(textC);

    s.backend.saveAsName = 'loop-mine.fluid';
    await s.actions.saveAs(id);
    expect(s.backend.disk.get('loop-mine.fluid')!.text).toBe(textB);
    expect(s.backend.disk.get('loop.fluid')!.text).toBe(textC);
    expect(doc(id)).toMatchObject({
      status: 'clean',
      dirty: false,
      displayName: 'loop-mine.fluid',
      savedHash: hashOf(textB),
    });
    expect(useRunStore.getState().runs[id]).toMatchObject({
      runId: 'r1',
      sourceHash: hashA,
      status: 'running',
    });
  });

  it('in a conflict, Reload takes the disk text clean and Compare changes nothing', async () => {
    const s = setup(true);
    s.backend.put('loop.fluid', versioned);
    const id = await open(s, 'loop.fluid');
    const textB = `${versioned}PU2 pump\n`;
    s.text.loadText(id, textB, false);
    useWorkspaceStore.getState().setText(id, hashOf(textB));
    const textC = `${versioned}V1 valve\n`;
    s.backend.disk.get('loop.fluid')!.text = textC;
    s.backend.disk.get('loop.fluid')!.modified = 9999;
    await s.actions.save(id);

    answerNext('close');
    await s.actions.compare(id);
    expect(s.text.textOf(id)).toBe(textB);
    expect(doc(id).status).toBe('conflict');

    await s.actions.reloadFromDisk(id);
    expect(s.text.textOf(id)).toBe(textC);
    expect(doc(id)).toMatchObject({
      status: 'clean',
      dirty: false,
      savedHash: hashOf(textC),
      fileModified: 9999,
    });
    expect(useFileStore.getState().notices[id]).toBeUndefined();
  });

  it('a cancelled Save As changes nothing (FILE006)', async () => {
    const s = setup(true);
    const id = active().documentId;
    s.text.loadText(id, versioned, false);
    useWorkspaceStore.getState().setText(id, hashOf(versioned));
    const before = doc(id);
    s.backend.saveAsName = null;
    await s.actions.saveAs(id);
    expect(doc(id)).toEqual(before);
    expect(useFileStore.getState().notices[id]).toBeUndefined();
  });

  it('Open matches a draft by handle identity or by name and saved hash, never by name alone, and offers the newer draft', async () => {
    const s = setup(true);
    const file = s.backend.put('loop.fluid', versioned);
    const draftText = `${versioned}PU2 pump\n`;
    const entry = {
      documentId: 'gone',
      displayName: 'loop.fluid',
      text: draftText,
      currentHash: hashOf(draftText),
      savedHash: hashOf(versioned),
      readOnly: false,
      savedAt: file.modified + 100,
      fingerprint: { name: 'loop.fluid', modified: file.modified },
    };
    // Same name, other file: no match.
    await s.recovery.put({ ...entry, savedHash: 'other-file' });
    let id = await open(s, 'loop.fluid');
    expect(useFileStore.getState().notices[id]).toBeUndefined();
    expect(s.recovery.entries.has('gone')).toBe(true);
    await s.actions.closeDocument(id);

    // Name and saved hash: a match, offered, and nothing replaced until the user says.
    await s.recovery.put(entry);
    id = await open(s, 'loop.fluid');
    expect(useFileStore.getState().notices[id]).toMatchObject({
      kind: 'divergent',
      draftId: 'gone',
      other: { text: draftText },
    });
    expect(s.text.textOf(id)).toBe(versioned);
    expect(s.recovery.entries.has('gone')).toBe(true);
    s.actions.restoreDraft(id);
    expect(s.text.textOf(id)).toBe(draftText);
    expect(doc(id).dirty).toBe(true);
    await Promise.resolve();
    expect(s.recovery.entries.has('gone')).toBe(false);
    answerNext('discard');
    await s.actions.closeDocument(id);

    // Handle identity, with a name that no longer matches.
    await s.recovery.put({
      ...entry,
      displayName: 'renamed.fluid',
      fingerprint: { name: 'renamed.fluid', modified: 0 },
    });
    await s.recovery.putHandle('gone', file as unknown as FileSystemFileHandle);
    id = await open(s, 'loop.fluid');
    expect(useFileStore.getState().notices[id]?.kind).toBe('divergent');
    s.actions.useFile(id);
    expect(s.text.textOf(id)).toBe(versioned);
    await Promise.resolve();
    expect(s.recovery.entries.has('gone')).toBe(false);
  });

  it('after a reload, a document on disk waits to be reopened; a newer draft becomes a divergence', async () => {
    const s = setup(true);
    s.backend.put('loop.fluid', versioned);
    const id = await open(s, 'loop.fluid');
    const draftText = `${versioned}PU2 pump\n`;
    s.text.loadText(id, draftText, false);
    useWorkspaceStore.getState().setText(id, hashOf(draftText));
    await s.clock.advance(1);
    await s.actions.writeRecovery(id);

    // The reload: the workspace remembers the document, the editor does not.
    useWorkspaceStore.setState({ documents: docs().map((d) => ({ ...d, loaded: false })) });
    s.text.texts.clear();
    await s.actions.start();
    expect(useFileStore.getState().notices[id]).toMatchObject({
      kind: 'reopen',
      other: { text: draftText },
    });
    expect(doc(id).loaded).toBe(false);

    await s.actions.reopen(id);
    expect(doc(id)).toMatchObject({ loaded: true, savedHash: hashOf(versioned) });
    expect(useFileStore.getState().notices[id]?.kind).toBe('divergent');
    s.actions.restoreDraft(id);
    expect(s.text.textOf(id)).toBe(draftText);
    expect(doc(id).dirty).toBe(true);
  });
});
