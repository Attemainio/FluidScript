import { ApiError, type ApiClient } from '../../api/client.ts';
import type { DraftStoreState } from '../../state/draftStore.ts';
import {
  debounceMs as defaultDebounceMs,
  validateDelayMs as defaultValidateDelayMs,
} from './debounce.ts';
import { LatencyTracker } from './latency.ts';

/** What the pipeline needs from the draft store: the four actions it drives. */
export type DraftActions = Pick<
  DraftStoreState,
  'beginCompile' | 'applyCompile' | 'applyValidate' | 'applyFault' | 'endCompile'
>;

/** The timer functions, injectable so a test can drive time. */
export interface Clock {
  readonly now: () => number;
  readonly setTimeout: (callback: () => void, ms: number) => unknown;
  readonly clearTimeout: (handle: unknown) => void;
}

interface Pending {
  readonly documentId: string;
  readonly revision: number;
  readonly script: string;
}

/**
 * The debounce pipeline (`51`): every edit restarts the idle timer; when it fires, the request in
 * flight is aborted and one `compile` is sent, so at most one is ever in flight (invariant 3); a
 * response for an older revision than the one applied is dropped (invariant 4); a failed compile
 * keeps the last model (invariant 2). A tab switch cancels the outgoing document's work (8b).
 *
 * The fast `validate` phase fires 100 ms into the debounce when the tracker says compiles are slow,
 * and its diagnostics are replaced by the compile's for the same or a newer revision (`44`).
 */
export class CompilePipeline {
  private readonly clock: Clock;
  private readonly debounceMs: number;
  private readonly validateDelayMs: number;
  private readonly sessions = new Map<string, string>();
  private pending: Pending | null = null;
  private last: Pending | null = null;
  private debounceHandle: unknown = null;
  private validateHandle: unknown = null;
  private inFlight: {
    readonly controller: AbortController;
    readonly documentId: string;
    readonly revision: number;
  } | null = null;
  private validateInFlight: AbortController | null = null;
  private requestsSent = 0;

  /** The client the pipeline sends through; the shell loads the metadata through the same one. */
  readonly client: ApiClient;

  private readonly drafts: DraftActions;

