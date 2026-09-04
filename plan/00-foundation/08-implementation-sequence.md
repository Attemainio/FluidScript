---
id: 08-implementation-sequence
title: Implementation sequence
tier: 00-foundation
status: draft
owns: [work-package decomposition inside a milestone, intra-milestone build order and its rationale, per-package verification, the pre-M0 prerequisite phase]
depends_on: [01-vision-and-scope, 03-repository-layout, 05-milestones-and-acceptance, 06-decision-log, 07-quality-attributes]
traces_to: [R-07, R-16, R-17, R-25, R-28, R-30, R-40, R-43]
open_questions: 0
last_review_pass: 0
---

# Implementation sequence

## Purpose

[`05-milestones-and-acceptance`](05-milestones-and-acceptance.md) says what must be true for a
milestone to exit. It deliberately says nothing about the order in which the work inside that
milestone is done. That order is not a matter of taste: several packages are cheap in one position
and a rewrite in another, and the difference is invisible until the rewrite is due.

This document fixes that order and states the reason for each placement. What breaks if it is wrong
is schedule, not correctness — but the failure mode is the expensive kind, where a milestone is
declared done and a later milestone discovers that a foundational shape has to change underneath
code that already depends on it.

## Responsibilities

**Owns.** The decomposition of each milestone into numbered work packages, the order of those
packages, the reason each sits where it does, the verification that closes each one, and the
pre-M0 prerequisite phase.

**Explicitly does not own.** The milestones themselves, their exit criteria, their demo scripts, or
the ordering constraints *between* milestones ([`05-milestones-and-acceptance`](05-milestones-and-acceptance.md));
the directory tree and migration steps ([`03-repository-layout`](03-repository-layout.md)); test
tiers and validation cases ([`62-testing-strategy`](../60-docs-and-devex/62-testing-strategy.md));
CI mechanics ([`63-ci-and-repo-hygiene`](../60-docs-and-devex/63-ci-and-repo-hygiene.md)); and the
content of any package, which is owned by its tier document.

## Contracts

### The phase ladder

A **phase** is one milestone's worth of work, except P0, which exists because M0 could not otherwise
start. A **work package** is one branch, one pull request, one squash merge.

| Phase | Milestone | Packages | Dominant risk |
|---|---|---|---|
| P0 | pre-M0 | 3 | Plan self-consistency |
| P1 | M0 | 4 | SharpProp native packaging |
| P2 | M1 | 9 | The lossless trivia model |
| P3 | M2a | 9 | The sizing/solve outer loop |
| P4 | M2b | 3 | Two datums in one circuit |
| P5 | M3 | 11 | The layout engine |
| P6 | M4 | 7 | Run isolation under `D-22` |
| P7 | M5 | 2 | Nothing, if P2.5 was done properly |
| P8 | M6 | — | Evidence-gated; not decomposed here |

P8 is deliberately empty. `05` defines M6's contents as separately justified before entry, so a
package list here would invent the requirements that justification is supposed to produce.
[`35-evolutionary-sizing`](../30-solver/35-evolutionary-sizing.md) is the one tier-10-to-50 document
that lands here rather than in a package, and it is named so that the coverage check below can tell a
document deferred on purpose from one nobody scheduled.

**Every document in tiers 10 through 50 is named somewhere in this file, and a test asserts it.**
Two have now turned out to be unscheduled — the formatter (`F-6`) and script compatibility (`F-10`) —
and both were found by working backwards from a milestone criterion rather than by reading this
document, because its package tables are a good plan and a poor inventory. Nothing in the structure
notices a file that no row names. Tier 00 is this plan's own foundation, tier 60 is process, and tier
70 is explicitly future, so the check covers the contracts that get implemented and nothing else.

### P0 — Prerequisites

M0's exit criteria cannot all be met at M0, and two contracts are ambiguous enough to send M1 work
back. P0 closes both before any code exists.

