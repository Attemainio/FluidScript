/** A point in world units, y up (`28` A1). */
export interface Point {
  readonly x: number;
  readonly y: number;
}

/** An axis-aligned box in world units: origin at the bottom-left, y up. */
export interface Box {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

/** The wire's `[x, y, width, height]` as a box. */
export function boxOf(values: readonly number[]): Box {
  return { x: values[0] ?? 0, y: values[1] ?? 0, width: values[2] ?? 0, height: values[3] ?? 0 };
}

/** The wire's `[x, y]` as a point. */
export function pointOf(values: readonly number[]): Point {
  return { x: values[0] ?? 0, y: values[1] ?? 0 };
}

/** A flattened `[x0, y0, x1, y1, …]` as points. */
export function pointsOf(values: readonly number[]): Point[] {
  const out: Point[] = [];
  for (let i = 0; i + 1 < values.length; i += 2) {
    out.push({ x: values[i] ?? 0, y: values[i + 1] ?? 0 });
  }
  return out;
}

export function centreOf(box: Box): Point {
  return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
}

export function grow(box: Box, by: number): Box {
  return { x: box.x - by, y: box.y - by, width: box.width + 2 * by, height: box.height + 2 * by };
}

/** True for a box with no area: an inline placement (`D-105`) on the wire. */
export function isDegenerate(box: Box): boolean {
  return box.width === 0 && box.height === 0;
}
