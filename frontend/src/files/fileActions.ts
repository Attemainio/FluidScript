import type { EditorState } from '@codemirror/state';

import type { ApiClient } from '../api/client.ts';
import type { Diagnostic } from '../api/types.ts';
import type { WorkspaceDocument, WorkspaceState } from '../state/workspaceStore.ts';
import type { RunStoreState } from '../state/runStore.ts';
import type { FileBackend, OpenedFile } from './backend.ts';
import type { FileStoreState } from './fileStore.ts';
import { hashOf } from './hash.ts';
import type { RecoveryEntry, RecoveryStore } from './recovery.ts';
import type { RecoveryScheduler } from './scheduler.ts';
import { templateText } from './template.ts';

/** How the actions reach the editor's text: read it, replace it, and learn whether the document is read-only. */
export interface TextAccess {
  textOf(documentId: string): string;
  /** Replaces the document's text and, when it is the active one, what the view shows. */
  loadText(documentId: string, text: string, readOnly: boolean): EditorState;
  /** The compile diagnostics the document currently has, and whether they describe its current text. */
  diagnosticsOf(documentId: string): {
    readonly diagnostics: readonly Diagnostic[];
    readonly current: boolean;
  };
  /** Applies a diagnostic's suggestion to the document. */
  applySuggestion(documentId: string, diagnostic: Diagnostic): void;
  /** Forgets a closed document everywhere but the workspace: the editor state, the draft, the run. */
  forget(documentId: string): void;
}

export interface FileActionDependencies {
  readonly backend: FileBackend;
  readonly recovery: RecoveryStore;
  readonly scheduler: RecoveryScheduler;
  readonly client: ApiClient;
  readonly text: TextAccess;
  readonly workspace: { getState(): WorkspaceState };
  readonly files: { getState(): FileStoreState };
  readonly runs: { getState(): RunStoreState };
  readonly now: () => number;
}

/** Recovery entries older than this are listed for cleanup, never deleted unasked (`58`). */
export const staleAfterMs = 30 * 24 * 60 * 60 * 1000;

const unsupported = new Set(['FS1702', 'FS1705']);

/** Whether the diagnostics say the text is a file this build cannot edit (`FILE005`). */
function isUnsupported(diagnostics: readonly Diagnostic[]): boolean {
  return diagnostics.some((d) => unsupported.has(d.code));
}

function fileName(displayName: string): string {
  return displayName.endsWith('.fluid') ? displayName : `${displayName}.fluid`;
}

/**
 * `58`'s use cases, one method each, written against the backend interface so the native and the
 * fallback path run the same code. Every failure ends in a notice or a dialog, never a throw the
 * user does not see; every state change goes through the workspace store, so the invariants that
 * store keeps (`clean` only after a completed write) hold whichever path took it.
 */
export class FileActions {
  private readonly d: FileActionDependencies;

  constructor(dependencies: FileActionDependencies) {
    this.d = dependencies;
  }

  private doc(documentId: string): WorkspaceDocument | null {
    return this.d.workspace.getState().documents.find((x) => x.documentId === documentId) ?? null;
  }

  private notify(
    documentId: string,
    kind: FileStoreState['notices'][string]['kind'],
    message: string,
    other?: { label: string; text: string },
    draftId?: string,
  ): void {
    this.d.files.getState().notify({
      kind,
      documentId,
      message,
      ...(other === undefined ? {} : { other }),
      ...(draftId === undefined ? {} : { draftId }),
    });
  }

  // ---- new / open -------------------------------------------------------------------------------

  /** New: template text, no handle, dirty (`58`). */
  newDocument(): string | null {
    const id = this.d.workspace.getState().open();
    if (id === null) {
      void this.refuseLimit();
      return null;
    }
    this.d.text.loadText(id, templateText, false);
    return id;
  }

  private async refuseLimit(): Promise<void> {
    await this.d.files.getState().ask({
      kind: 'limit',
      documentId: null,
      title: 'Eight documents are open',
      message: 'FluidScript keeps at most eight documents open at once. Close one to open another.',
      choices: [{ id: 'ok', label: 'OK', primary: true }],
    });
  }

