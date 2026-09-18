---
id: 09-project-state
title: Project state
tier: plan
status: living
owns: [which phase the project is in, which work packages have shipped and in what order, which defect records each phase closed, what the next package is]
depends_on: [08-implementation-sequence]
traces_to: []
open_questions: 0
last_review_pass: 0
---

# Project state

## Purpose

[`08-implementation-sequence`](08-implementation-sequence.md) says what the work is and in what order
it must be done. It is written entirely in the future tense and deliberately never changes as work
lands, because it is a plan and a plan that edits itself to match what happened stops being one.

This file is the other half: **what has actually happened, in what order, and where that leaves the
next session.** A session that reads `08` alone knows the whole map and not its own position on it.

It exists because context does not survive. This project has been built across many sessions, several
of them compacted, and everything a session learns that is not written into `plan/` or `/docs` is gone
when it ends. The commit log records *changes*; it does not record *state*, and reconstructing "which
phase are we in" from 141 commits costs a session's attention before any work starts.

## What this file is not

**It is not a defect list.** Every open question lives in the `defects.md` of the tier that owns it,
and that file is the only place it is described. This one carries the *count* and the *pointer*, so
that a number here going stale is visible rather than a description here disagreeing with the one that
matters.

**It does not restate closed defects either.** When a package completes it records which ids that
package closed, and nothing about them. The reasoning is in the tier's Closed table, written from the
beginning as `CLAUDE.md` requires, and copying a summary here would produce a second account that
drifts from the first.

**Closure attribution starts from this file, and is not reconstructed backwards.** P0 through P3.7
shipped before this record existed, and inferring which package closed which id from commit subjects
would put guesses in the one place a future session trusts. Those closures are attributed where they
already are: most Closed entries name their package in their own text — "fixed in `P3.4c`", "P2.8
closed all five" — and that is the record for everything before `0f8985e`.

**It is not a changelog.** A commit per line would be the log again. What is recorded is the package —
one branch, one merge, one closed verification — and the defect ids it moved.

**It does not follow [`_template.md`](_template.md).** Every other document in `plan/` is a contract
with Purpose · Responsibilities · Contracts · Invariants · Error cases · Worked example · Acceptance
criteria · Open questions. This is a record of work, which has none of those; the template's shape
would be filled with nothing.

## Where the project stands

> **M2a exited 2026-09-14; M2b exited 2026-09-15** — P4.1 (`c275bd9`), P4.2 (`bdf78f6`), P4.3
> (`17fbe3a`) — on the user's call, with one criterion deferred rather than ticked: `400HP01` needs
> a `heat_pump` kind, which is `D-80`'s and M4's; the ownership rule it tests is built and checked
> on a `chiller`. The audit of open defects before `P5.1` closed `C-4` (already met by P4.1),
> `L-36` (a `13` correction) and `C-67` — the last with `FS2119`, which found seventeen test
> fixtures and four syntax-tour lines writing a cooling load as a positive neutral duty.
> **P5 — M3, the usable static product — is in progress: P5.1a–c (layout hints, the model contract,
> symbols and the payload baseline) and P5.1d-1 (the layout solver in Core, `D-103`, with named
> styles, `D-104`) shipped 2026-09-15 and was taken to the user's pictures on 2026-09-16 (`D-105`,
> the router, the audit); P5.1d-2 built the rule-based engine of `D-106` whole on 2026-09-16, and
> the same day the user judged its pictures, the plan was re-evaluated and the engine restarted
> from an empty rule set against a ladder of scripts (`D-107`, `28` rewritten in four parts, `29`
> the step log). Step 1 -- one pump -- was drawn and corrected the same day: the user's four
> directions are `D-108` (heat left to right and loops clockwise as hard constraints H9/H10, a
> transform class per kind with exchangers and tanks mirrored never turned, every node laid out
> with its boundaries, the layout checked from its text), written into `28` as C1–C4 and A4/A6/A10
> and into `29` as a ten-step plan that reaches every sample; `C-88`–`C-90` filed for what the
> code lacks (the audit's five unmeasured constraints, the text in tests not Core, no transform
> class in the catalogue). Step 1 is redrawn with two nodes instead of ticks (hard 0); step 2 -- a
> pump feeding an exchanger -- is drawn (C3 and C4 exercised, C5 sequential placement provisional,
> hard 0, one bend); the user's first correction to it -- the inferred node between two
> components takes no place -- is in `28` A5 and closed its open question 1. Step 3 -- the loop
> closed with a load -- is drawn by C2 (source left flowing up, load right flowing down, the pump on
> the bottom rail; four bends, hard 0), step 4 put a valve on the return with no engine change and
> passed, and step 5 hung the primary off the exchanger's second side (C6: a loop member's flank
> chain runs level away from the loop; a declared pipe and a chain of inline elements spread along
> their run; hard 0, soft 0). The user's step 5 corrections: `D-109` (a symbol reversing on a
> line is mirrored, not half-turned) is in; pipe properties on a connection line is decided,
> `D-110` (option A: an implicit pipe per connection carrying properties, bare connections
> unchanged), as package P5.1e in `08`; C7 aligns a return under its supply. Step 5 stands.
> P5.1d-3 and P5.1e shipped 2026-09-18: the layout report lives in Core and the predicate sweep runs
> on every fixture; the samples, `01`'s reference circuits and the ladder write pipe properties on
> the connection line (rule I7). **P5.2 shipped the same day:** the REST host with `compile`,
> `solve`, `validate` and `metadata`, sessions with warm start and supersession (`41`), `07`'s
> limits as `413` and `FS4601`, the committed JSON schemas (`D-46` step 2) and OpenAPI; `edit` is
> deferred whole to P7.1 with the mutation API. `C-99` found and closed on the way (the exchanger's
> `u`, `ua` and `fouling` were dimensionless); `A-1`–`A-3`, `L-53` and `L-54` opened. Committed
> as `d1a1c08`. **P5.3, the design system, shipped the same day:** the tokens, the two themes as
> JSON with the cascade generated from them, custom theme files, eight primitives, and the first
> frontend tests -- contrast, palette, the literal scan -- on Vitest; `F-1`, `F-2` opened, `F-3`
> closed, in the new `50-frontend/defects.md`. The next package is P5.4, the app shell.
> Committed 2026-09-17 with the Api goldens regenerated to the ladder engine's sample layouts.
> `C-88` and `C-90` closed the same day: the audit measures all ten hard constraints and the
> transform class is on the wire. Step 6, the cooling loop, is drawn (the loop walk through
> junctions, the consumer fallback, C8 junctions on a rail; hard 0) and awaits corrections. The
> simple loop, the substation and the cooling loop samples are reached: their routing, corner and
> audit gates are live and pass. The
> seven layout samples run through the engine's fallback and their layout gates are skipped until
> the ladder reaches them.**
> The substation converges on `01`'s figures —
> UA 12.071 kW/K by ε-NTU and by LMTD at the solved state, 3.658 m², 0.895 / 1.793 kg/s — after
> four changes that were one defect from the outside (`S-32`): the exchanger's duty is
> `ε·Cmin·(T_in2 − T_in1)` from the port states, its design point pins a side's flow where nothing
> else does (`D-97`), the picked datum is the pump suction so the seed stays inside the property
> domain (`D-98`, `S-62`), and the promoted Kv seeds from the Kv law. `FS2109`–`FS2112` and `FS4008`
> are live; `hx.u_default` is withdrawn until it has a source (`D-99`, `C-78`).
> P3.0 through P3.9 shipped and every M2a criterion in `05` is ticked with the test that holds it.
> The last one, the solver-scale baseline, found `C-76`: every real-water property read leaked a
> 540 KB native CoolProp state, which is what had been killing the machine and the agent sessions
> with it. Fixed the same day; 861 unknowns now solve in 5.4 s at 165 MB, and every solve in the
> corpus is 4× faster. The R-17 coverage row is `62`'s governing-equation table.
> `C-75` closed with `D-96` the same day: a bootstrap provisional counts as free, so `head=15` is
> absorbed by the balancing valve (Kv 0.77) instead of refused. `S-61` (`FS3008` on a bound the
> path merely crossed) was found and closed with it.
> **All three M2a demo scripts converge**, as of 2026-09-14, and the header lands on `01`'s figures.
> `S-58` was the last blocker: a junction mixed its inlets by a plain average, so no valve position
> could move a mixed temperature. `D-91` (positive role capacities) and `D-92` (fixed flow as a flow
> residual) are in; `S-53`, `S-55`–`S-57` record what measuring them found and stay open.