| # | Package | Closes |
|---|---|---|
| P0.1 | Separate quality **harness** from quality **baseline** | `D-45` |
| P0.2 | Fix the source of truth for the model and realtime shapes | `D-46` |
| P0.3 | Independently reproduce every asserted reference number | The M2 arithmetic gates |

**P0.3 is not ceremony.** `05`, `01`, `07` and `62` assert hand-computed figures — 0.2392 kg/s,
0.0763 kg/s, 0.1630 kg/s, 5.28 m, DN20, 0.2871/0.3589 kg/s and 0.4306 through the header,
39 plates, UA 12.07 kW/K,
`dT2/dt = 0.020 K/s` — and those figures are the acceptance tests for P3, P4 and P6. Reproducing
them from an independent calculation costs a day. Discovering during P3 that a published figure was
wrong costs considerably more than a day, because the first assumption is always that the solver is
broken, and the solver is the thing least able to defend itself.

It paid for itself twice. All 71 derived figures reproduced on the first pass, but the *inputs* did
not:
P1.1 found `h(6 °C)` stated as 25 200 J/kg against CoolProp's 25 324, and the humid-air enthalpy row
stated as an ASHRAE ideal-gas value rather than what the backend returns. Both are corrected in the
documents that own them, and the derived flows moved with them — 0.2394 → 0.2392, 0.0764 → 0.0763.

The second time was `P3.4c`, and it was a figure that reproduced its own arithmetic while describing
the wrong circuit: the header's 0.6460 kg/s is the sum of the two subcircuit *loop* flows, which is
what the source would carry only if the header ran 40/60 °C. It runs 30/60, because both loads return
30 °C, so the source carries 0.4306 kg/s (`F-16`). A hand-check reproduces a number; only a hand-check
against the circuit reproduces the *right* number, and the way this one surfaced was the well-posedness
count reporting the fixture over-specified by one.

### P1 — M0: scaffold and the one real risk gate

| # | Package | Verification |
|---|---|---|
| P1.1 | The SharpProp spike | Compiles, publishes **and runs** on Windows and Linux; records API, ranges, basis, exceptions, construction cost, thread safety |
| P1.2 | Repository skeleton | `03`'s migration steps 1–7; a deliberately undocumented public member fails the build |
| P1.3 | CI, architecture tests, docs gate | Workflow green; all seven architecture assertions run as xUnit tests; docs gate runs against an empty registry |
| P1.4 | Harnesses without baselines | Five trait categories populated; Verify, Vitest, Playwright, axe all execute; `benchmarks/reference-environment.json` recorded |

**P1.1 runs first and alone.** It is the only package in the project that can invalidate a whole
tier. If SharpProp's real surface, native packaging, or returned types disagree with
[`21-fluid-and-state`](../20-core-domain/21-fluid-and-state.md), the contracts are revised before
M1 starts rather than after tier 20 is written against them. The UnitsNet decision in
[`03-repository-layout`](03-repository-layout.md) rests on this package's finding, and reversing it
later touches `Quantity`, the adapter, and every component.

**P1.3's assertions pass trivially on empty projects, and that is the point.** An architecture test
added after a boundary is broken is a refactor; the same test added before is a guard rail. The
same reasoning applies to the docs gate, which walks an empty registry today and fails on the first
day a component kind is registered without its page.

P1.2 also creates `samples/`, `docs/` and `benchmarks/`, and writes `samples/m1-syntax-tour.fluid`
and `samples/m1-syntax-reference.fluid` **before the lexer exists** — those files are the
specification the lexer is built against, not artifacts of having built it. Both were in fact written
in P2.3, one package late; the lexer's losslessness test is the first thing that needed them, which
is exactly the pressure that was supposed to be absent when they were written.

**They are two files because one cannot do both jobs.** The reference holds the syntax example from
[`01-vision-and-scope`](01-vision-and-scope.md) byte for byte and nothing else, so the nine-diagnostic
count that document fixes is assertable against it. The tour exercises every production, which means
more components, more inferred nodes and therefore a different diagnostic set — a single file
satisfying both would have to be the reference, and then it would exercise a third of the grammar.