  /** Open: the picker, then one new tab per file (`D-116`), each matched against recovery first. */
  async openFiles(): Promise<void> {
    let picked: readonly OpenedFile[];
    try {
      picked = await this.d.backend.open();
    } catch (error) {
      this.notify(
        this.d.workspace.getState().activeDocumentId,
        'FILE002',
        `Could not read the file: ${describe(error)}`,
      );
      return;
    }
    for (const file of picked) {
      if (this.d.workspace.getState().documents.length >= 8) {
        await this.refuseLimit();
        return;
      }
      await this.openOne(file);
    }
  }

  private async openOne(file: OpenedFile): Promise<void> {
    const fileHash = hashOf(file.text);
    const readOnly = isUnsupported(await this.inspect(file.text));
    const match = await this.matchRecovery(file, fileHash);
    const id = this.d.workspace.getState().openWith({
      displayName: file.name,
      currentHash: fileHash,
      savedHash: fileHash,
      readOnly,
      fileModified: file.modified,
      hasHandle: file.handle !== null,
    });
    if (id === null) {
      await this.refuseLimit();
      return;
    }
    this.d.text.loadText(id, file.text, readOnly);
    if (file.handle !== null) {
      await this.keepHandle(id, file.handle);
    }
    if (readOnly) {
      this.notify(
        id,
        'FILE005',
        'This file is a FluidScript version this build cannot edit. It is shown as it is; its bytes are preserved.',
      );
    }
    if (match !== null) {
      // A newer draft of this file exists (58): neither replaces the other without the user's word.
      this.notify(
        id,
        'divergent',
        `A draft of ${file.name} saved ${when(match.savedAt, this.d.now())} differs from the file. Restore the draft, or use the file as it is.`,
        { label: 'the draft', text: match.text },
        match.documentId,
      );
    }
  }

  /** Matches a recovery entry to a file just opened: by handle identity where possible, else by name and saved hash, never by name alone. */
  private async matchRecovery(file: OpenedFile, fileHash: string): Promise<RecoveryEntry | null> {
    const open = new Set(this.d.workspace.getState().documents.map((d) => d.documentId));
    let entries: readonly RecoveryEntry[];
    try {
      entries = await this.d.recovery.list();
    } catch {
      return null;
    }
    for (const entry of entries) {
      if (open.has(entry.documentId) || entry.currentHash === fileHash) {
        continue;
      }
      let same = false;
      if (file.handle !== null) {
        const stored = await this.d.recovery.getHandle(entry.documentId).catch(() => null);
        same = stored !== null && (await file.handle.isSameEntry(stored).catch(() => false));
      }
      if (!same) {
        same = entry.fingerprint?.name === file.name && entry.savedHash === fileHash;
      }
      if (same && entry.savedAt > (file.modified ?? 0)) {
        return entry;
      }
    }
    return null;
  }

  /** What compatibility says of the text, before the pipeline has: a validate, or nothing when the host is unreachable. */
  private async inspect(text: string): Promise<readonly Diagnostic[]> {
    try {
      return (await this.d.client.validate(text, new AbortController().signal)).diagnostics;
    } catch {
      return [];
    }
  }

  private async keepHandle(documentId: string, handle: FileSystemFileHandle): Promise<void> {
    try {
      await this.d.recovery.putHandle(documentId, handle);
    } catch {
      this.storeFailed();
    }
  }

  private storeFailed(): void {
    this.d.files.getState().setStoreUnavailable(true);
  }

  // ---- save -------------------------------------------------------------------------------------

  /** Save: in place when there is a handle and the text may be a file; otherwise Save As; a download where the browser cannot overwrite. */
  async save(documentId: string): Promise<void> {
    const doc = this.doc(documentId);
    if (doc === null || !(await this.mayBeAFile(doc))) {
      return;
    }
    const text = this.d.text.textOf(documentId);
    if (!this.d.backend.canOverwrite) {
      this.d.backend.download(fileName(doc.displayName), text);
      this.notify(
        documentId,
        'downloaded',
        `${fileName(doc.displayName)} was downloaded. The document is still unsaved here: this browser cannot write a file in place.`,
      );
      return;
    }
    const handle = doc.hasHandle
      ? await this.d.recovery.getHandle(documentId).catch(() => null)
      : null;
    if (handle === null) {
      await this.saveAs(documentId);
      return;
    }
    if (!(await this.d.backend.permit(handle))) {
      this.d.workspace.getState().setStatus(documentId, 'error');
      this.notify(
        documentId,
        'FILE001',
        `Permission to write ${doc.displayName} was not given. Save As or Download to keep the text.`,
      );
      return;
    }
    await this.write(documentId, handle, text);
  }

