import { renderToStaticMarkup } from 'react-dom/server';

import type { ModelContract, Quantity } from '../../api/types.ts';
import { builtinThemes } from '../../design/builtin.ts';
import { mixOklab, parseHex, toCss } from '../../design/color.ts';
import { worldUnitPx, type ThemeColors } from '../../design/tokens.ts';
import { Axes } from '../canvas/Axes.tsx';
import { legendNote, legendTitle, ticksOf } from '../canvas/legend.ts';
import type { PreparedScene, PreparedSymbol } from '../canvas/scene.ts';
import sceneCss from '../canvas/scene.css?raw';
import { SceneView } from '../canvas/SceneView.tsx';
import { formatValue } from '../hover/card.ts';
import { resolveCss } from './resolveCss.ts';

/** The export's theme: light by default so the file reads on paper whatever the app shows (`59`). */
export type ExportTheme = 'light' | 'dark';

/** `59`'s SVG options. The `text` option (embed a font, or outline it) is not here: `D-118`. */
export interface SvgExportOptions {
  readonly theme: ExportTheme;
  readonly includeLegend: boolean;
  readonly includeValues: boolean;
  readonly includeAxes: boolean;
  /** Omit the background rectangle: the PNG path's transparent option, and an SVG someone will place on their own ground. */
  readonly transparentBackground?: boolean;
}

/** What the `<desc>` records beyond the model (`59`): the document's name, when, and which build. */
export interface ExportMeta {
  readonly documentName: string;
  readonly generatedAt: Date;
  readonly appVersion: string;
}

/** The export as text with its pixel size, or the reason it was refused (`59` error cases). */
export type SvgExport =
  | {
      readonly ok: true;
      readonly svg: string;
      readonly width: number;
      readonly height: number;
      /** Non-blocking findings: a token the theme did not value, say. */
      readonly warnings: readonly string[];
    }
  | { readonly ok: false; readonly reason: string };

export const defaultSvgOptions: SvgExportOptions = {
  theme: 'light',
  includeLegend: true,
  includeValues: true,
  includeAxes: false,
};

/** `59`: a tight viewBox plus this share of the drawing on each side. */
const marginShare = 0.05;
/** The legend band under the drawing, pixels: title, ramp, ticks. */
const legendHeight = 64;
const legendRampWidth = 220;
const legendPad = 16;
/** A value label sits this far under its symbol's box, world units. */
const valueOffset = 0.22;

/**
 * Serializes a prepared scene as a standalone SVG (`59`). The drawing is the same `SceneView` the
 * canvas mounts, rendered to markup and given what a file needs and the app supplies at runtime:
 * the stylesheet with every token resolved to the chosen theme's colour, the font stack as text,
 * a background, a title and a provenance description, and optionally the legend, the axes and a
 * value under each symbol. Geometry is the scene's, untouched (`59` invariant 8).
 */
