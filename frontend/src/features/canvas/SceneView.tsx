import { fluidFill, typeMetrics, worldUnitPx } from '../../design/tokens.ts';
import type { Primitive } from '../../api/types.ts';
import type { Point } from './geometry.ts';
import type { Detail } from './detail.ts';
import { distance, f, pathOf, towards } from './path.ts';
import type { PreparedRoute, PreparedScene, PreparedSymbol } from './scene.ts';

/** The label's font size in world units: `55`'s canvas label at 1× (`D-73`). */
const labelSize = typeMetrics.canvasLabel.sizePx / worldUnitPx;
const emptySelection: ReadonlySet<string> = new Set();
const symbolStroke = 1.5;
const markRadius = 0.05;
const arrowLength = 0.16;
const arrowHalfWidth = 0.07;

/**
 * Draws a prepared scene in world units, y up: the caller's root transform maps it to pixels.
 * Every coordinate written here is the scene's (`53` invariant 2); strokes keep their pixel width
 * at any zoom. The same component renders the canvas, the golden test and, later, the export.
 */
export function SceneView({
  scene,
  detail,
  selected = emptySelection,
  band = null,
  stale = false,
  idPrefix = 'scene',
}: {
  scene: PreparedScene;
  detail: Detail;
  /** The selected component ids (`54`), drawn in the selection colour. */
  selected?: ReadonlySet<string>;
  /** A band of the scale, 0 to 1, from the legend's hover (`57`): symbols inside it stand out, the rest recede. */
  band?: readonly [number, number] | null;
  /** True while a newer text is compiling: the colours desaturate rather than pose as current (`57` invariant 7). */
  stale?: boolean;
  /** Prefix for the gradient ids, so two scenes on one page do not share them. */
  idPrefix?: string;
}): React.ReactNode {
  const className = [
    'scene',
    scene.solved ? '' : 'scene--unsolved',
    stale ? 'scene--stale' : '',
    band === null ? '' : 'scene--banded',
  ]
    .filter((c) => c.length > 0)
    .join(' ');
  const inBand = (position: number | null): boolean =>
    band !== null && position !== null && position >= band[0] && position <= band[1];

  return (
    <g className={className} fontFamily={`var(${typeMetrics.canvasLabel.family})`}>
      <g className="scene__routes">
        {scene.routes.map((route) => (
          <RouteView
            key={route.id}
            route={route}
            margin={scene.margin}
            solved={scene.solved}
            gradientId={`${idPrefix}-route-${route.id}`}
          />
        ))}
      </g>
      <g className="scene__symbols">
        {scene.symbols.map((symbol) => (
          <SymbolView
            key={symbol.id}
            symbol={symbol}
            detail={detail}
            solved={scene.solved}
            selected={selected.has(symbol.id)}
            banded={band === null ? null : inBand(symbol.scale)}
            gradientId={`${idPrefix}-symbol-${symbol.id}`}
          />
        ))}
        {scene.marks.map((mark) =>
          mark.kind === 'node' && mark.boundary ? (
            <circle
              key={mark.id}
              data-id={mark.id}
              className={mark.inferred ? 'scene__mark scene__mark--inferred' : 'scene__mark'}
              cx={mark.at.x}
              cy={mark.at.y}
              r={markRadius}
              strokeWidth={symbolStroke}
              vectorEffect="non-scaling-stroke"
            >
              <title>{mark.id}</title>
            </circle>
          ) : null,
        )}
      </g>
      {detail !== 'symbols' ? (
        <g
          className="scene__labels"
          fontSize={labelSize}
          fontWeight={typeMetrics.canvasLabel.weight}
        >
          {scene.labels
            .filter((label) => detail === 'all' || !label.inline)
            .map((label) => (
              <g key={label.ownerId}>
                {label.clear || label.inline ? null : (
                  // The layout could not place this label clear (53 label geometry, C-84): a leader
                  // from the label to its owner says which symbol it names.
                  <line
                    className="scene__label-leader"
                    data-owner={label.ownerId}
                    x1={label.at.x}
                    y1={label.at.y}
                    x2={label.owner.x}
                    y2={label.owner.y}
                  />
                )}
                <text
                  data-owner={label.ownerId}
                  className={
                    label.inferred ? 'scene__label scene__label--inferred' : 'scene__label'
                  }
                  transform={`translate(${label.at.x} ${label.at.y}) scale(1 -1)`}
                  textAnchor="middle"
                >
                  {label.text}
                </text>
              </g>
            ))}
        </g>
      ) : null}
    </g>
  );
}

