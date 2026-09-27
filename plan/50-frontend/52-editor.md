---
id: 52-editor
title: Script editor
tier: 50-frontend
status: implemented
owns: [editor component, syntax highlighting, completion, inline diagnostics, quick fixes, editor commands]
depends_on: [12-grammar, 44-diagnostics-contract, 51-frontend-architecture]
traces_to: [R-01, R-05, R-20, R-21, R-25, R-33, R-38, R-39, R-42, R-45]
open_questions: 0
last_review_pass: 0
---

# Script editor

## Purpose

Where the user actually works. The editor's job is to make FluidScript's density
([`11-language-overview`](../10-language/11-language-overview.md)'s principle P1) feel like an
advantage rather than a memory test: completion supplies the parameter names, inline diagnostics catch
the mistakes, and hover explains what a number means.

## Responsibilities

**Owns.** The editor component, syntax highlighting, completion, inline diagnostics, quick fixes, and
editor commands.

**Explicitly does not own.** The grammar ([`12-grammar`](../10-language/12-grammar.md)), the diagnostic
shape ([`44-diagnostics-contract`](../40-api/44-diagnostics-contract.md)), the debounce pipeline
([`51-frontend-architecture`](51-frontend-architecture.md)), write-back into the document
([`54-interaction-and-writeback`](54-interaction-and-writeback.md)).

## Editor choice

**CodeMirror 6.** Reasons, in order:

1. **Bundle size.** ~150 kB versus Monaco's ~2 MB. For a tool whose whole appeal is feeling light
   (`R-27`), a two-megabyte editor is a poor opening move.
2. **A real Lezer grammar** gives incremental parsing for highlighting, which is what makes
   highlighting instant without a round trip.
3. **Composable extensions** — highlighting, linting, completion, and decorations are independent, so
   FluidScript's specifics do not fight a monolith.
4. Monaco's advantage is its LSP integration, and there is no language server here.

## Syntax highlighting

**Client-side, from a tokenizer mirroring [`12-grammar`](../10-language/12-grammar.md).**

This is a second implementation of the grammar, which violates the instinct to have one. It is the
right trade: highlighting must be instant on every keystroke, and a round trip is not instant. The
mitigation is that the client's grammar only needs to be *lexically* correct — it classifies tokens, it
does not bind or validate — so it cannot disagree with the server about anything that matters.
Divergence shows as a mis-coloured token, not a wrong result.

**As built (P5.5, 2026-09-18; rebuilt on the blocks in P6.11 package 8, 2026-09-27): a CodeMirror
`StreamLanguage`, not a Lezer grammar.** The tokenizer is `features/editor/language/tokenizer.ts`, a
line-at-a-time port of Core's lexer (maximal munch on the unit table, `3WV` as a name, `30 kW` as one
quantity, `..`, a date or clock time as one token, no reserved words, no unit before an `=` even past
spaces) with the **open blocks** carried as the stream state -- a stack of heads and their indentation,
closed by a line no deeper than its head (`19` §Lines, blocks and names) -- and roles assigned the way
`LineParser.Classify` reads a line: a statement word at the start opens its statement, otherwise
the qualified name the line starts with is followed by `=` for a setting, `-` for a connection, anything
else for a declaration. A Lezer grammar would need `12`'s word classification, which is a
lookup in the unit table and a longest-match over it, and Lezer's tokenizer is a generated
automaton with no table lookup; the external-tokenizer escape hatch is the same hand-written code
with the grammar wrapped around it. What the stream tokenizer gives up is the syntax tree: folding,
bracket matching by tree and a tree-driven completion context. None of those is in this document's
scope, and completion reads the line, which is what `12`'s one-line-one-statement invariant makes
sufficient. The statement words, the event words, the unit symbols a script may write and `D-15`'s
thresholds come from the host's committed `language.json` (`LexiconWire`), generated into
`lexicon.generated.ts` and gated by a test, so the editor's lexicon cannot drift from the compiler's
(invariant 5). The corpus agreement test is
`TokenGoldenTests` on the Core side, writing one golden per sample with every token's offset, length
and kind, and `tokenizer.test.ts` on the frontend side, replaying the same samples through the
tokenizer against those goldens.

**Colours are the Visual Studio / VS Code palette**, mapped token by token in
[`55-design-system`](55-design-system.md). That is a deliberate departure from the HVAC palette used
everywhere else in the app: a keyword that is not blue reads as *wrong* before it reads as *different*,
and the editor is the one surface where familiarity beats character. The table below names the token
roles; `55` holds the hex values for both themes.

