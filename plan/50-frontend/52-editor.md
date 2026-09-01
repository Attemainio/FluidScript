---
id: 52-editor
title: Script editor
tier: 50-frontend
status: draft
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

**Client-side, from a Lezer grammar mirroring [`12-grammar`](../10-language/12-grammar.md).**

This is a second implementation of the grammar, which violates the instinct to have one. It is the
right trade: highlighting must be instant on every keystroke, and a round trip is not instant. The
mitigation is that the Lezer grammar only needs to be *lexically* correct — it classifies tokens, it
does not bind or validate — so it cannot disagree with the server about anything that matters.
Divergence shows as a mis-coloured token, not a wrong result.

**Colours are the Visual Studio / VS Code palette**, mapped token by token in
[`55-design-system`](55-design-system.md). That is a deliberate departure from the HVAC palette used
everywhere else in the app: a keyword that is not blue reads as *wrong* before it reads as *different*,
and the editor is the one surface where familiarity beats character. The table below names the token
roles; `55` holds the hex values for both themes.

| Token | Style |
|---|---|
| Keyword (`circuit`, `fluid`, `let`, `connections`) | Keyword colour, medium weight |
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
contextual on the cursor's syntactic position:

| Position | Offers |
|---|---|
| Start of a line, declaration section | Nothing (the user is naming a component) |
| Second token of a declaration | Every component kind, matched **alias-aware** — see below |
| After a kind or a parameter | That kind's remaining parameters, with dimension and range |
| After `param=` | **Dimension-filtered**: `let` names, `Component.property` references, and unit symbols, all restricted to the parameter's dimension |
| Start of a line, connection section | Every declared and inferred component name |
| After `-` in a connection | Component names, then `.port` after a dot |
| After `.` on a component | That component's properties |
| Inside `schedule` | Settable `component.parameter` targets, then a time or a range |

Indexed tank metadata is pattern-based (`D-32`). After `T1.` the editor offers already materialized ports plus
`in{1..16}`/`out{1..16}` templates; after `T1 tank` it offers `volume`, `layers`, `t`, the valid
`t1`…`tN` profile members for the resolved layer count, and elevation templates. Typing alias `v`
offers canonical `volume`; Tab inserts `volume`, while text typed without completion remains `v`.

**Completion detail shows the parameter's dimension, canonical unit, and range** — `power · Power ·
kW · typically 1…10000`. That turns completion into documentation and removes most of the reason to
leave the editor.

**Inferred component names are offered in connection position**, marked as inferred. A user who wants
to reference `HE1__3WV` should not have to type it from memory.

### Kind completion is alias-aware, and Tab commits the canonical spelling

`D-15` gave the binder three ways to reach a kind: normalisation, curated aliases, and similarity.
Completion has to see all three, or the editor will red-underline something the compiler accepts.

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
4. Similarity above `D-15`'s 0.70 threshold, ordered by score.

Rank 4 exists so that completion and the compiler agree: `pmp` compiles to `pump` with `FS1512`, so
`pmp` must also *complete* to `pump`. An editor that offered nothing for a string the compiler accepts
would teach the user that the completion list is the real vocabulary and the aliases are a trap.

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

RAD1 heat_exchanger power=│
```

At the cursor, `power` is `Power`, so:

| Offered | Not offered | Why |
|---|---|---|
| `Qtotal` — `120 kW` | `Tflow`, `dTdesign` | Wrong dimension |
| `BLR.power`, `LOAD.power` | `P1.dp`, `N2.t` | Wrong dimension |
| `kW`, `W`, `MW`, `hp` | `K`, `C`, `m` | Not `Power` symbols |

And at `in=│`, `Tflow` is offered and `dTdesign` is not — the distinction `13`'s two temperature
dimensions exist to make, surfaced at the moment it matters rather than as an `FS1302` afterwards.

**Filtering is on the dimension, not on the unit.** `power=30000 W` and `power=30` are both `Power`, so
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

BLR heat_ex⇥ power=150 out=Ts⇥
     └─ heat_exchanger              └─ Tsupply · Temperature · 70 °C
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
| Two kinds match within `D-15`'s ambiguity margin | Both listed adjacent; neither preselected |
| The kind failed to resolve | Value completion falls back to unfiltered — a broken script still completes |
| Recovery storage unavailable | Keep editing; visible persistent warning plus Download action (`58`) |

## Worked example

A user types `HE1 heat_ex`:

