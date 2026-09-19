import { create } from 'zustand';

import type { CompileResponse, Diagnostic, ModelContract, Timings } from '../api/types.ts';

/** What the solver is doing for a document, the status line's subject (`51`, `R-51`). */
export type SolveStatus = 'idle' | 'converging' | 'converged' | 'failed';

/** Which source owns the canvas: the draft, or a running simulation the user is watching (`51`). */
export type CanvasMode = 'draft' | 'simulation';

/** A document's draft state: the last successful model, the current diagnostics, and their revision. */
export interface DraftState {
  /** The last model a compile returned, kept through failed compiles (`51` invariant 2). */
  readonly model: ModelContract | null;
  /** The diagnostics of the newest response applied, from a compile or a validate. */
  readonly diagnostics: readonly Diagnostic[];
  /** The source revision the diagnostics describe. */
  readonly diagnosticsRevision: number;
  /** The source revision the model came from. */
  readonly modelRevision: number;
  /** The revision of the compile in flight, or `null` when none is. */
  readonly compiling: number | null;
  readonly status: SolveStatus;
  readonly timings: Timings | null;
  readonly canvasMode: CanvasMode;
  /** A request-level failure to show: the status and the host's correlation id when it sent one. */
  readonly fault: { readonly status: number; readonly correlationId?: string } | null;
  /** The reader's colour-scale switch (`57`): session-only, never written back; `null` follows the script's `show`. */
  readonly shown: string | null;
}

/** The draft store: one `DraftState` per document id (`D-39`). */
export interface DraftStoreState {
  readonly drafts: Readonly<Record<string, DraftState>>;
  beginCompile(documentId: string, revision: number): void;
  /** Applies a compile's answer for `revision`; a stale one, older than what is applied, is dropped (`51` invariant 4). */
  applyCompile(documentId: string, revision: number, response: CompileResponse): void;
  /** Applies a validate's diagnostics; they never replace a compile's for the same or a newer revision (`44`). */
  applyValidate(documentId: string, revision: number, diagnostics: readonly Diagnostic[]): void;
  /** Records a request-level failure; the model stays (`51` error cases). */
  applyFault(documentId: string, revision: number, status: number, correlationId?: string): void;
  endCompile(documentId: string, revision: number): void;
  setCanvasMode(documentId: string, mode: CanvasMode): void;
  /** Switches the property the colours follow, or back to the script's with `null` (`57` invariant 6: no request). */
  setShown(documentId: string, property: string | null): void;
  dispose(documentId: string): void;
}

const empty: DraftState = {
  model: null,
  diagnostics: [],
  diagnosticsRevision: -1,
  modelRevision: -1,
  compiling: null,
  status: 'idle',
  timings: null,
  canvasMode: 'draft',
  fault: null,
  shown: null,
};

/** Reads a document's draft, or the empty draft for one that has not compiled. */
export function draftOf(state: DraftStoreState, documentId: string): DraftState {
  return state.drafts[documentId] ?? empty;
}

function statusOf(response: CompileResponse): SolveStatus {
  const model = response.model;
  if (model === null || model === undefined) {
    return 'idle';
  }
  if (model.solve === null || model.solve === undefined) {
    return model.diagnostics.some((d) => d.severity === 'error') ? 'failed' : 'idle';
  }
  return model.solve.converged ? 'converged' : 'failed';
}

export const useDraftStore = create<DraftStoreState>()((set) => {
  const update = (documentId: string, change: (draft: DraftState) => DraftState): void =>
    set((state) => ({
      drafts: { ...state.drafts, [documentId]: change(state.drafts[documentId] ?? empty) },
    }));

  return {
    drafts: {},

    beginCompile: (documentId, revision) =>
      update(documentId, (draft) => ({ ...draft, compiling: revision, status: 'converging' })),

    applyCompile: (documentId, revision, response) =>
      update(documentId, (draft) => {
        if (revision < draft.modelRevision) {
          return draft;
        }
        const model = response.model ?? null;
        return {
          ...draft,
          model: model ?? draft.model,
          modelRevision: model === null ? draft.modelRevision : revision,
          diagnostics: model?.diagnostics ?? response.diagnostics ?? draft.diagnostics,
          diagnosticsRevision: revision,
          status: statusOf(response),
          timings: response.timings,
          compiling: draft.compiling === revision ? null : draft.compiling,
          fault: null,
        };
      }),

    applyValidate: (documentId, revision, diagnostics) =>
      update(documentId, (draft) =>
        revision <= draft.diagnosticsRevision
          ? draft
          : { ...draft, diagnostics, diagnosticsRevision: revision },
      ),

    applyFault: (documentId, revision, status, correlationId) =>
      update(documentId, (draft) => ({
        ...draft,
        compiling: draft.compiling === revision ? null : draft.compiling,
        status: draft.model === null ? 'idle' : draft.status,
        fault: correlationId === undefined ? { status } : { status, correlationId },
      })),

    endCompile: (documentId, revision) =>
      update(documentId, (draft) =>
        draft.compiling === revision
          ? {
              ...draft,
              compiling: null,
              status:
                draft.status === 'converging'
                  ? draft.model === null
                    ? 'idle'
                    : 'converged'
                  : draft.status,
            }
          : draft,
      ),

    setCanvasMode: (documentId, mode) =>
      update(documentId, (draft) => ({ ...draft, canvasMode: mode })),

    setShown: (documentId, shown) =>
      update(documentId, (draft) => (draft.shown === shown ? draft : { ...draft, shown })),

    dispose: (documentId) =>
      set((state) => {
        const { [documentId]: _gone, ...drafts } = state.drafts;
        return { drafts };
      }),
  };
});