M2a asks for three demo scripts to solve. All three do:

| Sample | State | Note |
|---|---|---|
| `m2-simple-loop.fluid` | **Converged** | `24`'s worked example reached rather than transcribed — pump head 5.28 m from nothing but the loop |
| `m2-cooling-loop.fluid` | **Converged** | Mixing node 19.99 °C against 20, return 49.94 against 50, 0.0763 kg/s recirculating |
| `m2-distribution-header.fluid` | **Converged** | One Newton iteration, three sizing passes: 0.1914 / 0.2392 kg/s drawn from the 60 °C header, 0.4307 through the source against `01`'s 0.4306, valves at 0.63 / 0.62 of travel. `S-58` |
| `m2-substation.fluid` | **Converged** | Two Newton iterations, two sizing passes: `HX1.ua` 12.071 kW/K, `HX1.area` 3.658 m², primary 0.895 kg/s at 85/45, secondary 1.793 kg/s at 60/40, `PCV.kv` 2.13, `SP.head` 10.2 m. `S-32`, `S-62`, `D-97`, `D-98` |
| `m4-storage-header.fluid` | **Converged** | Solves in one pass; nothing in it needs sizing |

The recorded status of every sample is asserted by `CorpusStatusTests.EachSampleStandsWhereItStood`,
which is the durable form of this table: a sample that starts solving without anyone noticing fails
that test rather than quietly improving.

## The phase ladder

| Phase | Milestone | Packages | State | Closed |
|---|---|---|---|---|
| P0 | pre-M0 | 3 | **Complete** | 2026-09-01 |
| P1 | M0 | 4 | **Complete** | 2026-09-01 |
| P2 | M1 | 10 | **Complete** | 2026-09-02 |
| P3 | M2a | 10 | **Complete** — every package shipped and every `05` criterion ticked | 2026-09-14 |
| P4 | M2b | 3 | **Complete** — every `05` criterion ticked but the heat-pump tag, whose kind does not exist until M4; M2b exited on that basis | 2026-09-15 |
| P5 | M3 | 11 | Not started | — |
| P6 | M4 | 7 | Not started | — |
| P7 | M5 | 2 | Not started | — |
| P8 | M6 | — | Evidence-gated; not decomposed | — |

`08` lists P2 as nine packages and P3 as nine. Both grew by one during execution and `08` records why
in each case: **P2.10** took the language half of `D-57`–`D-62` out of P3, and **P3.9** was created by
`D-70` when elevation turned out to be a parameter on every kind rather than a line in P3.6.

## What each phase delivered

### P0 — prerequisites · complete 2026-09-01 · `bcfd8e9`

All three packages in one commit, because none of them produces code: sequencing the implementation
(`D-45`, `D-46`) and independently reproducing every asserted reference number.

**P0.3 paid for itself twice**, and `08` keeps both accounts: an input figure was wrong (`h(6 °C)`
stated 124 J/kg off CoolProp), and later a derived figure reproduced its own arithmetic while
describing the wrong circuit.

### P1 — M0 scaffold · complete 2026-09-01 · `9fbb4cf`

The SharpProp spike, the repository skeleton, CI with the architecture tests and the docs gate, and
the five test-trait harnesses with no baselines behind them.

**P1.1 was the one package able to invalidate a tier**, and it did not — but it moved figures in
[`21-fluid-and-state`](20-core-domain/21-fluid-and-state.md) that the whole property tier rests on,
which is the outcome the gate exists to produce cheaply.

### P2 — M1, the language spine · complete 2026-09-02

| # | Package | Commit |
|---|---|---|
| P2.1 | Diagnostics, spans, the code registry | `386ea5b` |
| P2.2 | Dimensions, units, `Quantity` | `557ef39` |
| P2.3 | Lexer with trivia attached | `4e884d4` |
| P2.4 | Parser, AST, error recovery | `fe27ce3` |
| P2.5 | Printer and the round-trip fuzz | `491948b` |
| P2.6 | Component registry, kind resolution | `cbb2adc` |
| P2.7 | Binder steps 0–5, expressions | `d391b46` |
| P2.8 | Binder steps 6–11 | `643c261` |
| P2.9 | Version detection and the compatibility gate | `d7055a5` |
| P2.10 | The language half of `D-57`–`D-62` | `2fcc3c5` |

**M1's exit criterion is asserted, not claimed**: `01`'s nine-diagnostic count runs against
`samples/m1-syntax-reference.fluid`, and `Print(Parse(x)) == x` is a standing corpus-mutation fuzz from
P2.5 onward rather than a milestone check.

P2 is where most of tier 10 was found and closed — 35 of its 42 entries are in the Closed table, and
each names the package that closed it. Seven remain open; see
[`10-language/defects.md`](10-language/defects.md).

### P3 — M2a, the hydraulic core · complete 2026-09-14

| # | Package | Commit(s) | State |
|---|---|---|---|
| P3.1a | The three M2a reference circuits, transcribed | `5a16eff` | Shipped |
| P3.1 | `ISubstance`, `FluidState`, the SharpProp adapter, both fakes | `450df19` | Shipped |
| P3.2 | Property accuracy — V4, V5, V13, V14 | `d5e1a20` | Shipped |
| P3.0 | Sensors as solved observers | `13a764e` | Shipped |
| P3.3 | Component model, six kinds in duty mode | `3b38804`…`79f80b7` (7) | Shipped |
| P3.4a | Lowering, `CircuitGraph`, the cycle basis | `1a09e54` | Shipped |
| P3.4b | The counting argument, promotion, the `FS22xx` codes | `f244d98` | Shipped |
| P3.4c | Boundaries that declare themselves, the enthalpy datum | `50a0a5b` | Shipped |
| P3.5 | The catalogue, compiled and refusing its own rows | `b96673a` + sourcing | Shipped |
| P3.6a | Scaling, the state vector, the equation rows | `4dc5bfa`…`16e5637` (4) | Shipped |
| P3.6b | Newton, and the seed it cannot start without | `b5f5539` | Shipped |
| P3.7a | The seed that closes every mass balance | `1fe14ce` | Shipped |
| P3.7b | Sizing rules and the single outer loop | `91ac4fc`…`0689589` (5) | Shipped |
| P3.8 | The design point as the sizing point — `sized_at` (`D-94`) | `b079ec6` | Shipped 2026-09-14 |
| P3.9 | Elevation as an absolute height (`D-70`, `D-95`) | `38bfd61` | Shipped 2026-09-14 |

