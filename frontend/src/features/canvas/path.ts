import type { Point } from './geometry.ts';
import type { PreparedRoute } from './scene.ts';

/** A piece as a path: straight segments, with a fillet's corners rounded by up to a quarter margin. */
export function pathOf(
  piece: readonly Point[],
  corner: PreparedRoute['corner'],
  radius: number,
): string {
  const first = piece[0];
  if (first === undefined) {
    return '';
  }
  const parts = [`M${f(first.x)} ${f(first.y)}`];
  for (let i = 1; i < piece.length; i++) {
    const p = piece[i]!;
    const next = piece[i + 1];
    if (corner !== 'fillet' || next === undefined) {
      parts.push(`L${f(p.x)} ${f(p.y)}`);
      continue;
    }
    const previous = piece[i - 1]!;
    const r = Math.min(radius, distance(previous, p) / 2, distance(p, next) / 2);
    const before = towards(p, previous, r);
    const after = towards(p, next, r);
    parts.push(
      `L${f(before.x)} ${f(before.y)}`,
      `Q${f(p.x)} ${f(p.y)} ${f(after.x)} ${f(after.y)}`,
    );
  }
  return parts.join(' ');
}

export function distance(a: Point, b: Point): number {
  return Math.hypot(b.x - a.x, b.y - a.y);
}

export function towards(from: Point, to: Point, by: number): Point {
  const length = distance(from, to);
  if (length === 0) {
    return from;
  }
  return {
    x: from.x + ((to.x - from.x) * by) / length,
    y: from.y + ((to.y - from.y) * by) / length,
  };
}

export function f(value: number): string {
  return Number(value.toFixed(4)).toString();
}
