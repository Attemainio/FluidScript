---
id: 05-milestones-and-acceptance
title: Milestones and acceptance
tier: 00-foundation
status: draft
owns: [milestones M1-M6, per-milestone exit criteria, demo scripts, dependency order]
depends_on: [01-vision-and-scope, 03-repository-layout, 07-quality-attributes]
traces_to: [R-01, R-02, R-03, R-04, R-05, R-06, R-07, R-08, R-09, R-10, R-11, R-12, R-13, R-14, R-15, R-16, R-17, R-18, R-19, R-20, R-21, R-22, R-23, R-24, R-25, R-26, R-27, R-28, R-29, R-30, R-31, R-32, R-33, R-34, R-35, R-36, R-37, R-38, R-39, R-40, R-41, R-42, R-43, R-44, R-45]
open_questions: 0
last_review_pass: 0
---

# Milestones and acceptance

## Purpose

Turns the phase boundaries in [`01-vision-and-scope`](01-vision-and-scope.md) into checkable exit
criteria. A milestone is done when its criteria pass, not when its code is written. Each milestone
also names a **demo script** — a real `.fluid` file in `samples/` that must work end to end — because
a criterion that cannot be demonstrated on a concrete script is a criterion nobody can fail.

## Responsibilities

**Owns.** The milestone list, what each contains, its exit criteria, its demo script, and the ordering
constraints between them.

**Explicitly does not own.** How anything is built (tiers 10–50), the test framework and tiers
([`62-testing-strategy`](../60-docs-and-devex/62-testing-strategy.md)), release/CI mechanics
([`63-ci-and-repo-hygiene`](../60-docs-and-devex/63-ci-and-repo-hygiene.md)).

## Ordering constraints

```
M0 scaffold + risk gates
   └─► M1 language spine ──► M2a hydraulic ──► M2b coupled thermal ──► M3 static product
                                                                           └─► M4 transient ──► M5 write-back
                                                        M2a/M5 ───────────────► M6 extensions
```

Two constraints are hard and worth stating as reasons rather than arrows:

- **M2a cannot start before M1 exits.** The component model is defined in terms of parameters the
  binder produces. Building physics against a moving AST means rewriting it.
- **M6's evolutionary sizing cannot start before M2a exits**, because it optimizes over a solve. An
  optimizer wrapped around an untrustworthy solve produces confident nonsense.

M3 and M4 are separable in principle but sequenced deliberately (`D-29`): seeing a static diagram is
what makes the transient work reviewable.

## M0 — Scaffold and risk gates

Everything in [`03-repository-layout`](03-repository-layout.md)'s migration section.

**Demo script:** none. M0 is the explicit exception because it establishes executable scaffolding and
risk fixtures before the language can parse a `.fluid` script.

**Exit criteria**

- [ ] `dotnet build` from the root: zero warnings.
- [ ] `dotnet test` from the root: runs, zero failures (a single placeholder test is acceptable).
- [ ] `npm run dev` in `frontend/` serves a page.
- [ ] README exists and states what FluidScript is in one paragraph.
- [ ] The pinned SharpProp spike passes the water and humid-air rows in `07`/`21`, records its real API,
      valid ranges, basis, exceptions, construction cost, and concurrency behavior. If it disagrees
      with the planned adapter, implementation stops and the contracts are revised first.
- [ ] Benchmark fixtures record the reference environment and baselines for 200 declarations,
      200 solver unknowns, 800-unknown refusal/support behavior, and a 200-component render.
- [ ] Model and realtime JSON Schemas generate both C# and TypeScript DTO test fixtures.
- [ ] Accessibility automation plus keyboard and screen-reader test harnesses run in CI.

## M1 — Language spine

Lexer, parser, AST, binder, unit system, expressions, diagnostics, printer. **No physics whatsoever** —
a component is a name, a kind, and a bag of typed parameters at this stage.

**Demo script:** `samples/m1-syntax-tour.fluid` — exercises every grammar production, including the
brief's original example verbatim.

**Exit criteria**

- [ ] The current example, beginning `fluidscript 1`, parses with zero unexpected diagnostics; an
      unversioned unsaved draft gets `FS1701` and cannot be durably saved until fixed.
- [ ] `3WV` (leading digit) binds as an identifier; `style blue 2px fillet --` parses as positional
      style arguments.