  private async write(
    documentId: string,
    handle: FileSystemFileHandle,
    text: string,
  ): Promise<void> {
    const workspace = this.d.workspace.getState();
    const doc = this.doc(documentId)!;
    workspace.setStatus(documentId, 'saving');
    try {
      // The conflict check (58): what is on disk must be what was last read or written.
      const disk = await this.d.backend.read(handle);
      if (disk.modified !== doc.fileModified || hashOf(disk.text) !== doc.savedHash) {
        workspace.setStatus(documentId, 'conflict');
        this.notify(
          documentId,
          'FILE003',
          `${doc.displayName} changed on disk since it was last read. Reload it, save yours elsewhere, or compare the two.`,
          { label: 'on disk', text: disk.text },
        );
        return;
      }
      await this.d.backend.write(handle, text);
      const written = await this.d.backend.read(handle);
      workspace.markSaved(documentId, hashOf(text), written.modified);
      this.d.files.getState().dismiss(documentId);
      await this.clearRecovery(documentId);
    } catch (error) {
      workspace.setStatus(documentId, 'error');
      this.notify(
        documentId,
        'FILE002',
        `Saving ${doc.displayName} failed: ${describe(error)}. The text is still here; Save As or Download to keep it.`,
      );
    }
  }

  /** Save As: a new file, never the old one; cancelling changes nothing (`FILE006`). */
  async saveAs(documentId: string): Promise<void> {
    const doc = this.doc(documentId);
    if (doc === null || !(await this.mayBeAFile(doc))) {
      return;
    }
    const text = this.d.text.textOf(documentId);
    let saved: OpenedFile | null;
    try {
      saved = await this.d.backend.saveAs(fileName(doc.displayName), text);
    } catch (error) {
      this.d.workspace.getState().setStatus(documentId, 'error');
      this.notify(
        documentId,
        'FILE002',
        `Saving failed: ${describe(error)}. The text is still here.`,
      );
      return;
    }
    if (saved === null) {
      if (!this.d.backend.canOverwrite) {
        this.notify(
          documentId,
          'downloaded',
          `${fileName(doc.displayName)} was downloaded. The document is still unsaved here: this browser cannot write a file in place.`,
        );
      }
      return;
    }
    if (saved.handle !== null) {
      await this.keepHandle(documentId, saved.handle);
    }
    this.d.workspace.getState().markSaved(documentId, hashOf(text), saved.modified, saved.name);
    this.d.files.getState().dismiss(documentId);
    await this.clearRecovery(documentId);
  }

  /** Download: the bytes as a file the browser saves; never described as saving (`58` invariant 2). */
  download(documentId: string): void {
    const doc = this.doc(documentId);
    if (doc !== null) {
      this.d.backend.download(fileName(doc.displayName), this.d.text.textOf(documentId));
    }
  }

  /** The other half of `FS1701` (`08`): a read-only file is never written, and an unversioned draft is offered its version line instead. */
  private async mayBeAFile(doc: WorkspaceDocument): Promise<boolean> {
    if (doc.readOnly) {
      this.notify(
        doc.documentId,
        'FILE005',
        'This file is a FluidScript version this build cannot edit, so it cannot be saved from here; its bytes are preserved. Download keeps a copy.',
      );
      return false;
    }
    let { diagnostics, current } = this.d.text.diagnosticsOf(doc.documentId);
    if (!current) {
      diagnostics = await this.inspect(this.d.text.textOf(doc.documentId));
    }
    const unversioned = diagnostics.find((d) => d.code === 'FS1701');
    if (unversioned !== undefined) {
      this.notify(
        doc.documentId,
        'unversioned',
        "This draft states no language version, so it cannot become a file yet. Add 'fluidscript 1' as its first line.",
      );
      return false;
    }
    return true;
  }

  /** Inserts the version line `FS1701` suggests (the unversioned notice's action). */
  addVersionLine(documentId: string): void {
    const { diagnostics } = this.d.text.diagnosticsOf(documentId);
    const unversioned = diagnostics.find((d) => d.code === 'FS1701');
    if (unversioned !== undefined) {
      this.d.text.applySuggestion(documentId, unversioned);
    }
    this.d.files.getState().dismiss(documentId);
  }