| Token | Style |
|---|---|
| Statement word (`project`, `circuit`, `let`, `curve`, `run`; `at` and `over` in a run) | Keyword colour, medium weight |
| Component kind | Type colour |
| Identifier in declaration position | Emphasised — it is a name being introduced |
| Parameter name | Muted |
| Number / quantity | Number colour, with the unit suffix slightly dimmed |
| Unit symbol | Dimmed |
| Comment (`#` to end of line, `D-13`) | Comment colour, italic |
| Connection `-` | Punctuation |

**Dimming the unit suffix** — `30`**`kW`** — is a small thing that pays: it makes the number scannable
while keeping the unit legible, in a language where columns of numbers are the normal shape.

## Completion

Driven by `/api/v1/metadata` ([`42-rest-contract`](../40-api/42-rest-contract.md)), fetched once and
cached, plus the current compile's symbol table for anything the user has written. Completion is
contextual on the cursor's syntactic position, read as the parser reads a line (`19`): the
block the line sits in, then what its first name is followed by.

| Position | Offers |
|---|---|
| Start of a line, top level | The statement words (`fluidscript`, `project`, `let`, `curve`, `circuit`, `run`) |
| Start of a line in a `project`, `circuit`, `run` or `style:` block | That block's settings from the metadata's `blocks`, less those written; in a circuit also every component name (a connection starts there); in a run also `at`, `over`, the `let`s and the components (overrides) |
| Start of a line in a declaration block | That kind's parameters, less those written; for a controller, the settings its `type` takes (`D-168`) |
| Second token of a declaration | Every component kind, matched **alias-aware** — see below |
| After a kind or a parameter's value | That kind's remaining parameters (a controller's settings), with dimension and range |
| After `name =` | **Dimension-filtered** for a quantity: `let` names, `Component.property` references, and unit symbols, all restricted to the dimension; a setting's words (`corner`, `type`), the build's sets (substances, circuit roles, catalogues), or the script's own (its cases, circuits, curves, actuators, sensors), by the setting's `valueKind` |
| After `-` in a connection | Every declared and inferred component name |
| After `Name.` at a connection's end | The component's ports as a script spells them (`in`, `secondary.in`, `primary.in`); after `HX1.secondary.`, the rest |
| After a connection's chain | The pipe's parameters (`roughness = 0.05 mm`) |
| After `Name.` in a value | That component's properties |
| After a port on a declaration, `secondary.in.` | That port's quantities, less those written |
| In a run, after an event's time | Component names; after `Name.`, its settable parameters (a controller's `setpoint`); after `=`, a value of the target's dimension |
| After `curve NAME:` | The `let`s and `time`, the drivers a curve reads |

Indexed tank metadata is pattern-based (`D-32`). After `T1.` the editor offers already materialized ports plus
`in{1..16}`/`out{1..16}` templates; after `T1 tank` it offers `volume`, `layers`, `t`, the valid
`t1`…`tN` profile members for the resolved layer count, and elevation templates. Typing alias `v`
offers canonical `volume`; Tab inserts `volume`, while text typed without completion remains `v`.

**Completion detail shows the parameter's dimension, canonical unit, and range** — `power · Power ·
kW · typically 1…10000`. That turns completion into documentation and removes most of the reason to
leave the editor.

**Inferred component names are offered in connection position**, marked as inferred. A user who wants
to reference `HE1__3WV` should not have to type it from memory.

**Every setting comes from the metadata** (invariant 5): `blocks` carries each block's settings from
`SettingRegistry`, the table the reader checks a setting against, so the editor cannot offer a setting the
reader refuses (`L-86`, `A-9`). The script's own names -- a `cases` list, its curves, its circuits -- are read
from the document or the model, never from a list in the editor.

### Kind completion is alias-aware, and Tab commits the canonical spelling

`D-15` gave the binder three ways to reach a kind: normalisation, curated aliases, and similarity.
`D-170` keeps the first two as binding and makes the third a suggestion: a near spelling is `FS1502` with
the near kind as its fix. Completion sees all three -- the first two to offer what binds, the third to
offer the fix -- and, like the binder, reads a merely similar kind as no kind at all.

**Matching.** The typed prefix is normalised the same way the binder normalises — lowercased, `_` and
spaces removed — and matched against every kind's normalised keyword **and every normalised alias**.
So `heat_ex`, `heatex`, `HeatEx` and `exch` all reach `heat_exchanger`; `3w` and `mix` reach
`three_way_valve`; `rad` reaches `heat_exchanger` through the `radiator` alias.

**Tab commits the canonical keyword, never the alias that matched.** Typing `heat_ex` and pressing
Tab inserts `heat_exchanger`. Typing `rad` and pressing Tab inserts `heat_exchanger`, with the
matched alias shown in the completion detail so the substitution is not a surprise:

```
heat_ex│
  ┌────────────────────────────────────────────────────────┐
  │ heat_exchanger    Heat source, consumer, or exchanger  │
  │ heat_exchanger    via 'heater'                         │
  └────────────────────────────────────────────────────────┘
        Tab → heat_exchanger