function SymbolView({
  symbol,
  detail,
  solved,
  selected,
  banded,
  gradientId,
}: {
  symbol: PreparedSymbol;
  detail: Detail;
  solved: boolean;
  selected: boolean;
  /** Inside the legend's hovered band, outside it, or no band (`null`). */
  banded: boolean | null;
  gradientId: string;
}): React.ReactNode {
  // Symbol space (y up) → world: mirror, then the clockwise quarter turn; the y flip is the root's.
  const transform = `translate(${symbol.centre.x} ${symbol.centre.y}) rotate(${-symbol.rotation}) scale(${symbol.mirrored ? -1 : 1} 1)`;
  // An exchanger's fill runs from its inlet's colour to its outlet's across the symbol (57 Components).
  const gradient =
    solved && symbol.scaleFrom !== null && symbol.scaleTo !== null
      ? gradientAcross(symbol, symbol.scaleFrom, symbol.scaleTo)
      : null;
  const stateFill =
    symbol.scale === null || !solved
      ? 'var(--canvas-bg)'
      : gradient === null
        ? fluidFill(symbol.scale)
        : `url(#${gradientId})`;
  const className = [
    'scene__symbol',
    symbol.inferred ? 'scene__symbol--inferred' : '',
    selected ? 'scene__symbol--selected' : '',
    banded === true
      ? 'scene__symbol--in-band'
      : banded === false
        ? 'scene__symbol--out-of-band'
        : '',
  ]
    .filter((c) => c.length > 0)
    .join(' ');
  const description = `${symbol.id}, ${symbol.kind}${symbol.inferred ? ', inferred' : ''}`;

  return (
    <g
      data-id={symbol.id}
      className={className}
      tabIndex={0}
      role="img"
      aria-label={description}
      strokeWidth={symbolStroke}
    >
      <title>{description}</title>
      <g
        transform={transform}
        fill="none"
        strokeWidth={symbolStroke}
        strokeLinejoin="round"
        strokeLinecap="round"
        vectorEffect="non-scaling-stroke"
      >
        {gradient !== null ? (
          <defs>
            <linearGradient
              id={gradientId}
              gradientUnits="userSpaceOnUse"
              x1={f(gradient.from.x)}
              y1={f(gradient.from.y)}
              x2={f(gradient.to.x)}
              y2={f(gradient.to.y)}
            >
              <stop offset="0" style={{ stopColor: fluidFill(symbol.scaleFrom!) }} />
              <stop offset="1" style={{ stopColor: fluidFill(symbol.scaleTo!) }} />
            </linearGradient>
          </defs>
        ) : null}
        {symbol.primitives === null ? (
          <rect
            className="scene__unknown"
            x={-symbol.inner.width / 2}
            y={-symbol.inner.height / 2}
            width={symbol.inner.width}
            height={symbol.inner.height}
            vectorEffect="non-scaling-stroke"
          />
        ) : (
          symbol.primitives.map((primitive, index) => (
            <PrimitiveView key={index} primitive={primitive} stateFill={stateFill} />
          ))
        )}
      </g>
      {detail === 'values' || detail === 'all'
        ? symbol.ports.map((port) => (
            <circle
              key={port.name}
              className="scene__port"
              cx={port.at.x}
              cy={port.at.y}
              r={markRadius / 2}
              stroke="none"
            >
              <title>{port.name}</title>
            </circle>
          ))
        : null}
      {symbol.badge !== null ? (
        <circle
          className={`scene__badge scene__badge--${symbol.badge}`}
          cx={symbol.inner.x + symbol.inner.width}
          cy={symbol.inner.y + symbol.inner.height}
          r={markRadius * 1.4}
          stroke="none"
        >
          <title>{symbol.badge === 'error' ? 'has an error' : 'has a warning'}</title>
        </circle>
      ) : null}
      {symbol.sized ? (
        <rect
          className="scene__sized"
          x={symbol.inner.x - markRadius}
          y={symbol.inner.y - markRadius}
          width={markRadius * 2}
          height={markRadius * 2}
          vectorEffect="non-scaling-stroke"
        >
          <title>carries a sized or defaulted value</title>
        </rect>
      ) : null}
    </g>
  );
}

