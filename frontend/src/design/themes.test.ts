import { readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { builtinThemes } from './builtin.ts';
import { contrast, parseHex } from './color.ts';
import { contrastFailures } from './theme.ts';
import { renderThemeCss } from './themeCss.ts';
import { colorTokens, durationTokens, syntaxOpacity } from './tokens.ts';

const designDir = import.meta.dirname;
const srcDir = join(designDir, '..');

/** 55's syntax table, the hex values a script is coloured with. The theme files must match it exactly. */
const vsCodeTable = {
  '--syn-keyword': ['#0000FF', '#569CD6'],
  '--syn-kind': ['#267F99', '#4EC9B0'],
  '--syn-identifier': ['#001080', '#9CDCFE'],
  '--syn-parameter': ['#001080', '#9CDCFE'],
  '--syn-number': ['#098658', '#B5CEA8'],
  '--syn-unit': ['#098658', '#B5CEA8'],
  '--syn-string': ['#A31515', '#CE9178'],
  '--syn-comment': ['#008000', '#6A9955'],
  '--syn-operator': ['#000000', '#D4D4D4'],
  '--syn-reference': ['#795E26', '#DCDCAA'],
  '--syn-function': ['#795E26', '#DCDCAA'],
  '--editor-bg': ['#FFFFFF', '#1E1E1E'],
  '--editor-fg': ['#000000', '#D4D4D4'],
} as const;

/** 55's HVAC and status palettes. */
const hvacTable = {
  '--fluid-cold': ['#1B6CA8', '#4FA3D9'],
  '--fluid-cool': ['#3A8FB7', '#6FBBD9'],
  '--fluid-neutral': ['#5C7A89', '#8FA9B5'],
  '--fluid-warm': ['#C97B3C', '#E09E5F'],
  '--fluid-hot': ['#B23A2E', '#E06C5A'],
  '--fluid-air': ['#7A9E7E', '#9CC2A0'],
  '--fluid-steam': ['#8E7CC3', '#B0A0DC'],
  '--status-ok': ['#2E7D32', '#66BB6A'],
  '--status-info': ['#0277BD', '#4FC3F7'],
  '--status-warning': ['#ED6C02', '#FFB74D'],
  '--status-error': ['#C62828', '#EF5350'],
  '--status-stale': ['#9E9E9E', '#757575'],
} as const;

describe('the built-in themes', () => {
  it.each(['light', 'dark'] as const)('%s values every token and nothing else', (name) => {
    // 55 invariant 2: a missing token fails a test, not at runtime.
    const keys = Object.keys(builtinThemes[name]);
    expect(keys).toEqual([...colorTokens]);
  });

  it.each(['light', 'dark'] as const)('%s writes every value as a hex colour', (name) => {
    for (const token of colorTokens) {
      expect(parseHex(builtinThemes[name][token]), token).not.toBeNull();
    }
  });

  it.each(['light', 'dark'] as const)('%s meets WCAG AA on every declared pair', (name) => {
    // 55 invariant 3, asserted over the token set rather than eyeballed.
    const failures = contrastFailures(builtinThemes[name], name);
    expect(
      failures.map((f) => `${f.text} on ${f.surface}: ${f.ratio.toFixed(2)} < ${f.minimum}`),
    ).toEqual([]);
  });

  it('matches the VS Code table exactly in both themes', () => {
    // 55 invariant 4, against the hex values.
    for (const [token, [light, dark]] of Object.entries(vsCodeTable)) {
      expect(builtinThemes.light[token as keyof typeof vsCodeTable], token).toBe(light);
      expect(builtinThemes.dark[token as keyof typeof vsCodeTable], token).toBe(dark);
    }
  });

  it('carries the HVAC and status palettes as 55 writes them', () => {
    for (const [token, [light, dark]] of Object.entries(hvacTable)) {
      expect(builtinThemes.light[token as keyof typeof hvacTable], token).toBe(light);
      expect(builtinThemes.dark[token as keyof typeof hvacTable], token).toBe(dark);
    }
  });

  it('keeps the unit suffix readable at its opacity: opaque in light, 75 % in dark', () => {
    // 55's first FluidScript-specific decision, with the number that forced it: the light green at
    // 75 % over white would fall under 4.5:1, so the light value stays opaque.
    const lightUnit = parseHex(builtinThemes.light['--syn-unit'])!;
    const lightBg = parseHex(builtinThemes.light['--editor-bg'])!;
    const darkUnit = parseHex(builtinThemes.dark['--syn-unit'])!;
    const darkBg = parseHex(builtinThemes.dark['--editor-bg'])!;

    expect(contrast({ ...lightUnit, a: syntaxOpacity.unitLight }, lightBg)).toBeGreaterThanOrEqual(
      4.5,
    );
    expect(contrast({ ...lightUnit, a: syntaxOpacity.unitDark }, lightBg)).toBeLessThan(4.5);
    expect(contrast({ ...darkUnit, a: syntaxOpacity.unitDark }, darkBg)).toBeGreaterThanOrEqual(
      4.5,
    );
  });

  it('gives the canvas a background that is not the editor background', () => {
    // 55's worked example: the two panes are distinguishable at a glance.
    expect(builtinThemes.dark['--canvas-bg']).not.toBe(builtinThemes.dark['--editor-bg']);
    expect(builtinThemes.light['--canvas-bg']).not.toBe(builtinThemes.light['--editor-bg']);
  });
});

describe('the generated stylesheet', () => {
  it('is what the theme files render to', () => {
    // The docs gate's rule (62): regenerate in place on a mismatch and fail, so the diff is reviewed.
    const path = join(designDir, 'themes.generated.css');
    const expected = renderThemeCss(builtinThemes.light, builtinThemes.dark);
    const actual = readFileSync(path, 'utf8');

    if (actual !== expected) {
      writeFileSync(path, expected);
    }

    expect(
      actual,
      'themes.generated.css did not match the theme files and has been regenerated in place; review the change and run the tests again',
    ).toBe(expected);
  });

  it('zeroes every duration under reduced motion', () => {
    // 55: nonessential transitions are 0 ms and nothing depends on animation.
    const css = renderThemeCss(builtinThemes.light, builtinThemes.dark);
    const block =
      /@media \(prefers-reduced-motion: reduce\) \{([^}]*\{[^}]*\})/.exec(css)?.[1] ?? '';

    for (const token of durationTokens) {
      expect(block).toContain(`${token}: 0ms;`);
    }
  });

  it('applies the dark theme by preference only when nothing chose', () => {
    const css = renderThemeCss(builtinThemes.light, builtinThemes.dark);
    expect(css).toContain('@media (prefers-color-scheme: dark) {\n  :root:not([data-theme]) {');
    expect(css).toContain("[data-theme='dark'] {");
  });
});

