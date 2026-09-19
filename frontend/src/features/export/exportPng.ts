import type { ModelContract } from '../../api/types.ts';
import type { PreparedScene } from '../canvas/scene.ts';
import { renderExportSvg, type ExportMeta, type SvgExportOptions } from './exportSvg.tsx';

/** `59`'s PNG options: the SVG's, plus the resolution and whether the ground is painted. */
export interface PngExportOptions extends SvgExportOptions {
  readonly dpi: 96 | 150 | 300;
  readonly transparentBackground: boolean;
}

/**
 * What a browser will rasterize: Chromium and Firefox refuse a canvas over 16 384 px a side and
 * over about 268 million pixels in area; a refusal names the exact size so the reader can pick a
 * lower resolution rather than guess (`59` error cases).
 */
export const rasterLimit = { side: 16384, area: 268_435_456 } as const;

/** The pixel size a PNG at `dpi` would have, or why it cannot be made. */
export function pngDimensions(
  width: number,
  height: number,
  dpi: PngExportOptions['dpi'],
):
  | { readonly ok: true; readonly width: number; readonly height: number }
  | { readonly ok: false; readonly reason: string } {
  const factor = dpi / 96;
  const w = Math.ceil(width * factor);
  const h = Math.ceil(height * factor);
  if (w > rasterLimit.side || h > rasterLimit.side || w * h > rasterLimit.area) {
    return {
      ok: false,
      reason: `Cannot rasterize ${w} × ${h} px at ${dpi} dpi: a browser draws at most ${rasterLimit.side} px a side and ${rasterLimit.area.toLocaleString('en-US')} px in all. Choose a lower resolution, or export the SVG.`,
    };
  }
  return { ok: true, width: w, height: h };
}

/**
 * `59`'s `exportPng`: rasterizes the exact SVG the SVG export writes, through an image and a
 * canvas; there is no second drawing path. Runs only in a browser with a canvas.
 */
export async function exportPng(
  model: ModelContract,
  scene: PreparedScene,
  meta: ExportMeta,
  options: PngExportOptions,
  window: Window,
): Promise<
  | { readonly ok: true; readonly blob: Blob; readonly warnings: readonly string[] }
  | { readonly ok: false; readonly reason: string }
> {
  const rendered = renderExportSvg(model, scene, meta, options);
  if (!rendered.ok) {
    return rendered;
  }
  const size = pngDimensions(rendered.width, rendered.height, options.dpi);
  if (!size.ok) {
    return size;
  }
  const document = window.document;
  const canvas = document.createElement('canvas');
  canvas.width = size.width;
  canvas.height = size.height;
  const context = canvas.getContext('2d');
  if (context === null) {
    return {
      ok: false,
      reason: 'This browser gives no drawing context for a PNG; the SVG export is available.',
    };
  }
  const url = URL.createObjectURL(
    new Blob([rendered.svg], { type: 'image/svg+xml;charset=utf-8' }),
  );
  try {
    const image = document.createElement('img');
    image.decoding = 'sync';
    await new Promise<void>((resolve, reject) => {
      image.onload = () => resolve();
      image.onerror = () => reject(new Error('The SVG could not be decoded for rasterization.'));
      image.src = url;
    });
    context.drawImage(image, 0, 0, size.width, size.height);
  } catch (error) {
    return { ok: false, reason: error instanceof Error ? error.message : String(error) };
  } finally {
    URL.revokeObjectURL(url);
  }
  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/png'));
  return blob === null
    ? { ok: false, reason: 'The browser produced no PNG; the SVG export is available.' }
    : { ok: true, blob, warnings: rendered.warnings };
}
