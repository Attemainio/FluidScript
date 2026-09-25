import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import type { ModelContract } from '../../api/types.ts';
import { builtinThemes } from '../../design/builtin.ts';
import { mixOklab, parseHex, toCss } from '../../design/color.ts';
import { scaleTokens } from '../../design/tokens.ts';
import { f } from '../canvas/path.ts';
import { prepareScene } from '../canvas/scene.ts';
import { solvedGoldens } from '../../test/goldens.ts';
import { pngDimensions, rasterLimit } from './exportPng.ts';
import { defaultSvgOptions, exportSvg, renderExportSvg, type ExportMeta } from './exportSvg.tsx';
import { resolveCss } from './resolveCss.ts';

const goldensDir = fileURLToPath(new URL('./goldens/', import.meta.url));
const goldens = solvedGoldens();
const loop = goldens.find((g) => g.name === 'm2-cooling-loop')!.model;
const header = goldens.find((g) => g.name === 'm2-distribution-header')!.model;

/** A fixed moment and build, so the golden is the same file on every machine. */
const meta: ExportMeta = {
  documentName: 'plant_01',
  generatedAt: new Date('2026-09-19T00:00:00Z'),
  appVersion: '0.0.0-test',
};

function render(model: ModelContract, options = defaultSvgOptions): string {
  const result = renderExportSvg(model, prepareScene(model), meta, options);
  if (!result.ok) {
    throw new Error(result.reason);
  }
  return result.svg;
}