### P2 — M1: the language spine

Order matters more here than anywhere else in the project.

| # | Package | Why here |
|---|---|---|
| P2.1 | Diagnostics, spans, the code registry ([`16`](../10-language/16-diagnostics.md)) | Every stage downstream returns these. Added later, every signature changes. |
| P2.2 | Dimensions, units, `Quantity` ([`13`](../10-language/13-type-and-unit-system.md)) | Pure, hand-checkable, zero dependencies. `Temperature` and `TemperatureDelta` as separate dimensions is a type-system decision that cannot be retrofitted cheaply. |
| P2.3 | Lexer with trivia attached ([`12`](../10-language/12-grammar.md)) | Losslessness is a property of the lexer and the AST. Assert `concat(tokens including trivia) == source` here, where it is one line. |
| P2.4 | Parser, AST, error recovery ([`12`](../10-language/12-grammar.md)) | Enforce [`11`](../10-language/11-language-overview.md)'s invariant 7 as a test: one token of lookahead classifies every statement. |
| P2.5 | **Printer and the round-trip fuzz** ([`17`](../10-language/17-formatting-and-round-trip.md)) | **The one placement worth arguing about.** See below. |
| P2.6 | Component registry, kind resolution ([`15`](../10-language/15-semantic-model.md)) | Data, not code. The docs gate reads it, so it must exist before a kind can be added. |
| P2.7 | Binder steps 0–5, expressions ([`15`](../10-language/15-semantic-model.md), [`14`](../10-language/14-expressions-and-references.md)) | Circuits, symbol table, kinds, parameters, dependency graph, evaluation. No topology yet. |
| P2.8 | Binder steps 6–11 ([`15`](../10-language/15-semantic-model.md)) | Ports, connections, inference I1/I2/I3, attachments, control bindings, the schedule, validation, tags last. Closes M1: `01`'s nine-diagnostic count on the syntax reference is asserted here. |
| P2.9 | **Version detection and the compatibility gate** ([`18`](../10-language/18-script-compatibility.md)) | `18`'s invariant 2 — semantics are selected *before* parse and bind — is an ordering constraint, not a preference. See below. |
| P2.10 | **The language half of `D-57`–`D-62`** ([`12`](../10-language/12-grammar.md), [`15`](../10-language/15-semantic-model.md), [`13`](../10-language/13-type-and-unit-system.md)) | `curve`, `design`, the `at` clause and the short `control` form are grammar and binding, and every one of them is a *language* change that P3 would otherwise have to make underneath six component kinds. See below. |

**P2.1 delivers no shared result type, and the row said otherwise until it was built.** Every stage
does return its output alongside `ImmutableArray<Diagnostic>`, but the plan states that shape as a
*named record per stage* — `ParseResult`, `BindResult`, `CompatibilityResult` — each with a payload
property named after what it carries. A generic `StageResult<T>` would either duplicate those or
rename `Root` to `Value`, and nothing consumes stages uniformly enough to pay for it. What is
genuinely shared, and therefore what P2.1 ships, is `Diagnostic` and the registry behind it. Whether
a stage throws is a property asserted by that stage's own fuzz test, not a property a type can carry.

**P2.5 is deliberately fifth, not last.** `05` lists the printer last in M1's prose, and building it
there would be a mistake with a delayed invoice. The printer does not create losslessness; it
*reveals* whether the lexer and AST already have it. Written fifth, a trivia model that loses
information is a cheap AST change. Written eighth, it is a change underneath the binder, the
registry and every golden file. `05` already states that M5 depends on this and that it is much
cheaper now than retrofitted; this document names the position that makes that true. From P2.5
onward the corpus-mutation round-trip fuzz is a standing test, not a milestone check.

