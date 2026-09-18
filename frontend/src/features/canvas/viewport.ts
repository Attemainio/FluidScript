import { worldUnitPx } from '../../design/tokens.ts';
import type { Box, Point } from './geometry.ts';

/** The CAD viewport (`53`): a zoom and the pixel position of the world origin; y is up in the world. */
export interface Viewport {
  /** 1 means `worldUnitPx` pixels per world unit. */
  readonly zoom: number;
  /** Where the world origin sits, in pixels from the pane's top-left. */
  readonly originX: number;
  readonly originY: number;
}

export const minZoom = 0.1;
export const maxZoom = 10;
const fitMargin = 0.05;

/** The size of the pane, pixels. */
export interface Size {
  readonly width: number;
  readonly height: number;
}

/** 1× with the world origin at the pane's centre: `Home`. */
export function reset(size: Size): Viewport {
  return { zoom: 1, originX: size.width / 2, originY: size.height / 2 };
}

/** The whole scene in view with a 5 % margin, centred: `F`. */
export function fit(bounds: Box, size: Size): Viewport {
  if (bounds.width <= 0 || bounds.height <= 0 || size.width <= 0 || size.height <= 0) {
    return reset(size);
  }
  const usable = 1 - 2 * fitMargin;
  const zoom = clamp(
    Math.min(
      (size.width * usable) / (bounds.width * worldUnitPx),
      (size.height * usable) / (bounds.height * worldUnitPx),
    ),
  );
  const scale = zoom * worldUnitPx;
  const centreX = bounds.x + bounds.width / 2;
  const centreY = bounds.y + bounds.height / 2;
  return {
    zoom,
    originX: size.width / 2 - centreX * scale,
    originY: size.height / 2 + centreY * scale,
  };
}

/** Zooms by a factor about a pixel, which stays under the cursor (`53`: never the viewport centre). */
export function zoomAt(view: Viewport, factor: number, at: Point): Viewport {
  const zoom = clamp(view.zoom * factor);
  const ratio = zoom / view.zoom;
  return {
    zoom,
    originX: at.x - (at.x - view.originX) * ratio,
    originY: at.y - (at.y - view.originY) * ratio,
  };
}

export function pan(view: Viewport, dx: number, dy: number): Viewport {
  return { ...view, originX: view.originX + dx, originY: view.originY + dy };
}

/** The SVG transform that maps world units, y up, to the pane's pixels. */
export function rootTransform(view: Viewport): string {
  const scale = view.zoom * worldUnitPx;
  return `translate(${view.originX} ${view.originY}) scale(${scale} ${-scale})`;
}

/** The world rectangle the pane shows. */
export function visibleWorld(view: Viewport, size: Size): Box {
  const scale = view.zoom * worldUnitPx;
  const left = -view.originX / scale;
  const top = view.originY / scale;
  return {
    x: left,
    y: top - size.height / scale,
    width: size.width / scale,
    height: size.height / scale,
  };
}

/** The grid's spacing in world units for a zoom, or `null` below 0.5× where it is noise (`53`). */
export function gridStep(zoom: number): number | null {
  if (zoom < 0.5) {
    return null;
  }
  return zoom >= 3 ? 0.25 : 1;
}

function clamp(zoom: number): number {
  return Math.min(maxZoom, Math.max(minZoom, zoom));
}
