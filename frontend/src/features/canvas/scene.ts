import type {
  Component,
  Diagnostic,
  ModelContract,
  Placement,
  Primitive,
  ResolvedStyle,
  Route,
  SymbolDefinition,
} from '../../api/types.ts';
import {
  boxOf,
  centreOf,
  grow,
  isDegenerate,
  pointOf,
  pointsOf,
  type Box,
  type Point,
} from './geometry.ts';

/**
 * The prepared scene (`53`, `D-71`): what the canvas draws, the exporter serializes and the golden
 * test pins. Every coordinate in it is the layout's (`D-103`, `53` invariant 2): the renderer
 * chooses nothing about where things are, only how the marks look. Pure of the viewport and of
 * React, so a headless test and the export share it (`53` invariant 5).
 */
export interface PreparedScene {
  /** The layout's extent grown by the margin, world units, y up. */
  readonly bounds: Box;
  readonly margin: number;
  /** Every placement with a box, in the layout's order. */
  readonly symbols: readonly PreparedSymbol[];
  /** Inline placements (`D-105`): a boundary node's dot, a pipe's label point. */
  readonly marks: readonly PreparedMark[];
  /** Every route, from the back: signals, return, supply (`28` C16). */
  readonly routes: readonly PreparedRoute[];
  readonly labels: readonly PreparedLabel[];
}

export type Severity = 'error' | 'warning';

export interface PreparedSymbol {
  /** The component id, the only key (`D-34`). */
  readonly id: string;
  readonly kind: string;
  readonly symbolId: string;
  /** The strokes, or `null` for a kind the wire's symbol table does not hold (`53` error cases). */
  readonly primitives: readonly Primitive[] | null;
  readonly inner: Box;
  readonly centre: Point;
  /** Clockwise quarter turns as the layout states them. */
  readonly rotation: number;
  readonly mirrored: boolean;
  readonly inferred: boolean;
  readonly junction: boolean;
  /** The active scale's position, 0 to 1, for the `state` fill slot (`57`); `null` when unsolved. */
  readonly scale: number | null;
  /** The worst diagnostic addressed to the component (`R-24`). */
  readonly badge: Severity | null;
  /** True when a parameter of the component was sized or defaulted rather than stated (`D-02`). */
  readonly sized: boolean;
  readonly ports: readonly PreparedPort[];
  readonly style: ResolvedStyle | null;
}

export interface PreparedPort {
  readonly name: string;
  readonly at: Point;
  readonly direction: Point | null;
}

export interface PreparedMark {
  readonly id: string;
  readonly kind: 'node' | 'pipe';
  readonly at: Point;
  readonly inferred: boolean;
  /** A node with one connection is a boundary and draws a dot; with two it draws nothing (`D-105`). */
  readonly boundary: boolean;
}

export interface PreparedRoute {
  readonly id: string;
  readonly kind: 'pipe' | 'signal';
  readonly layer: string;
  /** The polyline cut around the hops this route owns; each piece is drawn on its own. */
  readonly pieces: readonly (readonly Point[])[];
  readonly arrow: 'forward' | 'reverse' | 'none';
  readonly corner: 'fillet' | 'round' | 'sharp';
  readonly scaleFrom: number | null;
  readonly scaleTo: number | null;
  readonly style: ResolvedStyle | null;
}

export interface PreparedLabel {
  readonly ownerId: string;
  /** The tag where the component has one, else its id (`D-34`); display only. */
  readonly text: string;
  readonly at: Point;
  readonly inferred: boolean;
  /** True for an inline element's label, shown only above `53`'s 3× level of detail. */
  readonly inline: boolean;
}

const layerOrder: Readonly<Record<string, number>> = { supply: 2, return: 1 };

