import { create } from 'zustand';

/** Where a transient run is (`51`, `43`); the frames and checkpoints come with the transient work. */
export type RunStatus = 'running' | 'ended' | 'stopped' | 'failed';

/** A document's run: the immutable snapshot it was started from and its state. Nothing here is a model object (`51` invariant 8). */
export interface RunState {
  readonly runId: string;
  readonly sourceHash: string;
  readonly status: RunStatus;
  /** The last verified frame's simulation time, seconds. */
  readonly timeS: number;
  readonly endTimeS: number;
}

/** The run store: at most one run per document (`D-39`), at most two running (`07`). */
export interface RunStoreState {
  readonly runs: Readonly<Record<string, RunState>>;
  start(documentId: string, run: Omit<RunState, 'status' | 'timeS'>): void;
  advance(documentId: string, timeS: number): void;
  finish(documentId: string, status: Exclude<RunStatus, 'running'>): void;
  dispose(documentId: string): void;
}

/** The most runs that may be active at once (`07`). */
export const maxConcurrentRuns = 2;

export const useRunStore = create<RunStoreState>()((set) => ({
  runs: {},
  start: (documentId, run) =>
    set((state) => ({
      runs: { ...state.runs, [documentId]: { ...run, status: 'running', timeS: 0 } },
    })),
  advance: (documentId, timeS) =>
    set((state) => {
      const run = state.runs[documentId];
      return run === undefined
        ? state
        : { runs: { ...state.runs, [documentId]: { ...run, timeS } } };
    }),
  finish: (documentId, status) =>
    set((state) => {
      const run = state.runs[documentId];
      return run === undefined
        ? state
        : { runs: { ...state.runs, [documentId]: { ...run, status } } };
    }),
  dispose: (documentId) =>
    set((state) => {
      const { [documentId]: _gone, ...runs } = state.runs;
      return { runs };
    }),
}));
