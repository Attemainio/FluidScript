---
id: 55-design-system
title: Design system
tier: 50-frontend
status: reviewed
owns: [design tokens, themes, colour palettes, syntax highlighting palette, typography, spacing, motion, primitives]
depends_on: [51-frontend-architecture]
traces_to: [R-26, R-27, R-22, R-33, R-48]
open_questions: 0
last_review_pass: 2
---

# Design system

## Purpose

`R-26` and `R-27`: light and dark themes natively, further themes configurable, a subtle HVAC-themed
palette with contrast where it carries meaning, and an overall feel that is playful rather than like an
engineering console. This document owns every colour, size, and duration in the application, so that
none of them is decided ad hoc in a component.

## Responsibilities

**Owns.** Design tokens, themes, all colour palettes including the syntax palette, typography, spacing,
elevation, motion, and the shared primitives.

**Explicitly does not own.** Layout of the canvas ([`53-canvas-renderer`](53-canvas-renderer.md)),
what the editor highlights ([`52-editor`](52-editor.md) decides *which* tokens; this document decides
what colour they are), log content ([`56-console-log`](56-console-log.md)), and the property colour
scales and legend ([`57-state-visualization`](57-state-visualization.md) — which builds its ramps from
the fluid tokens defined here).

## Two palettes, deliberately

A tension in the requirements, resolved rather than compromised:

| Surface | Palette | Reason |
|---|---|---|
| **Editor (syntax)** | The familiar **Visual Studio / VS Code** colours | Almost every user has read code in these colours for years. Novelty here is a cost, not a feature — a keyword that is not blue reads as *wrong* before it reads as *different*. |
| **Everything else** — chrome, canvas, log, panels | Subtle HVAC-themed palette | `R-26`/`R-27`. This is where the tool's character lives. |

Splitting them is the point: the code pane should feel like an editor the user already knows, and the
diagram pane should feel like the tool this project is trying to be. Applying the HVAC palette to
syntax would make the script harder to read for no gain; applying VS Code's grey chrome to the whole
app would make it look like an engineering console, which `R-27` explicitly rejects.

## Tokens

CSS custom properties on `:root`, redefined per theme. No component ever writes a literal colour.

```css
:root {
  /* ── surface ───────────────────────────── */
  --surface-base
  --surface-raised
  --surface-sunken
  --surface-overlay
  --border-subtle
  --border-strong

  /* ── text ──────────────────────────────── */
  --text-primary
  --text-secondary
  --text-muted
  --text-inverse
  --focus-ring       /* visible 2 px keyboard focus indicator */

  /* ── fluid accents (HVAC semantics) ────── */
  --fluid-cold        /* chilled water, supply    */
  --fluid-cool
  --fluid-neutral
  --fluid-warm
  --fluid-hot         /* heating water, return    */
  --fluid-air         /* air-side circuits        */
  --fluid-steam

  /* ── status ────────────────────────────── */
  --status-ok
  --status-info
  --status-warning
  --status-error
  --status-stale      /* values being recomputed  */

  /* ── canvas ────────────────────────────── */
  --canvas-bg
  --canvas-grid
  --canvas-axis-x     /* red   — R-22 */
  --canvas-axis-y     /* green — R-22 */
  --canvas-symbol
  --canvas-symbol-inferred
  --canvas-route
  --canvas-selection
  --canvas-hover

  /* ── syntax (VS Code parity) ───────────── */
  --syn-keyword
  --syn-kind
  --syn-identifier
  --syn-parameter
  --syn-number
  --syn-unit
  --syn-string
  --syn-comment
  --syn-operator
  --syn-reference
  --syn-error
  --syn-warning
}
```

## The HVAC palette

Colour carries meaning here rather than decorating: temperature maps to hue the way it does on every
schematic a designer has read.

| Token | Light | Dark | Meaning |
|---|---|---|---|
| `--fluid-cold` | `#1B6CA8` | `#4FA3D9` | Chilled water, supply |
| `--fluid-cool` | `#3A8FB7` | `#6FBBD9` | |
| `--fluid-neutral` | `#5C7A89` | `#8FA9B5` | Unheated, ambient |
| `--fluid-warm` | `#C97B3C` | `#E09E5F` | |
| `--fluid-hot` | `#B23A2E` | `#E06C5A` | Heating water, return |
| `--fluid-air` | `#7A9E7E` | `#9CC2A0` | Air-side |
| `--fluid-steam` | `#8E7CC3` | `#B0A0DC` | Steam |

