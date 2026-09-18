import { create } from 'zustand';

/** Where a selection came from, so the pane that made it does not react to its own change. */
export type SelectionOrigin = 'canvas' | 'editor' | 'log';

/** The selected components of a document (`54` Selection): ids only, never tags (`D-34`). */
export interface SelectionState {
  readonly selected: Readonly<Record<string, readonly string[]>>;
  readonly origin: Readonly<Record<string, SelectionOrigin>>;
  /** Replaces the selection; with `add`, toggles the id in it (Shift+click). */
  readonly select: (documentId: string, id: string, origin: SelectionOrigin, add?: boolean) => void;
  readonly clear: (documentId: string) => void;
}

export const useSelectionStore = create<SelectionState>()((set) => ({
  selected: {},
  origin: {},
  select: (documentId, id, origin, add = false) =>
    set((state) => {
      const current = state.selected[documentId] ?? [];
      const next = add
        ? current.includes(id)
          ? current.filter((x) => x !== id)
          : [...current, id]
        : [id];
      return {
        selected: { ...state.selected, [documentId]: next },
        origin: { ...state.origin, [documentId]: origin },
      };
    }),
  clear: (documentId) => set((state) => ({ selected: { ...state.selected, [documentId]: [] } })),
}));

const none: readonly string[] = [];

/** The document's selection, the one shared empty array when it has none, so a selector stays stable. */
export function selectionOf(state: SelectionState, documentId: string): readonly string[] {
  return state.selected[documentId] ?? none;
}