**P2.10 pulls the language half of P3.0 and P3.8 forward, and leaves the physics half where it was.**
The split those two packages were built on is between *language* and *physics*, not between sensors
and curves: a `curve` that parses, binds, sorts, interpolates and resolves its driver needs no fluid,
no catalogue and no solver, while the same feature's meaning at the design point is a sizing question.
Building the language half in P2 costs nothing it would not cost later; building it in P3 means adding
statements, diagnostics and a registry marker underneath the six component kinds P3.3 has by then
built. What P2.10 delivered:

- `curve` and its rows, `design`, the `at` clause, the short `control` form, and their printing
  (`D-57`–`D-61`).
- Binding step 0b, curve evaluation at the design point, the clamped and extrapolated end rules, the
  schedule-role registry, timestamps, and `FS1527`–`FS1533`.
- `D-62`, and the `L-37` unit-resolution defect it was found alongside.

What is left, and stays where `08` put it:

- **P3.0** keeps the *physics* of an instrument — an observer's reading is a solved property, and
  nothing solves yet. The registry markers and the placement rule are done.
- **P3.8** keeps the half that needs sizing: `design` as the sizing point (`D-58`'s first job), and a
  curve as a live function of time in a transient run. Today the binder computes the design-point
  value and records the dynamic reference as deferred; the stage that consumes either does not exist.

**P2.9 exists because `18-script-compatibility` had no work package at all**, and M1's first exit
criterion depends on one: an unversioned draft must get `FS1701` and must not be durably saveable.
This is the same defect as the formatter's (see the foundation's `F-6`) and was found the same way —
by checking a milestone's criteria against the packages that were supposed to deliver them.

`18` does not fit in one package, and splitting it is what makes P2.9 small:

- **P2.9 ships `Inspect` only** — detect the major, classify the disposition, gate the allowed
  actions, and raise `FS1701`, `FS1702` and `FS1705`. It runs *before* the parser, because `18`'s
  invariant 2 says semantics are selected before parse and bind. A gate placed after the binder has
  already committed to current semantics, which is the one thing `D-27` exists to prevent.
- **P3.5 owns `FS1703`** (a pinned catalogue that is absent or unsupported). There is no catalogue to
  be absent from until then.
- **P5.9 owns the other half of `FS1701`** — Core withholds the `Save` action, and
  [`58-file-lifecycle`](../50-frontend/58-file-lifecycle.md) is what disables the button and offers to
  insert the directive. A diagnostic nobody acts on is not a gate.
- **Migration — `PreviewMigration`, `ApplyMigration` and `FS1704` — is deferred with a stated
  trigger: the day a language major 2 exists.** It cannot be written or tested before then, since a
  migration from 1 to nothing has no target and no fixture. This is recorded here rather than left
  unscheduled, which is precisely the mistake this package was created to correct.

Through all of P2 there is no reference to `Core.Fluids`, `Core.Components` or `Core.Solvers`, and
P1.3's tier-10 architecture assertion guarantees it. P2 closes with the M1 `/docs` pages and
`samples/m1-syntax-tour.fluid` meeting every M1 criterion, and with the nine-diagnostic count the
syntax reference in [`01-vision-and-scope`](01-vision-and-scope.md) fixes asserted against
`samples/m1-syntax-reference.fluid`.

### P3 — M2a: the hydraulic core