  // ---- conflict ---------------------------------------------------------------------------------

  /** Reload: the disk's text replaces the document's, clean; the draft is gone from the editor but its recovery entry stays until the next write. */
  async reloadFromDisk(documentId: string): Promise<void> {
    const notice = this.d.files.getState().notices[documentId];
    const doc = this.doc(documentId);
    if (doc === null || notice?.other === undefined) {
      return;
    }
    const handle = await this.d.recovery.getHandle(documentId).catch(() => null);
    const disk =
      handle === null
        ? { text: notice.other.text, modified: doc.fileModified }
        : await this.d.backend.read(handle);
    this.d.text.loadText(documentId, disk.text, doc.readOnly);
    const workspace = this.d.workspace.getState();
    workspace.setLoaded(documentId, hashOf(disk.text));
    workspace.markSaved(documentId, hashOf(disk.text), disk.modified);
    this.d.files.getState().dismiss(documentId);
    await this.clearRecovery(documentId);
  }

  /** Compare: the two texts side by side, nothing changed (`58` invariant 3). */
  async compare(documentId: string): Promise<void> {
    const notice = this.d.files.getState().notices[documentId];
    const doc = this.doc(documentId);
    if (doc === null || notice?.other === undefined) {
      return;
    }
    await this.d.files.getState().ask({
      kind: 'compare',
      documentId,
      title: `${doc.displayName}: this document and ${notice.other.label}`,
      message: 'Nothing changes here. Close to go back to the choices.',
      choices: [{ id: 'close', label: 'Close', primary: true }],
      compare: {
        left: { label: 'this document', text: this.d.text.textOf(documentId) },
        right: { label: notice.other.label, text: notice.other.text },
      },
    });
  }

  /** On a divergence notice: the draft replaces the file's text, dirty. */
  restoreDraft(documentId: string): void {
    const notice = this.d.files.getState().notices[documentId];
    const doc = this.doc(documentId);
    if (doc === null || notice?.other === undefined) {
      return;
    }
    this.d.text.loadText(documentId, notice.other.text, doc.readOnly);
    this.d.workspace.getState().setLoaded(documentId, hashOf(notice.other.text));
    this.d.files.getState().dismiss(documentId);
    void this.dropDraft(notice.draftId);
    this.d.scheduler.noteEdit(documentId);
  }

  /** On a divergence notice: the file stands and the draft is let go. */
  useFile(documentId: string): void {
    const notice = this.d.files.getState().notices[documentId];
    this.d.files.getState().dismiss(documentId);
    void this.dropDraft(notice?.draftId);
  }

  private async dropDraft(draftId: string | undefined): Promise<void> {
    if (draftId !== undefined) {
      await this.d.recovery.remove(draftId).catch(() => undefined);
      await this.d.recovery.removeHandle(draftId).catch(() => undefined);
    }
  }

  // ---- close ------------------------------------------------------------------------------------

  /** Close: the run first (Stop or Cancel, `FILE009`), then the text (Save, Discard, Cancel); each a separate question. */
  async closeDocument(documentId: string): Promise<boolean> {
    const doc = this.doc(documentId);
    if (doc === null) {
      return false;
    }
    const files = this.d.files.getState();
    if (this.d.runs.getState().runs[documentId]?.status === 'running') {
      const answer = await files.ask({
        kind: 'close-run',
        documentId,
        title: `${doc.displayName} is running a simulation`,
        message:
          'Closing the document stops its run; nothing keeps a run whose document is gone. Cancel leaves both as they are.',
        choices: [
          { id: 'stop', label: 'Stop and close', primary: true },
          { id: 'cancel', label: 'Cancel' },
        ],
      });
      if (answer !== 'stop') {
        return false;
      }
      this.d.runs.getState().finish(documentId, 'stopped');
    }
    if (doc.loaded && doc.dirty && this.d.text.textOf(documentId) !== templateText) {
      const canSave = !doc.readOnly && this.d.backend.canOverwrite;
      const answer = await files.ask({
        kind: 'close-dirty',
        documentId,
        title: `${doc.displayName} has unsaved changes`,
        message: canSave
          ? 'Save them, discard them, or keep the document open.'
          : 'Download them, discard them, or keep the document open.',
        choices: [
          { id: 'save', label: canSave ? 'Save' : 'Download', primary: true },
          { id: 'discard', label: 'Discard' },
          { id: 'cancel', label: 'Cancel' },
        ],
      });
      if (answer === 'cancel') {
        return false;
      }
      if (answer === 'save') {
        if (canSave) {
          await this.save(documentId);
          if (this.doc(documentId)?.dirty !== false) {
            return false;
          }
        } else {
          this.download(documentId);
        }
      }
    }
    this.d.scheduler.cancel(documentId);
    this.d.workspace.getState().close(documentId);
    files.dismiss(documentId);
    this.d.text.forget(documentId);
    await this.clearRecovery(documentId);
    await this.d.recovery.removeHandle(documentId).catch(() => undefined);
    return true;
  }

