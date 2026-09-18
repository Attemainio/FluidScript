import { create } from 'zustand';

import type { RecoveryEntry } from './recovery.ts';

/** `58`'s error keys, plus the situations that are not errors but need the user's word. */
export type NoticeKind =
  | 'FILE001'
  | 'FILE002'
  | 'FILE003'
  | 'FILE005'
  | 'unversioned'
  | 'reopen'
  | 'divergent'
  | 'lost'
  | 'downloaded';

/** A banner over the editor about the active document: what happened, and what can be done about it. */
export interface FileNotice {
  readonly kind: NoticeKind;
  readonly documentId: string;
  readonly message: string;
  /** What the other side holds, for Compare and Reload: the disk's text in a conflict, the draft's on a divergence. */
  readonly other?: { readonly label: string; readonly text: string };
  /** The recovery entry the notice is about, when it belongs to no open document; removed once the user has chosen. */
  readonly draftId?: string;
}

/** A modal question. `choose` answers it; the asker awaits the answer. */
export interface FileDialog {
  readonly kind: 'close-dirty' | 'close-run' | 'limit' | 'compare' | 'drafts';
  readonly documentId: string | null;
  readonly title: string;
  readonly message: string;
  /** The answers offered, in order; the first is the default focus. */
  readonly choices: readonly {
    readonly id: string;
    readonly label: string;
    readonly primary?: boolean;
  }[];
  /** For `compare`: the two texts side by side. */
  readonly compare?: {
    readonly left: { readonly label: string; readonly text: string };
    readonly right: { readonly label: string; readonly text: string };
  };
  /** For `drafts`: the recovery entries listed. */
  readonly drafts?: readonly (RecoveryEntry & { readonly stale: boolean; readonly age: string })[];
}

export interface FileStoreState {
  /** False in a browser whose Save is a download (`58`: the action is labelled Download .fluid). */
  readonly canOverwrite: boolean;
  /** `FILE004`: the recovery store failed; shown until the page reloads. */
  readonly storeUnavailable: boolean;
  readonly notices: Readonly<Record<string, FileNotice>>;
  readonly dialog: (FileDialog & { readonly choose: (id: string) => void }) | null;
  setCanOverwrite(value: boolean): void;
  setStoreUnavailable(value: boolean): void;
  notify(notice: FileNotice): void;
  dismiss(documentId: string): void;
  /** Shows a dialog and resolves with the chosen answer's id; a dialog already open is answered `cancel` first. */
  ask(dialog: FileDialog): Promise<string>;
}

export const useFileStore = create<FileStoreState>()((set, get) => ({
  canOverwrite: true,
  storeUnavailable: false,
  notices: {},
  dialog: null,

  setCanOverwrite: (canOverwrite) => set({ canOverwrite }),
  setStoreUnavailable: (storeUnavailable) => set({ storeUnavailable }),

  notify: (notice) =>
    set((state) => ({ notices: { ...state.notices, [notice.documentId]: notice } })),

  dismiss: (documentId) =>
    set((state) => {
      if (!(documentId in state.notices)) {
        return state;
      }
      const { [documentId]: _gone, ...notices } = state.notices;
      return { notices };
    }),

  ask: (dialog) =>
    new Promise((resolve) => {
      get().dialog?.choose('cancel');
      set({
        dialog: {
          ...dialog,
          choose: (id) => {
            set({ dialog: null });
            resolve(id);
          },
        },
      });
    }),
}));
