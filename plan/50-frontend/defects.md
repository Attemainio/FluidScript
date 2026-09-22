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
custom theme files, eight of the twelve primitives, and the design tests. `51` by `P5.4` the same
day: the shell, the four stores, the typed client and the generated wire types, the debounce
pipeline with its validate phase. `52` by `P5.5` the same day: the editor with its tokenizer,
diagnostics, completion, the formatter's command and the benchmark's harness. `53` by `P5.6` the
same day: the scene, the viewport, the symbols, routes, labels and marks, with `57`'s flat fill
pulled forward. `56` and the hover and selection half of `54` by `P5.8` the same day. `58` by `P5.9`
and `57` by `P5.10`, both the same day. `59` by `P5.11` (2026-09-19), with the accessibility pass
over `53`. **Nothing has looked at `54`'s editing half**; its absence below means nothing has
looked, not that nothing is wrong.

**Next id: `U-11`.** The columns, their vocabularies, and the rule for filing, reopening and closing
are in [`08`](../08-implementation-sequence.md) under *Every package writes down what it found*.

## Open

| # | Filed | Effort | Risk | Basis | Document | What | Why it is still open |
|---|---|---|---|---|---|---|---|
| U-10 | 2026-09-20 | medium | med | hunch | [`59`](59-static-export.md), [`53`](53-canvas-renderer.md), [`62`](../60-docs-and-devex/62-testing-strategy.md), [`07`](../00-foundation/07-quality-attributes.md) | **P5.11's browser-only checks have not run: the PNG pixel comparison, the four-viewer opening test, axe with layout, 200 % zoom, a screen reader's reading, and the panning budget** | `59` A1 wants the SVG opened in Chrome, Edge, Firefox and Inkscape and A2 the PNG compared pixel-wise to a rasterized golden; `62` wants Playwright running axe plus keyboard-only, screen-reader smoke, 200 % zoom, reduced-motion and focus-order scenarios; `07` wants 50 fps p95 panning the 200-component fixture. None of that runs where this was built: no browser launches in the environment (`U-4`) and no 200-component golden exists on the frontend side. What did run, 2026-09-19: the export goldens and every structural criterion in `export.test.ts`; the SVG rasterized by cairo and once by hand by the Windows Edge through `<img>` and `drawImage`, identically; axe-core under jsdom over the mounted app with a solved plant, clean once the status line's landmark and the editor's accessible name were fixed (`Accessibility.test.tsx`); contrast from the theme files (`themes.test.ts`); Node's prepare-and-render of the 24-placement header at 4.8 ms, which extrapolates to ~40 ms at 200 placements and says the 8 ms commit budget is not met by extrapolation. What closes it: a Playwright run against a real browser (the config and `e2e/` exist) with the scenarios `62` lists, the PNG comparison, Inkscape by hand once, and a 200-component sample compiled into an Api golden so the frontend can measure what `07` asks. |
| U-8 | 2026-09-20 | small | med | hunch | [`58`](58-file-lifecycle.md) | **The File System Access path has not run in a real browser** | P5.9 built both file paths against one `FileBackend` interface and ran every `58` scenario against a fake disk on each. The fallback (`<input type=file>`, a download link) is the one jsdom can exercise; the native path -- `showOpenFilePicker`, `showSaveFilePicker`, `createWritable`, `queryPermission`/`requestPermission`, a handle read back from IndexedDB after a reload -- needs a user gesture in Chromium, which no headless run here can give (the same wall as `U-4`). `NativeBackend` is forty lines against a documented API and may still be wrong in a way the fake cannot show: the writable's `abort` on failure, the `AbortError` a cancelled picker throws, the permission state after a reload. What closes it: a session in Chromium opening, saving, reloading and reopening a file, with what was seen written into `58`. Filed 2026-09-18 with P5.9. |
| U-9 | 2026-09-20 | small | low | hunch | [`58`](58-file-lifecycle.md) | **Re-permission after a reload is a gesture `58` did not design** | A persisted handle needs `requestPermission` before it can be read or written, and the browser only grants it from a user gesture, so a document on disk cannot be reopened silently at launch. P5.9 shows a *Reopen file* notice and reads the file when it is pressed; a draft newer than the file then becomes the divergence choice. The wording, the notice's place and what happens to a tab the user never reopens (it stays, empty, with its notice) are the frontend's own; `58`'s recovery section assumes the file is readable. What closes it: the user's judgement of the notice in use, or a `58` paragraph that specifies the flow. Filed 2026-09-18 with P5.9. |
| U-4 | 2026-09-20 | small | low | hunch | [`51`](51-frontend-architecture.md), [`D-49`](../00-foundation/06-decision-log.md) | **The debounce is 300 ms by default, not by measurement** | `D-49` makes the debounce a measured value with a 200 ms typing-cadence floor and `D-48`'s gate as its ceiling, recorded as a baseline. P5.4 ships `51`'s recorded default of 300 ms in `pipeline/debounce.ts` because the measurement is `D-48`'s keystroke-to-squiggle benchmark, which is Playwright driving the real editor, and the editor is P5.5. P5.5 built the benchmark (`frontend/e2e/latency.bench.ts`, `npm run bench`, `62`) and could not run it: the machine it was built on has no browser Playwright can launch (Chromium's headless shell wants `libnspr4`/`libnss3`, which need root to install, and the Windows-side Edge cannot be reached from WSL without exposing a debugging port). What closes it: `npx playwright install-deps chromium` or the apt equivalent on a machine with root, then `npm run bench` on the syntax tour and the 200-declaration script, the value moved inside the bounds, and the baseline recorded from `diagnostics/keystroke-latency.md`. A value that lands more than a factor of two from 300 ms is `D-49`'s requirement conversation, not a benchmark result. |
| U-7 | 2026-09-20 | medium | low | measured | [`53`](53-canvas-renderer.md), [`25`](../20-core-domain/25-layout-hints.md), [`D-30`](../00-foundation/06-decision-log.md) | **Nobody folds: `FS2402` says a group starts collapsed and nothing collapses it** | `25` has Core report `FS2402` when a pipe's expansion passes ten members or a scene five hundred elements, "starts folded", and `53`'s error cases and `D-30`'s thresholds have the renderer collapse every collapsible group at that point so a large plant meets `07`'s budgets. Core reports the code and lays every member out; P5.6 draws every placement it is given and has no notion of a group, so a discretized pipe with `nodes=40` shows forty inline nodes at 3× and forty placements at every zoom. Nothing is wrong at the sample sizes, and the level of detail hides the inline names below 3×. What is missing is the fold itself, on whichever side owns it: the layout can place a folded group as one inline element (the frontend then knows nothing), or the renderer can hide the members and draw the parent's label with a count. The first keeps `53` invariant 2 intact and is where I would put it. Filed 2026-09-18 with P5.6. |
| U-2 | 2026-09-20 | small | low | hunch | [`55`](55-design-system.md), [`53`](53-canvas-renderer.md) | **The proportional label metric is a reservation, not a measurement** | `D-73` has the canvas label's box come from a declared advance width. The monospace readout's 0.6 em is the largest of the stack's real metrics; the label's 0.62 em is what a tag of capitals and digits in Segoe UI needs, with nothing behind it but that estimate, because no label has been laid out yet. When P5.6 draws the first labelled scene, measure the widest sample tag in each font of the stack against `0.62 × characters × 11 px` and move the number if one overflows. |