/** Prepares the scene of a model. A model with `solved: false` prepares the same way: topology with no state (`53` invariant 4). */
export function prepareScene(model: ModelContract): PreparedScene {
  const layout = model.layout;
  const margin = layout.margin;
  const components = new Map<string, Component>(model.components.map((c) => [c.id, c]));
  const symbols = new Map<string, SymbolDefinition>(model.symbols.map((s) => [s.id, s]));
  const badges = worstBySubject(model.diagnostics);
  const connectionsOf = countConnections(model);

  const preparedSymbols: PreparedSymbol[] = [];
  const marks: PreparedMark[] = [];
  const labels: PreparedLabel[] = [];

  for (const placement of layout.placements) {
    const component = components.get(placement.componentId);
    const inferred = component?.origin.startsWith('inferred') ?? true;
    const inner = boxOf(placement.inner);
    const labelAt = pointOf(placement.labelAt);
    const text = component?.tag ?? placement.componentId;

    if (isDegenerate(inner)) {
      const kind = placement.symbolId.startsWith('pipe') ? 'pipe' : 'node';
      marks.push({
        id: placement.componentId,
        kind,
        at: centreOf(inner),
        inferred,
        boundary: kind === 'node' && (connectionsOf.get(placement.componentId) ?? 0) < 2,
      });
      labels.push({ ownerId: placement.componentId, text, at: labelAt, inferred, inline: true });
      continue;
    }

    const definition = symbols.get(placement.symbolId);
    preparedSymbols.push({
      id: placement.componentId,
      kind: component?.kind ?? placement.symbolId,
      symbolId: placement.symbolId,
      primitives: definition?.primitives ?? null,
      inner,
      centre: centreOf(inner),
      rotation: placement.rotation,
      mirrored: placement.mirrored,
      inferred,
      junction: Object.keys(placement.anchors).length >= 3 && placement.symbolId.startsWith('node'),
      scale: placement.scale,
      badge: badges.get(placement.componentId) ?? null,
      sized: component !== undefined && isSized(component),
      ports: portsOf(placement),
      style: placement.style ?? null,
    });
    labels.push({ ownerId: placement.componentId, text, at: labelAt, inferred, inline: false });
  }

  const corner = cornerOf(model.style.default.corner) ?? 'sharp';
  const routes = [...layout.routes]
    .sort((a, b) => (layerOrder[a.layer] ?? 0) - (layerOrder[b.layer] ?? 0))
    .map((route) =>
      prepareRoute(
        route,
        margin / 4,
        layout.flow[route.id],
        cornerOf(route.style?.corner) ?? corner,
      ),
    );

  return {
    bounds: grow(boxOf(layout.extent), margin),
    margin,
    symbols: preparedSymbols,
    marks,
    routes,
    labels,
  };
}

function portsOf(placement: Placement): PreparedPort[] {
  return Object.entries(placement.anchors).flatMap(([name, anchor]) =>
    anchor === undefined
      ? []
      : [
          {
            name,
            at: pointOf(anchor.at),
            direction:
              anchor.direction === null || anchor.direction === undefined
                ? null
                : pointOf(anchor.direction),
          },
        ],
  );
}

function prepareRoute(
  route: Route,
  gap: number,
  flow: string | undefined,
  corner: PreparedRoute['corner'],
): PreparedRoute {
  return {
    id: route.id,
    kind: route.kind === 'signal' ? 'signal' : 'pipe',
    layer: route.layer,
    pieces: cutAtHops(pointsOf(route.points), pointsOf(route.hops), gap),
    arrow: route.kind === 'signal' ? 'none' : arrowOf(flow),
    corner,
    scaleFrom: route.scaleFrom,
    scaleTo: route.scaleTo,
    style: route.style ?? null,
  };
}

function arrowOf(flow: string | undefined): PreparedRoute['arrow'] {
  return flow === 'forward' || flow === 'reverse' ? flow : 'none';
}

function cornerOf(corner: string | null | undefined): PreparedRoute['corner'] | null {
  return corner === 'fillet' || corner === 'round' || corner === 'sharp' ? corner : null;
}

/**
 * Breaks a route around each hop it owns, a quarter margin either side, so the route in front runs
 * through the gap (`28` C16). A port of the Core instrument's `Pieces`, so both pictures agree.
 */
export function cutAtHops(
  points: readonly Point[],
  hops: readonly Point[],
  gap: number,
): Point[][] {
  const first = points[0];
  if (first === undefined) {
    return [];
  }
  const pieces: Point[][] = [];
  let piece: Point[] = [first];

  for (let i = 1; i < points.length; i++) {
    const a = points[i - 1]!;
    const b = points[i]!;
    const dx = Math.sign(b.x - a.x);
    const dy = Math.sign(b.y - a.y);
    const along = (p: Point): number => (p.x - a.x) * dx + (p.y - a.y) * dy;
    const onSegment = hops
      .filter(
        (h) =>
          Math.abs((h.x - a.x) * dy - (h.y - a.y) * dx) < 1e-9 &&
          along(h) > 0 &&
          (b.x - h.x) * dx + (b.y - h.y) * dy > 0,
      )
      .sort((p, q) => along(p) - along(q));

    for (const h of onSegment) {
      piece.push({ x: h.x - dx * gap, y: h.y - dy * gap });
      pieces.push(piece);
      piece = [{ x: h.x + dx * gap, y: h.y + dy * gap }];
    }
    piece.push(b);
  }

  pieces.push(piece);
  return pieces;
}

function worstBySubject(diagnostics: readonly Diagnostic[]): Map<string, Severity> {
  const out = new Map<string, Severity>();
  for (const d of diagnostics) {
    const subject = d.component;
    if (subject === null || subject === undefined || d.severity === 'info') {
      continue;
    }
    if (d.severity === 'error' || !out.has(subject)) {
      out.set(subject, d.severity === 'error' ? 'error' : 'warning');
    }
  }
  return out;
}

function countConnections(model: ModelContract): Map<string, number> {
  const out = new Map<string, number>();
  for (const connection of model.connections) {
    for (const end of [connection.from, connection.to]) {
      out.set(end.component, (out.get(end.component) ?? 0) + 1);
    }
  }
  return out;
}

function isSized(component: Component): boolean {
  return Object.values(component.parameters).some((p) => p !== undefined && p.source !== 'stated');
}
