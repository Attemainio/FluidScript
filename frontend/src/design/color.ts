/**
 * Colour arithmetic for the design system: parsing a theme's hex values, WCAG 2.1 contrast, and
 * compositing a translucent colour over a surface. Nothing here formats an engineering number.
 */

/** A colour as sRGB channels 0..255 with an alpha 0..1. */
export interface Rgba {
  readonly r: number;
  readonly g: number;
  readonly b: number;
  readonly a: number;
}

/**
 * Parses `#RGB`, `#RGBA`, `#RRGGBB` or `#RRGGBBAA`, the only forms a theme file may use, or
 * returns `null` for anything else. Case does not matter.
 */
export function parseHex(text: string): Rgba | null {
  const match = /^#([0-9a-f]{3,8})$/i.exec(text.trim());
  if (match === null) {
    return null;
  }

  const digits = match[1] ?? '';
  if (digits.length === 3 || digits.length === 4) {
    const wide = [...digits].map((d) => d + d).join('');
    return parseHex('#' + wide);
  }

  if (digits.length !== 6 && digits.length !== 8) {
    return null;
  }

  const channel = (at: number): number => Number.parseInt(digits.slice(at, at + 2), 16);
  return {
    r: channel(0),
    g: channel(2),
    b: channel(4),
    a: digits.length === 8 ? channel(6) / 255 : 1,
  };
}

/** Composites `top` over an opaque `under`, the colour the eye sees for a dimmed token. */
export function over(top: Rgba, under: Rgba): Rgba {
  const mix = (t: number, u: number): number => Math.round(t * top.a + u * (1 - top.a));
  return { r: mix(top.r, under.r), g: mix(top.g, under.g), b: mix(top.b, under.b), a: 1 };
}

/** Returns `color` at `alpha`, the theme's opacity device for parameter names and unit suffixes. */
export function withAlpha(color: Rgba, alpha: number): Rgba {
  return { ...color, a: alpha };
}

/** Relative luminance per WCAG 2.1, of an opaque colour. */
export function luminance(color: Rgba): number {
  const linear = (channel: number): number => {
    const c = channel / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * linear(color.r) + 0.7152 * linear(color.g) + 0.0722 * linear(color.b);
}

/**
 * The WCAG 2.1 contrast ratio between a text colour and the surface it sits on, 1..21. A
 * translucent text colour is composited over the surface first; the surface is taken as opaque.
 */
export function contrast(text: Rgba, surface: Rgba): number {
  const seen = text.a < 1 ? over(text, surface) : text;
  const lighter = Math.max(luminance(seen), luminance(surface));
  const darker = Math.min(luminance(seen), luminance(surface));
  return (lighter + 0.05) / (darker + 0.05);
}