function PrimitiveView({
  primitive,
  stateFill,
}: {
  primitive: Primitive;
  stateFill: string;
}): React.ReactNode {
  const fill =
    primitive.fill === 'state' ? stateFill : primitive.fill === 'stroke' ? 'currentColor' : 'none';
  const dash = primitive.dashed === true ? { strokeDasharray: '3 2' } : {};
  const common = { fill, vectorEffect: 'non-scaling-stroke' as const, ...dash };

  switch (primitive.kind) {
    case 'rect':
      return (
        <rect
          x={primitive.x ?? 0}
          y={primitive.y ?? 0}
          width={primitive.width ?? 0}
          height={primitive.height ?? 0}
          {...common}
        />
      );
    case 'circle':
      return (
        <circle cx={primitive.x ?? 0} cy={primitive.y ?? 0} r={primitive.r ?? 0} {...common} />
      );
    case 'line':
      return (
        <line
          x1={primitive.from?.[0] ?? 0}
          y1={primitive.from?.[1] ?? 0}
          x2={primitive.to?.[0] ?? 0}
          y2={primitive.to?.[1] ?? 0}
          {...common}
        />
      );
    case 'polygon':
      return <polygon points={pairs(primitive.points ?? [])} {...common} />;
    default:
      return <polyline points={pairs(primitive.points ?? [])} {...common} />;
  }
}

function pairs(values: readonly number[]): string {
  const out: string[] = [];
  for (let i = 0; i + 1 < values.length; i += 2) {
    out.push(`${values[i]},${values[i + 1]}`);
  }
  return out.join(' ');
}

function RouteView({
  route,
  margin,
  solved,
  gradientId,
}: {
  route: PreparedRoute;
  margin: number;
  solved: boolean;
  gradientId: string;
}): React.ReactNode {
  const width = route.style?.strokeWidth ?? 2;
  const stated = route.style?.stroke ?? undefined;
  const dashed = route.kind === 'signal' || route.style?.pattern === 'dashed';
  // A pipe is a gradient between its ends' values (57 Connections); a stated stroke colour keeps its word (D-104), and an end with no value leaves the pipe neutral (invariant 5).
  const ends =
    solved && stated === undefined && route.scaleFrom !== null && route.scaleTo !== null
      ? gradientAlong(route.pieces)
      : null;
  const stroke = ends === null ? stated : `url(#${gradientId})`;
  const className = [
    'scene__route',
    `scene__route--${route.kind}`,
    `scene__route--${route.layer}`,
    ends === null ? '' : 'scene__route--scaled',
  ]
    .filter((c) => c.length > 0)
    .join(' ');

  return (
    <g
      data-id={route.id}
      className={className}
      fill="none"
      style={stroke === undefined ? undefined : { stroke, color: stated ?? 'currentColor' }}
      strokeWidth={width}
      strokeLinejoin={route.corner === 'sharp' ? 'miter' : 'round'}
      strokeLinecap="round"
      strokeDasharray={dashed ? '6 4' : undefined}
    >
      {ends !== null ? (
        <defs>
          <linearGradient
            id={gradientId}
            gradientUnits="userSpaceOnUse"
            x1={f(ends.from.x)}
            y1={f(ends.from.y)}
            x2={f(ends.to.x)}
            y2={f(ends.to.y)}
          >
            <stop offset="0" style={{ stopColor: fluidFill(route.scaleFrom!) }} />
            <stop offset="1" style={{ stopColor: fluidFill(route.scaleTo!) }} />
          </linearGradient>
        </defs>
      ) : null}
      {route.pieces.map((piece, index) => (
        <path
          key={index}
          d={pathOf(piece, route.corner, margin / 4)}
          vectorEffect="non-scaling-stroke"
        />
      ))}
      {route.arrow !== 'none' ? (
        <Arrow pieces={route.pieces} reverse={route.arrow === 'reverse'} />
      ) : null}
    </g>
  );
}