## Traps

What a session working against these documents gets wrong first. Promoted from *Observations*
or from a closed entry when a session hits it a second time; read before working in this tier.

_None promoted yet._

## Closed

| # | Filed | Closed | Effort | Document | What was wrong | What changed |
|---|---|---|---|---|---|---|
| U-5 | 2026-09-20 | 2026-09-22 | medium | [`52`](52-editor.md), [`26`](../20-core-domain/26-model-contract.md), [`15`](../10-language/15-semantic-model.md) | **A deferred `let` has no dimension on the wire, so completion offers it unfiltered** | The binder now types a deferred expression without evaluating it (`BindingRun.DimensionOf`): a quantity literal or a reference's written unit types by its spelling, a component property by the registry's dimension (a stated parameter by its parameter's), a `let` by its own value or, deferred, by its expression with a cycle guard, a constant by its own; a product or quotient combines exponent vectors through `FromVector`, a sum keeps the dimensioned side over a bare number and resolves a shared pressure spelling to the difference; a curve, a call or an unresolved name leaves it unknown. `BindingSymbol.Dimension` carries it, `BindingWire.Dimension` sends it for a deferred binding too, and completion filters a typed deferred `let` like any other while an untyped one stays offered everywhere, dimmed. Measured: `let x = 1.2*HE1.dp` types `PressureDelta`, `x/2 + 5 kPa` too, `HE1.power/(4180 J/(kg*K) * 20 dK)` types `MassFlow`; `BinderTests`, `ModelContractBuilderTests` and `completion.test.ts` hold it, `52`'s criterion is ticked. Effort as estimated. |
| U-6 | 2026-09-20 | 2026-09-22 | tiny | [`52`](52-editor.md) | **An ambiguous kind lists both candidates, but the first is preselected** | CodeMirror preselects the first option of every list and `selectOnOpen` is global, so `sensr` -- `p_sensor` and `t_sensor` within `D-15`'s margin -- listed both and Enter inserted the first, which is the choice the compiler refuses to make. `@codemirror/autocomplete` has no per-result way to open unselected, so the row's other closure was taken: where `complete` marks the pair ambiguous, `toOptions` (new `completion/options.ts`, which now owns the item-to-option mapping the source had inline) puts the typed text itself first, boosted, with a detail saying it matches two kinds equally and an `apply` that inserts nothing. Enter leaves the text as typed; an arrow key reaches the pair. `completion.test.ts` holds the header on the ambiguous input it finds and its absence on `pmp`; `52`'s criterion is ticked. Effort as estimated. |
| U-1 | 2026-09-20 | 2026-09-20 | — | [`55`](55-design-system.md) | **The literal scan reads CSS, not TSX attributes** | Changed 2026-09-19 (sweep tier 2), now that the canvas exists to say which attributes are geometry: the scan reads `.tsx` for a presentational size -- `width`/`height`/`strokeWidth`/`fontSize`/`size` given a literal number, and an inline style whose box, spacing, font or border property is -- and passes geometry (`x`, `y`, `r`, a `viewBox`: Core's number mapped to pixels, `D-103`) and zeros (a measured box before its first measurement). It found the axes' `strokeWidth={1.5}`, the grid's `strokeWidth={1}` and four `font-size: Npx` in the export's own stylesheet; the strokes moved to `scene.css`/`shell.css` under a new `--stroke-axis` token and `--hairline`, the export's sizes to the type-scale tokens (`--text-label-size`, `--text-readout-size`, `--text-heading-weight`), which moved the legend tick from 10 px to 11 px in the goldens. `worldStrokes` accepts a `px` suffix on a stylesheet width so the axes export at the right world width. |
| U-3 | 2026-09-20 | 2026-09-20 | — | [`55`](55-design-system.md) | **The token list left out the editor's ground, its foreground and the function role** | `55`'s syntax table has thirteen rows, including the editor background and foreground and a function colour, and the `:root` block above it had eleven `--syn-*` names and no editor pair. A theme file written to the list could not be the dark theme: `#1E1E1E` had no token to live in. Found 2026-09-18 writing `tokens.ts`. | `--syn-function`, `--editor-bg` and `--editor-fg` are in the list and in both themes; the invariant-2 test would refuse a theme without them. |

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

**The editor is a text area and the canvas is a list, on purpose.** `51`'s acceptance list is
about the request stream -- one in flight, no stale overwrite, the validate phase, the model kept
through a failure -- and a text area drives that stream exactly as CodeMirror will. What P5.4 could
not do without the editor is measure the debounce (`U-4`) and hold one CodeMirror state per document;
the text lives in `features/editor/documents.ts`, a map outside React state, which is the shape
`51` invariant 1 asks for and what a state-per-document becomes. The canvas pane prints component
and connection counts and the component list, enough to see the last model survive a failed
compile; P5.6 draws.

**The log and the status line are the shell's slots, not `56`'s log.** `51`'s shell has both and
P5.8 owns `56`: lifecycle, grouping, filtering, the header's `solved in 14 ms`. P5.4's `LogPane`
lists the active document's diagnostics with `56`'s glyphs and its fold persists; `StatusLine` is
`51`'s four states with a distinct glyph and word each, naming the computation and the document.
P5.8 replaces the list's body and keeps the slot.

**The generated `Symbol` interface shadows the global.** `26`'s symbol definition is `Symbol` on
the wire, and `json-schema-to-typescript` emits `export interface Symbol` from the title. Inside a
module that is legal and the global is only shadowed there; `api/types.ts` re-exports it as
`SymbolDefinition`, and every consumer imports from that module rather than from the generated
file, so the name is seen once.

**The preview page was scaffolding**, and P5.4's shell replaced it the same day; the `.syn-*`
classes in `theme.css` are the names `52` binds the tokenizer's roles to and stay.

**The editor's grammar is a stream tokenizer, not a Lezer grammar, and `52` now says why.** The
short form: `12`'s word classification is a longest-match lookup in the unit table, which a
generated Lezer automaton cannot express and an external tokenizer would hand-write anyway; the
tree a grammar would give buys folding and bracket matching, neither in `52`. The cost that is real
is that a Lezer grammar would have been a second, checkable statement of `12`; the stream tokenizer
is a port of the lexer's rules with the same corpus test (`TokenGoldenTests` on both sides), so the
check is there and the statement is not. If a later package wants the tree, the tokenizer is the
external tokenizer it would wrap.

**Two test hooks reach into the editor, and neither is a React prop.** The shell test and the
benchmark both need the live `EditorView`, which lives in a ref inside `EditorPane`. The test's way
in is `activeView.ts`, a registry the pane writes to when it mounts (`activeEditorView()`); the
benchmark's is a dev-only object on `window` (`window.fluidscript`) that the pane sets under
`import.meta.env.DEV`, never in a build; the same block reads `?script=` from the URL so a headless
screenshot can open on a real document, which is how the product was first looked at. Both exist because `documents.ts` holds a state per
document outside React (`51` invariant 1) and the pipeline's handler is registered once and looked
up at dispatch time (`registerEditorHandler`), so that a state created in one test cannot capture
the pipeline of another. The first shape, states capturing the pane's pipeline in a closure, passed
alone and failed in a suite, which is how it was found.

**The exchanger's label sits on its top edge.** The symbol's `labelAnchor` is `[0, 0.65]` on a box
whose top is `0.5`, so the label's baseline is 0.15 world units above the box, 9 px at 1×, and the
port marker at the top anchor sits in the same gap. In the first pictures (2026-09-18, the
substation and the header) `100HE02` reads as touching the symbol while `100PU01`, at 0.65 over a
0.5 top on a round symbol, reads clear. The number is the catalogue's (`SymbolCatalog`, Core), the
renderer draws where it is told (`53` invariant 2), and P5.6b's screenshot pass is where it gets
judged; a change is one number in Core and every golden.

**The state fill is `color-mix`, and the literal scan allows it in one file.** `57`'s ramp is the
five fluid stops of `55`; the fill for a scale position mixes the two neighbouring stops with
`color-mix(in oklab, …)`, which the literal scan's colour pattern catches, so `fluidFill` lives in
`design/tokens.ts`, the one file the scan exempts. That is the right home: the ramp is a palette
definition, not a use of one. What it costs is that a test cannot resolve the colour without a
browser; the golden pins the expression.

**The log's reconciliation is React's key, and the test asserts the element.** `56` asks that an
unchanged entry not move or re-render between compiles. A list keyed by code and component gives
that for free: React keeps the element for a key that is still there, and the leaving entries are
kept in state for 150 ms with a class so the fade can run. The interaction test holds the element
across four compiles and asserts identity, which is the property, not a proxy for it.

**Selection carries its origin so a pane does not answer itself.** The editor selects when the
caret lands on a declaration and scrolls when the selection came from elsewhere; without the
origin, a caret move would scroll the editor to the line the caret is on, and a canvas click
would move the caret. The store records who changed it last; the only reader of that field is the
editor.

**The metadata's parameter order was the hash order of a dictionary.** Committing the metadata
document as a golden for the completion tests (`A-5`) showed it: two runs of the Api host listed
a kind's parameters in different orders, so the ETag differed between processes for the same
registry. Ordered by name now, on the Api side; the frontend never sorted, and its completion
ranking is its own.

**A parameter name is no longer one token, and the editor had three places that assumed it was**
(P5.13a, `D-120`, 2026-09-20). The tokenizer classed the identifier before `=` as a parameter, so in
`in[2].t=85` only `t` was one and `in` fell to the reference rule (`.` after a word); the completion's
"already written" set collected the same single token, so `in.t=40` marked `t` as written and offered
`in.t` again; and the value filter after `=` looked `t` up as the parameter, which no kind has. All
three now run a name back from the `=` over words, dots, brackets and the index between them while
the tokens touch -- the same adjacency the parser demands -- and the `written` set, the highlight and
the dimension filter agree on `in[2].t`. Two things were added rather than fixed: after `in.` or
`in[2].` on a declaration line the completion offers the quantities that port takes (`t`; `t`,
`flow`, `dp`, `dt` on an exchanger's second inlet; `level` on a tank's), which is the rule the
[syntax page](../../docs/functions/syntax.md) teaches and was not derivable from the kind's flat
parameter list before; and the port completion after `T1.` spells a family member from the wire's new
`pattern` (`in[{index}]`) rather than concatenating prefix and digits, since the model's port ids stay
`in2` and the script must not. The tokenizer's rule-5 clause grew with Core's (`[`, `.`+word).
`velocity`, `reynolds`, `viscosity`, `cp` and `k` from `57`'s table are still not quantities the
table knows, and `show velocity` is `FS1210`; `57` now says so. A register finding on the way: this
file and `00-foundation/defects.md` both number their entries `F-n` (`U-16` is a foundation entry,
`U-10` a frontend one), and `09` cites both; one of them should carry a different letter before a
third reader trips on it.
