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

/**
 * Mixes two colours in Oklab, the space the browser's oklab colour mixing blends in, so an export that has
 * to write a literal colour lands on the same one the live canvas shows (`59` invariant 2).
 * `share` is the weight of `a`, 0..1; alpha is mixed linearly.
 */
export function mixOklab(a: Rgba, b: Rgba, share: number): Rgba {
  const t = Math.min(1, Math.max(0, share));
  const la = toOklab(a);
  const lb = toOklab(b);
  const mixed = fromOklab([
    la[0] * t + lb[0] * (1 - t),
    la[1] * t + lb[1] * (1 - t),
    la[2] * t + lb[2] * (1 - t),
  ]);
  return { ...mixed, a: a.a * t + b.a * (1 - t) };
}

/** `#rrggbb`, or `rgba(r g b / a)` for a translucent colour, in lower case: what a standalone SVG can carry. */
export function toCss(color: Rgba): string {
  const hex = (channel: number): string =>
    Math.round(Math.min(255, Math.max(0, channel)))
      .toString(16)
      .padStart(2, '0');
  const opaque = `#${hex(color.r)}${hex(color.g)}${hex(color.b)}`;
  return color.a >= 1
    ? opaque
    : `rgba(${Math.round(color.r)}, ${Math.round(color.g)}, ${Math.round(color.b)}, ${Number(color.a.toFixed(3))})`;
}

/** sRGB → Oklab (Björn Ottosson's published matrices), on linear light. */
function toOklab(color: Rgba): [number, number, number] {
  const linear = (channel: number): number => {
    const c = channel / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  const r = linear(color.r);
  const g = linear(color.g);
  const b = linear(color.b);
  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
  const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
  return [
    0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s,
    1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s,
    0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s,
  ];
}

function fromOklab([L, A, B]: [number, number, number]): Rgba {
  const l = (L + 0.3963377774 * A + 0.2158037573 * B) ** 3;
  const m = (L - 0.1055613458 * A - 0.0638541728 * B) ** 3;
  const s = (L - 0.0894841775 * A - 1.291485548 * B) ** 3;
  const gamma = (linear: number): number => {
    const c = Math.min(1, Math.max(0, linear));
    return (c <= 0.0031308 ? 12.92 * c : 1.055 * c ** (1 / 2.4) - 0.055) * 255;
  };
  return {
    r: gamma(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
    g: gamma(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
    b: gamma(-0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s),
    a: 1,
  };
}
