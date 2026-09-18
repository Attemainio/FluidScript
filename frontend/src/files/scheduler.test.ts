import { describe, expect, it } from 'vitest';

import { FakeClock } from '../test/fakes.ts';
import { idleMs, minIntervalMs, RecoveryScheduler } from './scheduler.ts';

describe('the recovery timer (58)', () => {
  it('writes 1 s after the last edit, and while editing continues no more often than every 5 s', async () => {
    const clock = new FakeClock();
    const writes: number[] = [];
    const scheduler = new RecoveryScheduler(clock, () => writes.push(clock.now()));

    scheduler.noteEdit('a');
    await clock.advance(idleMs - 1);
    scheduler.noteEdit('a');
    await clock.advance(idleMs - 1);
    expect(writes).toEqual([]);
    await clock.advance(1);
    expect(writes).toEqual([2 * idleMs - 1]);

    // An edit right after a write waits for the interval, not just the idle gap.
    scheduler.noteEdit('a');
    await clock.advance(idleMs);
    expect(writes).toHaveLength(1);
    await clock.advance(minIntervalMs - idleMs);
    expect(writes).toHaveLength(2);
    expect(writes[1]! - writes[0]!).toBe(minIntervalMs);
  });

  it('keeps one timer per document and cancels on save or close', async () => {
    const clock = new FakeClock();
    const writes: string[] = [];
    const scheduler = new RecoveryScheduler(clock, (id) => writes.push(id));
    scheduler.noteEdit('a');
    scheduler.noteEdit('b');
    scheduler.cancel('a');
    await clock.advance(idleMs);
    expect(writes).toEqual(['b']);
    expect(scheduler.pending('a')).toBe(false);
  });
});