**P3.1a and P3.4c were not in the plan.** P3.1a transcribed the reference circuits before anything
could solve them and found two defects in the documents that define them. P3.4c began as a change to
what a boundary declaration means and turned into two corrections to the counting argument itself —
`08` keeps the account of why, because the shape of it ("the package that finds a defect is the one
that tries to *use* the thing") is a planning lesson rather than a state fact.

**P3.8 was smaller than planned, and half of it moved.** Measured before anything was written,
`design` already sized end to end: the binder folds every curve to its design-point value before
lowering, so no sizing rule needed to read `ProjectSettings.Design` and `C-51`'s "fraction-of-peak
rule" turned out to be the wrong shape. What shipped is `D-94`'s `sized_at` clause — one component
reading the curve at its own bivalent point, the closed-circuit closure sizing its backup, and the
fraction reported as the parameter's basis. The live-curve half (a curve as a function of time in a
transient run) moved to P6.1, where a clock first exists; `08` records the re-scope.

**P3.9 shipped as `D-70` with one amendment and one deferral.** Measured first: the relative rise
already carried the right physics (313 kPa and 314 J/kg over a 32 m riser, +0.0125 K of friction
heating on an open one), so the package was the language and the propagation, not the equations.
`elevation` is now a height on every single-height kind; a pipe has none and its rise is derived;
a bare node-to-node link carries `ρgΔz` in the assembler, in the arriving enthalpy and in sizing's
loop walk (the last found by measurement: 45.8 m of head before it counted the link); the tank's
port fraction is `in1_level`. The amendment is `D-95` — an omitted height is inherited from the
neighbourhood, not 0, or a roof would need `elevation=32` on every line. The deferral is the tank's
per-port `z_tank + f·H`, which waits for P6.2 to give the tank a height; its ports take the vessel's
one height meanwhile, recorded in `22` and `23`. `S-60` was filed on the way: a tall loop with no
`p=` fails at the seed with `FS3007` and nothing names the fill pressure; with heights on every node
it became `FS2220` the same day, arithmetic before the seed.

P3 is where tiers 20 and 30 were largely written and largely corrected: 49 of tier 20's 68 entries and
41 of tier 30's 56 are closed. Both Closed tables carry the attribution.

### P4 — M2b, coupled thermal rating · in progress

