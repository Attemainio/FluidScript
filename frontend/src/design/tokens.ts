/**
 * The design system's vocabulary (plan/50-frontend/55-design-system.md).
 *
 * Every colour, size and duration the application uses is named here and valued in a theme file
 * under `themes/`. No component writes a literal; a test scans for one. The names are the contract a
 * custom theme is validated against, so adding a token here is adding it to the public theme format.
 */

/** The colour tokens, in `55`'s order. A theme values every one of them. */
export const colorTokens = [
  // surface
  '--surface-base',
  '--surface-raised',
  '--surface-sunken',
  '--surface-overlay',
  '--border-subtle',
  '--border-strong',
  // text
  '--text-primary',
  '--text-secondary',
  '--text-muted',
  '--text-inverse',
  '--focus-ring',
  // fluid accents, the seven stops 57's ramps interpolate between
  '--fluid-cold',
  '--fluid-cool',
  '--fluid-neutral',
  '--fluid-warm',
  '--fluid-hot',
  '--fluid-air',
  '--fluid-steam',
  // status
  '--status-ok',
  '--status-info',
  '--status-warning',
  '--status-error',
  '--status-stale',
  // canvas
  '--canvas-bg',
  '--canvas-grid',
  '--canvas-axis-x',
  '--canvas-axis-y',
  '--canvas-symbol',
  '--canvas-symbol-inferred',
  '--canvas-route',
  '--canvas-selection',
  '--canvas-hover',
  // syntax, VS Code parity
  '--syn-keyword',
  '--syn-kind',
  '--syn-identifier',
  '--syn-parameter',
  '--syn-number',
  '--syn-unit',
  '--syn-string',
  '--syn-comment',
  '--syn-operator',
  '--syn-reference',
  '--syn-function',
  '--syn-error',
  '--syn-warning',
  '--editor-bg',
  '--editor-fg',
] as const;

/** A colour token's name. */
export type ColorToken = (typeof colorTokens)[number];

/** A theme: a value for every colour token. This is the shape of a theme file. */
export type ThemeColors = Readonly<Record<ColorToken, string>>;

/**
 * The tokens that are the same in every theme: the 4 px spacing scale, radii, motion, and the type
 * scale. A theme does not redefine them. They are still tokens, because `55`'s invariant 1 forbids
 * the literal in a component, not in the definition.
 */
export const scaleTokens = {
  '--space-1': '4px',
  '--space-2': '8px',
  '--space-3': '12px',
  '--space-4': '16px',
  '--space-5': '20px',
  '--space-6': '24px',
  '--space-7': '28px',
  '--space-8': '32px',
  '--hairline': '1px',
  '--stroke-axis': '1.5px',
  '--radius-control': '3px',
  '--radius-panel': '6px',
  '--radius-overlay': '8px',
  '--shadow-raised': '0 1px 2px rgb(0 0 0 / 0.08), 0 2px 8px rgb(0 0 0 / 0.06)',
  '--shadow-overlay': '0 4px 16px rgb(0 0 0 / 0.16)',
  '--focus-ring-width': '2px',
  '--font-mono': "ui-monospace, 'Cascadia Code', 'JetBrains Mono', Consolas, monospace",
  '--font-ui': "system-ui, -apple-system, 'Segoe UI', sans-serif",
  '--text-editor-size': '13px',
  '--text-editor-leading': '1.5',
  '--text-body-size': '13px',
  '--text-body-leading': '1.4',
  '--text-heading-size': '15px',
  '--text-heading-weight': '600',
  '--text-label-size': '11px',
  '--text-label-weight': '500',
  '--text-readout-size': '12px',
  '--log-height': '120px',
  '--selection-opacity': '0.3',
} as const;

/**
 * The durations, each zeroed under `prefers-reduced-motion: reduce`. Canvas geometry has no entry:
 * it never animates (`55` invariant 6).
 */
export const motionTokens = {
  '--motion-hover': '150ms',
  '--motion-value': '200ms',
  '--motion-selection': '100ms',
  '--motion-theme': '200ms',
  '--ease-out': 'cubic-bezier(0, 0, 0.2, 1)',
  '--ease-in-out': 'cubic-bezier(0.4, 0, 0.2, 1)',
} as const;

/** The motion tokens that are durations, the ones reduced motion zeroes. */
export const durationTokens = [
  '--motion-hover',
  '--motion-value',
  '--motion-selection',
  '--motion-theme',
] as const;

/**
 * The opacity the syntax palette dims two roles to (`55`): the parameter name is the identifier hue
 * at 85 %, and the unit suffix is the number hue at 75 % in Dark+ and fully opaque in Light+, where
 * 75 % of the light green would not clear 4.5:1 on white.
 */