| # | Package | Why here |
|---|---|---|
| P3.0 | **Sensors as solved observers** ([`22`](../20-core-domain/22-component-model.md), `D-61`) | Before P3.3, not after. P3.3 builds six component kinds; adding a seventh family afterwards is six rewrites instead of one addition. The language half — the kinds, the `at` clause, the actuated-parameter marker — shipped in P2.10; what is left is what an instrument *reads*, which is a solved property. |
| P3.1 | `ISubstance`, `FluidState`, the single SharpProp adapter, **both** fakes | The constant-property fake buys test speed; the linear-in-temperature fake is what catches a component that only works with constant properties. One without the other is a false sense of coverage. |
| P3.2 | Property-accuracy tier — V4, V5, and the two basis cases V13 and V14 | Wrong physics at the source invalidates everything above it, so it is proved before anything is built on it. V13 and V14 belong here rather than later because they are about *this* layer's conventions — gauge against absolute, and per kg of dry air against per kg of mixture — and both are silent when wrong. `62`'s rule 3 costs real sourcing effort: only density has a published closed form to check against across the range, so the other three properties are pinned at one state and otherwise checked behaviourally. |
| P3.3 | Component model, six kinds in duty mode ([`22`](../20-core-domain/22-component-model.md)) | The zero-allocation `EvaluateResiduals` test is written **in this package**. Retrofitting it across six component types later is a rewrite. |
| P3.4 | Lowering, `CircuitGraph`, boundaries, well-posedness ([`23`](../20-core-domain/23-topology-and-graph.md)) | The "no syntax type in `CircuitGraph`" assertion goes live here. Ran as three: **a** the graph, **b** boundaries, counting and promotion, **c** the boundary kinds and the two consistency codes. **It needs one thing P3.5 owns** — a pipe has a bore and a script states a designation — so `P3.4a` takes an injected `IBoreLookup`. The packages do not swap: the seam is wanted anyway, because P3.7 re-instantiates components as sizing chooses values and lowering has to be re-runnable against changing geometry (`F-18`, `C-24`). |
| P3.5 | Catalogue ([`27`](../20-core-domain/27-component-catalog.md)) | Sizing cannot be written against a catalogue that does not exist. V12 closes it. The table is compiled C# rather than a data file (`D-66`), and its rows ship **unverified and refused** until two public sources per row are recorded — `Catalogs/SOURCES.md` carries the checklist and a test asserts the refusal. |
| P3.6 | Scaling, then Newton ([`36`](../30-solver/36-numerics-and-convergence.md), [`32`](../30-solver/32-steady-state-newton.md)) | **Scaling first.** An unscaled residual norm measures the pressure equation and nothing else; Newton built first is tuned against a meaningless number, and the tolerances then have to be redone. **It also takes [`31`](../30-solver/31-solver-architecture.md)'s seam** — `EquationSystem`, `ISolver`, `SolveResult` — which this table used to leave to P3.7 alongside the outer loop. Newton has nothing to solve without them; P3.7 keeps the loop, sizing and solver selection. Ran as two: **a** the assembled system and its scaling, **b** Newton. |
| P3.7 | Sizing and the single outer loop ([`24`](../20-core-domain/24-auto-sizing.md), [`31`](../30-solver/31-solver-architecture.md)) | One loop, one convergence test, one cap. Building sizing before the solve exists produces a second loop by default, which is exactly what `31` forbids. |
| P3.8 | **The design point as the sizing point** ([`24`](../20-core-domain/24-auto-sizing.md), [`15`](../10-language/15-semantic-model.md), `D-57`–`D-60`) | After sizing, because `design` *is* the sizing point (`D-58`) and a curve with nothing to size against demonstrates nothing. The language half shipped in P2.10 and is not repeated here; this is sizing reading `ProjectSettings.Design`, and a transient run re-reading a curve it was handed as deferred. |
| P3.9 | **Elevation as an absolute height** ([`02`](02-glossary.md), [`15`](../10-language/15-semantic-model.md), [`22`](../20-core-domain/22-component-model.md), `D-70`) | After `P3.6`, because the energy half of a height is one of `D-69`'s fluxes and there is nothing to hang it on before that. Its own package rather than a line in `P3.6`: it adds a parameter to every kind, a glossary decision (`elevation` already means a signed rise on a pipe and a normalized layer fraction on a tank), height propagation in lowering, a diagnostic for the inferred node between two heights, and a `/docs` row per kind. **`C-41`'s one-line half was taken early** — an omitted height stopped being a sizing candidate before `P3.7` could act on it. |

**P3.4c was not planned, and the shape of why is worth keeping.** It began as a change to what a
boundary declaration means (`D-64`) and turned into two corrections to the counting argument itself —
a stated `flow` is not an equation (`C-26`), and a closed circuit has an enthalpy datum nothing had
counted (`C-30`, `D-65`) — plus two reference circuits that had no solution (`F-15`, `F-16`). None of
those were reachable from `23`'s worked example, which balances at 20 = 20 on an open circuit. The
package that finds a defect is very often the one that tries to *use* the thing rather than the one
that builds it, and the trigger here was a user asking for a sample script to be corrected.