  // ---- recovery ---------------------------------------------------------------------------------

  /** The scheduler's write: the document's draft to the store, never to its file. */
  async writeRecovery(documentId: string): Promise<void> {
    const doc = this.doc(documentId);
    if (doc === null || !doc.loaded || !doc.dirty) {
      return;
    }
    const text = this.d.text.textOf(documentId);
    if (text === templateText) {
      return;
    }
    const workspace = this.d.workspace.getState();
    workspace.setRecoveryStatus(documentId, 'pending');
    try {
      await this.d.recovery.put({
        documentId,
        displayName: doc.displayName,
        text,
        currentHash: hashOf(text),
        savedHash: doc.savedHash,
        readOnly: doc.readOnly,
        savedAt: this.d.now(),
        fingerprint: doc.hasHandle ? { name: doc.displayName, modified: doc.fileModified } : null,
      });
      workspace.setRecoveryStatus(documentId, 'written');
    } catch {
      workspace.setRecoveryStatus(documentId, 'failed');
      this.storeFailed();
    }
  }

  private async clearRecovery(documentId: string): Promise<void> {
    this.d.scheduler.cancel(documentId);
    await this.d.recovery.remove(documentId).catch(() => undefined);
    this.d.workspace.getState().setRecoveryStatus(documentId, 'none');
  }

  /**
   * On launch: every document the workspace remembers but whose text this session does not have
   * (`51` persists no text) is found again -- from its recovery entry when that is the only copy,
   * from its file when it has one, which needs the user's gesture -- and the entries that belong to
   * no open document are listed as drafts.
   */
  async start(): Promise<void> {
    const workspace = this.d.workspace.getState();
    let entries: readonly RecoveryEntry[] = [];
    try {
      entries = await this.d.recovery.list();
    } catch {
      this.storeFailed();
    }
    const byId = new Map(entries.map((e) => [e.documentId, e]));
    for (const doc of workspace.documents) {
      if (doc.loaded) {
        continue;
      }
      const entry = byId.get(doc.documentId) ?? null;
      if (doc.hasHandle) {
        this.notify(
          doc.documentId,
          'reopen',
          entry === null
            ? `${doc.displayName} is on disk. Reopen it to read the file; the browser asks for permission.`
            : `${doc.displayName} is on disk and a draft of it saved ${when(entry.savedAt, this.d.now())} is here. Reopen the file, or restore the draft.`,
          entry === null ? undefined : { label: 'the draft', text: entry.text },
        );
        this.d.text.loadText(doc.documentId, '', doc.readOnly);
      } else if (entry !== null) {
        // The draft is the only copy there is: it is the document.
        this.d.text.loadText(doc.documentId, entry.text, entry.readOnly);
        this.d.workspace.getState().setLoaded(doc.documentId, hashOf(entry.text), entry.readOnly);
        this.d.workspace.getState().setRecoveryStatus(doc.documentId, 'written');
      } else {
        this.d.text.loadText(doc.documentId, templateText, false);
        this.d.workspace.getState().setLoaded(doc.documentId, hashOf(templateText), false);
        if (doc.dirty) {
          this.notify(
            doc.documentId,
            'lost',
            `${doc.displayName} had unsaved text that was not recovered${this.d.files.getState().storeUnavailable ? ': the recovery store is unavailable' : ''}. It starts again from the template.`,
          );
        }
      }
    }
  }