```

**Why the canonical form rather than what the user typed**, when `D-15` would accept either: an alias
that survives into the file is a spelling the next reader has to resolve, and the printer never emits
one ([`15-semantic-model`](../10-language/15-semantic-model.md) invariant 10). Committing the
canonical form on Tab means aliases do their job — getting the user unstuck — without accumulating in
the source. A user who deliberately wants `radiator` in their text types it in full and never opens
completion; nothing rewrites it.

**Ranking**, since `heat_ex` matches one kind but `v` matches several:

1. Exact normalised match on the canonical keyword.
2. Prefix match on the canonical keyword, shortest keyword first.
3. Exact or prefix match on an alias, shown as `via '{alias}'`.
4. Similarity above `D-15`'s 0.70 threshold, ordered by score, shown as `did you mean …?`.

Rank 4 exists so that completion and the compiler agree: `pmp` is `FS1502` with `pump` as its fix
(`D-170`), so `pmp` *completes* to `pump` -- the fix, offered where it is typed -- while the component
written `P1 pmp` has no kind, for the editor as for the binder, and its parameters are not offered.

**Ambiguity is shown, not resolved.** Where `D-15`'s 0.05 margin would produce `FS1513`, completion
lists both candidates adjacent and picks neither by default — the editor must not make a choice the
compiler refuses to make.

### Value completion is dimension-filtered

After `param=`, the parameter's dimension is known from the registry, and everything offered is
filtered to it. This is the rule that makes `let` bindings pay for themselves.

```
let dTdesign = 20 K
let Tflow    = 70 C
let Qtotal   = 120 kW

RAD1 heat_exchanger power = │
```

At the cursor, `power` is `Power`, so:

| Offered | Not offered | Why |
|---|---|---|
| `Qtotal` — `120 kW` | `Tflow`, `dTdesign` | Wrong dimension |
| `BLR.power`, `LOAD.power` | `P1.dp`, `N2.t` | Wrong dimension |
| `kW`, `W`, `MW`, `hp` | `K`, `C`, `m` | Not `Power` symbols |

And at `in.t = │`, `Tflow` is offered and `dTdesign` is not — the distinction `13`'s two temperature
dimensions exist to make, surfaced at the moment it matters rather than as an `FS1302` afterwards.

**Filtering is on the dimension, not on the unit.** `power = 30000 W` and `power = 30` are both `Power`, so
both `W` and `kW` are offered; what is excluded is `K`, which would be a different dimension and a real
error. Filtering on the *canonical* unit instead would hide the explicit-unit escape hatch `D-07`
exists to provide.

**Unnamed dimensions are offered with a warning marker, not hidden.** A `let` whose expression produced
an unnamed dimension — `Q / dT`, which is W/K — is legal inside an expression and only fails when
stored ([`13`](../10-language/13-type-and-unit-system.md)'s `FS1304`). Completion shows it dimmed with
its derived unit, because a user who wrote it probably meant it somewhere and hiding it makes the
binding look like it failed to evaluate.

**Deferred `let`s are offered too**, marked as such. `let x = 1.2*HE1.dp` has no value until the solve,
but it has a *dimension* as soon as `HE1.dp`'s dimension is known, which is at bind time. Offering it
with its dimension and `—` for the value is more useful than omitting it, and it is the difference
between completion that works while the script is broken and completion that only works when it is not.

**Where the dimension is unknown, everything is offered.** A parameter on a component whose kind failed
to resolve has no registry entry, so no filter is possible. Completion falls back to every `let` and
every property, unfiltered, rather than offering nothing — principle P4 applied to the editor: a
half-written script is the normal state, and the editor's job is to stay useful in it.

### The two halves compose

The kind completion and the value completion are what make an aliased, `let`-heavy script writable
without memorising anything:

```
let Tsupply = 70 C