**P3.0 and P3.8 are new work from `D-57`–`D-61`, and their positions are the whole content of the
decision to split them.** Sensors are a component-model change and must precede P3.3, which builds the
six kinds; curves have a meaning that depends on a design point, and a design point means nothing
before there is something to size. Putting them at opposite ends of P3 is not convenience — building
either at the other's position costs a rewrite. **Both rows narrowed when P2.10 took the language half
of each**, which is what the two packages had in common and the one part neither needed P3 for.

### P4 — M2b: coupled thermal rating

| # | Package | Why here |
|---|---|---|
| P4.1 | Rated two-sided exchanger, ε-NTU **and** LMTD as separate routes | Two formulations sharing no code is what makes UA = 12.07 kW/K a validation rather than a regression. Written as one route with a switch, the agreement proves nothing. |
| P4.2 | Two coupled hydraulic graphs, two pressure datums | The substation is the only fixture that has them; `D-17` closed the isolated-subgraph ambiguity this package implements. |
| P4.3 | `D-36` circuit ownership from the enthalpy-losing side | Computed from the heat-transfer edge with no layout input. Its test swaps the source blocks and asserts the tag does not move. |

P4's regression gate is that the one-sided exchanger's answers on the cooling loop and the simple
loop do not move. A rated model that changes a duty-mode answer has changed something it was not
allowed to touch.

### P5 — M3: the usable static product

The largest phase and the one where scope creeps, because every package is visible.

| # | Package |
|---|---|
| P5.1 | Model contract and layout hints ([`26`](../20-core-domain/26-model-contract.md), [`25`](../20-core-domain/25-layout-hints.md)) — Core-side, closed by golden files before a pixel exists |
| P5.2 | REST and diagnostics contracts, host, sessions, cancellation ([`42`](../40-api/42-rest-contract.md), [`44`](../40-api/44-diagnostics-contract.md), [`41`](../40-api/41-api-architecture.md)) |
| P5.3 | Design tokens and themes ([`55`](../50-frontend/55-design-system.md)) |
| P5.4 | App shell, the four state domains, the debounce pipeline ([`51`](../50-frontend/51-frontend-architecture.md)) |
| P5.5 | Editor: syntax palette, completion, inline diagnostics, and the Core-side **formatter** ([`52`](../50-frontend/52-editor.md), [`17`](../10-language/17-formatting-and-round-trip.md)) |
| P5.6 | Canvas viewport and Core-owned symbols ([`53`](../50-frontend/53-canvas-renderer.md), `D-24`) |
| P5.7 | **The layout engine** ([`53`](../50-frontend/53-canvas-renderer.md)) |
| P5.8 | Hover, selection, console log, status line ([`54`](../50-frontend/54-interaction-and-writeback.md), [`56`](../50-frontend/56-console-log.md)) |
| P5.9 | File lifecycle and document tabs ([`58`](../50-frontend/58-file-lifecycle.md), `D-39`) |
| P5.10 | State visualization and colour scales ([`57`](../50-frontend/57-state-visualization.md)) |
| P5.11 | SVG/PNG export, accessibility pass, M3 baselines ([`59`](../50-frontend/59-static-export.md), `D-45`) |

**P5.3 precedes every component that has a colour**, so the "no literal colour outside the theme
files" assertion never has to be enforced retroactively across a built UI.

**P5.5 carries the formatter, which no package claimed until it was looked for.** `17` owns it and
`52` binds it to `Shift+Alt+F`, but `08` scheduled only the printer (P2.5), so the one operation of
the pair that is allowed to move text had no home. It belongs with the command that invokes it rather
than with the printer it must not become: written beside the printer it would share a code path, and
`17`'s whole thesis is that these two operations stay separate.

