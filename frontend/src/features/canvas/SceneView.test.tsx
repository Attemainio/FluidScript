import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import { solvedGoldens } from '../../test/goldens.ts';
import { detailFor, type Detail } from './detail.ts';
import { pathOf } from './path.ts';
import { prepareScene, type PreparedScene } from './scene.ts';
import { SceneView } from './SceneView.tsx';

const goldensDir = fileURLToPath(new URL('./goldens/', import.meta.url));

/** Rendered inside an `<svg>`, as the pane and the export do; outside one React hoists every `<title>` to the head. */
function render(scene: PreparedScene, detail: Detail): string {
  return renderToStaticMarkup(
    <svg xmlns="http://www.w3.org/2000/svg">
      <SceneView scene={scene} detail={detail} />
    </svg>,
  );
}

describe('the scene view', () => {
  it.each(solvedGoldens())(
    'renders $name to the committed SVG byte for byte (53 invariant 1)',
    ({ name, model }) => {
      // The renderer's golden: the same scene is the same markup on every machine, and the
      // markup is what 59 exports. Regenerated in place on a mismatch and failed once, like every
      // other gate, so a change to the drawing is reviewed as a diff.
      const scene = prepareScene(model);
      const markup = `${render(scene, 'all')}\n`;
      const file = `${goldensDir}${name}.svg`;
      mkdirSync(goldensDir, { recursive: true });
      const committed = existsSync(file) ? readFileSync(file, 'utf8') : null;
      if (committed !== markup) {
        writeFileSync(file, markup);
      }
      expect(
        committed,
        `${name}.svg did not match and has been regenerated; review and rerun`,
      ).toBe(markup);
    },
  );

  it('keys every element by id and never by tag, and labels by tag (D-34)', () => {
    const header = solvedGoldens().find((g) => g.name === 'm2-distribution-header')!.model;
    const markup = render(prepareScene(header), 'names');
    expect(markup).toContain('data-id="PU_AHU"');
    expect(markup).toContain('data-id="PU_RAD"');
    expect(markup).not.toContain('data-id="101PU01"');
    expect(markup).toContain('>101PU01</text>');
    expect(markup).toContain('>102PU01</text>');
    expect(markup).toContain('data-owner="PU_AHU"');
  });

  it('places every symbol and label at a coordinate the layout gave (53 invariant 2)', () => {
    for (const { model } of solvedGoldens()) {
      const scene = prepareScene(model);
      const markup = render(scene, 'all');
      const translates = [...markup.matchAll(/translate\(([-\d.]+) ([-\d.]+)\)/g)].map((m) => [
        Number(m[1]),
        Number(m[2]),
      ]);
      const allowed = new Set(
        model.layout.placements.flatMap((p) => [
          `${p.inner[0]! + p.inner[2]! / 2},${p.inner[1]! + p.inner[3]! / 2}`,
          `${p.labelAt[0]},${p.labelAt[1]}`,
        ]),
      );
      expect(translates.length).toBeGreaterThan(0);
      for (const [x, y] of translates) {
        expect(allowed.has(`${x},${y}`)).toBe(true);
      }
    }
  });

  it('shows and hides by level of detail: no labels below 0.5×, inline names above 3×', () => {
    const loop = solvedGoldens().find((g) => g.name === 'm2-cooling-loop')!.model;
    const scene = prepareScene(loop);
    const at = (zoom: number): string => render(scene, detailFor(zoom));
    expect(at(0.3)).not.toContain('<text');
    expect(at(1)).toContain('>100PU01</text>');
    expect(at(1)).not.toContain('>PU1__HE1</text>');
    expect(at(1)).not.toContain('class="scene__port"');
    expect(at(2)).toContain('class="scene__port"');
    expect(at(4)).toContain('>PU1__HE1</text>');
  });

  it('changes only the arrows when the solved flow reverses (53 invariant 7)', () => {
    const loop = solvedGoldens().find((g) => g.name === 'm2-cooling-loop')!.model;
    const reversed = {
      ...loop,
      layout: {
        ...loop.layout,
        flow: Object.fromEntries(Object.keys(loop.layout.flow).map((id) => [id, 'reverse'])),
      },
    };
    const strip = (markup: string): string =>
      markup.replace(/<polygon class="scene__arrow"[^>]*><\/polygon>/g, '');
    const forward = render(prepareScene(loop), 'names');
    const backward = render(prepareScene(reversed), 'names');
    expect(forward).not.toBe(backward);
    expect(strip(forward)).toBe(strip(backward));
  });

  it('draws an unsolved model red: the class on the scene and no state fill', () => {
    const loop = solvedGoldens().find((g) => g.name === 'm2-cooling-loop')!.model;
    const refused = { ...loop, solve: null };
    const diverged = { ...loop, solve: { ...loop.solve!, converged: false } };
    for (const model of [refused, diverged]) {
      const markup = render(prepareScene(model), 'names');
      expect(markup).toContain('class="scene scene--unsolved"');
      expect(markup).not.toContain('color-mix');
    }
    expect(render(prepareScene(loop), 'names')).toContain('class="scene"');
    expect(render(prepareScene(loop), 'names')).toContain('color-mix');
  });

  it('draws an unknown symbol as a labelled rectangle', () => {
    const loop = solvedGoldens().find((g) => g.name === 'm2-cooling-loop')!.model;
    const odd = {
      ...loop,
      layout: {
        ...loop.layout,
        placements: loop.layout.placements.map((p) =>
          p.componentId === 'PU1' ? { ...p, symbolId: 'widget.future' } : p,
        ),
      },
    };
    const markup = render(prepareScene(odd), 'names');
    expect(markup).toContain('class="scene__unknown"');
    expect(markup).toContain('>100PU01</text>');
  });
});

describe('a route path', () => {
  const corner = [
    { x: 0, y: 0 },
    { x: 2, y: 0 },
    { x: 2, y: 2 },
  ];

  it('is straight segments for a sharp corner', () => {
    expect(pathOf(corner, 'sharp', 0.125)).toBe('M0 0 L2 0 L2 2');
  });

  it('rounds a fillet corner by a quarter margin, never more than half a segment', () => {
    expect(pathOf(corner, 'fillet', 0.125)).toBe('M0 0 L1.875 0 Q2 0 2 0.125 L2 2');
    const tight = [
      { x: 0, y: 0 },
      { x: 0.1, y: 0 },
      { x: 0.1, y: 2 },
    ];
    expect(pathOf(tight, 'fillet', 0.125)).toBe('M0 0 L0.05 0 Q0.1 0 0.1 0.05 L0.1 2');
  });
});
