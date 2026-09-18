import { describe, expect, it } from 'vitest';

import { worldUnitPx } from '../../design/tokens.ts';
import { detailFor } from './detail.ts';
import { fit, gridStep, pan, reset, rootTransform, visibleWorld, zoomAt } from './viewport.ts';

const size = { width: 800, height: 600 };

describe('the viewport', () => {
  it('resets to 1× with the origin at the centre (Home)', () => {
    expect(reset(size)).toEqual({ zoom: 1, originX: 400, originY: 300 });
    expect(rootTransform(reset(size))).toBe(
      `translate(400 300) scale(${worldUnitPx} ${-worldUnitPx})`,
    );
  });

  it('fits the scene with a 5 % margin, centred, on the tighter axis (F)', () => {
    const bounds = { x: -1, y: -2, width: 12, height: 4 };
    const view = fit(bounds, size);
    // 12 units into 90 % of 800 px is 60 px/unit at 1×, so the zoom is exactly 1.
    expect(view.zoom).toBeCloseTo(1, 6);
    const world = visibleWorld(view, size);
    expect(world.x + world.width / 2).toBeCloseTo(5, 6);
    expect(world.y + world.height / 2).toBeCloseTo(0, 6);
  });

  it('zooms about the cursor: the world point under it stays put', () => {
    const view = reset(size);
    const at = { x: 100, y: 500 };
    const before = visibleWorld(view, size);
    const worldUnder = {
      x: before.x + at.x / worldUnitPx,
      y: before.y + before.height - at.y / worldUnitPx,
    };
    const zoomed = zoomAt(view, 2, at);
    const after = visibleWorld(zoomed, size);
    const scale = zoomed.zoom * worldUnitPx;
    expect(after.x + at.x / scale).toBeCloseTo(worldUnder.x, 9);
    expect(after.y + after.height - at.y / scale).toBeCloseTo(worldUnder.y, 9);
  });

  it('clamps the zoom to 0.1× and 10×', () => {
    const view = reset(size);
    expect(zoomAt(view, 100, { x: 0, y: 0 }).zoom).toBe(10);
    expect(zoomAt(view, 0.001, { x: 0, y: 0 }).zoom).toBe(0.1);
  });

  it('pans in pixels', () => {
    expect(pan(reset(size), 10, -20)).toEqual({ zoom: 1, originX: 410, originY: 280 });
  });

  it('steps the grid and the detail with the zoom (53)', () => {
    expect(gridStep(0.3)).toBeNull();
    expect(gridStep(1)).toBe(1);
    expect(gridStep(3)).toBe(0.25);
    expect(detailFor(0.4)).toBe('symbols');
    expect(detailFor(1)).toBe('names');
    expect(detailFor(2)).toBe('values');
    expect(detailFor(3)).toBe('all');
  });
});
