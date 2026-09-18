---
id: 50-frontend-defects
title: What implementing against the frontend tier found
tier: 50-frontend
owns: [defect and observation record for documents 51-59]
---

# What implementing against the frontend tier found

Defects, deferrals and observations from implementing against `51`–`59`. The rule and its reasoning
are in [`08-implementation-sequence`](../08-implementation-sequence.md).

`55` was implemented by `P5.3` (2026-09-18): the tokens, the two themes, the generated cascade,
custom theme files, eight of the twelve primitives, and the design tests. `51` was read for the
file layout and the UI store, and one field of that store exists. **Nothing has looked at `52`–`54`
or `56`–`59`**; their absence below means nothing has looked, not that nothing is wrong.

## Open

| # | Document | What | Why it is still open |
|---|---|---|---|
| F-1 | [`55`](55-design-system.md) | **The literal scan reads CSS, not TSX attributes** | `55` invariant 1 forbids a literal colour, size or duration outside the token definitions, and `63` lists the check with the architecture tests. The test scans every `.ts`, `.tsx` and `.css` under `src/` for a colour in any form, and `.css` for a `px`/`ms`/`em` literal; a size in a TSX attribute -- `<svg width="240">`, an inline `style={{ width: 240 }}` -- passes. Colours in TSX are caught (the preview's SVG strokes are `var(--…)` for that reason); sizes in TSX are a review matter until a canvas exists to say which attributes are geometry (Core's numbers, which are not tokens) and which are presentation. Filed 2026-09-18 with P5.3. |
| F-2 | [`55`](55-design-system.md), [`53`](53-canvas-renderer.md) | **The proportional label metric is a reservation, not a measurement** | `D-73` has the canvas label's box come from a declared advance width. The monospace readout's 0.6 em is the largest of the stack's real metrics; the label's 0.62 em is what a tag of capitals and digits in Segoe UI needs, with nothing behind it but that estimate, because no label has been laid out yet. When P5.6 draws the first labelled scene, measure the widest sample tag in each font of the stack against `0.62 × characters × 11 px` and move the number if one overflows. |

## Closed

| # | Document | What was wrong | What changed |
|---|---|---|---|
| F-3 | [`55`](55-design-system.md) | **The token list left out the editor's ground, its foreground and the function role** | `55`'s syntax table has thirteen rows, including the editor background and foreground and a function colour, and the `:root` block above it had eleven `--syn-*` names and no editor pair. A theme file written to the list could not be the dark theme: `#1E1E1E` had no token to live in. Found 2026-09-18 writing `tokens.ts`. | `--syn-function`, `--editor-bg` and `--editor-fg` are in the list and in both themes; the invariant-2 test would refuse a theme without them. |

## Observations

**The values `55` did not give were chosen to pass the contrast test, and that is their only
basis.** `55` fixes the fluid, status and syntax palettes to the hex. The surfaces, text, borders,
focus ring and canvas colours it names without valuing; P5.3 valued them as a cool grey ramp and
moved `--text-muted` and `--text-secondary` until every declared pair cleared 4.5:1 in both themes
(`#586672` on `#EEF1F4` is 4.6:1 in light; `#95A2AF` on `#262F3A` is 4.9:1 in dark). They are
the numbers to argue with when the product is looked at, and the test is what keeps an argument
from breaking readability.

**A custom theme's fallback is the built-in that was selected, not always light.** `55` says a
missing token "falls back to the built-in value per token" without saying which built-in. A user
who writes a dark theme and leaves out the borders wants dark borders; P5.3 fills from dark when
Dark was the selection at load time and from light otherwise, and the toolbar note says which.

**The generated stylesheet is written the way Prettier would write it.** The gate compares the file
to the renderer's output byte for byte, and `format` runs Prettier over `src/`, so a renderer that
wrote `#FAFBFC` or double-quoted strings would flip on every format. Lower-case hex and single
quotes in the renderer; the theme files keep `55`'s upper case, which is data, not style.

**The preview page is scaffolding.** `features/theme/ThemePreview.tsx` shows the fluid stops, the
badges, `55`'s worked example coloured with the syntax classes, and three strokes on a canvas
ground, so a theme can be judged before the editor and canvas exist. P5.4's shell replaces it; the
`.syn-*` classes in `theme.css` are the names `52` binds the Lezer tokens to and stay.