- [ ] `let dT = 30 dK` followed by `out=20C+dT` evaluates to 50 °C, stored as 323.15 K.
- [ ] `power=30` and `power=30 kW` and `power=30000 W` produce the same internal quantity.
- [ ] A cyclic reference (`A.x = B.y`, `B.y = A.x`) produces one diagnostic naming both, not a stack
      overflow.
- [ ] Deleting a random character from the demo script still yields a bound model for every unaffected
      statement, plus exactly one error (parser recovery, `R-05`).
- [ ] Printing a parsed script reproduces the input **byte for byte**, comments and blank lines
      included. Then: print(parse(print(parse(x)))) == print(parse(x)) for every sample.
- [ ] Every diagnostic code emitted appears in [`16-diagnostics`](../10-language/16-diagnostics.md)'s
      table.

The byte-for-byte round trip is the criterion that makes M5 possible. It is much cheaper to get right
now than to retrofit once the printer has been written loosely.

## M2a — Hydraulic core

Liquid properties for the hydronic v1 domain (`D-28`), flow components in duty mode, topology construction, catalogue-backed sizing,
explicit minor losses, and the Newton hydraulic solve.

**Demo scripts:** `samples/m2-cooling-loop.fluid` (the mixing circuit — topology) and
`samples/m2-simple-loop.fluid` (one series loop — sizing and solver arithmetic), both
defined in [`01-vision-and-scope`](01-vision-and-scope.md).

**Exit criteria**

- [ ] Water properties at 20 °C / 1 bar meet `07`'s property-validity row.
- [ ] Humid air at 25 °C / 50 % RH returns humidity ratio and enthalpy within `07`'s relative
      tolerances and dew point within its absolute 0.1 K tolerance.
- [ ] The demo circuit solves: every node has a temperature, a pressure, and a mass flow.
- [ ] Loop closure, mass balance, and duty-mode energy balance meet `07`/`62`'s explicit tolerances.
- [ ] `PU1 pump` with no parameters is sized, and its head equals the loop's total pressure drop
      within tolerance — 5.28 m on the simple loop.
- [ ] The cooling loop's recirculation branch carries non-zero flow (0.0764 kg/s), and its mixing node
      sits at 20 °C between a 6 °C primary and a 50 °C return.
- [ ] Adding `head=15` to the pump makes the solver honour it and either satisfy or report the
      resulting mismatch — an explicit value constrains rather than seeds (`R-02`).
- [ ] The cooling loop's three implicit nodes appear where the script declared none — `PU1__HE1`,
      `HE1__3WV` and `3WV__P1`, one per directly-connected pair of non-node components (`R-06`). Rule
      I2 inserts exactly one node per such pair, so "two between `HE1` and `3WV`" would be wrong.
- [ ] A pipe declared with 4 internal nodes shows a monotonic temperature profile along its length.
- [ ] `minor_loss` contributes the stated K loss; omitted local loss is exactly zero and the basis says
      so. A pump with known flow but no explicit resistance sizes to zero head with `FS2312`.
- [ ] Test coverage of `src/FluidScript.Core/Components` and `/Solvers` gives every governing equation
      independent hand-checked and regression tests (`R-17`).

## M2b — Coupled thermal rating

The two-sided rated heat exchanger (`R-35`, `D-17`) and two thermally coupled hydraulic graphs.

**Demo:** `samples/m2-substation.fluid`, defined in `01`.

**Exit criteria**

- [ ] **The substation sizes to 39 plates and a 4.90 K approach**, and its ε-NTU and LMTD routes agree
      on UA = 12.07 kW/K to within rounding — two formulations sharing no code, which is what makes it
      a validation rather than a regression test.
- [ ] The substation solves as **two hydraulic circuits** with two pressure datums, one stated and one
      auto-picked, and produces no `FS2213`.
- [ ] A one-sided `heat_exchanger` behaves exactly as it did before `D-17` on both other demo scripts —
      the rated model must not change a duty-mode answer.
- [ ] A duty above what the inlet temperatures allow reports `FS2111` naming the thermodynamic maximum,
      rather than sizing an enormous exchanger.
- [ ] `FS4008` fires on a design below the minimum approach. It was allocated in M1 and dead until now;
      an allocated-but-unreachable code is a specification that never got finished.
- [ ] An under-determined circuit is reported as such, not solved to garbage; a closed loop with no
      stated pressure is *not* one of those — it gets an auto-picked datum and solves (`FS2201`).

