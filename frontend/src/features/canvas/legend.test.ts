import { describe, expect, it } from 'vitest';

import type { Scale } from '../../api/types.ts';
import { bandsOf, legendNote, legendTitle, niceStep, ticksOf } from './legend.ts';

function scale(overrides: Partial<Scale>): Scale {
  return {
    property: 'temperature',
    displayName: 'Temperature',
    unit: '°C',
    kind: 'sequential',
    domain: { min: 0, max: 60, nice: true },
    degenerate: false,
    ...overrides,
  };
}

describe('the legend (57)', () => {
  it("ticks a niced domain on the 1-2-5 step, 57's worked example included", () => {
    // 57: raw 6…50 nices to 5…50 with ticks at 5, 15, 25, 35, 45, 50 -- the step is 10, and the ends stay.
    expect(niceStep(45)).toBe(10);
    expect(ticksOf(5, 50).map((t) => t.value)).toEqual([5, 10, 20, 30, 40, 50]);
    expect(ticksOf(0, 60).map((t) => t.value)).toEqual([0, 20, 40, 60]);
    expect(ticksOf(280, 380).map((t) => t.value)).toEqual([280, 300, 320, 340, 360, 380]);
    expect(ticksOf(0.16, 0.24).map((t) => t.value)).toEqual([0.16, 0.18, 0.2, 0.22, 0.24]);
  });

  it('keeps the ends of a fixed domain that is not on the step', () => {
    expect(ticksOf(3, 77).map((t) => t.value)).toEqual([3, 20, 40, 60, 77]);
    const ticks = ticksOf(3, 77);
    expect(ticks[0]!.position).toBe(0);
    expect(ticks[ticks.length - 1]!.position).toBe(1);
  });

  it('bands lie between consecutive ticks, as positions', () => {
    expect(bandsOf(ticksOf(0, 60))).toEqual([
      [0, 1 / 3],
      [1 / 3, 2 / 3],
      [2 / 3, 1],
    ]);
  });

  it('names the property and its unit, never a bare ramp (invariant 1)', () => {
    expect(legendTitle(scale({}))).toBe('Temperature · °C');
    expect(legendTitle(scale({ unit: '' }))).toBe('Temperature');
  });

  it('says so when every element is alike, and when nothing is solved (invariants 3, 9)', () => {
    expect(legendNote(scale({ domain: { min: 20, max: 20, nice: false }, degenerate: true }))).toBe(
      'all 20 °C',
    );
    expect(legendNote(scale({ domain: null }))).toBe('No solved temperature values');
    expect(legendNote(scale({}))).toBeNull();
    expect(ticksOf(20, 20)).toEqual([{ value: 20, position: 0.5 }]);
  });
});
