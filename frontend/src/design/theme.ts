import { contrast, parseHex, withAlpha } from './color.ts';
import {
  colorTokens,
  contrastPairs,
  focusRingSurfaces,
  syntaxOpacity,
  type ColorToken,
  type ThemeColors,
} from './tokens.ts';

/** A theme file as written: a name and a value for some or all of the colour tokens. */
export interface ThemeFile {
  readonly name: string;
  readonly colors: Readonly<Partial<Record<ColorToken, string>>>;
}

/** The outcome of reading a theme file: the file, or why it was refused. */
export type ParsedTheme =
  { readonly ok: true; readonly theme: ThemeFile } | { readonly ok: false; readonly error: string };

/**
 * A theme made whole against a built-in: every token valued, with the tokens the file did not
 * value (or valued with something that is not a colour) named so the warning can say which.
 */
export interface ResolvedTheme {
  readonly name: string;
  readonly colors: ThemeColors;
  readonly missing: readonly ColorToken[];
  readonly invalid: readonly ColorToken[];
  readonly unknown: readonly string[];
}

/** A text/surface pair that fails WCAG AA in a theme. */
export interface ContrastFailure {
  readonly text: string;
  readonly surface: string;
  readonly ratio: number;
  readonly minimum: number;
}

const tokenSet: ReadonlySet<string> = new Set<string>(colorTokens);

/**
 * Reads a theme file's text. Malformed JSON, or JSON that is not an object with a `colors`
 * object, is refused with the parser's message; it never throws, because the file is the user's
 * (`55` error cases).
 */
export function parseTheme(text: string): ParsedTheme {
  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch (error) {
    return { ok: false, error: error instanceof Error ? error.message : String(error) };
  }

  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return { ok: false, error: 'A theme is a JSON object with "name" and "colors".' };
  }

  const record = value as Record<string, unknown>;
  const colors = record['colors'];
  if (typeof colors !== 'object' || colors === null || Array.isArray(colors)) {
    return { ok: false, error: 'A theme needs a "colors" object mapping token names to colours.' };
  }

  const name =
    typeof record['name'] === 'string' && record['name'].length > 0 ? record['name'] : 'custom';
  const entries = Object.entries(colors as Record<string, unknown>).filter(
    (entry): entry is [string, string] => typeof entry[1] === 'string',
  );

  return { ok: true, theme: { name, colors: Object.fromEntries(entries) as ThemeFile['colors'] } };
}

/**
 * Fills a theme's gaps from `fallback`, token by token (`55`: a custom theme missing tokens falls
 * back to the built-in value per token, and warns once naming them). A value that does not parse
 * as a hex colour counts as invalid and falls back the same way.
 */
export function resolveTheme(file: ThemeFile, fallback: ThemeColors): ResolvedTheme {
  const colors: Partial<Record<ColorToken, string>> = {};
  const missing: ColorToken[] = [];
  const invalid: ColorToken[] = [];

  for (const token of colorTokens) {
    const value = file.colors[token];
    if (value === undefined) {
      missing.push(token);
      colors[token] = fallback[token];
    } else if (parseHex(value) === null) {
      invalid.push(token);
      colors[token] = fallback[token];
    } else {
      colors[token] = value;
    }
  }

  const unknown = Object.keys(file.colors).filter((key) => !tokenSet.has(key));
  return { name: file.name, colors: colors as ThemeColors, missing, invalid, unknown };
}

/**
 * Every text/surface pair in `contrastPairs` that a theme fails, plus the focus ring against each
 * surface at 3:1 and the two dimmed syntax roles against the editor background at 4.5:1. Empty for
 * both built-in themes, by test; a custom theme that fails is loaded with a warning, not refused.
 */
export function contrastFailures(
  colors: ThemeColors,
  variant: 'light' | 'dark',
): ContrastFailure[] {
  const failures: ContrastFailure[] = [];
  const colorOf = (token: ColorToken) => parseHex(colors[token]);

  const check = (text: string, surface: string, ratio: number, minimum: number): void => {
    if (ratio < minimum) {
      failures.push({ text, surface, ratio, minimum });
    }
  };

  for (const pair of contrastPairs) {
    const text = colorOf(pair.text);
    const surface = colorOf(pair.surface);
    if (text !== null && surface !== null) {
      check(pair.text, pair.surface, contrast(text, surface), pair.minimum);
    }
  }

  const ring = colorOf('--focus-ring');
  for (const token of focusRingSurfaces) {
    const surface = colorOf(token);
    if (ring !== null && surface !== null) {
      check('--focus-ring', token, contrast(ring, surface), 3);
    }
  }

  const editorBg = colorOf('--editor-bg');
  const parameter = colorOf('--syn-parameter');
  const unit = colorOf('--syn-unit');
  if (editorBg !== null && parameter !== null) {
    const seen = withAlpha(parameter, syntaxOpacity.parameter);
    check('--syn-parameter @ 85 %', '--editor-bg', contrast(seen, editorBg), 4.5);
  }
  if (editorBg !== null && unit !== null) {
    const alpha = variant === 'dark' ? syntaxOpacity.unitDark : syntaxOpacity.unitLight;
    const seen = withAlpha(unit, alpha);
    check(
      `--syn-unit @ ${Math.round(alpha * 100)} %`,
      '--editor-bg',
      contrast(seen, editorBg),
      4.5,
    );
  }

  return failures;
}

/**
 * Puts a theme on the document root. A built-in theme is a `data-theme` attribute and the
 * stylesheet does the rest; `null` clears it so `prefers-color-scheme` decides; a custom theme is
 * the attribute `custom` with every token set inline, so the same cascade applies and switching
 * back clears them.
 */
export function applyTheme(
  root: HTMLElement,
  theme: { readonly name: string | null; readonly colors?: ThemeColors },
): void {
  for (const token of colorTokens) {
    root.style.removeProperty(token);
  }

  if (theme.colors === undefined) {
    if (theme.name === null) {
      delete root.dataset['theme'];
    } else {
      root.dataset['theme'] = theme.name;
    }
    return;
  }

  root.dataset['theme'] = 'custom';
  for (const token of colorTokens) {
    root.style.setProperty(token, theme.colors[token]);
  }
}
