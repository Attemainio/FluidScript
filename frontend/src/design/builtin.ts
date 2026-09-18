import darkFile from './themes/dark.json' with { type: 'json' };
import lightFile from './themes/light.json' with { type: 'json' };
import type { ThemeColors } from './tokens.ts';

/** The two themes that ship (`55`), read from the same JSON format a custom theme is written in. */
export const builtinThemes = {
  light: lightFile.colors as ThemeColors,
  dark: darkFile.colors as ThemeColors,
} as const;

/** A built-in theme's name. */
export type BuiltinThemeName = keyof typeof builtinThemes;
