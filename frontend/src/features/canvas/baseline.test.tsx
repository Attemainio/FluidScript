import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import { solvedGoldens } from '../../test/goldens.ts';
import { prepareScene } from './scene.ts';
import { SceneView } from './SceneView.tsx';

/**
 * The M3 render baseline `D-45` asks for, measured where this environment can measure it: scene
 * preparation and static markup of the largest reference circuit, in Node. The browser-side
 * numbers (`07`: 50 fps p95 panning the 200-component fixture, 8 ms per commit) need a browser
 * and a fixture the frontend does not hold; `53`'s criterion stays marked unmeasured. The bound
 * here is loose on purpose: it catches a regression of an order of magnitude, not a slow CI box.
 */
describe('the render baseline (D-45, M3)', () => {
  it('prepares and renders the largest reference circuit well inside a frame budget', () => {
    const largest = solvedGoldens().sort(
      (a, b) => b.model.layout.placements.length - a.model.layout.placements.length,
    )[0]!;
    const scene = prepareScene(largest.model);
    // Warm once: the first render pays module and JIT costs that no frame does.
    renderToStaticMarkup(
      <svg>
        <SceneView scene={scene} detail="all" />
      </svg>,
    );
    const runs = 20;
    const started = performance.now();
    for (let i = 0; i < runs; i++) {
      renderToStaticMarkup(
        <svg>
          <SceneView scene={prepareScene(largest.model)} detail="all" />
        </svg>,
      );
    }
    const perRender = (performance.now() - started) / runs;
    console.info(
      `render baseline: ${largest.name}, ${largest.model.layout.placements.length} placements, ${largest.model.layout.routes.length} routes: ${perRender.toFixed(2)} ms per prepare+render (Node)`,
    );
    expect(perRender).toBeLessThan(100);
  });
});