  /** Reopen (after a reload): the file is read again; a draft newer than it becomes a divergence notice. */
  async reopen(documentId: string): Promise<void> {
    const doc = this.doc(documentId);
    if (doc === null) {
      return;
    }
    const notice = this.d.files.getState().notices[documentId];
    const handle = await this.d.recovery.getHandle(documentId).catch(() => null);
    if (handle === null || !(await this.d.backend.permit(handle))) {
      this.notify(
        documentId,
        'FILE001',
        `${doc.displayName} could not be read again: permission was not given. ${notice?.other === undefined ? 'Close the document, or open the file anew.' : 'Restore the draft, or download it.'}`,
        notice?.other,
      );
      return;
    }
    let disk;
    try {
      disk = await this.d.backend.read(handle);
    } catch (error) {
      this.notify(
        documentId,
        'FILE002',
        `${doc.displayName} could not be read: ${describe(error)}.`,
        notice?.other,
      );
      return;
    }
    const diskHash = hashOf(disk.text);
    const readOnly = isUnsupported(await this.inspect(disk.text));
    this.d.text.loadText(documentId, disk.text, readOnly);
    const workspace = this.d.workspace.getState();
    workspace.setLoaded(documentId, diskHash, readOnly);
    workspace.markSaved(documentId, diskHash, disk.modified);
    const draft = notice?.other;
    if (draft !== undefined && hashOf(draft.text) !== diskHash) {
      this.notify(
        documentId,
        'divergent',
        `The draft of ${doc.displayName} differs from the file on disk. Restore the draft, or use the file as it is.`,
        draft,
      );
    } else {
      this.d.files.getState().dismiss(documentId);
      await this.clearRecovery(documentId);
    }
  }

  /** Restore (after a reload, a document on disk): the draft is the text, dirty, without reading the file. */
  restoreWithoutFile(documentId: string): void {
    const notice = this.d.files.getState().notices[documentId];
    const doc = this.doc(documentId);
    if (doc === null || notice?.other === undefined) {
      return;
    }
    this.d.text.loadText(documentId, notice.other.text, doc.readOnly);
    this.d.workspace.getState().setLoaded(documentId, hashOf(notice.other.text));
    this.d.files.getState().dismiss(documentId);
  }

  /** The drafts that belong to no open document, oldest last, stale ones marked (`58`: listed for explicit cleanup). */
  async listDrafts(): Promise<
    readonly (RecoveryEntry & { readonly stale: boolean; readonly age: string })[]
  > {
    const open = new Set(this.d.workspace.getState().documents.map((d) => d.documentId));
    let entries: readonly RecoveryEntry[] = [];
    try {
      entries = await this.d.recovery.list();
    } catch {
      this.storeFailed();
    }
    const now = this.d.now();
    return entries
      .filter((e) => !open.has(e.documentId))
      .sort((a, b) => b.savedAt - a.savedAt)
      .map((e) => ({ ...e, stale: now - e.savedAt > staleAfterMs, age: when(e.savedAt, now) }));
  }

  /** Restore a listed draft into a new tab, dirty; the entry then belongs to that tab. */
  async restoreDraftToTab(entry: RecoveryEntry): Promise<void> {
    const id = this.d.workspace.getState().openWith({
      displayName: entry.displayName,
      currentHash: entry.currentHash,
      savedHash: entry.savedHash,
      readOnly: entry.readOnly,
      fileModified: entry.fingerprint?.modified ?? null,
      hasHandle: false,
    });
    if (id === null) {
      await this.refuseLimit();
      return;
    }
    this.d.text.loadText(id, entry.text, entry.readOnly);
    await this.d.recovery.remove(entry.documentId).catch(() => undefined);
    this.d.scheduler.noteEdit(id);
  }

  async discardDraft(entry: RecoveryEntry): Promise<void> {
    await this.d.recovery.remove(entry.documentId).catch(() => undefined);
    await this.d.recovery.removeHandle(entry.documentId).catch(() => undefined);
  }

  downloadDraft(entry: RecoveryEntry): void {
    this.d.backend.download(fileName(entry.displayName), entry.text);
  }
}

function describe(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/** "3 minutes ago", for a draft's age. */
export function when(savedAt: number, now: number): string {
  const s = Math.max(0, Math.round((now - savedAt) / 1000));
  if (s < 60) {
    return 'just now';
  }
  const m = Math.round(s / 60);
  if (m < 60) {
    return `${m} minute${m === 1 ? '' : 's'} ago`;
  }
  const h = Math.round(m / 60);
  if (h < 48) {
    return `${h} hour${h === 1 ? '' : 's'} ago`;
  }
  const d = Math.round(h / 24);
  return `${d} days ago`;
}
