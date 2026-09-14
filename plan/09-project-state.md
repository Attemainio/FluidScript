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

> **M2a exited 2026-09-14; P4 — M2b, coupled thermal rating — is next and not started.**
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
| `m2-substation.fluid` | `NonFinite` | M2b's fixture, not M2a's. `S-32` |
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
| P4 | M2b | 3 | Not started | — |
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
| 10 · Language | 7 | [`10-language/defects.md`](10-language/defects.md) |
| 20 · Core domain | 20 | [`20-core-domain/defects.md`](20-core-domain/defects.md) |
| 30 · Solver | 17 | [`30-solver/defects.md`](30-solver/defects.md) |
| 60 · Docs and dev-ex | 2 | [`60-docs-and-devex/defects.md`](60-docs-and-devex/defects.md) |
| | **47** | |

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
4. **P4.1 — the rated two-sided exchanger**, ε-NTU and LMTD as two routes sharing no code, against
   the substation's UA = 12.07 kW/K. Read `21`/`22`'s rated-mode contract, `S-32`, `S-14b` and
   `C-73` first, and look the ε-NTU relations up rather than deriving them. Two small follow-ons
   `C-75`'s closure names, neither blocking: a promoted Kv seeds at the bootstrap's 630 rather than
   from the Kv law, and a solved Kv has no basis line saying what it absorbed. `F-19`'s budget
   re-derivation now has real numbers to work from (16 µs per water state, not 63).

## Standing baselines

Numbers a session can check in one command, so that "did I break something" has an answer that is not
a judgement.

| Baseline | Value | Where |
|---|---|---|
| Core test suite | **1542 passed, 0 failed, 4 skipped** (229 MB working set for the whole run; was 8.9 GB before `C-76`'s test-side half), ~60 s with the `Diagnostic` classes, ~15 s without | `FluidScript.Core.Tests` |
| API test suite | **2 passed, 0 failed** | `FluidScript.Api.Tests` |
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
