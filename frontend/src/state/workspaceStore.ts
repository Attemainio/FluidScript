import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

import { hashOf } from '../files/hash.ts';
import { templateText } from '../files/template.ts';

/** Where a document stands with its file (`58`). `clean` and `dirty` follow from the hashes; the rest are what an action left behind. */
export type DocumentStatus = 'clean' | 'dirty' | 'saving' | 'conflict' | 'error';

/** Whether the document's recovery copy is up to date (`58`). */
export type RecoveryStatus = 'none' | 'pending' | 'written' | 'failed';

/** One open document as the workspace knows it (`58`'s `DocumentState`, less the text, which is the editor's). */
export interface WorkspaceDocument {
  readonly documentId: string;
  readonly displayName: string;
  /** The hash of the last completed named-file write or of the bytes opened; null for a document that has never been a file. */
  readonly savedHash: string | null;
  /** The hash of the text as it is now. */
  readonly currentHash: string;
  readonly status: DocumentStatus;
  readonly recoveryStatus: RecoveryStatus;
  /** `currentHash !== savedHash` (`58` invariant 1): the tab's dot, and what the close prompt asks about. */
  readonly dirty: boolean;
  /** An unsupported file (`FILE005`): shown, never edited, its bytes preserved. */
  readonly readOnly: boolean;
  /** The file's last-modified time when it was last read or written, epoch milliseconds; null without a file. */
  readonly fileModified: number | null;
  /** Whether a file handle exists for the document (the handle itself lives in the recovery store's handle table). */
  readonly hasHandle: boolean;
  /** Whether this session holds the document's text. False after a reload until the file is reopened or the draft restored. Not persisted. */
  readonly loaded: boolean;
}

/** What Open or a restore brings in: the text's hash, where it came from, and how it may be treated. */
export interface OpenedDocument {
  readonly displayName: string;
  readonly currentHash: string;
  readonly savedHash: string | null;
  readonly readOnly: boolean;
  readonly fileModified: number | null;
  readonly hasHandle: boolean;
}

/** The workspace: the open documents in tab order and the one that is active (`D-39`, `58`). */
export interface WorkspaceState {
  readonly documents: readonly WorkspaceDocument[];
  readonly activeDocumentId: string;
  /** Opens a new document from the template and makes it active; refused, returning `null`, at the cap (`07`, `FILE007`). */
  open(displayName?: string): string | null;
  /** Opens a document with text from a file or a recovered draft; refused, returning `null`, at the cap. */
  openWith(opened: OpenedDocument): string | null;
  close(documentId: string): void;
  activate(documentId: string): void;
  rename(documentId: string, displayName: string): void;
  /** The text changed: its hash decides `dirty`, and `clean`/`dirty` unless an action's status stands. */
  setText(documentId: string, currentHash: string): void;
  /** A named-file write completed, or a file was (re)read: the one way `savedHash` changes (`58` invariant 1). */
  markSaved(
    documentId: string,
    savedHash: string,
    fileModified: number | null,
    displayName?: string,
  ): void;
  setStatus(documentId: string, status: DocumentStatus): void;
  setRecoveryStatus(documentId: string, status: RecoveryStatus): void;
  /** Text arrived for a document that had none (after a reload); `currentHash` is the text's. */
  setLoaded(documentId: string, currentHash: string, readOnly?: boolean): void;
}

/** The most documents open at once (`07`). */
export const maxDocuments = 8;