BLR heat_ex⇥ power = 150 out.t = Ts⇥
     └─ heat_exchanger                   └─ Tsupply · Temperature · 70 °C
```

Two Tab presses, no documentation, and the result is the canonical spelling with a dimensionally
correct reference. That is the same argument `D-15` makes for aliases in the binder, made one layer
earlier where it costs the user nothing at all.

## Inline diagnostics

From the compile response ([`44`](../40-api/44-diagnostics-contract.md)):

- **Error** — red wavy underline over the range.
- **Warning** — amber wavy underline.
- **Info** — no mark ([`44`](../40-api/44-diagnostics-contract.md)'s severity mapping).
- **Hover** shows the message, the code, and `related` locations as links.
- **Quick fix** — a lightbulb where `suggestion` is present; applying it is one undoable edit.

**Diagnostics are applied as a decoration set replaced wholesale per compile**, not incrementally
patched. Simpler, and it cannot leave a stale squiggle behind — which is the failure users notice.

**Squiggles persist across the debounce gap.** A stale squiggle for one debounce interval is much
better than flickering them off and on with every keystroke. `D-49` leans on this: because flicker
is already handled here, flicker is not what bounds how short the debounce may be — the bound is
that a half-typed token must not be reported as wrong.

## Hover

Two kinds:

| Hovering | Shows |
|---|---|
| A diagnostic range | The message, code, and related locations |
| A component name | Its kind, its resolved parameters with `stated`/`sized`/`default`, and its solved state |
| A parameter name | Dimension, canonical unit, range, and how omission resolves (`sized` or `default`), including its basis |
| A `let` name | Its evaluated value |
| A quantity | Its value in SI and in alternative units |

The component hover is the same data as the canvas hover ([`54`](54-interaction-and-writeback.md)),
sourced from the same model. One implementation, two mount points.

**As built:** P5.5 ships the diagnostic hover; P5.8 the component hover (the canvas's card,
`54`), the `let` hover (value, unit and dimension, or that it is deferred) and the quantity hover.
The quantity hover names the dimension and the canonical unit and, since `A-6` closed (2026-09-22),
converts to SI and to every other unit of the dimension from the factors and offsets
`metadata.dimensions[].conversions` carries -- `13`'s table travels on the wire, so no second copy
exists on the client (`quantityCard` in `hover/card.ts`). The parameter hover is the completion
detail's information and is left to completion.

## Commands

| Command | Binding | Behaviour |
|---|---|---|
| Format | `Shift+Alt+F` | The formatter ([`17`](../10-language/17-formatting-and-round-trip.md)), one undoable edit |
| Rename | `F2` | Renames a component and every reference, via `/api/v1/edit` |
| Go to definition | `Ctrl+Click` | Jumps to a component's declaration; nothing for an inferred one |
| Toggle comment | `Ctrl+/` | Inserts or removes `# ` at the line start |
| Run | `Ctrl+Enter` | Starts a transient run |
| Solve | `Ctrl+Shift+Enter` | Explicit steady solve (stricter than compile) |
| Save | `Ctrl+S` | Saves the named `.fluid` file, or opens Save As when unnamed (`58`) |
| Open | `Ctrl+O` | Opens a local `.fluid` file after dirty-change confirmation |

Standard file shortcuts retain their standard meaning. Solve uses `Ctrl+Shift+Enter`; transient Run
uses `Ctrl+Enter`. Toolbar labels show the shortcuts and never rely on a one-time toast.

**As built:** Format, Go to definition, Toggle comment and Solve ship with P5.5. Rename waits for
the mutation API and its endpoint (P7.1); Run for the transient solver (M4); Save and Open for the
file lifecycle (P5.9). Format is the host's `POST /api/v1/format` ([`42`](../40-api/42-rest-contract.md)),
applied as one transaction, and dropped rather than applied if the document changed while the
request was out. Tab and Enter both accept a completion.

## File and recovery integration

[`58-file-lifecycle`](58-file-lifecycle.md) owns New/Open/Save/Save As/download, dirty state,
conflicts, and IndexedDB recovery (`D-27`, phased into M3 by `D-29`). CodeMirror supplies current bytes and hashes; file operations apply
changes as explicit editor transactions. Recovery never clears dirty state and never masquerades as a
successful named-file save.