  constructor(
    client: ApiClient,
    drafts: DraftActions,
    options: {
      readonly clock?: Clock;
      readonly debounceMs?: number;
      readonly validateDelayMs?: number;
      readonly latency?: LatencyTracker;
      readonly sessionId?: (documentId: string) => string;
    } = {},
  ) {
    this.client = client;
    this.drafts = drafts;
    this.clock = options.clock ?? {
      now: () => performance.now(),
      setTimeout: (callback, ms) => setTimeout(callback, ms),
      clearTimeout: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>),
    };
    this.debounceMs = options.debounceMs ?? defaultDebounceMs;
    this.validateDelayMs = options.validateDelayMs ?? defaultValidateDelayMs;
    this.latency = options.latency ?? new LatencyTracker();
    this.sessionOf = options.sessionId ?? ((id) => this.sessionFor(id));
  }

  /** The compile-latency tracker driving the validate phase. */
  readonly latency: LatencyTracker;

  private readonly sessionOf: (documentId: string) => string;

  /** How many compile requests have been sent; a test's counter. */
  get sent(): number {
    return this.requestsSent;
  }

  /** Whether a compile is in flight. */
  get busy(): boolean {
    return this.inFlight !== null;
  }

  /** An edit: the document's new text at `revision`. Restarts the debounce. */
  edit(documentId: string, script: string, revision: number): void {
    this.pending = { documentId, revision, script };
    this.last = this.pending;
    this.clearTimers();
    this.debounceHandle = this.clock.setTimeout(() => this.fire('compile'), this.debounceMs);
    if (this.latency.validatePhase) {
      this.validateHandle = this.clock.setTimeout(() => void this.validate(), this.validateDelayMs);
    }
  }

  /**
   * Sends what is pending now, without waiting for the debounce. `solve` is the Solve button's path:
   * the stricter endpoint (`42`), on the current text even when nothing is pending.
   */
  flush(mode: 'compile' | 'solve' = 'compile'): void {
    if (this.pending === null && mode === 'solve' && this.last !== null) {
      this.pending = this.last;
    }
    if (this.pending !== null) {
      this.clearTimers();
      this.fire(mode);
    }
  }

  /** Cancels everything for `documentId`: the timers, the request in flight (`51` 8b). */
  cancel(documentId: string): void {
    if (this.pending?.documentId === documentId) {
      this.pending = null;
      this.clearTimers();
    }
    if (this.inFlight?.documentId === documentId) {
      this.inFlight.controller.abort();
      this.inFlight = null;
    }
    this.validateInFlight?.abort();
    this.validateInFlight = null;
  }

  /** Cancels all work; on unmount. */
  dispose(): void {
    this.pending = null;
    this.clearTimers();
    this.inFlight?.controller.abort();
    this.inFlight = null;
    this.validateInFlight?.abort();
    this.validateInFlight = null;
  }

  private clearTimers(): void {
    if (this.debounceHandle !== null) {
      this.clock.clearTimeout(this.debounceHandle);
      this.debounceHandle = null;
    }
    if (this.validateHandle !== null) {
      this.clock.clearTimeout(this.validateHandle);
      this.validateHandle = null;
    }
  }

  private sessionFor(documentId: string): string {
    let session = this.sessions.get(documentId);
    if (session === undefined) {
      session =
        typeof crypto !== 'undefined' && 'randomUUID' in crypto
          ? crypto.randomUUID()
          : `${documentId}-${Date.now()}`;
      this.sessions.set(documentId, session);
    }
    return session;
  }

  private fire(mode: 'compile' | 'solve' = 'compile'): void {
    const request = this.pending;
    this.debounceHandle = null;
    if (request === null) {
      return;
    }
    this.pending = null;

    // One in flight: the older request is aborted client-side, and the host cancels its solve (41).
    this.inFlight?.controller.abort();
    const controller = new AbortController();
    this.inFlight = { controller, documentId: request.documentId, revision: request.revision };
    this.requestsSent++;
    this.drafts.beginCompile(request.documentId, request.revision);

    const started = this.clock.now();
    const body = { sessionId: this.sessionOf(request.documentId), script: request.script };
    const call =
      mode === 'solve'
        ? this.client.solve(body, controller.signal)
        : this.client.compile(body, controller.signal);
    void call
      .then(
        (response) => {
          if (controller.signal.aborted) {
            return;
          }
          this.latency.record(this.clock.now() - started);
          this.drafts.applyCompile(request.documentId, request.revision, response);
        },
        (error: unknown) => {
          if (controller.signal.aborted) {
            return; // a newer request is already running
          }
          if (error instanceof ApiError) {
            if (!error.superseded) {
              this.drafts.applyFault(
                request.documentId,
                request.revision,
                error.status,
                error.problem.correlationId,
              );
            }
          } else {
            this.drafts.applyFault(request.documentId, request.revision, 0);
          }
        },
      )
      .finally(() => {
        if (this.inFlight?.controller === controller) {
          this.inFlight = null;
        }
        this.drafts.endCompile(request.documentId, request.revision);
      });
  }

  private async validate(): Promise<void> {
    const request = this.pending;
    this.validateHandle = null;
    if (request === null) {
      return;
    }

    this.validateInFlight?.abort();
    const controller = new AbortController();
    this.validateInFlight = controller;
    try {
      const response = await this.client.validate(request.script, controller.signal);
      if (!controller.signal.aborted) {
        this.drafts.applyValidate(request.documentId, request.revision, response.diagnostics);
      }
    } catch {
      // Validation is advisory; the compile that follows carries the diagnostics.
    } finally {
      if (this.validateInFlight === controller) {
        this.validateInFlight = null;
      }
    }
  }
}