**P5.7 is the phase's schedule risk**, and it is placed after the viewport so it has something to
render into but before hover and log so those are built against real placements. It carries two
layout modes (`D-38`), the corner rule (`D-44`), mandatory collapse, non-overlap at 200 components,
and deterministic ordering. It is built **headless against golden layout-hint fixtures** and
unit-tested on placements before it is attached to a canvas;
[`62-testing-strategy`](../60-docs-and-devex/62-testing-strategy.md) is explicit that a screenshot
alone cannot test it.

### P6 — M4: make it move

| # | Package |
|---|---|
| P6.1 | Transport delay and time integration on a fixed graph ([`33`](../30-solver/33-transient-time-domain.md)) |
| P6.2 | Stratified tank ([`33`](../30-solver/33-transient-time-domain.md), `D-32`) — V15, V16, V17 |
| P6.3 | Controllers, actuator limits, anti-windup ([`34`](../30-solver/34-controllers.md)) |
| P6.4 | `RunSnapshot` and run isolation (`D-22`, [`07`](07-quality-attributes.md)) |
| P6.5 | Backend worker and the WebSocket contract ([`43`](../40-api/43-realtime-contract.md)) |
| P6.6 | Frontend Web Worker, frame reconstruction, playback ([`51`](../50-frontend/51-frontend-architecture.md)) |
| P6.7 | Detached runs across tabs (`D-39`, `D-42`) — V23 |

Sizing is frozen for the duration of a run: it is a design-point property, and re-running it per
frame would make the model's geometry a function of time.

**V8 is P6's payoff** — a transient run to steady state agreeing with Newton, from two solvers
sharing no numerical code. It is the strongest evidence in the suite and the test most likely to be
skipped as slow.

### P7 — M5: close the loop

| # | Package |
|---|---|
| P7.1 | The mutation API ([`17`](../10-language/17-formatting-and-round-trip.md)) |
| P7.2 | On-canvas property editing and the write-back round trip ([`54`](../50-frontend/54-interaction-and-writeback.md)) |

Two packages, because P2.5 did the hard part. If the printer tidies while printing, this is the
phase where the project stalls, and the fix is upstream in the AST rather than here.

## Every package writes down what it found

**A package that implements against a tier's documents records what it found in that tier's
`defects.md`, in the same commit.** One file per tier folder, named `defects.md` everywhere — a second
permitted name means half the findings land in the file nobody greps.

The rule is one line long and the reason is not: implementing a specification is the only review that
executes it, and it has found something in every package so far. Those findings are worth more later
than they are on the day. A session six weeks from now needs to know that `FS1107` is absent
deliberately rather than forgotten, that a threshold was chosen rather than derived, and that a
document's worked example is wrong — none of which is visible from the code, the git history, or the
document itself, because a document that has been corrected no longer records that it was ever wrong.

Which folder: the tier whose documents the finding is *about*, not the tier the code landed in. P2.6
implemented a registry into `Core/Language` and found two defects in
[`22-component-model`](../20-core-domain/22-component-model.md); those belong to
`20-core-domain/defects.md`.

**"Implements against" includes writing against a tier's contracts without implementing any of
them**, and this is the reading that gets missed. P3.0 through P3.4a built components whose interface
[`22`](../20-core-domain/22-component-model.md) says waits on `31` for the shape of `SolveContext`,
whose residuals carry `36`'s smoothing constants, and whose graph exists for `32` to assemble — and
created no `30-solver/defects.md` at all, for four packages running. Every finding about that tier
sat in `20-core-domain/defects.md`, where the tier that owns it would not have looked.

**Create a tier's file the first time the tier is read at implementation depth, not the first time it
has a defect.** A tier folder with no `defects.md` reads as a tier nothing has touched, which is the
opposite of the truth in exactly the case that matters. An empty file whose header says which of its
documents have been read and which have not carries real information; an absent one carries a wrong
signal for free.