## Invariants

1. The CodeMirror document is the script's only copy.
2. Highlighting never requires a network round trip.
3. Diagnostics are replaced wholesale per compile; no stale decoration survives.
4. Applying a quick fix is one undo step.
5. Every completion item comes from `/metadata` or the current compile's symbol table; none is
   hard-coded. **In particular the alias list and the similarity threshold come from the server**, so
   the editor cannot disagree with the binder about what resolves.
5a. Accepting a kind completion inserts the **canonical keyword**, never the alias or prefix that
   matched it.
5b. Every value completion offered after `param=` has the parameter's dimension, or the parameter's
   dimension is unknown and the filter is off. There is no third case.
5c. Indexed completion never offers a tank port above 16 or a profile temperature above the resolved
   `layers`; accepting a template materializes exactly the selected member.
6. Editor state (cursor, selection, undo, folds) survives a model update.
7. No editor feature blocks typing — every network-dependent feature degrades to absent.

Invariant 6 is the one that write-back most threatens: applying server-returned edits must preserve
the cursor, or every canvas interaction moves the user's caret.

## Error cases

| Situation | Behaviour |
|---|---|
| `/metadata` unavailable | Completion is empty; everything else works |
| Compile fails | Squiggles from the last response persist; a status indicator shows "offline" |
| Quick fix no longer applies (document changed) | Silently skipped, re-offered after the next compile |
| Script exceeds the size limit | An error decoration on line 1 with the limit |
| A `let` has not yet evaluated | Offered with its dimension and `—` for the value, not omitted |
| A `let` has an unnamed dimension | Offered dimmed, with its derived unit shown |
| Two kinds match within `D-15`'s ambiguity margin | Both listed adjacent; neither preselected -- the preselected first option is the typed text, which inserts nothing (`U-6`) |
| The kind failed to resolve | Value completion falls back to unfiltered — a broken script still completes |
| Recovery storage unavailable | Keep editing; visible persistent warning plus Download action (`58`) |

## Worked example

A user types `HE1 heat_ex` on a line of a circuit block:

```
t=0     '  HE1 heat_ex' — the tokenizer reads a name followed by a name, a declaration:
        HE1 in declaration position, 'heat_ex' in kind position. HE1 renders emphasised.
t=0     Completion triggers on the kind position. 'heat_ex' normalises to 'heatex' and
        prefix-matches the canonical keyword, so it ranks first:
            heat_exchanger    Heat source, consumer, or two-sided exchanger.
                              Ports: in, out, secondary.in, secondary.out.
            heat_exchanger    via 'heater'
        Selected; Tab accepts and inserts the canonical 'heat_exchanger' (D-15).
t=0     Document is now 'HE1 heat_exchanger'. Completion re-triggers on the parameter
        position:
            power    Power · kW · typically 1…10000 — duty; positive adds heat
            in.t     Temperature · °C · typically −50…300 — inlet constraint
            out.t    Temperature · °C · typically −50…300 — outlet constraint
            dt       TemperatureDelta · K — rise, as an alternative to in.t/out.t
            dp       PressureDelta · kPa — drop at design flow
            flow     MassFlow · kg/s — flow constraint
t=300   Debounce fires. Compile returns FS1507 ('HE1' is not connected to anything) as a
        warning. An amber squiggle appears under HE1.
t=300   The canvas shows HE1 floating, unconnected.
```

Parameters offered with their dimensions, ranges, and meanings, and a warning that it is not wired
in — without opening `/docs`. That is what makes a terse language usable, and it all comes from the
same metadata that documents the component.

**The same session, three keystrokes later**, showing the dimension filter:

```
t=1200  User types 'out.t = Ts'. The cursor is after 'out.t =', so the parameter is
        known to be Temperature. The symbol table holds three lets:
            Tsupply   Temperature       70 °C
            dTdesign  TemperatureDelta  20 K
            Qtotal    Power             120 kW
        Only Tsupply is offered — 'Ts' prefix-matches it, and the other two are the
        wrong dimension. dTdesign is excluded even though it is a temperature-ish
        thing, which is 13's Temperature/TemperatureDelta split doing its job before
        the compile rather than after it.
t=1200  Tab accepts. Document reads 'HE1 heat_exchanger power = 150 out.t = Tsupply'.
```

