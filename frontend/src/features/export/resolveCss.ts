import { mixOklab, parseHex, toCss } from '../../design/color.ts';
import { scaleTokens, type ThemeColors } from '../../design/tokens.ts';

/**
 * Turns the canvas's token-valued CSS into literal CSS for a standalone SVG (`59` invariant 1).
 *
 * The live canvas writes `var(--fluid-hot)` and an oklab colour mix and lets the browser
 * resolve them against the theme. Neither survives outside the app: a file has no theme, and
 * Inkscape reads neither `color-mix` nor a custom property. So every `color-mix` is blended here
 * in Oklab, the space the browser blends in, and every `var(--token)` is replaced by the chosen
 * theme's value, which is why an exported colour is the colour the canvas showed (`59` invariant 2).
 * A token nothing values is left as it was and reported, never silently dropped.
 */
export function resolveCss(
  text: string,
  colors: ThemeColors,
): { readonly text: string; readonly unresolved: readonly string[] } {
  const unresolved = new Set<string>();
  const valueOf = (token: string): string | null =>
    (colors as Readonly<Record<string, string>>)[token] ??
    (scaleTokens as Readonly<Record<string, string>>)[token] ??
    null;

  // color-mix(in oklab, var(--a) N%, var(--b)) — the one form fluidFill writes.
  const mixed = text.replace(
    /color-mix\(in oklab,\s*var\((--[\w-]+)\)\s*(\d+(?:\.\d+)?)%,\s*var\((--[\w-]+)\)\)/g,
    (whole, a: string, share: string, b: string) => {
      const from = parseHex(valueOf(a) ?? '');
      const to = parseHex(valueOf(b) ?? '');
      if (from === null || to === null) {
        unresolved.add(from === null ? a : b);
        return whole;
      }
      return toCss(mixOklab(from, to, Number(share) / 100));
    },
  );

  const resolved = mixed.replace(/var\((--[\w-]+)\)/g, (whole, token: string) => {
    const value = valueOf(token);
    if (value === null) {
      unresolved.add(token);
      return whole;
    }
    const color = parseHex(value);
    return color === null ? value : toCss(color);
  });

  return { text: resolved, unresolved: [...unresolved].sort() };
}