function newId(): string {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `doc-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

function untitled(documents: readonly WorkspaceDocument[]): string {
  const taken = new Set(documents.map((d) => d.displayName));
  for (let n = 1; ; n++) {
    const name = `plant_${String(n).padStart(2, '0')}`;
    if (!taken.has(name)) {
      return name;
    }
  }
}

const templateHash = hashOf(templateText);

function fresh(displayName: string): WorkspaceDocument {
  return {
    documentId: newId(),
    displayName,
    savedHash: null,
    currentHash: templateHash,
    status: 'dirty',
    recoveryStatus: 'none',
    dirty: true,
    readOnly: false,
    fileModified: null,
    hasHandle: false,
    loaded: true,
  };
}

/** Re-derives `dirty` and, where no action's status stands, `status` from the hashes. */
function settle(d: WorkspaceDocument): WorkspaceDocument {
  const dirty = d.savedHash === null || d.currentHash !== d.savedHash;
  const status: DocumentStatus =
    d.status === 'clean' || d.status === 'dirty' ? (dirty ? 'dirty' : 'clean') : d.status;
  return d.dirty === dirty && d.status === status ? d : { ...d, dirty, status };
}

const first = fresh('plant_01');

/**
 * Persisted as documents without their text (`51`): the text is the editor's and, across a
 * reload, the recovery store's (`58`), so a reload restores the tabs, their order, their file
 * state and which one is active -- and marks each not loaded until its text is found again.
 */
export const useWorkspaceStore = create<WorkspaceState>()(
  persist(
    (set, get) => {
      const update = (
        documentId: string,
        change: (d: WorkspaceDocument) => WorkspaceDocument,
      ): void => {
        set((state) => ({
          documents: state.documents.map((d) =>
            d.documentId === documentId ? settle(change(d)) : d,
          ),
        }));
      };

      return {
        documents: [first],
        activeDocumentId: first.documentId,

        open: (displayName) => {
          const { documents } = get();
          if (documents.length >= maxDocuments) {
            return null;
          }
          const document = fresh(displayName ?? untitled(documents));
          set({ documents: [...documents, document], activeDocumentId: document.documentId });
          return document.documentId;
        },

        openWith: (opened) => {
          const { documents } = get();
          if (documents.length >= maxDocuments) {
            return null;
          }
          const document = settle({
            ...fresh(opened.displayName),
            savedHash: opened.savedHash,
            currentHash: opened.currentHash,
            readOnly: opened.readOnly,
            fileModified: opened.fileModified,
            hasHandle: opened.hasHandle,
          });
          set({ documents: [...documents, document], activeDocumentId: document.documentId });
          return document.documentId;
        },

        close: (documentId) =>
          set((state) => {
            const index = state.documents.findIndex((d) => d.documentId === documentId);
            if (index < 0) {
              return state;
            }
            let documents = state.documents.filter((d) => d.documentId !== documentId);
            if (documents.length === 0) {
              documents = [fresh('plant_01')];
            }
            const active =
              state.activeDocumentId === documentId
                ? (documents[Math.min(index, documents.length - 1)]?.documentId ??
                  documents[0]!.documentId)
                : state.activeDocumentId;
            return { documents, activeDocumentId: active };
          }),

        activate: (documentId) =>
          set((state) =>
            state.documents.some((d) => d.documentId === documentId)
              ? { activeDocumentId: documentId }
              : state,
          ),

        rename: (documentId, displayName) => update(documentId, (d) => ({ ...d, displayName })),

        // An edit proves the text is here: a document edited before the launch pass reaches it is loaded.
        setText: (documentId, currentHash) =>
          update(documentId, (d) =>
            d.currentHash === currentHash && d.loaded ? d : { ...d, currentHash, loaded: true },
          ),

        markSaved: (documentId, savedHash, fileModified, displayName) =>
          update(documentId, (d) => ({
            ...d,
            savedHash,
            currentHash: savedHash,
            fileModified,
            hasHandle: true,
            status: 'clean',
            displayName: displayName ?? d.displayName,
          })),

        setStatus: (documentId, status) => update(documentId, (d) => ({ ...d, status })),

        setRecoveryStatus: (documentId, recoveryStatus) =>
          update(documentId, (d) =>
            d.recoveryStatus === recoveryStatus ? d : { ...d, recoveryStatus },
          ),

        setLoaded: (documentId, currentHash, readOnly) =>
          update(documentId, (d) => ({
            ...d,
            loaded: true,
            currentHash,
            readOnly: readOnly ?? d.readOnly,
          })),
      };
    },
    {
      name: 'fluidscript.workspace',
      storage: createJSONStorage(() => localStorage),
      partialize: (state) => ({
        documents: state.documents.map(({ loaded: _loaded, ...d }) => d),
        activeDocumentId: state.activeDocumentId,
      }),
      merge: (persisted, current) => {
        const stored = persisted as Partial<WorkspaceState> | undefined;
        const documents = (stored?.documents ?? []).map((d) =>
          settle({ ...fresh(d.displayName), ...d, loaded: false }),
        );
        if (documents.length === 0) {
          return current;
        }
        const active = documents.some((d) => d.documentId === stored?.activeDocumentId)
          ? stored!.activeDocumentId!
          : documents[0]!.documentId;
        return { ...current, documents, activeDocumentId: active };
      },
    },
  ),
);