The "hand-checked numbers" phrasing is deliberate: a test asserting the solver's own output is a
regression test, not a validation test, and both are needed.

## M3 — Usable static product

REST API, versioned local files, canvas/editor, Core-owned symbols delivered here by `D-24`, layout, hover/log/themes,
accessibility, and SVG/PNG export.

**Demo:** open the app, paste the M2 script, see the diagram.

**Exit criteria**

- [ ] Typing in the editor updates the diagram after ~300 ms idle, and *not* on every keystroke (`R-21`).
- [ ] A syntax error draws a squiggle at the right span and does not blank the diagram — the last good
      render persists.
- [ ] The canvas zooms and pans; the origin shows a red X axis and a green Y axis (`R-22`).
- [ ] Hovering any component shows its temperature, pressure, and flow (`R-23`).
- [ ] A warning renders on the component *and* appears in the log with human phrasing (`R-24`).
- [ ] Light and dark themes both render legibly, switchable at runtime (`R-26`).
- [ ] The demo circuit's layout is readable without manual adjustment — no crossing pipes on a simple
      loop and no overlapping symbols. The supported 200-component fixture is also overlap-free after
      mandatory initial collapse; an explicitly over-limit fixture may overlap only with `FS5001` and
      `degraded: true`, as specified by [`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md).
- [ ] Thermal stages are monotone left to right: cooling/source groups left, coupling or conversion
      centrally, and heating consumers right. Parallel sources/consumers share a stage, while return
      branches retain their real right-to-left fluid arrows (`R-44`, `D-31`).
- [ ] Every component kind delivered through M3 has a distinct symbol; later component milestones add
      their symbol under the same Core-owned contract before they exit.
- [ ] New/Open/Save/Save As, upload/download fallback, dirty/conflict/error state, and crash recovery
      pass `58` without ever calling recovery data “saved”.
- [ ] Every component's `SymbolId` resolves to Core metadata used by both the canvas and exported SVG.
- [ ] SVG and PNG exports pass [`59-static-export`](../50-frontend/59-static-export.md)'s
      standalone, metadata, and gradient checks.
- [ ] Keyboard-only and screen-reader users can edit, inspect, run static solve, read diagnostics and
      state, save, and export; WCAG 2.2 AA and `07` budgets pass.

## M4 — Make it move

Transient solver, transport delay, `D-32` stratified tank, controllers, WebSocket streaming, playback UI.

**Demo scripts:** `samples/m4-demand-step.fluid` — a cooling loop whose load steps from 30 kW to 45 kW
at t = 60 s, with a PI controller on the three-way valve; and `samples/m4-storage-header.fluid` — two
source boundaries feeding a five-layer 300 dm³ tank and two consumer boundaries in parallel.

**Exit criteria**

- [ ] `fluid dynamic water` selects the transient model; `fluid water` stays steady-state.
- [ ] The step disturbance produces a visible transient that settles to the new equilibrium, and that
      equilibrium equals what a steady-state solve of the post-step system returns, within tolerance.
- [ ] Transport delay is observable: a temperature change at the heat exchanger reaches a node 20 m
      downstream later than one 5 m downstream, by roughly length ÷ velocity.
- [ ] `T1 tank` defaults visibly to 300 dm³ and five layers; `container` and `v` bind as aliases but
      canonical metadata and completion emit `tank` and `volume`.
- [ ] The storage header materializes exactly `in1`, `in2`, `out1`, and `out2`; its 30% ports map to
      layer 2 and its 90% ports to layer 5, bottom to top.
- [ ] The canvas and SVG export use the Core-owned tank symbol, showing five distinct layer bands and
      four anchors at their mapped elevations; M4 does not add a frontend-only glyph.
- [ ] With the constant-property fixture, the storage header begins with `dT2/dt = 0.020 K/s`; every
      accepted step conserves tank mass and enthalpy, and density-inversion remixing conserves both.
- [ ] `layers=1` matches a fully mixed 300 dm³ control volume; increasing `layers` converges on the
      independently tabulated plug-displacement reference without changing total capacity.
- [ ] The PI controller settles without sustained oscillation on the demo case, and anti-windup is
      demonstrated by a test that saturates the actuator.
- [ ] The canvas and SVG export use the Core-owned controller symbol and expose its measurement and
      actuator relationships without a frontend-only glyph.
- [ ] Frames stream over the WebSocket and playback starts before the run finishes (`R-19`).
- [ ] Stop/disconnect/invariant/worker/protocol failure stops and joins the run within `07`'s bound,
      retaining only the last verified frame.
- [ ] Under `D-22`, editing, saving, or temporarily breaking the draft does **not** cancel, mutate, block, or replace
      the active immutable run snapshot. The UI labels its source revision and offers explicit restart.
- [ ] Backend integration uses the dedicated worker; browser decode/delta/scale/render preparation uses
      a Web Worker; only a bounded coalesced SVG DOM commit uses the UI thread.

## M5 — Close the loop

Canvas write-back: editing a property on the diagram edits the script.

**Demo script:** `samples/m5-writeback.fluid` — the M2 cooling loop with an editable `3WV.kv`, a
symbol-valued valve characteristic, and comments surrounding the declaration to prove byte preservation.

**Exit criteria**

- [ ] Changing a valve's `Kv` on the canvas inserts or updates `kv=` on that component's line.
- [ ] The rest of the script is byte-identical afterwards — comments, spacing, and unrelated lines
      untouched (`R-25`).
- [ ] The edit is undoable in the editor as a single unit.
- [ ] An edit to an auto-sized value converts it into an explicit constraint, and the diagram
      re-solves showing the consequence.
- [ ] No edit path can produce a script that does not parse.

The byte-identical criterion is the whole difficulty. It is why M1 demands a lossless printer.

## M6 — Evidence-driven extensions

Evolutionary sizing, DXF/versioned-model interchange, persistent sensors, air-side work, and subsystem
composition are separately justified before entry. Exit criteria are defined when an item is planned;
listing them now would be inventing requirements ahead of the information needed to state them.

## Documentation gate — applies to every milestone

A milestone does **not** exit until every user-visible feature it added has its `/docs` page (`R-28`).
This is not a separate documentation milestone, because a separate documentation milestone never
happens. The gate is per-milestone, and
[`61-documentation-plan`](../60-docs-and-devex/61-documentation-plan.md) owns the page template.

## Worked example

Tracing one requirement through the milestones — `R-02`, "every parameter optional":

| Milestone | What `R-02` means here | Criterion |
|---|---|---|
| M1 | A declaration with no parameters binds successfully; absence is representable, distinct from a default | `3WV three_way_valve` binds with zero parameters and no diagnostic |
| M2 | Absence triggers sizing; presence constrains it | Pump with no `head` is sized; with `head=15` the value is honoured |
| M3 | The canvas shows which values were sized versus stated | Sized values render visually distinct from explicit ones |
| M5 | Editing a sized value on canvas promotes it to explicit | The edit writes `kv=` into the script |

A requirement that cannot be traced this way through the milestones it touches is either
under-specified or is not really a requirement.

### Requirement-to-milestone coverage

| Milestone | Requirement ids delivered or gated |
|---|---|
| M0 | R-07, R-08, R-17, R-30, R-40, R-43 |
| M1 | R-01–R-06, R-20, R-29, R-33, R-39 |
| M2a | R-02, R-06, R-07, R-09–R-11, R-16–R-17, R-32, R-43 |
| M2b | R-08, R-35, R-43 |
| M3 | R-18, R-20–R-24, R-26–R-31, R-33–R-34, R-37–R-40, R-42, R-44 |
| M4 | R-09, R-12–R-14, R-19, R-23–R-24, R-27, R-41, R-43, R-45 |
| M5 | R-25 |
| M6 deferred | R-15, R-36; later DXF/model-interchange part of R-31 |

`R-28`'s documentation gate applies to every row, not just M3.

## Invariants

1. Every milestone after the explicitly exempt M0 has a demo script in `samples/` that its criteria
   are checked against.
2. Every exit criterion is answerable yes/no without interpretation.
3. A milestone's criteria reference only work inside that milestone or an earlier one.
4. The documentation gate applies to every milestone, not to a separate one.

## Acceptance criteria

- [ ] Every milestone after M0 has at least one demo script in `samples/`; M0 retains its explicit
      no-script exemption.
- [ ] Every exit criterion is answerable yes/no without interpretation.
- [ ] Every `R-` id in [`01-vision-and-scope`](01-vision-and-scope.md) appears in at least one
      milestone's criteria, or is explicitly deferred to M6.

## Open questions

None. `07-quality-attributes`, `36-numerics-and-convergence`, and `62-testing-strategy` own the exact
validity, convergence, and assertion tolerances referenced by these gates.