```
t=0     'HE1 heat_ex' — Lezer classifies HE1 as an identifier in declaration position,
        'heat_ex' as an identifier in kind position. HE1 renders emphasised.
t=0     Completion triggers on the kind position. 'heat_ex' normalises to 'heatex' and
        prefix-matches the canonical keyword, so it ranks first:
            heat_exchanger    Heat source, consumer, or two-sided exchanger.
                              Ports: in, out, in2, out2.
            heat_exchanger    via 'heater'
        Selected; Tab accepts and inserts the canonical 'heat_exchanger' (D-15).
t=0     Document is now 'HE1 heat_exchanger'. Completion re-triggers on the parameter
        position:
            power    Power · kW · typically 1…10000 — duty; positive adds heat
            in       Temperature · °C · typically −50…300 — inlet constraint
            out      Temperature · °C · typically −50…300 — outlet constraint
            dt       TemperatureDelta · dK — rise, as an alternative to in/out
            dp       PressureDelta · kPa — drop at design flow
            flow     MassFlow · kg/s — flow constraint
t=300   Debounce fires. Compile returns FS1507 (the component is in no connection) as a
        warning. An amber squiggle appears under HE1.
t=300   The canvas shows HE1 floating, unconnected.
```

Parameters offered with their dimensions, ranges, and meanings, and a warning that it is not wired
in — without opening `/docs`. That is what makes a terse language usable, and it all comes from the
same metadata that documents the component.

**The same session, three keystrokes later**, showing the dimension filter:

```
t=1200  User types 'out=Ts'. The cursor is after 'out=', so the parameter is known to
        be Temperature. The symbol table holds three lets:
            Tsupply   Temperature       70 °C
            dTdesign  TemperatureDelta  20 dK
            Qtotal    Power             120 kW
        Only Tsupply is offered — 'Ts' prefix-matches it, and the other two are the
        wrong dimension. dTdesign is excluded even though it is a temperature-ish
        thing, which is 13's Temperature/TemperatureDelta split doing its job before
        the compile rather than after it.
t=1200  Tab accepts. Document reads 'HE1 heat_exchanger power=150 out=Tsupply'.
```

Without the filter that list is three items and one of them produces `FS1302`. With it the list is one
item and it is right. The cost is a dimension lookup the editor already has from `/metadata`.

## Acceptance criteria

- [ ] Highlighting updates within one frame of a keystroke, with no network activity.
- [ ] Completion in each position of the table offers exactly the listed set.
- [ ] Completion detail shows dimension, canonical unit, and range.
- [ ] A quick fix applies as one undo step and leaves the cursor sensible.
- [ ] Squiggles persist through the debounce gap without flicker.
- [ ] Cursor, selection, and undo survive a model update and a canvas-initiated edit.
- [ ] The editor is fully usable with the network disabled.
- [ ] A dirty draft survives reload through `58`; unavailable recovery storage is visible and offers Download.
- [ ] The Lezer grammar and the server agree on token classification over the whole sample corpus —
      a test that catches the two grammars diverging.

### Completion acceptance criteria

- [ ] `heat_ex`, `heatex`, `HeatEx` and `exch` all offer `heat_exchanger`; Tab inserts the canonical
      keyword in every case.
- [ ] Every alias in the registry is reachable by completion, asserted by walking the registry rather
      than by a fixed list — an alias the binder accepts and completion hides is a trap.
- [ ] `pmp` offers `pump`, matching what the binder does with it (`FS1512`). Completion and the
      compiler accept the same set of strings.
- [ ] An input inside `D-15`'s ambiguity margin lists both candidates with neither preselected.
- [ ] After `out=`, a `let` of dimension `Temperature` is offered and one of `TemperatureDelta` is not.
      After `dt=`, the reverse. This is the single highest-value completion test, because it is the
      distinction `FS1302` exists to catch.
- [ ] After `power=`, both `kW` and `W` are offered — filtering is by dimension, never by canonical
      unit, or the explicit-unit escape hatch disappears.
- [ ] A `let` whose value is deferred is offered with its dimension and no value.
- [ ] A parameter on an unresolved kind offers everything rather than nothing.
- [ ] `container` and `v` find canonical `tank` and `volume`; `T1.in2` completion materializes `in2`,
      and no completion offers `in17` or a `tN` above the tank's resolved layer count.
- [ ] Completion never inserts text that fails to parse — asserted by accepting every offered item in
      every position across the sample corpus and re-parsing.

## Open questions

None. Lezer performs lexical classification plus section tracking; v1 is one open document without a
file tree; standard Save/Open shortcuts are retained; alias canonicalization is an explicit whole-file
command and never an automatic rewrite.