Muted, desaturated versions of the schematic conventions — blue supply, red return — so that a diagram
with twenty components does not vibrate. Saturation is reserved for status and selection, which is what
"contrast where it is needed" means in practice.

**Fluid colour is derived from a solved property by default** — temperature unless the script says
otherwise — interpolating cold → hot across the circuit's own range, and overridden by the `style`
directive ([`12-grammar`](../10-language/12-grammar.md)). A diagram then reads at a glance: the hot leg
is warm-coloured without anyone specifying it. The ramp, the domain, the legend, and the `show`
directive that selects the property are owned by
[`57-state-visualization`](57-state-visualization.md); this document supplies only the seven `--fluid-*`
stops it interpolates between.

### Status

| Token | Light | Dark | Used for |
|---|---|---|---|
| `--status-ok` | `#2E7D32` | `#66BB6A` | Converged, within limits |
| `--status-info` | `#0277BD` | `#4FC3F7` | Inference notices |
| `--status-warning` | `#ED6C02` | `#FFB74D` | `FS4001`, `FS4006`, sizing warnings |
| `--status-error` | `#C62828` | `#EF5350` | Errors, non-convergence |
| `--status-stale` | `#9E9E9E` | `#757575` | Values awaiting a re-solve |

## The syntax palette — Visual Studio parity

Mapped from VS Code's default **Light+** and **Dark+** themes, so a FluidScript script looks like code
in the editor the user already uses.

| FluidScript token | VS Code role | Light+ | Dark+ |
|---|---|---|---|
| Keyword (`circuit`, `fluid`, `let`, `connections`, `dynamic`) | keyword | `#0000FF` | `#569CD6` |
| Component kind (`heat_exchanger`, `pump`) | type / class | `#267F99` | `#4EC9B0` |
| Component identifier in declaration position (`HE1`, `3WV`) | variable declaration | `#001080` | `#9CDCFE` |
| Parameter name (`power`, `kv`) | parameter | `#001080` @ 85 % | `#9CDCFE` @ 85 % |
| Number literal | number | `#098658` | `#B5CEA8` |
| Unit suffix (`kW`, `°C`) | number, dimmed to 75 % | `#098658` @75 % | `#B5CEA8` @75 % |
| String literal | string | `#A31515` | `#CE9178` |
| Comment (`#` to end of line) | comment, italic | `#008000` | `#6A9955` |
| Operator / punctuation (`=`, `-`, `.`) | operator | `#000000` | `#D4D4D4` |
| Member reference (`HE1.dp`) | property | `#795E26` | `#DCDCAA` |
| Function (`min`, `max`, `sqrt`) | function | `#795E26` | `#DCDCAA` |
| Editor background | — | `#FFFFFF` | `#1E1E1E` |
| Editor foreground | — | `#000000` | `#D4D4D4` |

**Two FluidScript-specific decisions on top of the mapping:**

1. **The unit suffix is the number colour at 100 % opacity in Light+ and 75 % in Dark+**, not a
   separate hue. The light value must remain fully opaque to clear 4.5:1 against white; the dark value
   clears the threshold at 75 %. `30`**`kW`** stays
   scannable as one value while the unit recedes — important in a language where columns of numbers are
   the normal shape, and no VS Code role corresponds to it.
2. **Declaration-position identifiers are the variable colour, and parameter names are the same hue at
   85 % opacity.** VS Code gives both roles one colour, which would make `HE1` and `power` identical —
   but [`52-editor`](52-editor.md) calls the declaration identifier *emphasised*, and two things cannot
   both be emphasised relative to each other. Dimming the parameter is the smaller change and it keeps
   the left column of the declaration section reading as a list of names, which is what it is. It is
   the same device used for the unit suffix, for the same reason.

**Semantic highlighting deliberately not used** in v1: it would require the server's binder to
drive colours, which reintroduces a round trip into the one thing that must be instant
([`52-editor`](52-editor.md)). Lexical highlighting produces the table above correctly; the only thing
it cannot distinguish is a valid component kind from an invalid one, and the squiggle already says that.

## Typography

| Role | Family | Size | Weight |
|---|---|---|---|
| Editor | `ui-monospace, "Cascadia Code", "JetBrains Mono", Consolas, monospace` | 13 px / 1.5 | 400 |
| UI body | `system-ui, -apple-system, "Segoe UI", sans-serif` | 13 px / 1.4 | 400 |
| UI heading | same | 15 px | 600 |
| Canvas label | same | 11 px | 500 |
| Numeric readout | the monospace stack, **tabular figures** | 12 px | 400 |