function Arrow({
  pieces,
  reverse,
}: {
  pieces: readonly (readonly Point[])[];
  reverse: boolean;
}): React.ReactNode {
  // On the longest segment, at its middle, pointing with the flow.
  let best: { a: Point; b: Point; length: number } | null = null;
  for (const piece of pieces) {
    for (let i = 1; i < piece.length; i++) {
      const a = piece[i - 1]!;
      const b = piece[i]!;
      const length = distance(a, b);
      if (best === null || length > best.length) {
        best = { a, b, length };
      }
    }
  }
  if (best === null || best.length < arrowLength * 2) {
    return null;
  }
  const from = reverse ? best.b : best.a;
  const to = reverse ? best.a : best.b;
  const mid = { x: (from.x + to.x) / 2, y: (from.y + to.y) / 2 };
  const tip = towards(mid, to, arrowLength / 2);
  const back = towards(mid, from, arrowLength / 2);
  const dx = (to.x - from.x) / best.length;
  const dy = (to.y - from.y) / best.length;
  const left = { x: back.x - dy * arrowHalfWidth, y: back.y + dx * arrowHalfWidth };
  const right = { x: back.x + dy * arrowHalfWidth, y: back.y - dx * arrowHalfWidth };
  return (
    <polygon
      className="scene__arrow"
      points={`${f(tip.x)},${f(tip.y)} ${f(left.x)},${f(left.y)} ${f(right.x)},${f(right.y)}`}
      stroke="none"
    />
  );
}

/** A route's gradient runs from its first point to its last, in world units: the straight line between the ends of an orthogonal run, which is the two-point interpolation `57` says it is. */
function gradientAlong(pieces: readonly (readonly Point[])[]): { from: Point; to: Point } | null {
  const first = pieces[0]?.[0];
  const lastPiece = pieces[pieces.length - 1];
  const last = lastPiece?.[lastPiece.length - 1];
  if (first === undefined || last === undefined) {
    return null;
  }
  if (Math.abs(first.x - last.x) < 1e-9 && Math.abs(first.y - last.y) < 1e-9) {
    return null;
  }
  return { from: first, to: last };
}

/**
 * An exchanger's gradient runs from its inlet port to its outlet port, in symbol space, since the
 * gradient is referenced from inside the symbol's transform: world → symbol is the inverse of
 * `translate(c) rotate(-rot) scale(m 1)`.
 */
function gradientAcross(
  symbol: PreparedSymbol,
  _from: number,
  _to: number,
): { from: Point; to: Point } | null {
  const inlet =
    symbol.ports.find((p) => p.name === 'in') ?? symbol.ports.find((p) => p.name.startsWith('in'));
  const outlet =
    symbol.ports.find((p) => p.name === 'out') ??
    symbol.ports.find((p) => p.name.startsWith('out'));
  if (inlet === undefined || outlet === undefined) {
    return null;
  }
  const local = (p: Point): Point => {
    const dx = p.x - symbol.centre.x;
    const dy = p.y - symbol.centre.y;
    const angle = (symbol.rotation * Math.PI) / 180;
    const rx = dx * Math.cos(angle) - dy * Math.sin(angle);
    const ry = dx * Math.sin(angle) + dy * Math.cos(angle);
    return { x: symbol.mirrored ? -rx : rx, y: ry };
  };
  return { from: local(inlet.at), to: local(outlet.at) };
}
