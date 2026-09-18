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
}: {
  scene: PreparedScene;
  detail: Detail;
  /** The selected component ids (`54`), drawn in the selection colour. */
  selected?: ReadonlySet<string>;
}): React.ReactNode {
  return (
    <g
      className={scene.solved ? 'scene' : 'scene scene--unsolved'}
      fontFamily={`var(${typeMetrics.canvasLabel.family})`}
    >
      <g className="scene__routes">
        {scene.routes.map((route) => (
          <RouteView key={route.id} route={route} margin={scene.margin} />
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
              <text
                key={label.ownerId}
                data-owner={label.ownerId}
                className={label.inferred ? 'scene__label scene__label--inferred' : 'scene__label'}
                transform={`translate(${label.at.x} ${label.at.y}) scale(1 -1)`}
                textAnchor="middle"
              >
                {label.text}
              </text>
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
}: {
  symbol: PreparedSymbol;
  detail: Detail;
  solved: boolean;
  selected: boolean;
}): React.ReactNode {
  // Symbol space (y up) → world: mirror, then the clockwise quarter turn; the y flip is the root's.
  const transform = `translate(${symbol.centre.x} ${symbol.centre.y}) rotate(${-symbol.rotation}) scale(${symbol.mirrored ? -1 : 1} 1)`;
  const stateFill = symbol.scale === null || !solved ? 'var(--canvas-bg)' : fluidFill(symbol.scale);
  const className = [
    'scene__symbol',
    symbol.inferred ? 'scene__symbol--inferred' : '',
    selected ? 'scene__symbol--selected' : '',
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

function RouteView({ route, margin }: { route: PreparedRoute; margin: number }): React.ReactNode {
  const width = route.style?.strokeWidth ?? 2;
  const stroke = route.style?.stroke ?? undefined;
  const dashed = route.kind === 'signal' || route.style?.pattern === 'dashed';
  const className = `scene__route scene__route--${route.kind} scene__route--${route.layer}`;

  return (
    <g
      data-id={route.id}
      className={className}
      fill="none"
      style={stroke === undefined ? undefined : { stroke, color: stroke }}
      strokeWidth={width}
      strokeLinejoin={route.corner === 'sharp' ? 'miter' : 'round'}
      strokeLinecap="round"
      strokeDasharray={dashed ? '6 4' : undefined}
    >
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