**The two canvas roles carry a declared advance-width metric, and layout uses it (`D-73`).** The
error table below already forbids layout depending on a specific font's metrics; measuring a label by
rendering it is exactly that dependency, and it is also unavailable in the layout worker, which has no
DOM. So `Canvas label` and `Numeric readout` each publish an advance width per character at their
stated size, a label's box is `advance x characters x size`, and the resolved font is only required to
*fit inside* the box the table reserved. A wider fallback overflows its own box and moves no placement
— the only degradation available if a diagram is to be identical on two machines.

**Tabular figures on every numeric readout.** Values that update 600 times a second during playback
must not shift horizontally as digits change width — the jitter is small and deeply distracting.
The value/unit text itself uses `D-14`'s dimension-wide display unit; this document styles it but never
chooses or converts the engineering unit.

## Spacing, radius, elevation, motion

4 px base scale: `--space-1` 4 px through `--space-8` 32 px. Radius: 3 px controls, 6 px panels, 8 px
overlays.

Elevation by border plus a soft shadow, never by a lighter surface alone — a lighter surface breaks in
a high-contrast theme.

| Motion | Duration | Easing | Where |
|---|---|---|---|
| Hover reveal | 150 ms | ease-out | Cards, tooltips |
| Value change | 200 ms | ease-in-out | Numeric transitions during playback |
| Selection | 100 ms | ease-out | Canvas emphasis |
| Panel resize | none | — | Follows the pointer directly |
| Theme switch | 200 ms | ease-in-out | Colour cross-fade |

**Nothing animates position on the canvas.** A component that slides to a new place after a recompile
looks alive and makes the diagram impossible to read while typing. Values cross-fade; geometry cuts.

### Canvas spacing tokens

Canvas spacing is a separate scale from UI spacing, in **world units** rather than pixels, because it
survives zoom and the UI scale does not.

| Token | Default | Meaning |
|---|---|---|
| `--canvas-spacing-default` | 20 | The gap between adjacent component bounding boxes when the script says nothing (`D-37`) |
| `--canvas-spacing-min` | 8 | Floor; a script asking for less is clamped and told so |
| `--canvas-rail-gap` | 120 | Vertical distance between a header's supply and return rails (`D-38`) |
| `--canvas-branch-stride` | 160 | Horizontal distance between adjacent members stacked on a header |

**Sparse is the default and it is a deliberate cost.** Tight packing fits more on screen and is what a
generic layout produces; the reference drawings this convention comes from leave valves, sensors and
fittings well apart, and at fit zoom a tightly packed run of six inline symbols reads as one smear.
The default is set so the common case needs no `spacing` line at all — `P1`'s standard applied to
presentation.

`spacing` from the script overrides `--canvas-spacing-default` only. The rail and stride tokens stay
under the design system's control, because a user asking for tighter components is not asking for
their supply and return rails to converge.

**These are the renderer's numbers, not Core's.** They live here rather than in `LayoutHints` for the
same reason `spacing` does (`D-37`): they are distances, and Core holds none.

Under `@media (prefers-reduced-motion: reduce)`, every nonessential duration token becomes `0ms` and
transient playback updates values without cross-fades. Progress remains visible through text and
frame position; no information depends on animation. `--focus-ring` is a 2 px solid token with a
minimum 3:1 contrast against every adjacent surface and is used by every keyboard-focusable primitive.

## Theming

```
:root                    → light tokens
[data-theme="dark"]      → dark overrides
@media (prefers-color-scheme: dark) → dark when no explicit choice
[data-theme="<custom>"]  → user themes
```

`R-26`'s "option to configure other themes": a theme is a JSON file of token values, validated against
the token list, loadable at runtime, persisted in localStorage. Two ship built in; the format is public
so a user can write a third.

**Contrast is validated, not eyeballed.** Every text-on-surface pair meets WCAG AA (4.5:1 body, 3:1
large), asserted by a test over the token set. A custom theme failing contrast gets a warning, not a
rejection — it is the user's tool.

## Primitives

`Button` · `IconButton` · `Panel` · `Card` · `Tooltip` · `Toolbar` · `Slider` · `NumericInput` ·
`Badge` · `Tabs` · `SplitPane` · `StatusDot`

Small, unstyled-by-default, tokens only. **No component library dependency** — a dozen primitives is
less work than fighting a library's theming, and it keeps the bundle small, which is `R-27`'s
lightness requirement expressed in kilobytes.

## Invariants