export function renderExportSvg(
  model: ModelContract,
  scene: PreparedScene,
  meta: ExportMeta,
  options: SvgExportOptions = defaultSvgOptions,
): SvgExport {
  // A symbol id the wire's table does not hold: the canvas draws a labelled box, the file must not (59 error cases).
  const unresolved = scene.symbols.filter((symbol) => symbol.primitives === null);
  if (unresolved.length > 0) {
    return {
      ok: false,
      reason: `Cannot export: no symbol for ${unresolved.map((s) => `${s.id} (${s.symbolId})`).join(', ')}.`,
    };
  }

  const colors: ThemeColors = builtinThemes[options.theme];
  const warnings: string[] = [];
  const scale = worldUnitPx;
  const marginX = scene.bounds.width * marginShare;
  const marginY = scene.bounds.height * marginShare;
  const width = round((scene.bounds.width + 2 * marginX) * scale);
  const drawingHeight = round((scene.bounds.height + 2 * marginY) * scale);
  const withLegend = options.includeLegend && scene.scale !== null;
  const height = drawingHeight + (withLegend ? legendHeight : 0);
  // World → pixel: the same map the canvas's root transform applies, y flipped.
  const originX = -(scene.bounds.x - marginX) * scale;
  const originY = (scene.bounds.y + scene.bounds.height + marginY) * scale;
  const px = (x: number): number => originX + x * scale;
  const py = (y: number): number => originY - y * scale;

  // Rendered inside an <svg> so React treats the tree as SVG, then unwrapped: the root written
  // below carries the title and description React would hoist out of an svg it owned.
  const drawing = renderToStaticMarkup(
    <svg>
      <g transform={`translate(${f(originX)} ${f(originY)}) scale(${scale} ${-scale})`}>
        {options.includeAxes ? <Axes /> : null}
        <SceneView scene={scene} detail="values" idPrefix="export" />
      </g>
    </svg>,
  )
    .replace(/^<svg[^>]*>/, '')
    .replace(/<\/svg>$/, '');
  const values = options.includeValues ? valueLabels(model, scene, px, py) : '';
  const legend = withLegend ? legendMarkup(scene, colors, drawingHeight, width) : '';

  const style = resolveCss(
    `${worldStrokes(sceneCss, scale)}\n${exportCss}`.replace(/\/\*[\s\S]*?\*\//g, ''),
    colors,
  );
  const body = resolveCss(
    `${literalArrows(worldStrokes(drawing, scale))}${values}${legend}`,
    colors,
  );
  for (const token of [...style.unresolved, ...body.unresolved]) {
    warnings.push(`The theme values no ${token}; that colour is left as written.`);
  }

  const circuits = model.circuits.map((c) => c.name).join(', ');
  const title = `${circuits.length > 0 ? circuits : meta.documentName} — FluidScript diagram`;
  const background =
    options.transparentBackground === true
      ? ''
      : `<rect class="export__background" width="${width}" height="${height}"/>`;

  const svg = [
    `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" role="img" aria-labelledby="export-title export-desc" data-theme="${options.theme}">`,
    `<title id="export-title">${escape(title)}</title>`,
    `<desc id="export-desc">${escape(description(model, scene, meta))}</desc>`,
    `<style>${style.text.trim()}</style>`,
    background,
    body.text,
    `</svg>`,
  ].join('\n');

  return { ok: true, svg, width, height, warnings: [...new Set(warnings)] };
}

/** `59`'s `exportSvg`: the file as a Blob, or the refusal. */
export function exportSvg(
  model: ModelContract,
  scene: PreparedScene,
  meta: ExportMeta,
  options: SvgExportOptions = defaultSvgOptions,
):
  | { readonly ok: true; readonly blob: Blob; readonly warnings: readonly string[] }
  | { ok: false; reason: string } {
  const result = renderExportSvg(model, scene, meta, options);
  return result.ok
    ? {
        ok: true,
        blob: new Blob([result.svg], { type: 'image/svg+xml;charset=utf-8' }),
        warnings: result.warnings,
      }
    : result;
}

/** The rules the file needs beyond the scene's own sheet: the ground, the type, the legend and the values. */
const exportCss = `
svg { font-family: var(--font-ui); }
.export__background { fill: var(--canvas-bg); }
.export__value { font-family: var(--font-mono); font-size: 11px; fill: var(--text-primary); text-anchor: middle; }
.export__value--unsolved { fill: var(--text-muted); font-style: italic; }
.export__legend-title { font-size: 12px; font-weight: 600; fill: var(--text-primary); }
.export__legend-tick { font-family: var(--font-mono); font-size: 10px; fill: var(--text-secondary); text-anchor: middle; }
.export__legend-note { font-size: 11px; fill: var(--text-muted); }
.export__legend-frame { fill: none; stroke: var(--border-subtle); }
.scene__route .scene__arrow { fill: var(--canvas-route); }
.scene__route--signal .scene__arrow { fill: var(--text-muted); }
`;

/**
 * The provenance `<desc>` (`59`): everything a reader needs to know which build, which source and
 * which scale drew this, one `key: value` per line. Tags are recorded as of the source hash.
 */
function description(model: ModelContract, scene: PreparedScene, meta: ExportMeta): string {
  const p = model.provenance;
  const solve = model.solve;
  const shown =
    scene.scale === null
      ? 'none'
      : scene.scale.domain === null
        ? `${scene.property} (${scene.scale.unit}), no solved values`
        : `${scene.property} (${scene.scale.unit}), ${formatValue(scene.scale.domain.min)} to ${formatValue(scene.scale.domain.max)}`;
  const status = !scene.solved
    ? 'not solved'
    : `solved${solve !== null && solve !== undefined ? ` in ${solve.iterations} iterations` : ''}`;
  return [
    'FluidScript diagram',
    `document: ${meta.documentName}`,
    `application: ${meta.appVersion}`,
    `model contract: ${model.contractVersion}`,
    `language major: ${p.languageMajor}`,
    `source hash: ${p.sourceHash}`,
    `catalogue: ${p.catalog.id} ${p.catalog.version}`,
    `property backend: ${p.propertyBackend.id} ${p.propertyBackend.version}`,
    `atmosphere: ${p.atmosphereKPaAbsolute} kPa absolute`,
    `status: ${status}`,
    `shown: ${shown}`,
    'tags: equipment tags are as of the source hash above; an insertion above a component renumbers it (D-34)',
    `generated: ${meta.generatedAt.toISOString()}`,
  ].join('\n');
}

/** A value under each symbol: the shown property at the component, or "not solved" (`59` error cases). */
function valueLabels(
  model: ModelContract,
  scene: PreparedScene,
  px: (x: number) => number,
  py: (y: number) => number,
): string {
  const components = new Map(model.components.map((c) => [c.id, c]));
  const labels = scene.symbols
    .map((symbol) => {
      const x = px(symbol.centre.x);
      const y = py(symbol.inner.y - valueOffset) + 4;
      if (!scene.solved) {
        return `<text class="export__value export__value--unsolved" x="${f(x)}" y="${f(y)}">not solved</text>`;
      }
      const quantity = valueOf(components.get(symbol.id)?.state ?? null, symbol, scene.property);
      if (quantity === null) {
        return '';
      }
      const text = `${formatValue(quantity.value)}${quantity.unit.length > 0 ? ` ${quantity.unit}` : ''}`;
      return `<text class="export__value" x="${f(x)}" y="${f(y)}">${escape(text)}</text>`;
    })
    .filter((label) => label.length > 0);
  return labels.length === 0 ? '' : `<g class="export__values">${labels.join('')}</g>`;
}

/**
 * The state field a property reads at a component, as `57` has it: a node its one state, anything
 * else its outlet. Enthalpy and density are positions on the wire but not values, so they draw no
 * label rather than a number reconstructed from a colour (`59` invariant 3).
 */
function valueOf(
  state: ModelContract['components'][number]['state'],
  symbol: PreparedSymbol,
  property: string,
): Quantity | null {
  if (state === null || state === undefined) {
    return null;
  }
  const node = symbol.kind === 'node';
  const field: keyof NonNullable<typeof state> | null =
    property === 'temperature'
      ? node
        ? 't'
        : 'tOut'
      : property === 'pressure'
        ? node
          ? 'p'
          : 'pOut'
        : property === 'flow'
          ? 'flow'
          : property === 'pressure_drop'
            ? 'dp'
            : null;
  if (field === null) {
    return null;
  }
  const quantity = state[field];
  return quantity !== null &&
    quantity !== undefined &&
    typeof quantity === 'object' &&
    'value' in quantity &&
    typeof quantity.value === 'number'
    ? (quantity as Quantity)
    : null;
}

/** The legend under the drawing: title, the ramp in Oklab steps, the 1-2-5 ticks (`57`, `59`). */
function legendMarkup(
  scene: PreparedScene,
  colors: ThemeColors,
  top: number,
  width: number,
): string {
  const scale = scene.scale!;
  const rampWidth = Math.min(legendRampWidth, width - 2 * legendPad);
  const x = legendPad;
  const titleY = top + 18;
  const rampY = top + 26;
  const rampHeight = 10;
  const note = legendNote(scale);
  const stops = rampStops(colors);
  const ticks =
    scale.domain === null || scale.degenerate
      ? ''
      : ticksOf(scale.domain.min, scale.domain.max)
          .map(
            (tick) =>
              `<text class="export__legend-tick" x="${f(x + tick.position * rampWidth)}" y="${rampY + rampHeight + 14}">${escape(formatValue(tick.value))}</text>`,
          )
          .join('');
  const ramp =
    note === null
      ? `<defs><linearGradient id="export-legend-ramp" x1="0" y1="0" x2="1" y2="0">${stops}</linearGradient></defs>` +
        `<rect x="${x}" y="${rampY}" width="${f(rampWidth)}" height="${rampHeight}" fill="url(#export-legend-ramp)"/>` +
        `<rect class="export__legend-frame" x="${x}" y="${rampY}" width="${f(rampWidth)}" height="${rampHeight}"/>`
      : `<text class="export__legend-note" x="${x}" y="${rampY + 10}">${escape(note)}</text>`;
  return (
    `<g class="export__legend" role="group" aria-label="${escape(legendTitle(scale))}">` +
    `<text class="export__legend-title" x="${x}" y="${titleY}">${escape(legendTitle(scale))}</text>` +
    ramp +
    ticks +
    `</g>`
  );
}

/** The ramp's stops as literal colours: the five tokens with Oklab blends between, since an SVG gradient blends in sRGB. */
function rampStops(colors: ThemeColors): string {
  const tokens = ['--fluid-cold', '--fluid-cool', '--fluid-neutral', '--fluid-warm', '--fluid-hot'];
  const parsed = tokens.map(
    (token) => parseHex(colors[token as keyof ThemeColors]) ?? { r: 0, g: 0, b: 0, a: 1 },
  );
  const stops: string[] = [];
  const steps = 4;
  for (let segment = 0; segment < parsed.length - 1; segment++) {
    for (let step = 0; step < steps; step++) {
      const share = step / steps;
      const offset = (segment + share) / (parsed.length - 1);
      const color = mixOklab(parsed[segment + 1]!, parsed[segment]!, share);
      stops.push(`<stop offset="${f(offset)}" stop-color="${toCss(color)}"/>`);
    }
  }
  stops.push(`<stop offset="1" stop-color="${toCss(parsed[parsed.length - 1]!)}"/>`);
  return stops.join('');
}

/**
 * The canvas keeps a stroke's pixel width at any zoom with `vector-effect: non-scaling-stroke`, which
 * a browser and Inkscape honour and many converters (Office, cairo) do not: they draw a 2 px line
 * as 2 world units, a blob. The file has one scale, so every width and dash is written in world
 * units instead and the effect dropped -- the same picture, with nothing left to the viewer.
 */
function worldStrokes(markup: string, scale: number): string {
  const world = (px: string): string => f(Number(px) / scale);
  return markup
    .replace(/ vector-effect="non-scaling-stroke"/g, '')
    .replace(/stroke-width="([\d.]+)"/g, (_, px: string) => `stroke-width="${world(px)}"`)
    .replace(/stroke-width:\s*([\d.]+)/g, (_, px: string) => `stroke-width: ${world(px)}`)
    .replace(
      /stroke-dasharray="([\d. ]+)"/g,
      (_, dashes: string) =>
        `stroke-dasharray="${dashes.trim().split(/\s+/).map(world).join(' ')}"`,
    )
    .replace(
      /stroke-dasharray:\s*([\d. ]+)/g,
      (_, dashes: string) => `stroke-dasharray: ${dashes.trim().split(/\s+/).map(world).join(' ')}`,
    );
}

/**
 * An arrow takes its route's colour through `currentColor`, which a converter without CSS
 * inheritance loses. A route with a stated colour gets its arrow's fill written out; the rest are
 * named by class in the export's own rules.
 */
function literalArrows(markup: string): string {
  return markup.replace(
    /(<g data-id="[^"]*" class="scene__route[^"]*"[^>]*style="[^"]*color:(#[0-9a-fA-F]{3,8}|[a-zA-Z]+)[;"][^>]*>)([\s\S]*?)(<\/g>)/g,
    (whole: string, open: string, color: string, inner: string, close: string) =>
      color === 'currentColor'
        ? whole
        : `${open}${inner.replace(/<polygon class="scene__arrow" /g, `<polygon class="scene__arrow" style="fill:${color}" `)}${close}`,
  );
}

function round(value: number): number {
  return Math.ceil(value);
}

function f(value: number): string {
  return String(Number(value.toFixed(4)));
}

function escape(text: string): string {
  return text
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}