Each entry states what was found, whether it is closed or still open, and — for a closed one — what
was changed. An entry that was fixed by amending the document says so, because the document now reads
as though it was always right. Entries that are neither defects nor fixes go under **Observations**:
a constant chosen with no specification behind it, a deliberate omission, a trap the next
implementation will otherwise re-derive.

## Invariants

1. A work package is one branch, one pull request, one squash merge, and `main` is green after it.
2. A package that adds a user-visible feature adds its `/docs` page in the same pull request. There
   is no package whose job is documentation.
3. `dotnet test --filter-trait Category=Unit` is under two seconds at every package boundary, not only at
   milestone boundaries.
4. No package in P2 references a type in `FluidScript.Core.Fluids`, `.Components` or `.Solvers`.
5. A package that changes a settled decision adds a `D-` entry in the same pull request.
6. No package is started while an earlier package in the same phase is unmerged, except P1.1 and
   P1.2, which are independent by construction.
7. A package that implements against a tier's documents updates that tier's `defects.md` in the same
   commit, or states in the commit that it found nothing. A package that writes *against* a tier's
   contracts without implementing them is covered by this, and creating the file is part of it.

## Error cases

| Situation | What happens | Why it matters |
|---|---|---|
| P1.1 finds SharpProp's surface differs from `21` | Implementation stops; tier 20 contracts are revised and re-reviewed before P2 starts | This is the gate's whole purpose. Proceeding writes six components against an adapter that cannot exist. |
| P0.3 cannot reproduce a published figure | The figure is corrected in its owning document, with a `D-` entry if a decision depended on it | An uncorrected figure becomes a failing acceptance test that looks like a solver bug |
| A package's exit verification fails | The package is not merged; the phase does not advance | Invariant 6 means a failing package blocks its successor rather than being carried |
| The unit tier exceeds two seconds | Fixed in the package that broke it | It degrades gradually and is painful to recover once several packages have contributed |
| A milestone criterion cannot be met by its phase's packages | The phase gains a package; `05`'s criterion is not weakened | A criterion relaxed to fit the code stops being a criterion |

## Worked example

Where the M1 packages land for one line of the syntax reference — `3WV three_way_valve`:

| Package | What it contributes | Observable after it |
|---|---|---|
| P2.1 | `Diagnostic`, severity, span, the registry | Nothing for this line yet; the type every later package returns, and the generated diagnostic page it is checked against |
| P2.2 | `Quantity`, dimensions | Nothing for this line — it has no values |
| P2.3 | `ident(3WV)`, `ident(three_way_valve)`, trailing trivia | Tokens concatenate back to the source line |
| P2.4 | `ComponentDeclarationSyntax`, zero parameters | The AST node, with `3WV` as an identifier despite the leading digit |
| P2.5 | Byte-identical print of the line, comments intact | `Print(Parse(x)) == x` for the whole sample |
| P2.6 | `three_way_valve` resolves to a registered kind | An unknown kind would be `FS1502` with an `Unknown` kind, not a crash |
| P2.7 | `ComponentSymbol` with **zero** parameters present | `R-02`: absence is representable and distinct from a default |
| P2.8 | Three ports materialized, connections bound, tag `101TV01` assigned last | The M1 criterion "binds with zero parameters and no diagnostic" |
| P2.9 | Nothing for this line — the gate reads the `fluidscript 1` above it | Its disposition is `Current`, so every action including `Save` is allowed |

Eight packages for one line, and each is observable on its own. That is what makes a package
reviewable: the criterion it closes is checkable without the next one existing.

## Acceptance criteria

- [ ] Every phase's package list covers every exit criterion of its milestone in `05`.
- [ ] Every package states its verification, answerable yes/no without interpretation.
- [ ] Every placement that differs from the reading order of `05`'s prose states why — currently
      P2.5, P3.6 and P5.3.
- [ ] No package appears in two phases.
- [ ] P8 remains undecomposed until M6 entry is justified.

## Open questions

None. P0 closes the two that blocked M0 (`D-45`, `D-46`); everything else this document sequences is
already specified by the tier document that owns it.