1. No literal colour, size, or duration outside the token definitions.
2. Every token is defined in every shipped theme; a missing token fails a test, not at runtime.
3. Text/surface pairs meet WCAG AA in both built-in themes.
4. The syntax palette matches the VS Code table above exactly in both themes.
5. Theme switching requires no reload and loses no state.
6. Canvas geometry never animates.
7. Every numeric readout uses tabular figures.
8. Canvas label geometry comes from the declared advance-width metric, never from measuring rendered
   text (`D-73`). No placement is a function of which font resolved.

## Error cases

| Situation | Behaviour |
|---|---|
| Custom theme missing tokens | Fall back to the built-in value per token; warn once, naming the tokens |
| Custom theme fails contrast | Load it; show a dismissible warning |
| Custom theme is malformed JSON | Refuse; keep the current theme; show the parse error |
| `prefers-color-scheme` unavailable | Default to light |
| Monospace font unavailable | The stack falls back; layout must not depend on a specific font's metrics |

## Worked example

The M2 demo in dark theme:

**Editor** — background `#1E1E1E`, foreground `#D4D4D4`:

```
circuit coolingLoop                          # name
^^^^^^^ #569CD6                                ^^^^ #6A9955 italic
        ^^^^^^^^^^^ #9CDCFE

HE1 heat_exchanger power=30 in=20 out=50
^^^ #9CDCFE
    ^^^^^^^^^^^^^^ #4EC9B0
                   ^^^^^ #9CDCFE
                         ^^ #B5CEA8
```

Anyone who has used VS Code reads this without adjusting: blue keyword, teal type, light-blue
identifiers, green numbers, green italic comments.

**Canvas** — background `--canvas-bg` (a very dark blue-grey, not the editor's neutral `#1E1E1E`, so
the two panes are distinguishable at a glance):

| Element | Colour |
|---|---|
| `HE1` symbol | Stroked `--fluid-warm` (`#E09E5F`) — outlet is 50 °C, the circuit's hot end |
| `N1` symbol | `--fluid-cold` (`#4FA3D9`) — 6 °C, the primary supply and the coldest point |
| `N2` symbol | `--fluid-cool` (`#6FBBD9`) — 20 °C, the mixing-node temperature |
| Route from `HE1` | Gradient between the two, showing the temperature along the run |
| Inferred nodes | `--canvas-symbol-inferred`, ~40 % opacity |
| `3WV` warning badge | `--status-warning` (`#FFB74D`) |
| X axis | `--canvas-axis-x` red · Y axis `--canvas-axis-y` green |
| Grid | `--canvas-grid`, barely visible |

The diagram reads as temperature at a glance — the blue side is cold, the orange side is hot — without
a legend and without anyone writing a `style` directive. That is `R-26`'s "coloring should be fluid and
HVAC themed" doing actual work, and it sits beside an editor that looks like every other editor,
which is the split this document exists to hold.

## Acceptance criteria

- [ ] A test asserts no literal colour appears outside the theme files.
- [ ] Every token is present in both built-in themes.
- [ ] WCAG AA contrast passes for every text/surface pair in both themes.
- [ ] Light+ unit suffixes remain fully opaque and meet 4.5:1; Dark+ unit suffixes at 75 % also meet
      4.5:1 against the editor background.
- [ ] Every keyboard-focusable primitive uses `--focus-ring`, which meets 3:1 on adjacent surfaces.
- [ ] With `prefers-reduced-motion: reduce`, all nonessential transitions are 0 ms and playback state
      remains fully available through text and frame position.
- [ ] The syntax palette matches the VS Code table exactly, asserted against the hex values.
- [ ] A script screenshot in dark theme is visually indistinguishable from VS Code's colouring of the
      equivalent tokens.
- [ ] Theme switching loses no editor or canvas state.
- [ ] Numeric readouts do not shift horizontally during playback.
- [ ] Forcing the monospace and UI stacks to a deliberately wider fallback changes no placement in any
      prepared scene; the affected labels overflow their own reserved boxes and nothing else moves.
- [ ] A malformed custom theme leaves the current theme intact.
- [ ] Solved temperature colouring is on by default, can be disabled without changing the script, and
      uses a text/legend cue so changing colour cannot be mistaken for topology instability.

## Open questions

None. The table pins Light+/Dark+ token values rather than tracking future VS Code defaults. Solved
temperature colouring is enabled by default with a session toggle. `R-27` means restrained responsive
feedback—soft colour, rounded geometry, short motion—not illustrated or character-like symbols
(`D-30`).
