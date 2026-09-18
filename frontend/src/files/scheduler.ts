/**
 * When recovery is written (`58`): 1 s after the last edit, and while a document stays dirty and
 * keeps changing, no more often than every 5 s. One timer per document, so a background tab's
 * draft is protected on its own clock.
 */
export interface Clock {
  now(): number;
  setTimeout(callback: () => void, ms: number): unknown;
  clearTimeout(handle: unknown): void;
}

export const idleMs = 1000;
export const minIntervalMs = 5000;

export class RecoveryScheduler {
  private readonly timers = new Map<string, unknown>();
  private readonly lastWrite = new Map<string, number>();

  private readonly clock: Clock;
  private readonly write: (documentId: string) => void;

  constructor(clock: Clock, write: (documentId: string) => void) {
    this.clock = clock;
    this.write = write;
  }

  /** An edit happened: (re)arm the document's timer for the idle delay, but not before the interval since its last write. */
  noteEdit(documentId: string): void {
    const pending = this.timers.get(documentId);
    if (pending !== undefined) {
      this.clock.clearTimeout(pending);
    }
    const since = this.clock.now() - (this.lastWrite.get(documentId) ?? -Infinity);
    const delay = Math.max(idleMs, minIntervalMs - since);
    this.timers.set(
      documentId,
      this.clock.setTimeout(() => {
        this.timers.delete(documentId);
        this.lastWrite.set(documentId, this.clock.now());
        this.write(documentId);
      }, delay),
    );
  }

  /** The document was saved or closed: nothing pending should fire for it. */
  cancel(documentId: string): void {
    const pending = this.timers.get(documentId);
    if (pending !== undefined) {
      this.clock.clearTimeout(pending);
      this.timers.delete(documentId);
    }
    this.lastWrite.delete(documentId);
  }

  /** Whether a write is pending for the document. */
  pending(documentId: string): boolean {
    return this.timers.has(documentId);
  }
}