export const syntaxOpacity = {
  parameter: 0.85,
  unitLight: 1,
  unitDark: 0.75,
} as const;

/**
 * The type scale's two canvas roles publish an advance width per character, in em, and layout uses
 * it (`D-73`): a label's box is `advance × characters × size`, and the resolved font only has to fit
 * inside. The monospace figure covers the whole stack -- Consolas 0.55, Cascadia Code 0.586,
 * JetBrains Mono 0.6 em, from the fonts' own metrics. The proportional figure is this project's
 * reservation for tags of capitals and digits (Segoe UI's capitals average about 0.6 em); it is the
 * number to test when a label overflows its box.
 */
export const typeMetrics = {
  canvasLabel: { sizePx: 11, weight: 500, advanceEm: 0.62, family: '--font-ui' },
  numericReadout: { sizePx: 12, weight: 400, advanceEm: 0.6, family: '--font-mono' },
} as const;

/**
 * The text-on-surface pairs a theme must keep readable, asserted at WCAG AA (`55` invariant 3).
 * `large` pairs are heading-sized and need 3:1; the rest need 4.5:1.
 */
export const contrastPairs: ReadonlyArray<{
  readonly text: ColorToken;
  readonly surface: ColorToken;
  readonly minimum: 4.5 | 3;
}> = [
  ...(
    ['--surface-base', '--surface-raised', '--surface-sunken', '--surface-overlay'] as const
  ).flatMap(
    (surface) =>
      [
        { text: '--text-primary', surface, minimum: 4.5 },
        { text: '--text-secondary', surface, minimum: 4.5 },
        { text: '--text-muted', surface, minimum: 4.5 },
      ] as const,
  ),
  { text: '--text-inverse', surface: '--status-error', minimum: 4.5 },
  { text: '--text-inverse', surface: '--status-ok', minimum: 4.5 },
  { text: '--text-inverse', surface: '--status-info', minimum: 4.5 },
  { text: '--text-inverse', surface: '--fluid-cold', minimum: 4.5 },
  { text: '--text-inverse', surface: '--fluid-hot', minimum: 4.5 },
  { text: '--editor-fg', surface: '--editor-bg', minimum: 4.5 },
  { text: '--syn-keyword', surface: '--editor-bg', minimum: 4.5 },
  { text: '--syn-kind', surface: '--editor-bg', minimum: 4.5 },
  { text: '--syn-identifier', surface: '--editor-bg', minimum: 4.5 },
  { text: '--syn-number', surface: '--editor-bg', minimum: 4.5 },
  { text: '--syn-string', surface: '--editor-bg', minimum: 4.5 },
  { text: '--syn-comment', surface: '--editor-bg', minimum: 4.5 },
  { text: '--syn-reference', surface: '--editor-bg', minimum: 4.5 },
  { text: '--canvas-symbol', surface: '--canvas-bg', minimum: 3 },
  { text: '--canvas-route', surface: '--canvas-bg', minimum: 3 },
];

/** The surfaces the 2 px focus ring must stand out from at 3:1 (`55`). */
export const focusRingSurfaces: readonly ColorToken[] = [
  '--surface-base',
  '--surface-raised',
  '--surface-sunken',
  '--surface-overlay',
  '--editor-bg',
  '--canvas-bg',
];

/**
 * The canvas's pixel scale: how many CSS pixels one world unit is at 1× zoom (`53`, `55`). A pump
 * is 1 × 1 world unit (`D-103`), so at 1× it is 60 px across, the scale the Core `SceneSvg`
 * instrument draws the ladder's pictures at; the two pictures are the same size on purpose.
 */
export const worldUnitPx = 60;

/** The sequential ramp's stops, cold to hot, that `fluidFill` interpolates between (`57`). */
const fluidRamp = [
  '--fluid-cold',
  '--fluid-cool',
  '--fluid-neutral',
  '--fluid-warm',
  '--fluid-hot',
] as const;

/**
 * The fill for a position on the active scale, 0 (cold) to 1 (hot), as a CSS colour expression
 * mixing the two neighbouring ramp stops. It is the only place a colour is composed rather than
 * named, which is why it lives with the tokens: the ramp is `55`'s palette, not a literal.
 */
export function fluidFill(position: number): string {
  const clamped = Math.min(1, Math.max(0, position));
  const scaled = clamped * (fluidRamp.length - 1);
  const lower = Math.min(fluidRamp.length - 2, Math.floor(scaled));
  const share = Math.round((scaled - lower) * 100);
  const from = fluidRamp[lower]!;
  const to = fluidRamp[lower + 1]!;
  return `color-mix(in oklab, var(${from}) ${100 - share}%, var(${to}))`;
}