describe('the SVG export (59)', () => {
  it.each(goldens)('exports $name to the committed SVG byte for byte', ({ name, model }) => {
    // The export's golden: the canvas markup plus what a file needs. Regenerated in place on a
    // mismatch and failed once, like the scene goldens, so a change to the file is reviewed as a diff.
    const markup = `${render(model)}\n`;
    const file = `${goldensDir}${name}.svg`;
    mkdirSync(goldensDir, { recursive: true });
    const committed = existsSync(file) ? readFileSync(file, 'utf8') : null;
    if (committed !== markup) {
      writeFileSync(file, markup);
    }
    expect(committed, `${name}.svg did not match and has been regenerated; review and rerun`).toBe(
      markup,
    );
  });

  it('is standalone: no token, no color-mix, no external reference, and a generic font at the end of the stack (invariant 1, D-118)', () => {
    for (const { model } of goldens) {
      const svg = render(model);
      expect(svg).not.toContain('var(--');
      expect(svg).not.toContain('color-mix(');
      expect(svg).not.toMatch(/url\((?!#)/);
      expect(svg).not.toContain('<script');
      expect(svg).not.toContain('@import');
      expect(svg).toContain('font-family');
      expect(svg).not.toContain('@font-face');
    }
    expect(scaleTokens['--font-ui']).toMatch(/,\s*sans-serif$/);
    expect(scaleTokens['--font-mono']).toMatch(/,\s*monospace$/);
  });

  it('carries the same elements as the canvas: every id, every label, gradients and the legend (invariant 2, criteria)', () => {
    const svg = render(header);
    // Ids are the element keys and tags the drawn text (D-34, invariant 5a).
    expect(svg).toContain('data-id="PU_AHU"');
    expect(svg).toContain('data-id="PU_RAD"');
    expect(svg).toContain('>101PU01</text>');
    expect(svg).toContain('>102PU01</text>');
    expect(svg).not.toContain('data-id="101PU01"');
    // The exchanger's gradient and the pipes' gradients survive, with literal stops.
    const loopSvg = render(loop);
    expect(loopSvg).toContain('id="export-symbol-HE1"');
    expect(loopSvg).toMatch(/stop-color:#[0-9a-f]{6}/);
    expect(loopSvg).toContain('class="export__legend"');
    expect(loopSvg).toContain('Temperature · °C');
    expect(loopSvg).toContain('>60</text>');
    // The value under a symbol is the wire's number with its unit, never a colour read back.
    expect(loopSvg).toContain('>50.02 °C</text>');
    expect(loopSvg).toContain('>6 °C</text>');
  });

  it('writes the provenance the reader needs into <desc>, and names the file in <title>', () => {
    const svg = render(loop);
    const desc = /<desc id="export-desc">([\s\S]*?)<\/desc>/.exec(svg)![1]!;
    expect(desc).toContain('application: 0.0.0-test');
    expect(desc).toContain('model contract: 2.3');
    expect(desc).toContain('language major: 1');
    expect(desc).toContain(`source hash: ${loop.provenance.sourceHash}`);
    expect(desc).toContain('catalogue: steel_en10255 2026.1');
    expect(desc).toContain('property backend: sharp-prop');
    expect(desc).toContain('atmosphere: 101.325 kPa absolute');
    // The count is the solver's, not the export's: it moves whenever the seed does (S-47).
    expect(desc).toContain(`status: solved in ${loop.solve!.iterations} iterations`);
    expect(desc).toContain('shown: temperature (°C), 0 to 60');
    expect(desc).toContain('tags: equipment tags are as of the source hash above');
    expect(desc).toContain('generated: 2026-09-19T00:00:00.000Z');
    expect(desc).not.toContain('fluidscript 1'); // never the source text
    expect(svg).toContain('<title id="export-title">coolingLoop — FluidScript diagram</title>');
    expect(svg).toMatch(/<svg [^>]*role="img" aria-labelledby="export-title export-desc"/);
  });

  it('keeps the scene geometry exactly: every symbol centre, port and route point is the prepared value (invariant 8)', () => {
    for (const { model } of goldens) {
      const scene = prepareScene(model);
      const svg = render(model);
      for (const symbol of scene.symbols) {
        expect(svg).toContain(`translate(${symbol.centre.x} ${symbol.centre.y})`);
        for (const port of symbol.ports) {
          expect(svg).toContain(`cx="${port.at.x}" cy="${port.at.y}"`);
        }
      }
      for (const route of scene.routes) {
        const first = route.pieces[0]![0]!;
        expect(svg).toContain(`M${f(first.x)} ${f(first.y)}`);
      }
    }
  });

  it('draws light on white whatever the app shows, and dark only when asked', () => {
    const light = render(loop);
    const dark = render(loop, { ...defaultSvgOptions, theme: 'dark' });
    expect(light).toContain(`fill: ${builtinThemes.light['--canvas-bg'].toLowerCase()}`);
    expect(dark).toContain(`fill: ${builtinThemes.dark['--canvas-bg'].toLowerCase()}`);
    expect(light).toContain('data-theme="light"');
    expect(dark).toContain('data-theme="dark"');
  });

  it('honours the options: no legend, no values, axes, no background', () => {
    const bare = render(loop, {
      theme: 'light',
      includeLegend: false,
      includeValues: false,
      includeAxes: true,
      transparentBackground: true,
    });
    expect(bare).not.toContain('class="export__legend"');
    expect(bare).not.toContain('class="export__value"');
    expect(bare).toContain('class="canvas-axes"');
    expect(bare).not.toContain('class="export__background"');
    const full = render(loop);
    expect(full).not.toContain('class="canvas-axes"');
    // The legend adds a band under the drawing rather than covering it.
    const heightOf = (svg: string): number => Number(/height="(\d+)"/.exec(svg)![1]);
    expect(heightOf(full)).toBeGreaterThan(heightOf(bare));
  });

  it('labels every value "not solved" on an unsolved model rather than a zero (invariant 4)', () => {
    const unsolved: ModelContract = {
      ...loop,
      solve: { ...loop.solve!, converged: false },
      layout: {
        ...loop.layout,
        placements: loop.layout.placements.map((p) => ({ ...p, scale: null, scales: {} })),
      },
    };
    const svg = render(unsolved);
    expect(svg).toContain('>not solved</text>');
    expect(svg).not.toContain('>0 °C</text>');
    const desc = /<desc id="export-desc">([\s\S]*?)<\/desc>/.exec(svg)![1]!;
    expect(desc).toContain('status: not solved');
  });

  it('refuses a scene whose symbol id does not resolve, rather than drawing a placeholder (error cases)', () => {
    const odd: ModelContract = {
      ...loop,
      layout: {
        ...loop.layout,
        placements: loop.layout.placements.map((p) =>
          p.componentId === 'PU1' ? { ...p, symbolId: 'widget.future' } : p,
        ),
      },
    };
    const result = renderExportSvg(odd, prepareScene(odd), meta);
    expect(result.ok).toBe(false);
    expect(result.ok ? '' : result.reason).toContain('PU1 (widget.future)');
    expect(exportSvg(odd, prepareScene(odd), meta).ok).toBe(false);
  });

  it('is offered as an SVG blob', () => {
    const result = exportSvg(loop, prepareScene(loop), meta);
    expect(result.ok && result.blob.type).toBe('image/svg+xml;charset=utf-8');
  });
});

describe('the colour resolution', () => {
  it('blends color-mix in Oklab to the colour the browser shows, and values every token', () => {
    const cold = parseHex(builtinThemes.light['--fluid-cold'])!;
    const cool = parseHex(builtinThemes.light['--fluid-cool'])!;
    const { text, unresolved } = resolveCss(
      'fill="color-mix(in oklab, var(--fluid-cold) 40%, var(--fluid-cool))" stroke="var(--canvas-symbol)"',
      builtinThemes.light,
    );
    expect(unresolved).toEqual([]);
    expect(text).toBe(
      `fill="${toCss(mixOklab(cold, cool, 0.4))}" stroke="${builtinThemes.light['--canvas-symbol'].toLowerCase()}"`,
    );
    // The mix sits between its ends and is neither of them.
    const mixed = parseHex(toCss(mixOklab(cold, cool, 0.4)))!;
    expect(mixed.r).toBeGreaterThan(Math.min(cold.r, cool.r) - 1);
    expect(mixed.r).toBeLessThan(Math.max(cold.r, cool.r) + 1);
    expect(toCss(mixOklab(cold, cool, 1))).toBe(toCss(cold));
    expect(toCss(mixOklab(cold, cool, 0))).toBe(toCss(cool));
  });

  it('writes a translucent token as rgba and reports a token nothing values', () => {
    const { text, unresolved } = resolveCss(
      'stroke: var(--canvas-symbol-inferred); fill: var(--no-such-token);',
      builtinThemes.light,
    );
    expect(text).toContain('stroke: rgba(43, 58, 71, 0.4)');
    expect(text).toContain('var(--no-such-token)');
    expect(unresolved).toEqual(['--no-such-token']);
  });
});

describe('the PNG dimensions', () => {
  it('scales by dpi and refuses over the raster limit with the exact size', () => {
    expect(pngDimensions(800, 600, 96)).toEqual({ ok: true, width: 800, height: 600 });
    expect(pngDimensions(800, 600, 300)).toEqual({ ok: true, width: 2500, height: 1875 });
    const refused = pngDimensions(6000, 6000, 300);
    expect(refused.ok).toBe(false);
    expect(refused.ok ? '' : refused.reason).toContain('18750 × 18750 px at 300 dpi');
    expect(refused.ok ? '' : refused.reason).toContain(String(rasterLimit.side));
    const wide = pngDimensions(rasterLimit.side + 1, 10, 96);
    expect(wide.ok).toBe(false);
  });
});
