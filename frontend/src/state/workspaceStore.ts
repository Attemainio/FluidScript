import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

/** One open document as the workspace knows it (`58`): its id, its name, and whether it has unsaved work. */
export interface WorkspaceDocument {
  readonly documentId: string;
  readonly displayName: string;
  readonly dirty: boolean;
}

/** The workspace: the open documents in tab order and the one that is active (`D-39`, `58`). */
export interface WorkspaceState {
  readonly documents: readonly WorkspaceDocument[];
  readonly activeDocumentId: string;
  /** Opens a new document and makes it active; refused, returning `null`, at the cap (`07`). */
  open(displayName?: string): string | null;
  close(documentId: string): void;
  activate(documentId: string): void;
  rename(documentId: string, displayName: string): void;
  setDirty(documentId: string, dirty: boolean): void;
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

const first: WorkspaceDocument = { documentId: newId(), displayName: 'plant_01', dirty: false };

/**
 * Persisted as ids and names only (`51`): the text is the editor's and recovery is `58`'s
 * IndexedDB, so a reload restores the tabs and their order, not their contents.
 */
export const useWorkspaceStore = create<WorkspaceState>()(
  persist(
    (set, get) => ({
      documents: [first],
      activeDocumentId: first.documentId,

      open: (displayName) => {
        const { documents } = get();
        if (documents.length >= maxDocuments) {
          return null;
        }
        const document: WorkspaceDocument = {
          documentId: newId(),
          displayName: displayName ?? untitled(documents),
          dirty: false,
        };
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
            documents = [{ documentId: newId(), displayName: 'plant_01', dirty: false }];
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

      rename: (documentId, displayName) =>
        set((state) => ({
          documents: state.documents.map((d) =>
            d.documentId === documentId ? { ...d, displayName } : d,
          ),
        })),

      setDirty: (documentId, dirty) =>
        set((state) => ({
          documents: state.documents.map((d) =>
            d.documentId === documentId && d.dirty !== dirty ? { ...d, dirty } : d,
          ),
        })),
    }),
    {
      name: 'fluidscript.workspace',
      storage: createJSONStorage(() => localStorage),
      partialize: (state) => ({
        documents: state.documents,
        activeDocumentId: state.activeDocumentId,
      }),
    },
  ),
);
