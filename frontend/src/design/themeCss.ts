import {
  colorTokens,
  durationTokens,
  motionTokens,
  scaleTokens,
  syntaxOpacity,
  type ThemeColors,
} from './tokens.ts';

/**
 * Renders the stylesheet the built-in themes ship as (`55` Theming): `:root` carries the light
 * values, `[data-theme="dark"]` the dark overrides, the same overrides apply under
 * `prefers-color-scheme: dark` when nothing chose, and reduced motion zeroes every duration.
 *
 * The output is checked in as `themes.generated.css` and a test regenerates it, so the JSON theme
 * files stay the one source and the first paint needs no script. Hex values are written in lower
 * case and strings in single quotes, which is how Prettier leaves them, so the file is stable
 * under both.
 */
export function renderThemeCss(light: ThemeColors, dark: ThemeColors): string {
  const lines: string[] = [];
  lines.push(
    '/* Generated from src/design/themes/*.json by src/design/themeCss.ts. Do not edit. */',
  );
  lines.push('');
  lines.push(':root {');
  lines.push('  color-scheme: light;');
  for (const token of colorTokens) {
    lines.push(`  ${token}: ${light[token].toLowerCase()};`);
  }
  lines.push(`  --syn-parameter-opacity: ${syntaxOpacity.parameter};`);
  lines.push(`  --syn-unit-opacity: ${syntaxOpacity.unitLight};`);
  lines.push('');
  for (const [token, value] of Object.entries(scaleTokens)) {
    lines.push(`  ${token}: ${value};`);
  }
  lines.push('');
  for (const [token, value] of Object.entries(motionTokens)) {
    lines.push(`  ${token}: ${value};`);
  }
  lines.push('}');
  lines.push('');

  const darkBlock = (selector: string, indent: string): void => {
    lines.push(`${indent}${selector} {`);
    lines.push(`${indent}  color-scheme: dark;`);
    for (const token of colorTokens) {
      lines.push(`${indent}  ${token}: ${dark[token].toLowerCase()};`);
    }
    lines.push(`${indent}  --syn-unit-opacity: ${syntaxOpacity.unitDark};`);
    lines.push(`${indent}}`);
  };

  darkBlock("[data-theme='dark']", '');
  lines.push('');
  lines.push('@media (prefers-color-scheme: dark) {');
  darkBlock(':root:not([data-theme])', '  ');
  lines.push('}');
  lines.push('');
  lines.push('@media (prefers-reduced-motion: reduce) {');
  lines.push('  :root {');
  for (const token of durationTokens) {
    lines.push(`    ${token}: 0ms;`);
  }
  lines.push('  }');
  lines.push('}');
  lines.push('');

  return lines.join('\n');
}