Without the filter that list is three items and one of them produces `FS1302`. With it the list is one
item and it is right. The cost is a dimension lookup the editor already has from `/metadata`.

## Acceptance criteria

- [x] Highlighting updates within one frame of a keystroke, with no network activity. (P5.5: the stream tokenizer runs in the view's own update; nothing in the language package touches the client.)
- [x] Completion in each position of the table offers exactly the listed set. (P5.5: `completion.test.ts`, one test per row against the committed metadata golden.)
- [x] Completion detail shows dimension, canonical unit, and range. (P5.5; the range is in the canonical unit since `A-5`.)
- [x] A quick fix applies as one undo step and leaves the cursor sensible. (P5.5: one transaction tagged `quickfix`.)
- [x] Squiggles persist through the debounce gap without flicker. (P5.5: diagnostics are set only when a compile answers.)
- [ ] Cursor, selection, and undo survive a model update and a canvas-initiated edit. (A model update touches only the diagnostics set, which is a decoration, so the state survives by construction; a canvas-initiated edit is P5.7's to assert.)
- [x] The editor is fully usable with the network disabled. (P5.5: completion, format and go-to-definition return nothing rather than throwing when the host or the model is missing.)
- [ ] A dirty draft survives reload through `58`; unavailable recovery storage is visible and offers Download.
- [x] The client's tokenizer and the server agree on token classification over the whole sample corpus —
      a test that catches the two grammars diverging. (P5.5: `TokenGoldenTests` and `tokenizer.test.ts` over the same goldens. The tokenizer disagreed with all nine samples from package 7 until package 8 rebuilt it on the blocks (`U-11`); they agree again.)

### Completion acceptance criteria

- [x] `heat_ex`, `heatex`, `HeatEx` and `exch` all offer `heat_exchanger`; Tab inserts the canonical
      keyword in every case.
- [x] Every alias in the registry is reachable by completion, asserted by walking the registry rather
      than by a fixed list — an alias the binder accepts and completion hides is a trap.
- [x] `pmp` offers `pump` as the fix, as the binder does (`FS1502`, `D-170`), and `P1 pmp` has no kind:
      its parameters are not offered. Completion and the compiler bind the same set of strings.
- [x] Every block's settings come from the metadata's `blocks`, and each one the table lists is one the
      reader takes. (Package 8: `SettingRegistryTests` writes each setting and finds no `FS1503`;
      `completion.test.ts` reads the settings from the committed metadata golden.)
- [x] A controller's block offers the settings its `type` takes: `band` for a `PI`, `differential` for an
      `onoff`, and a `curve` controller no `setpoint`.
- [x] A port is offered as a script spells it: `HX1.` offers `secondary.in` and `primary.in`, never `in[2]`
      (`D-179`, `A-8`).
- [x] Completion never throws at any offset of any sample, with or without a model.
- [x] An input inside `D-15`'s ambiguity margin lists both candidates with neither preselected. (Both listed adjacent: yes. Neither preselected: `U-6`, closed 2026-09-22 -- the list's first option is the typed text itself and inserts nothing, since CodeMirror's preselection is global.)
- [x] After `out.t =`, a `let` of dimension `Temperature` is offered and one of `TemperatureDelta` is not.
      After `dt =`, the reverse. This is the single highest-value completion test, because it is the
      distinction `FS1302` exists to catch.
- [x] After `power =`, both `kW` and `W` are offered — filtering is by dimension, never by canonical
      unit, or the explicit-unit escape hatch disappears.
- [x] A `let` whose value is deferred is offered with its dimension and no value. (`U-5`, closed 2026-09-22: the binder types a deferred expression from the units and properties it names, and the wire carries the dimension.)
- [x] A parameter on an unresolved kind offers everything rather than nothing.
- [x] `container` and `v` find canonical `tank` and `volume`; `T1.in2` completion materializes `in2`,
      and no completion offers `in17` or a `tN` above the tank's resolved layer count.
- [x] Completion never inserts text that fails to parse — asserted by accepting every offered item in
      every position across the sample corpus and re-parsing. (P5.5: every item is re-lexed by the tokenizer as one token of the expected kind; the parse is the next compile's.)

## Open questions

None. The client tokenizer performs lexical classification plus block tracking; v1 is one open document without a
file tree; standard Save/Open shortcuts are retained; alias canonicalization is an explicit whole-file
command and never an automatic rewrite.