| # | Package | Commit(s) | State |
|---|---|---|---|
| P4.1 | The rated two-sided exchanger: ε-NTU as the residual route, LMTD as the reported one (`D-97`, `D-98`, `D-99`) | `c275bd9` | Shipped 2026-09-15 |
| P4.2 | Two coupled hydraulic graphs, two pressure datums | (with P4.1's follow-up) | **Shipped 2026-09-15, as a verification.** The partition, the two datums and `D-17`'s `FS2213` exemption were built in P3.4 and P4.1 solved through them; what remained was `23`'s three unticked substation criteria, one of which had no test — a coupled exchanger on two `Branch.Path`s, no junction, no mass balance. Added and ticked. |
| P4.3 | `D-36` circuit ownership from the enthalpy-losing side | (this commit) | Shipped 2026-09-15 |

**P4.3 is a binder step, not a graph one.** `D-36` and `25` describe ownership as read off the
heat-transfer edge the layout hints build, but a tag is a binder product computed on every keystroke,
before anything lowers. `BindingRun.ResolveOwnership` runs after `Validate` and before `AssignTags`:
it walks each side's ports through inferred nodes to the first declared component and takes its
circuit, reads the losing side from the duty's sign (role words carry it, `D-91`) or from whichever
side's stated terminals drop, and rewrites `ComponentSymbol.CircuitName` — so the tag, the graph's
`CircuitOf` and the layout hints all agree without a second traversal. The substation as two blocks
tags `HX1` `400HE01` from either block in either order; `LOAD` stays `100HE01`; a `chiller` between
the same circuits is `100HE01`; two circuits with no readable direction fall back to the lower number
with `FS2216` anchored on the declaration. `WellPosedness.ReportOwnership`, which raised `FS2216`
for a fallback it never applied and against the wrong fallback order (both sides in one circuit is
not ambiguous), is gone. `RatedExchangerSolveTests.TheSolvedStateIsIdenticalWhicheverCircuitDeclaresTheExchanger`
is `23`'s "ownership never reaches the physics" test. Nine new tests, 1604/0/4.

**P4.1 was four changes wearing one defect.** `S-32` was filed as "nothing computes the side-2
flow", a registry group and an `ImpliedFlow` away. Measured, that would have seeded the right flow
and constrained nothing: the substation's primary has stated pressures at both ends and a promoted
valve, and its flow was whatever Kv 630 passed. What closed it: (1) `ExchangerRating` on the lowered
component and `HeatExchanger.Duty` — `ε(UA/Cmin, Cr)·Cmin·(T_in2 − T_in1)` from the port states,
allocation-free, with `Effectiveness` (counterflow blended C¹ across `Cr → 1`, parallel, crossflow by
bisection) and `LogMeanTemperatureDifference` sharing no code; (2) `D-97`, the design point as a
flow pin on a side nothing else pins, the same rule giving Rated mode its pin; (3) `D-98`, the
picked datum at the pump suction, found by a scratch experiment after everything else was right
and the seed still died at 80 kPa absolute (`S-62`); (4) `SolutionSeed.PromotedKv` and per-side
duty shifts in the seed's temperature levels, so `PCV.kv` starts at 2.88 for a solved 2.13.
`ThermalSizer` sizes `ua` (and `area` from a stated `u`, `plates` from a stated `plate_area`) from
the design point alone, checks feasibility before inverting (`FS2111`), holds the approach to
`hx.approach_min` (`FS4008`, the first `FS4xxx` code to fire), and returns diagnostics the outer loop
merges into the solve's. The solve report gained *exchanger ratings at the solution*, printing
`UA … rated, … by LMTD` side by side. `BranchFlows.Duty` learned `dt`/`dt2` so a rated loop stated
as `in`+`dt` seeds at its design flow rather than 0.1 kg/s. What was deliberately not done: no `U`
is invented (`D-99`), `lamella` is unused, side-2 `dp` stays the duty-mode default, and the plate
step is not applied — all `C-78`, all waiting on `27`'s plate catalogue. 50 new tests; every sample
in the corpus converged or unchanged; only the `FS2201` text moved on closed loops.

### P5 — M3, the usable static product · in progress

| # | Package | Commit(s) | State |
|---|---|---|---|
| P5.1a | `LayoutHints` per `25`, with `BranchShapes` (`D-100`) and `FS2401`–`FS2403` | (this commit) | Shipped 2026-09-15 |
| P5.1b | `ModelContract` per `26`: wire records in Core, the serializer in the Api, goldens | (this commit) | Shipped 2026-09-15 |
| P5.1c | Symbol strokes per `D-24` and `53`'s inventory; the 200-component payload baseline | `969db66` | Shipped 2026-09-15 |
| P5.1d-1 | The layout solver in Core (`D-103`): placements with inner and outer boxes, stub-and-join routes, named styles (`D-104`), inline elements and alignment (`D-105`) | (with P5.1d-2's first commit, 2026-09-17) | Shipped 2026-09-16 |
| P5.1d-2 | The layout engine built rule by rule against the ladder ([`28`](20-core-domain/28-layout-solver.md) parts A–D, [`29`](20-core-domain/29-layout-ladder.md); `D-106`, `D-107`, `D-108`, `D-109`, `D-110`, `D-112`, `D-113`, `D-114`) | (this commit, 2026-09-17, with P5.1d-1's engine work) | Steps 1 to 10 accepted 2026-09-17; step 11a (two independent loops, stacked) accepted; the syntax tour's circuits follow one at a time |
| P5.1d-3 | The layout report (`D-100`) and `62`'s predicate gates: `SceneText` in Core with its raster, the audit's three gaps (`C-95`), `LayoutPredicateTests` | (this commit, 2026-09-18) | Shipped 2026-09-18 |
| P5.1e | Pipe properties on a connection line (`D-110`): the grammar's trailing property list, the printer round trip, the implicit `pipe` per connection (rule I7) with `length` defaulting to zero, the samples, `01`'s reference circuits and the ladder scripts rewritten to it, `docs/functions/pipe.md` | `44e30fa` | Shipped 2026-09-18; `L-51` closed; `C-97` (the implicit pipe's source span) opened |
| P5.2 | REST and diagnostics contracts, host, sessions, cancellation ([`42`](40-api/42-rest-contract.md), [`44`](40-api/44-diagnostics-contract.md), [`41`](40-api/41-api-architecture.md)): `compile`/`solve`/`validate`/`metadata`, sessions with warm start and supersession, `07`'s limits, the committed JSON schemas, OpenAPI, `docs/advanced/using-the-api.md` | `d1a1c08` | Shipped 2026-09-18; `edit` deferred to P7.1; `C-99` closed; `A-1`–`A-3`, `L-53`, `L-54` opened |
| P5.3 | Design tokens and themes ([`55`](50-frontend/55-design-system.md)): `tokens.ts`, the two themes as JSON and the generated cascade, custom theme files, the UI store's `theme`, eight primitives, Vitest with the design tests, `docs/advanced/themes.md` | (this commit, 2026-09-18) | Shipped 2026-09-18; `F-3` closed; `F-1`, `F-2` opened; `Tooltip`, `Slider`, `NumericInput`, `SplitPane` land with their first consumers |

**P5.1a is `LayoutHintsDerivation.Derive(graph, model, branchFlows)`**, a pure function of the
lowered graph, its model and the solved branch flows, returning the hints and its three
informational codes. Every one of `25`'s worked examples is a test: the cooling loop's `Order`,
`Rank`, one loop walk `[N2, PU1, PU1__HE1, HE1, HE1__3WV, 3WV]`, four inferred of ten and one
Neutral stage; the storage header's `Source [S1, S2] · Storage [T1] · Consumer [RAD_NETWORK,
AHU_NETWORK]`; the distribution header's one group of two, equal `BranchShapes`
(`[pipe, three_way_valve, pump, heat_exchanger, pipe]`) that survive renaming and differ on an
inserted valve; the substation's source side before `HX1` and its heating side after, stable under
block swap. Three things `25` had to be made precise about while implementing, all written into it:
loops are *banded* rather than collapsed (the header's cycle basis holds four loops, two of them
through both consumers); classification is *relative to a pivot* (an extended exchanger or a tank),
which is why an open loop with no pivot is one Neutral stage rather than a source and a consumer
either side of nothing; and a registered Neutral role classifies nothing, so `FS2403` fires only for
a `Source`/`Consumer` role contradicted by its members' duty sign. The header sample's parent ring
therefore shares rank 0 with its branches — `25`'s example said `Source [heating's boundary]` for a
sample that has none and is corrected — and whether it should be a Source band on its duty sign
alone is `25`'s one open question, left for the canvas package (P5.6) to answer with a diagram in front of it. A
subcircuit written as connections (`F-16`'s mixing branch) gets its parent and anchors read off the
graph — node contacts with exactly one other circuit, not mutual — because the binder binds them
only from `supply`/`return` lines. `NavigationOrder` became one tab order over flow components and
instruments together. Docs: `advanced/how-the-diagram-is-arranged.md` and a role table on
`circuit.md`. Nineteen tests, 1629/0/4. The user's two addenda to `D-100` — the layout report is
columnar text like the solve report, not JSON; layout reasons on a bounding box and port anchors,
strokes are for drawing — are recorded in `62` and `53`. Filed `C-79`: the cycle basis is the solver's, not the drawing's, and on the header every component is a loop member.

**P5.1b is `ModelContractBuilder.Build(input)` in `FluidScript.Core.Model` and
`ModelContractJson` in `FluidScript.Api/Contracts`.** The builder projects the bound model, the
lowered graph and the outer-loop result into `26`'s records -- every number in the script's canonical
unit beside its unit, six significant digits, `stated`/`sized`/`default` with a basis, the solved
operating point per component and connection read back through the new `SolvedStates`, `25`'s hints
field for field, `44`'s diagnostics in both position forms, provenance, and the `show` directive
resolved to a scale. The serializer is the Api's because `D-47` says Core names no serializer and
the architecture tests enforce it: the first draft had it in Core and three tests said so, which is
what they are for; `D-101` records the split and reads `41`'s invariant 1 as *no domain type on the
wire*. Eight golden files (four samples, compile-only and solved) are checked in under
`FluidScript.Api.Tests/Contracts/Goldens` and regenerate only with `FLUIDSCRIPT_UPDATE_GOLDENS=1`;
the round trip is byte-identical in both forms. `26` records ten precisions the shape needed, the
one that matters most being that a promoted head or Kv is `sized` on the wire with the solver as its
basis. `L-50` filed: the binder does not bind `show`, so `FS1210`–`FS1214` are unregistered and the
contract reads the directive off the syntax. Docs: `functions/model-contract.md`, generated from the
records' own XML docs by the docs gate, and `show.md`'s resolution table. Core 1648/0/4; Api 15/0.

**P5.1c is the strokes inside `SymbolCatalog` and the payload baseline.** Every symbol `53`'s
inventory lists now carries its primitives on the wire -- with `fill: "state"` marking the slot the
colour scale paints, `fill: "stroke"` a solid mark, and `dashed` on the controller's bubble -- and
`docs/functions/model-contract.md` gains a symbols section whose table the docs gate generates from
the catalogue. `53`'s inventory gained the sensor row it had been missing since `D-61`. The
200-component reference model is generated, not checked in: `ReferenceModels.DistributionHeader(18)`
in `FluidScript.Fixtures`, `01`'s header with eighteen pumped consumers, exactly 200 components and
19 circuits. Measured: 189.8 KiB compile, 237.6 KiB solved with every state, ~1.2 ms warm
serialization, 1.1--1.5 s to solve; `05`'s M3 payload criterion is ticked on the server side and
`26` holds the numbers. `D-102`, asked for by the user mid-package: every anchor carries its outward
direction, a symbol may offer alternative arrangements of its ports (the exchanger's `u` beside its
through-pass default), `25`'s `PortSides` is read off the default arrangement, and `53` states the
Manhattan-plus-bends cost the renderer picks arrangement and rotation by. Core 1656/0/4; Api 18/0.

**P5.1d-1 is `LayoutSolver.Solve(graph, model, hints, margin)` in `FluidScript.Core.Layout`**, and
it exists because the user stopped P5.1c's plan mid-sentence: the frontend was about to own
placement, and *all solving and calculation should be made in Core* -- the frontend is a renderer.
`D-103` records that; the `CLAUDE.md` non-negotiable now says geometry never moves to the frontend
either. The solver is three passes over the hints. *Cells*: a distribution group's parent becomes two
rails and each child branch a column between them (the longest simple path through the branch, so a
bypass edge cannot shortcut it), a loop a ring with bare corners (`D-44`), a chain hangs outward,
and a model with neither is placed by thermal stage. *Orientation*: every symbol's arrangement
(`D-102`), quarter turn and mirror are chosen by Manhattan stub-to-target length plus a bend
penalty, two passes so later neighbours can move earlier choices. *Coordinates and routes*: each
column and row is sized to its widest and tallest symbol plus twice the margin, which is what makes
`D-103`'s invariant -- no inner box inside another's outer box -- a property of the grid rather than
a check; a route leaves each anchor for `margin / 2` along its direction and the two stubs are
joined by the cleanest of the simple orthogonal joins (no box crossed first, then fewest bends, then
shortest). The wire carries it as `layout.margin`, `extent`, `placements[]` and `routes[]` (`26`).
`D-104` puts the styles beside it: `style name = tokens` defines, `style name` on a circuit and
`style=name` on a component apply, `fill=` is keyed and `show` overrides only the fill; Core
resolves named colours and the wire carries `#rrggbb`. `spacing` is the margin now, 0.5 by default,
and the samples' `spacing 20` -- twenty pumps -- became `0.75` (`55` records why). Measured: the
solver is 24 ms on the 200-component header after `D-105`, under `07`'s 30 ms; the payload grew
to 278.5 / 325.7 KiB.
Seven layout SVGs under `diagnostics/layout/` are what a session looks at, since it cannot see a
canvas, each with a `.txt` beside it listing every placement and route in world units.
**The user reviewed the first pictures on 2026-09-15 and named five things wrong with them**, which
became `D-105` (2026-09-16): a declared pipe was a box, an inferred node a circle as big as a
junction, an exchanger's off-centre pass met the pump with a jog, ties fell to enumeration order,
and the three-way valve's body was not one a manufacturer builds. Now a pipe and a two-port node are
points on their run (no cell, no box, nothing drawn for the node, a label for the pipe), a junction
is a 0.2 dot with one pipe per side, a symbol slides in its cell so its run anchor sits on the line,
a designer's orientation is a cost below a bend, routes are penalised for running along a drawn pipe,
labels stay upright beside the placed box, and `three_way_valve` has `a`–`ab` straight with `b` the
angle port (Belimo, Siemens VXG -- cited in `53`). **Then the user pointed at `53` itself**, and
the cells were re-planned to its shape: every branch and every loop is one U (`ShapeU`/`PlaceU`) --
supply run along the top, the last exchanger down the far side with its neighbouring valves, return
along the bottom, the bypass junction under its valve, members that lead out of a loop climbing the
near side, the entry junction in the top-left corner, all four corners reserved so no chain lands on
one -- and a loop runs the way the plant is piped, read off port roles rather than the solved
orientation hint so the compile and the solved drawings are the same drawing. `53`'s worked example
was updated for the valve body (the valve climbs the left vertical instead of sitting on the corner)
and three of its acceptance boxes are ticked; filed `C-83` (rail ends, where a chain hangs off a U, bypass-leg components). The 200-component header solves in 24 ms, under `07`'s
30 ms, because two hundred fewer cells are routed. Filed `C-80` (bypass legs hug the branch line), `C-81` (orientation ties fall to enumeration
order), `C-82` (nested headers are laid out as chains); closed `L-1`. Core 1695 total/0 failed/4 skipped, twice; Api 18/0.

**P5.1d-1 continued on 2026-09-16 with the router and the audit, and ended with `D-106`.**
`OrthogonalRouter` replaced the simple joins: a Hanan grid over every outer box and every laid pipe,
Dijkstra with bends and pipe-following penalised and no doubling back, a straight join when two
ports face each other with nothing between, hops recorded where a route crosses an earlier one,
each port's stub reserved as a lane so no later pipe wraps it, and the stub a whole margin long from
the inner boundary to the outer one, turning only from there. `SceneAudit` (Core) is the layout's
validator: inner box inside an outer box, a pipe through an outer box, a pipe beside a pipe closer
than the margin, with the run-ownership and port-pitch exemptions; `SceneText` (tests) writes the
scene as text per sample -- rotation, inner and outer boxes, each port at both boundaries with its
vector, every route with its band, the audit's findings -- because the user diagnoses text faster than
SVG. Then the user reviewed the four pictures against sketches and named the drift: the substation
and the simple loop were laid out by a cost and a router rediscovering what one rule states (the
exchanger takes the flow down, so the pump is below it), and four bends appeared where there is
one. Their specification of the rule-based engine is `28` (kept verbatim beside it) and `D-106`; the
pictures were brought to the sketches by hand-tuned hints (`_approach`, `FlankStep`, `Jog` lanes) to
prove the audit and the text, and those hints are what `28`'s stages replace. Measured after the
router: `header-200` 21 ms (`07`'s 30 ms). Core 1702/0/4 twice; Layout 55/55; Api 18/0 with goldens
regenerated. Filed `C-84` (labels are placed, not laid out) and `C-85` (the thermal-stage fallback
stacks); `C-80`, `C-81`, `C-83` are answered by `28`'s stages rather than fixed in P5.1d-1's engine.

**P5.1d-2 built the engine whole the same day, then started again.** The first build implemented
all six stages of the `D-106` list: y up everywhere in Core, link directions by roles and
propagation, inline elements contracted into runs, a header laid per `28`'s branch rule, simple
loops by Tarjan SCC solved by a four-side partition search into rigid groups, everything else hung
from placed ports, the router last. Seven samples audit-clean (hard 0 / soft 0), `header-200`
23.5 ms, Layout 55/55. The user judged the pictures wrong in the same way the P5.1d-1 pictures were
wrong -- exchangers lying down, a chain turning vertical, flow reading top to bottom -- and asked
for the plan to be re-evaluated and the engine restarted one component at a time. The evaluation
kept `D-103`, `D-100`'s standard and `D-106`'s principle; dropped `D-38`'s picture, `D-31`'s bands,
and orientation-as-cost (`D-102`, `D-105` item 4); and found the P5.1a hints `Rank`, `PortSides`,
`Loops`, `LoopOrientations` and `BranchShapes` to be the old engines in data form. `D-107` records
all of it. The state now: `28` rewritten as model / standard / rules / candidates; `29` the ladder;
`25` reduced to the nine hints that survive, the wire and goldens with it; `53` a renderer document;
a fresh `LayoutEngine` with `28` A6 (boundary stubs) and C1 (the first component at the origin);
the old engines parked in `~/fluidscript-attic/2026-09-16-engine/`. Step 1 is drawn: one pump,
two stub ticks, hard 0. `LayoutSolverTests`' routing and audit gates and `LayoutTimingTests` are
skipped until the ladder reaches the samples; `LayoutLadderTests` is the gate. `C-86` and `C-87`
were filed against the first build and stay open as observations for the ladder to answer.

**Step 1's correction came as four standing rules, not one picture note (`D-108`, 2026-09-16).**
Heat flows left to right and every flow loop runs clockwise -- promoted from `28` B's fourth
priority to hard constraints H9 and H10, which together fix a loop's source on the left flowing up,
its consumer on the right flowing down, and re-derive `R-48`'s rails for a closed ring that
`D-107` had withdrawn with `D-38`; an exchanger, a tank and a heat pump are mirrored and never
turned, as a transform class the catalogue carries (`free`, `standing`, `upright` -- the tank's
layers are why the last is not the second); every node, boundary nodes included, is laid out with
its box and outer boundary, withdrawing the stub-and-tick `28` A6 a session had decided; and a
layout is checked from its text, which moves into Core and gains a raster. `28` C now holds C1
(corrected: the first component is the heat source) and C2–C4 *stated*; `29` gained the planned
ten steps, each asking the user one question, ending at every sample. Two questions are left for
the pictures: whether an inferred node's boundary takes clearance (step 2) and which free-turning
members leave a loop's bottom for a vertical (step 3). Filed `C-88` (the audit measures four of ten
hard constraints), `C-89` (`SceneText` in tests, `62`'s second text withdrawn), `C-90` (no
transform class in the catalogue; `Transform.All` offers every kind eight).

**Step 6 was accepted on 2026-09-17 after three corrections and one decision (`D-112`).** The
cooling loop's first drawing put the three-way valve on the loop's right side with the loop passing
through it; the user wanted it at the top-right corner turning the flow, the junction at the
bottom-right corner, and the supply under the return. That became `28` C9 (a consumer that can turn
the corner takes it), C10 (a junction beside the consumer takes the bottom-right corner), and C7 and
C8 widened (a loop is one root for pairing open ends; a corner junction's free port goes level). A
second correction repacked the loop: C10 had cleared the consumer against a junction box that was
about to move. The session then had the sample state its ports so the loop would leave by the angle
port, and the user withdrew the reasoning: the switched ports are interchangeable on the drawing.
`D-112`: the symbol offers a `swapped` arrangement and C9 admits it at the corner, so the sample as
committed and the stated variant draw byte-identical geometry; the sample swap was reverted before
commit. Steps 1–5, the simple loop and the substation are byte-identical to what the user accepted;
the cooling loop draws with two bends. Open from the ladder: `28` open question 2,
`C-89`, P5.1e, the source-side corners of C10 when a step needs them.

**Step 7 (2026-09-17) drew the ring with one injection branch twice.** The first draw was the
plan's worked example, a vertical column with the pump turned to vertical; the user called it
technically correct and not the intended layout, and gave three rules (`D-113`): a pump is level,
a member on a side with slack sits at its middle, and an inner loop is laid out first as a block
by the same rules as the cooling loop, the header routing around the blocks afterwards. `28` C11
is now the block -- the first rigid group (A8) the engine builds, laid out at a provisional
origin and slid into the ring with its runs -- with C12 (centring) and C13 (level pumps, the
`level` transform class on the wire) beside it, and open question 2 answered for pumps. The
user then had the block present its inlet and outlet together to its parent -- the split
junction at the bottom-left corner, the return rail level from it -- and asked for loops to be
found recursively into nested blocks, which `UnitOf` now does. The third draw is audit-clean,
four bends, with steps 1–6 byte-identical, and the user accepted it. Every ring and block is
now a layout group when it is one component to the rest of the system -- one inlet, one outlet,
a tap that returns to the source's ring not counting (A8 as built: `loop-n`, nested, in the text
and as frames in the picture; not on the wire until P5.1d-3). `D-114`: a node with two
connections is inline whether declared or inferred, which closed the gap the datum node had put
between the source and the block. Step 8, the second branch, is where two blocks of one shape
must draw alike.

**Step 8 (2026-09-17) drew the header with two branches, in parallel and in series.** The user
asked for both scripts and that they pass before being drawn, so the ladder got a solve gate
(`EveryStepSolvesAndSettles`, `62`), which found `C-91` (a lone injection valve sized against the
whole ring) and `S-63` (two branches in series never converge) before a line was laid. The parallel
header is `m2-distribution-header` verbatim: the radiator block takes the ring's right side as
step 7's did, and the AHU branch hangs between the rails under `N3` and over `N5` -- `28` C14,
`D-108`'s branch rule in its closed form -- one bend to feed it and one to return it. The series
header puts the radiator block on the top rail with its outlet facing on and steps down into the
AHU block (C11 widened). Both are audit-clean with no soft findings, the two blocks congruent,
steps 1–7 byte-identical. With branches in the rules the 200-component header draws through them,
hard 0 and soft 0, so `LayoutTimingTests` is off skip at 56 ms best-of-five -- over `07`'s 30 ms
line, `C-92` -- and the distribution header's sample gates are live. The user accepted the series
picture and corrected the parallel one once: the junction a branch returns to stands directly under
the one that feeds it, as a loop's supply and return nodes align (C14 as it stands). Four branches
in series (8c, a staircase of blocks) and four in parallel (8d, three hanging and the last on the
rail) then drew with no rule touched, and the user accepted all of step 8; a parallel header with
one branch in series (8e) needed C14 to read a branch as a chain of blocks, and draws clean. Step 9,
the tank between two supplies and two returns, drew with no rule added -- the open fan is C5 from
the head, the tank upright, every pipe straight -- and the storage header's sample gates are live.
Step 10, a sensor and a controller on step 4's loop, drew with the first engine's instrument rule
and the user corrected it twice over: a signal leaves a sensor by a side other than its own
connection and reaches the controller in one bend with a whole stub, the sensor sliding along its
inline node's rail to make room (`28` C15); and at a crossing the route in front runs through while
the one behind breaks -- signals behind return behind supply, the hotter circuit in front when two
overlap (C16, the temperature rank open as `28` question 3). Every route carries a `layer` on the
wire now. The user accepted the corrected picture: every planned rung is climbed.

**Step 11 (2026-09-17) began the syntax tour.** Five independent circuits in one file drew as one
fragment and a fallback column (hard 71); the user's rule is that independent circuits go under
each other in script order, never side by side, and to approach the tour two loops at a time.
`28` C17: the graph's fragments, each laid out on its own canvas by the existing rules and stacked
one margin apart, left edges aligned, in the order the script declares them. Two loops draw clean
(11a, with the user) and the tour itself falls to hard 4 untouched. The solve gate found `C-93`:
the binder refuses two independent circuits in one project as one disconnected circuit
(`FS2213`), which the syntax tour, documented as not meant to be solved, had hidden. Step 11b
drew the tour's first two circuits and, through three readings with the user, made the code match
`D-114` (a boundary is never inline), widened C7 to every open end, and turned a valve placed from
a pipe so that the flow leaves it to the right; the tour's expressions circuit was rewired to read
as plant (`L-52` filed from the user's remark on supply nodes). Step 11c took the tour's two loops
with no heat source: `28` C18 lays a sourceless loop out as a ring from its consumer with a bare
left corner, and C1 reads a component's role from its written kind, so a load whose power is a
curve is a consumer before any curve is read. Both loops draw clean; the tour stands at hard 2,
all in the open supply-to-return form no rule draws yet (11d). `C-94` filed: a controller's
signal runs through the valve it drives when sensor and controller land on opposite sides of a
rail; `S-64` filed: wiring the tour's `ahu` between two stated pressures makes the seed run the
parallel load backwards, and the sample carries `# does not seed: S-64` (`62`). Step 11d, the same
day: the open supply-to-return form has its rule (`28` C19 -- the supply and the return are the
two left ends of a ring's rails, the chain under the supply, the looped path on the right), `C-94`
closed (a signal that would cross a box or run along a line is routed round them), and the syntax
tour draws hard 0, soft 0 and joins `Reached`; `C-95` filed for the three audit gaps the faulty
picture exposed. Every sample now draws audit-clean. **`D-115`, the same day, from the user's
reading of that picture:** the boundary kinds are `inlet` and `outlet` (`supply` and `return` are
a circuit's pipes), a boundary has exactly one connection (`FS2205`, an error), and a closed
circuit without a stated pressure is warned (`FS2201`). The rename runs through the keyword table,
`BoundaryRole`, the attachment statements, the wire's `inletAnchorId`/`outletAnchorId`, every
sample and ladder script, the docs page `inlet-outlet.md` and `22`'s tables; the tour and step 11b
write junctions after the inlet and before the outlet, C19 is keyed to them, and `S-64` and `L-52`
close. Core 1732/0/4, Api 18/0 with goldens regenerated. The tour's reading after that
(2026-09-17): a chain taller than the open form's block rebuilds the block deeper so the return
runs straight (C12), and a fragment's declared boundaries share one root so they align (C7); step
11b's one-path form is the chain alone with the outlet at its foot (C19). The whole ladder was
then drawn at `spacing 1`: every step and sample hard 0, soft 0 but the tank form, `C-96` filed.
Core 1734/0/4. P5.1d-3 on 2026-09-18 (the report in Core with its raster, `C-89`; the audit's three
gaps, `C-95`; the predicate sweep): Core 1796/0/4, Api contracts 16/0. P5.1e the same day: a
connection line ends in `name=value` pipe properties, held on the `ConnectionSyntax` and printed as
written; the binder declares one `pipe` per connection on the line (I7, `{A}__{B}`, created in step 1
so its parameters bind and evaluate like a declaration's) and wires the connection through it, I2's
nodes beside it named `{pipe}__in` / `__out`; the factory gives an implicit pipe with no `length` a
decided default of zero, and `Pipe` accepts it. Every sample, `01`'s three reference circuits and the
24 ladder scripts write their pipes on the line (49 pipes); the tour keeps `PA3` declared because its
far end is the `outlet` attachment's, not a connection's. The rule number: `11` already had I4 (flow
direction) and I6 (chains), so the implicit pipe is I7. Core 1805/0/4 (two rows more: the pipe page's new example block is parsed as a sample), Api 18/0, goldens regenerated; `C-98` filed for the test process's intermittent CoolProp crash at exit.

**P5.2 is the host in `FluidScript.Api` and one pipeline behind four routes** (2026-09-18).
`ScriptPipeline` is the whole of a request: the compatibility gate (`18`), parse, `07`'s
declaration and token limits, bind, the `solve` escalation of `FS1507`/`FS1511` to errors, the
catalogue pin, the substance, `OuterLoop.Prepare` for the unknown limit, the solve, then
`ModelContractJson`; `validate` returns after the bind, `solve: false` before the solver, and the
three share every stage above the one they stop at (`42` invariant 3). Over a limit is `FS4601` in
the model's diagnostics with the solver skipped, and only the byte ceiling is a status (`413`).
Sessions (`41`) are `(apiMajor, sessionId)` in a `SessionStore` with a thirty-minute idle eviction:
a new request on a session cancels the draft in flight, which is answered `499`, and a converged
solve is remembered as a `WarmStart` — the solution with a topology hash, sixteen hex of SHA-256
over the unknowns' `Kind:Owner:Name` — that `OuterLoop.RunAsync` takes as its seed when the next
script's hash matches, retrying cold when the warm start fails to converge. Measured on the cooling
loop: the warm compile takes the first solution as its Newton guess and converges in fewer
iterations; a whitespace edit warm-starts, a changed topology is cold. `metadata` is a lazily built
document with an ETag (a matching `If-None-Match` is `304`), covering every registry kind with its
parameters, aliases, families and units, every dimension, every diagnostic code live and retired,
the symbols, the catalogues, the property backend and the limits; `/openapi/v1.json` is generated
from the handlers. Request-level failures are RFC 9457 problem details: `400` naming the missing
`field`, `413` with `bytes` and `limit`, `500` with `FS9001` and a `correlationId`. The JSON schemas
of the model contract, the compile response and metadata are exported from the C# records and
committed under `Api/Contracts/Schemas` (`D-46` step 2), gated like the goldens. `edit` is P7.1's,
with the mutation API it fronts — decided with the user, no stub. The metadata test found `C-99`:
the exchanger's `u`, `ua` and `fouling` had been dimensionless in every model since their rows
were added, a static-initialisation-order slip, now closed, and the Api goldens moved on fourteen
`unit` lines. Filed: `A-1` (the equation system is prepared twice per solved request), `A-2`
(`docsIndex` is a path until the docs are served), `A-3`/`L-54` (base-unit spelling on the wire
for the two dimensions the unit table cannot name), `L-53` (`FS1503` spans the value with the
name). `docs/advanced/using-the-api.md` is the page. Core 1805/0/4, Api 55/0.

**P5.3 is `frontend/src/design` and the first frontend tests** (2026-09-18). `tokens.ts` names
every colour token in `55`'s order (with `--syn-function`, `--editor-bg` and `--editor-fg`, which
the list had left out, `F-3`), the theme-independent scales, the durations, the syntax opacities,
`D-73`'s advance widths and the contrast pairs. The two built-in themes are `themes/light.json` and
`themes/dark.json` in the public custom-theme format, and `themeCss.ts` renders `55`'s cascade from
them -- `:root` light, `[data-theme='dark']`, the same under `prefers-color-scheme` when nothing
chose, every duration zeroed under reduced motion -- into a checked-in stylesheet gated by a test
that regenerates it. `theme.ts` reads a custom file without throwing, fills its gaps per token from
the selected built-in naming them, measures every declared pair at WCAG AA and loads a failing theme
with the pair named; `applyTheme` is an attribute for a built-in and inline properties under
`custom`, so switching re-mounts nothing. The UI store of `51` exists with its `theme` field,
Zustand persisted to localStorage, and a rendered test rehydrates it. The primitives are `Button`,
`IconButton`, `Panel`, `Card`, `Badge`, `StatusDot`, `Toolbar`, `Tabs`; the four with drag or
positioning behaviour wait for a consumer. The scaffold's hero, logos and accent-purple CSS are
gone; `App.tsx` is the provider, a toolbar with the theme control, and a preview page for judging
a theme until P5.4's shell. The values `55` did not fix -- surfaces, text, borders, canvas -- were
chosen to clear the contrast test and have no other basis (the defects file says so). Vitest and
jsdom are the test runner (`62`, `63`); `npm test` is 25/0, `tsc -b` and oxlint clean, Prettier
clean. `docs/advanced/themes.md` is the page. Core and Api unchanged.

### After P3.7b — the convergence work · 2026-09-07 to 2026-09-09 · 60 commits

**This is state no phase table shows, and it is most of the last three days.** P3.7b closed with the
outer loop built and `24`'s worked example reached. What followed belongs to no package: it is the
difference between a solver that runs and demo scripts that converge, and it was driven by the defect
register rather than by `08`.

The load-bearing ones, by what they settled:

| Area | Entries | Outcome |
|---|---|---|
| Seeding | `S-26`, `S-30`, `S-35`, `S-46`, `S-49`, `S-50`, `S-51` | The seed went from "a number per unknown" to a construction with stated properties — mass-consistent, inside the property domain, off every bound, oriented by the pumps, and with no branch at rest |
| Mixing | `S-58` | A junction's arriving enthalpy is the mass-weighted mix of its inlets, not their average — the one-line defect under `S-48` and `S-51`, found by stating a position and reading the converged number |
| The fourth plant | `S-59`, `D-93` | Two pumped sources, two mixing consumers, one direct — built as a scratch experiment on 2026-09-14 and kept as `OuterLoopTests.TwoPumpedSourcesShareALoadTheirConsumersSetAndEveryPumpKeepsItsOwnLoop`. It found the closure running after the count and a source pump sized to a loop through a consumer's pump; converges in two iterations on hand figures |
| Three-way valves | `C-60`, `C-61`, `C-63`, `C-66`, `D-85`, `D-88` | Ports named `ab`/`a`/`b` as manufacturers label them, sized by authority, and identified by the port name the script *wrote* rather than by walking the graph |
| Counting and rank | `S-33`, `S-36`, `S-39`, `S-41`, `S-43`, `D-86`, `D-90` | A singular system now names the equation its other rows imply, instead of naming a component to blame |
| Pressure boundaries | `S-38`, `S-44`, `D-86`, `D-87` | A stated pressure is a boundary only on a boundary; a temperature on an interior node is a setpoint that promotes the split holding it |
| Rounding direction | `C-62`, `D-89` | A valve rounds down against a free pump and up against bounded pressures, and two-way valves now get the context that decides which |
| Diagnostics | — | One solve report answers every question the ad-hoc probes were asking; `diagnostics/` carries the timings |

**Not everything here was progress.** `be69f5c` audited all 49 open defects against the code and found
four already solved and two that were never work — which is the cost of a register that only ever
grows, and the reason a periodic audit is now part of the workflow below.

## Open questions, by tier

Counts only. Every description lives in the file named.

| Tier | Open | File |
|---|---|---|
| 00 · Foundation | 1 | [`00-foundation/defects.md`](00-foundation/defects.md) |
| 10 · Language | 8 | [`10-language/defects.md`](10-language/defects.md) |
| 20 · Core domain | 21 | [`20-core-domain/defects.md`](20-core-domain/defects.md) |
| 30 · Solver | 16 | [`30-solver/defects.md`](30-solver/defects.md) |
| 60 · Docs and dev-ex | 2 | [`60-docs-and-devex/defects.md`](60-docs-and-devex/defects.md) |
| | **48** | |

Tiers 40, 50 and 70 have no defect record because nothing has implemented against them yet. Their
absence means nothing has looked, not that nothing is wrong — the same caveat each existing file
carries about its own unread documents.

**Nothing open blocks the three demo scripts any more.** The header's remaining entries were each
measured on a *variant* and stay open on their own merits: `S-53` (the seed doubles a three-way
valve's inlet legs when the source outlet is omitted), `S-55` (driver analysis misses distribution
pumps once a source valve is added), `S-56` (a zero-duty consumer inherits its sibling's flow),
`S-52` (`FS2211` sends the user to the balanced half) and `S-45`'s residue, which `FS2218` now
makes visible without deciding. `L-47` (closed) records the sign discussion of 2026-09-14: `power`
never carries flow direction, terminals stay port-bound, and `FS3013`/`FS1308` say so. Nothing stands between here and M2a's exit any more; it exited 2026-09-14 with `C-76` closed. P3.8 closed `C-51` with `D-94` on 2026-09-14; the M2a
sweep of 2026-09-14 filed `C-74` (no `FS23xx` code is registered; sizing speaks in notes) and `C-75`,
and closed `F-24` by moving the model-contract payload criterion to M3. A four-instance code review
of Core the same day, asked for the `C-76` class, found no second leak and three ways a script could
take the process down --- two stack overflows and a binder crash, `L-48`/`L-49` --- plus the
tier-20 sweep recorded as `C-77`; all closed before P4.1 starts. The review's untriggered aspects
(error handling in Binding/Syntax, resource management outside Fluids/Solvers, architecture) are
unassessed, not clean.

## What is next

1. **`01`'s header listing has drifted from the sample that meets its figures**, in three recorded
   ways: attachment replaced by hand wiring (`F-16`/`F-17`), `PU_MAIN` removed because the consumer
   pumps drive the whole loop (`S-55`'s subject), and `load` in place of `heat_exchanger` (`D-91`).
   The figures and the tag table are unchanged. Updating the listing to the sample verbatim is a
   spec edit and the user's call; until then the sample is the reference and `01` the intent.
2. **Whether the fourth plant becomes a fourth reference circuit.** It converges on hand figures as
   a test fixture; promoting it to `samples/` and `01` is a spec addition (`D-11`) and the user's
   call. Its natural next variants — one source off (`S-56`, and there is no check valve to stop
   reverse flow through it), a source-side mixing valve (`S-55`) — are the open entries it points at.
3. **`S-53`'s four ordered fixes**, and the valve-sizing observation under `S-58`: an
   equal-percentage valve sized for authority at full open sits at 0.6 travel dropping 24–45 kPa,
   and the pump pays.
4. **P5 — M3, the usable static product** (`08`); `P5.1`, the model contract and layout hints, is
   Core-side and closed by golden files before a pixel exists. `D-100` (2026-09-15) triaged a
   proposed layout standard before P5.1 started: hard constraints as a named class, an explicit
   priority order, equivalent assemblies drawn congruently (P5.1a's `BranchShapes`, withdrawn by
   `D-107`), edit stability as an invariant, three spacing tiers (dropped by `D-107`), and a text layout report
   (`LayoutExplanation`, P5.1d-3's since `D-103`) so a session can read a placement it cannot see. Temperature
   ordering of branches and barycentre reordering are deferred with reasons.
5. **The layout ladder has reached every sample** (`29`, 2026-09-17): all 24 layouts draw hard 0,
   soft 0 at the default spacing, and the rules are keyed to shapes, never to a circuit. What it has
   not reached is `29`'s closing list (the open form with more than two paths, a branch off the
   bottom rail, a block with no corner-taker, the valve residue of the loop search, a declared
   pipe off a ring); each waits for a script that needs it. The user closed P5.1d-2 there
   (2026-09-18: "the layout is at a sufficient level now; adjustments when the product is
   testable"), which is the sign-off `08`'s exit asked a person for. **P5.1d-3 shipped the same
   day:** the layout report is `SceneText` in Core with `28` A10's raster and `62`'s metrics
   (`C-89`); the audit measures cycles through inline nodes, a pipe through its own component and
   signal lines against boxes and pipes (`C-95`, three tests on bent scenes); and the predicate
   sweep is `LayoutPredicateTests` -- determinism, the port-for-port bijection, normalised routes,
   the transform class, spacing as presentation, congruent branches and three edit-stability
   fixtures -- over thirteen fixtures, with `62`'s L1--L19 table reconciled to the `D-107` engine
   (L13 and L16 withdrawn, L4, L11 and L15 the renderer's). `08`'s other exit, the reflow's
   monotonicity argument, is met vacuously: there is no reflow; a form that cannot finish backtracks
   whole. Still open from the package: `C-92` (48 ms against the 30 ms line, unprofiled), corpus
   mutation over the samples (`62`), and the report on the wire. Next: **P5.1e** (`D-110`, pipe
   properties on a connection line), then P5.2.
   What `P4` left behind, none of it blocking: `C-78` (the plate catalogue's shopping list — a cited
   `U`, a plate step, the `lamella` correlation, `FS2311`), `22`'s unticked crossover criterion (a
   solve driven across `C₁ = C₂`, not just the duty relation stepped over it), `C-75`'s last
   follow-on (a solved Kv has no basis line saying what it absorbed), and the `400HP01` criterion
   that waits on M4's `heat_pump` kind. `F-19`'s budget re-derivation has real numbers to work
   from (16 µs per water state, not 63).

## Standing baselines

Numbers a session can check in one command, so that "did I break something" has an answer that is not
a judgement.

| Baseline | Value | Where |
|---|---|---|
| Core test suite | **1805 total, 0 failed, 4 skipped** (four unrelated; the layout timing test is live since step 8), ~65 s with the `Diagnostic` classes, ~15 s without | `FluidScript.Core.Tests` |
| API test suite | **55 passed, 0 failed**, ~5 s | `FluidScript.Api.Tests` |
| Frontend tests | **25 passed, 0 failed**, ~7 s | `cd frontend && npm test` |
| Frontend checks | `tsc -b`, `npm run lint`, `npm run format:check` all clean | `frontend/` |
| Build | **0 warnings** (`TreatWarningsAsErrors`) | `dotnet build` |
| Unit tier | under 2 s | `--filter-trait Category=Unit` |

`dotnet test` discovers zero tests in this environment; the binaries under
`~/.dotnet-artifacts/bin/<project>/debug/` are run directly. That and the Visual Studio `obj/`
collision that produces hundreds of spurious `CS0246`s are written up in
[`60-docs-and-devex/defects.md`](60-docs-and-devex/defects.md) under Observations, which is the
authoritative account of both. **Environment traps are the one thing that also belongs in an agent's
own memory**, because their whole value is firing before the mistake rather than after someone goes
looking. Nothing about the project's state does: that is this file's job.

## Updating this file

A phase or package is not complete until this file says so. The workflow — what to read before
starting, what to write when a package closes, and when a defect gets an entry — is stated once in
`CLAUDE.md` and pointed at from `AGENTS.md`. It is not repeated here, because a workflow described in
two places is a workflow that disagrees with itself.

What this file needs when a package closes:

- Its row in the phase table, with the commit and the date.
- The defect ids that package **closed** — ids only, never their reasoning.
- Any open id it **created**, if that id changes what the next session should do.
- The standing baselines, if they moved.
- The "Where the project stands" block, if the position moved.
