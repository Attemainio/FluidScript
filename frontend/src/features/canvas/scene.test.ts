import { describe, expect, it } from 'vitest';

import type { ModelContract } from '../../api/types.ts';
import { cutAtHops, prepareScene } from './scene.ts';
import { typeMetrics, worldUnitPx } from '../../design/tokens.ts';
import { solvedGoldens } from '../../test/goldens.ts';

const goldens = solvedGoldens();
const header = goldens.find((g) => g.name === 'm2-distribution-header')!.model;
const loop = goldens.find((g) => g.name === 'm2-cooling-loop')!.model;

describe('prepareScene', () => {
  it('draws every placement with a box as a symbol and every inline one as a mark, keyed by id', () => {
    for (const { model } of goldens) {
      const scene = prepareScene(model);
      const boxed = model.layout.placements.filter((p) => p.inner[2] !== 0 || p.inner[3] !== 0);
      expect(scene.symbols.map((s) => s.id)).toEqual(boxed.map((p) => p.componentId));
      expect(scene.symbols.length + scene.marks.length).toBe(model.layout.placements.length);
      expect(scene.routes.length).toBe(model.layout.routes.length);
      expect(scene.labels.length).toBe(model.layout.placements.length);
    }
  });

  it('reserves every label the box the layout gave, from a metric that is the canvas font (D-73, C-84)', () => {
    for (const { model } of goldens) {
      // The layout boxed the labels from its declared metric; the canvas draws them with this font. The two
      // must agree, or a label the layout kept clear overflows on screen.
      // The size leaves Core rounded to a micro world unit, which is 6e-5 px at 60 px per unit.
      expect(model.layout.labelMetric.size * worldUnitPx).toBeCloseTo(typeMetrics.canvasLabel.sizePx, 3);
      expect(model.layout.labelMetric.advance).toBe(typeMetrics.canvasLabel.advanceEm);

      const scene = prepareScene(model);
      for (const label of scene.labels) {
        const placement = model.layout.placements.find((p) => p.componentId === label.ownerId)!;
        expect([label.box.x, label.box.y, label.box.width, label.box.height]).toEqual(placement.labelBox);
        expect(label.clear).toBe(placement.labelClear);
        // Boxes cross the wire at four decimals.
        expect(label.box.height).toBeCloseTo(model.layout.labelMetric.size, 3);
        expect(label.box.width).toBeCloseTo(
          Math.max(1, label.text.length) * model.layout.labelMetric.advance * model.layout.labelMetric.size,
          3,
        );
      }
    }
  });

  it('takes every coordinate from the layout: centres, anchors, route points (53 invariant 2)', () => {
    for (const { model } of goldens) {
      const scene = prepareScene(model);
      for (const symbol of scene.symbols) {
        const placement = model.layout.placements.find((p) => p.componentId === symbol.id)!;
        expect(symbol.centre).toEqual({
          x: placement.inner[0]! + placement.inner[2]! / 2,
          y: placement.inner[1]! + placement.inner[3]! / 2,
        });
        for (const port of symbol.ports) {
          expect(placement.anchors[port.name]?.at).toEqual([port.at.x, port.at.y]);
        }
      }
      for (const route of scene.routes) {
        const wire = model.layout.routes.find((r) => r.id === route.id)!;
        const flat = route.pieces.flat().flatMap((p) => [p.x, p.y]);
        // No hops in the samples, so one piece equal to the polyline.
        expect(flat).toEqual(wire.points);
      }
    }
  });

  it('labels a component with its tag and keys it by its id (D-34)', () => {
    const scene = prepareScene(header);
    const pump = scene.labels.find((l) => l.ownerId === 'PU_AHU')!;
    expect(pump.text).toBe('101PU01');
    const other = scene.labels.find((l) => l.ownerId === 'PU_RAD')!;
    expect(other.text).toBe('102PU01');
    expect(scene.symbols.some((s) => s.id.includes('101PU01'))).toBe(false);
    // A node has no tag and keeps its id.
    expect(scene.labels.find((l) => l.ownerId === 'N1')!.text).toBe('N1');
  });

  it('marks inferred elements, boundary nodes and the arrows from the solved flow', () => {
    const scene = prepareScene(loop);
    expect(scene.marks.find((m) => m.id === 'PU1__HE1')).toMatchObject({
      kind: 'node',
      inferred: true,
      boundary: false,
    });
    expect(scene.marks.find((m) => m.id === '3WV__N3')).toMatchObject({
      kind: 'pipe',
      inferred: true,
    });
    expect(scene.symbols.find((s) => s.id === 'N1')).toMatchObject({
      inferred: false,
      junction: false,
    });
    expect(scene.symbols.find((s) => s.id === 'N2')!.junction).toBe(true);
    // One arrow per drawn run: PU1 - HE1 (c2, c3), HE1 - 3WV (c4, c5) and 3WV - N3 (c7, c8, c9)
    // each pass through inferred inline elements and carry one arrow, on the longest piece.
    const arrows = scene.routes.filter((r) => r.arrow !== 'none').map((r) => r.id);
    expect(arrows).toEqual(['c0', 'c1', 'c3', 'c4', 'c6', 'c7']);
    expect(scene.routes.filter((r) => r.arrow === 'none').map((r) => r.id)).toEqual([
      'c2',
      'c5',
      'c8',
      'c9',
    ]);
    expect(scene.routes.find((r) => r.id === 'c0')!.corner).toBe('fillet');
  });

  it('orders the routes from the back: signal, return, supply (28 C16)', () => {
    const layers = prepareScene(header).routes.map((r) => r.layer);
    const firstSupply = layers.indexOf('supply');
    expect(layers.slice(firstSupply).every((l) => l === 'supply')).toBe(true);
    expect(layers.slice(0, firstSupply).every((l) => l === 'return')).toBe(true);
  });

  it('carries the scale position, the sized marker and the worst diagnostic per component', () => {
    const withDiagnostics: ModelContract = {
      ...loop,
      diagnostics: [
        {
          code: 'FS1507',
          severity: 'warning',
          message: 'w',
          range: null,
          component: 'HE1',
          suggestion: null,
          related: [],
        },
        {
          code: 'FS2101',
          severity: 'error',
          message: 'e',
          range: null,
          component: 'HE1',
          suggestion: null,
          related: [],
        },
        {
          code: 'FS1510',
          severity: 'info',
          message: 'i',
          range: null,
          component: 'PU1',
          suggestion: null,
          related: [],
        },
      ],
    };
    const scene = prepareScene(withDiagnostics);
    const he1 = scene.symbols.find((s) => s.id === 'HE1')!;
    expect(he1.badge).toBe('error');
    expect(he1.scale).toBeCloseTo(0.8335, 3);
    expect(scene.symbols.find((s) => s.id === 'PU1')!.badge).toBeNull();
    // The pump states nothing, so every parameter of it was sized or defaulted; HE1 states power, in and out but not dp.
    expect(scene.symbols.find((s) => s.id === 'PU1')!.sized).toBe(true);
  });

  it('follows the property the reader switches to, from the wire, with no request (57 invariant 6, D-117)', () => {
    const byScript = prepareScene(loop);
    expect(byScript.property).toBe('temperature');
    expect(byScript.scale?.unit).toBe('°C');
    expect(byScript.available).toEqual(['temperature', 'pressure', 'flow']);

    const pressure = prepareScene(loop, 'pressure');
    expect(pressure.property).toBe('pressure');
    expect(pressure.scale?.displayName).toBe('Pressure');
    const pump = pressure.symbols.find((s) => s.id === 'PU1')!;
    expect(pump.scale).toBeCloseTo(0.8865, 3);
    // The pump's gradient runs from its suction to its discharge; the route into it ends at the suction's value.
    expect(pump.scaleFrom).toBeCloseTo(0.4, 3);
    expect(pump.scaleTo).toBeCloseTo(0.8865, 3);
    expect(pressure.routes.find((r) => r.id === 'c1')!.scaleTo).toBeCloseTo(0.4, 3);

    // A property the wire does not carry falls back to the script's, never to an empty scale.
    expect(prepareScene(loop, 'viscosity').property).toBe('temperature');
  });

  it('gives an exchanger its inlet and outlet positions, and a node none (57 Components)', () => {
    const scene = prepareScene(loop);
    const he1 = scene.symbols.find((s) => s.id === 'HE1')!;
    expect(he1.scaleFrom).toBeCloseTo(0.3333, 3);
    expect(he1.scaleTo).toBeCloseTo(0.8335, 3);
    const n3 = scene.symbols.find((s) => s.id === 'N3');
    const node = n3 ?? scene.symbols.find((s) => s.kind === 'node')!;
    expect(node.scaleFrom).toBeNull();
    expect(node.scaleTo).toBeNull();
  });

  it('draws an unknown symbol id as a labelled rectangle rather than failing (53 error cases)', () => {
    const odd: ModelContract = {
      ...loop,
      layout: {
        ...loop.layout,
        placements: loop.layout.placements.map((p) =>
          p.componentId === 'PU1' ? { ...p, symbolId: 'widget.future' } : p,
        ),
      },
    };
    const scene = prepareScene(odd);
    expect(scene.symbols.find((s) => s.id === 'PU1')!.primitives).toBeNull();
    expect(scene.labels.find((l) => l.ownerId === 'PU1')!.text).toBe('100PU01');
  });

  it('prepares an unsolved model as topology with no state (53 invariant 4)', () => {
    const unsolved: ModelContract = {
      ...loop,
      layout: {
        ...loop.layout,
        placements: loop.layout.placements.map((p) => ({ ...p, scale: null, scales: {} })),
      },
    };
    expect(prepareScene(unsolved).symbols.every((s) => s.scale === null)).toBe(true);
  });
});

describe('cutAtHops', () => {
  it('breaks a route a quarter margin either side of each hop it owns, in order along the segment', () => {
    const points = [
      { x: 0, y: 0 },
      { x: 4, y: 0 },
      { x: 4, y: 2 },
    ];
    const pieces = cutAtHops(
      points,
      [
        { x: 3, y: 0 },
        { x: 1, y: 0 },
        { x: 4, y: 1 },
      ],
      0.125,
    );
    expect(pieces).toEqual([
      [
        { x: 0, y: 0 },
        { x: 0.875, y: 0 },
      ],
      [
        { x: 1.125, y: 0 },
        { x: 2.875, y: 0 },
      ],
      [
        { x: 3.125, y: 0 },
        { x: 4, y: 0 },
        { x: 4, y: 0.875 },
      ],
      [
        { x: 4, y: 1.125 },
        { x: 4, y: 2 },
      ],
    ]);
  });

  it('ignores a hop that is not on the route', () => {
    const points = [
      { x: 0, y: 0 },
      { x: 2, y: 0 },
    ];
    expect(cutAtHops(points, [{ x: 1, y: 1 }], 0.1)).toEqual([points]);
  });
});
