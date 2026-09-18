/**
 * The thresholds, each with `51`'s default: the window size, the p95 that enables the phase, the
 * duration counted as quick and how many quick compiles in a row disable it.
 */
export interface LatencyOptions {
  readonly windowSize?: number;
  readonly enableAboveMs?: number;
  readonly disableBelowMs?: number;
  readonly disableAfter?: number;
}

/**
 * Decides whether the fast `validate` phase runs (`51` two-phase feedback): on when the rolling p95
 * of the last twenty completed compiles is over 100 ms, off again after fifty consecutive compiles
 * under 75 ms. Two thresholds, so the phase does not chatter around one.
 */
export class LatencyTracker {
  private readonly window: number[] = [];
  private quickStreak = 0;
  private enabled = false;

  private readonly options: LatencyOptions;

  constructor(options: LatencyOptions = {}) {
    this.options = options;
  }

  /** Whether the validate phase is on. */
  get validatePhase(): boolean {
    return this.enabled;
  }

  /** The p95 of the window, or 0 before any compile completed. */
  get p95Ms(): number {
    if (this.window.length === 0) {
      return 0;
    }
    const sorted = [...this.window].sort((a, b) => a - b);
    return sorted[Math.min(sorted.length - 1, Math.ceil(sorted.length * 0.95) - 1)] ?? 0;
  }

  /** Records one completed compile's wall time and updates the phase. */
  record(durationMs: number): void {
    const size = this.options.windowSize ?? 20;
    this.window.push(durationMs);
    if (this.window.length > size) {
      this.window.shift();
    }

    if (durationMs < (this.options.disableBelowMs ?? 75)) {
      this.quickStreak++;
    } else {
      this.quickStreak = 0;
    }

    if (!this.enabled && this.p95Ms > (this.options.enableAboveMs ?? 100)) {
      this.enabled = true;
      this.quickStreak = 0;
    } else if (this.enabled && this.quickStreak >= (this.options.disableAfter ?? 50)) {
      this.enabled = false;
    }
  }
}