describe('the source tree', () => {
  it('writes no literal colour, size or duration outside the token definitions', () => {
    // 55 invariant 1 and 63's architecture-test table. The token definitions are the theme files,
    // tokens.ts and the stylesheet generated from them; everything else names a token.
    // color.ts composes a colour from parsed channels for the export (59) and names none.
    const allowed = new Set(['design/tokens.ts', 'design/themes.generated.css', 'design/color.ts']);
    const offenders: string[] = [];
    const colour = /#[0-9a-f]{3,8}\b|\b(?:rgba?|hsla?|oklch|color-mix)\(/i;
    const cssMeasure = /(?<![\w-])\d+(?:\.\d+)?(?:px|ms|rem|em)\b/;

    for (const file of walk(srcDir)) {
      const relative = file.slice(srcDir.length + 1).replaceAll('\\', '/');
      if (
        allowed.has(relative) ||
        relative.startsWith('design/themes/') ||
        relative.endsWith('.test.ts') ||
        relative.endsWith('.test.tsx')
      ) {
        continue;
      }

      const text = readFileSync(file, 'utf8');
      text.split('\n').forEach((line, index) => {
        const code = line.replace(/\/\/.*$|\/\*.*?\*\//g, '');
        if (colour.test(code)) {
          offenders.push(`${relative}:${index + 1} colour: ${line.trim()}`);
        }
        if (relative.endsWith('.css') && cssMeasure.test(code) && !/^\s*--/.test(code)) {
          offenders.push(`${relative}:${index + 1} measure: ${line.trim()}`);
        }
      });
    }

    expect(offenders).toEqual([]);
  });
});

function* walk(dir: string): Generator<string> {
  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) {
      yield* walk(path);
    } else if (/\.(?:ts|tsx|css)$/.test(entry)) {
      yield path;
    }
  }
}
