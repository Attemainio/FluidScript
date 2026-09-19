import type { Scale } from '../../api/types.ts';
import { formatValue } from '../hover/card.ts';

/** One legend tick: its value and where it sits along the ramp, 0 to 1. */
export interface Tick {
  readonly value: number;
  readonly position: number;
}

/** The 1-2-5 step that gives about five ticks across a span; the same rule Core nices a domain with. */
export function niceStep(span: number): number {
  if (!(span > 0)) {
    return 1;
  }
  const raw = span / 5;
  const power = 10 ** Math.floor(Math.log10(raw));
  const fraction = raw / power;
  return (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * power;
}

/**
 * The legend's ticks (`57`): the 1-2-5 values inside the domain, and its two ends when they are not
 * on the step (a fixed `show t 3..77` keeps its ends). A degenerate domain has one tick.
 */
export function ticksOf(min: number, max: number): Tick[] {
  if (max <= min) {
    return [{ value: min, position: 0.5 }];
  }
  const step = niceStep(max - min);
  const values: number[] = [];
  const first = Math.ceil(min / step - 1e-9) * step;
  for (let v = first; v <= max + 1e-9; v += step) {
    values.push(Number(v.toFixed(10)));
  }
  if (values[0] !== min) {
    values.unshift(min);
  }
  if (values[values.length - 1] !== max) {
    values.push(max);
  }
  return values.map((value) => ({ value, position: (value - min) / (max - min) }));
}

/** The bands between consecutive ticks, as positions, for the hover that highlights a band's elements. */
export function bandsOf(ticks: readonly Tick[]): (readonly [number, number])[] {
  const out: (readonly [number, number])[] = [];
  for (let i = 1; i < ticks.length; i++) {
    out.push([ticks[i - 1]!.position, ticks[i]!.position]);
  }
  return out;
}

/** What the legend says at its head: the property and its unit, never a bare ramp (`57` invariant 1). */
export function legendTitle(scale: Scale): string {
  return scale.unit.length > 0 ? `${scale.displayName} · ${scale.unit}` : scale.displayName;
}

/** The legend's one-line state where there is no ramp to read: every element alike, or nothing solved. */
export function legendNote(scale: Scale): string | null {
  if (scale.domain === null) {
    return `No solved ${scale.displayName.toLowerCase()} values`;
  }
  if (scale.degenerate) {
    return `all ${formatValue(scale.domain.min)}${scale.unit.length > 0 ? ` ${scale.unit}` : ''}`;
  }
  return null;
}
