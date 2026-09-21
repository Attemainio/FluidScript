---
id: 70-core-refactoring
title: Core refactoring plan
tier: plan
status: draft
owns: [which sections of FluidScript.Core are rewritten wholesale and in what order, the parameter-ownership model, the solved-view seam between solver and reporting, the rollback discipline of the layout ring forms, what a refactoring package may and may not change]
depends_on: [08-implementation-sequence, 06-decision-log, 15-semantic-model, 22-component-model, 23-topology-and-graph, 24-auto-sizing, 26-model-contract, 28-layout-solver, 29-layout-ladder, 32-steady-state-newton, 36-numerics-and-convergence, 62-testing-strategy]
traces_to: [R-11, R-17]
open_questions: 1
last_review_pass: 0
---

# Core refactoring plan

## Purpose

**Status (2026-09-21).** R0–R5 shipped in nine commits (`d8c43d5` … `23acf17`); `D-130` states the
actuator order and `D-131` names the seams as binding. R6 is deferred until a feature opens its files.
The rows in [`09`](09-project-state.md) carry what each package measured and what it left undone.

`FluidScript.Core` shipped as a preliminary version and has since closed roughly two hundred register
rows, one at a time, each inside the structure it found. The fixes are individually right and each is
cited at its site. What they left behind is structure that a design made with today's knowledge would
not have: one question answered eleven ways, one pipeline written three times, one number derived by
three formulas. This document says **where that structure is, why it is where it is, what the clean
shape of each part is, and in what order the parts are rewritten** so that behaviour stays fixed and
every golden that moves does so once and on purpose.

It is an architecture document written from a review, not a work list written from a wish. The review
is the five-scope `dotnet-code-review` pass of 2026-09-21 (report under
`.claude/dotnet-toolkit/review/`, git-ignored; its findings are restated here, with file and line, so
nothing depends on that file surviving). Three of its claims the reviewers could not verify without
git were verified by the session and are marked so below.

The audience is the engineer deciding whether a rewrite is worth its risk. The numbers are there so
the argument can be checked, not to decorate it.

## Responsibilities

**Owns.** The list of rewrite targets and the order they are taken in; the shape each is rewritten to;
the parameter-ownership model (`ParameterState`) that the sizing, well-posedness and reporting code
will share; the solved-view seam (`SolvedStates`) between the solver and the two renderers; the
rollback discipline of the layout ring forms; the rules a refactoring package must obey.

**Explicitly does not own.** The behaviour being preserved: the promotion table is
[`23`](20-core-domain/23-topology-and-graph.md)'s, the sizing rules are
[`24`](20-core-domain/24-auto-sizing.md)'s, the ladder is [`29`](20-core-domain/29-layout-ladder.md)'s,
the counting scheme is [`36`](30-solver/36-numerics-and-convergence.md)'s. Any change to what a
circuit solves to, prints or draws is a defect row or a `D-`, never a refactoring step. The order of
this plan relative to feature work is [`08`](08-implementation-sequence.md)'s; this document proposes
a position and `08` decides it.

## The diagnosis

Three weeks of churn did not land evenly. Per file, since 2026-09-05:

| File | Commits | Closed rows naming it | Lines |
|---|---|---|---|
| `Solvers/OuterLoop.cs` | 29 | 27 | 1,419 |
| `Topology/WellPosedness.cs` | 22 | 18 | 1,863 |
| `Solvers/SolutionSeed.cs` | 17 | 12 | 1,761 |
| `Layout/LayoutEngine.cs` | 17 | (ladder steps) | 3,693 |
| `Diagnostics/SolveExplanation.cs` | 15 | | 1,206 |
| `Model/ModelContractBuilder.cs` | 14 | | 1,022 |
| `Topology/ComponentFactory.cs` | 13 | | |
| `Solvers/EquationSystem.cs` | | 10 | 1,478 |
| `Sizing/ValveSizer.cs` | 7 | 8 | |

`LayoutEngine` churns because the ladder is still growing, not because it is being repaired; each
step is a new rule. The other rows are repair. And the repairs cluster on **one question that the code
never gave a home: who owns a parameter right now.** Is `PU1.head` stated, decided by a default,
sized, sized-but-provisional, promoted by a constraint, or free? `C-75`, `C-91`, `C-109`, `C-111`,
`S-45`, `S-48`, `S-56`, `S-69`, `D-96` are all "the wrong thing was free" or "two things claimed one
thing". The question is asked, with its own dictionary reads and its own rebuilt `"name.parameter"`
string key, at:

| Site | Asks |
|---|---|
| `WellPosedness.IsFree` `:1052-1056` | stated / defaulted / sized-minus-provisional |
| `OuterLoop.Claimed` `:1212-1215` | stated / defaulted / promoted |
| `ComponentFactory.Sized` `:414-438` | stated-and-default absence, filtered by registry |
| `ComponentFactory.Stated` `:477-490`, `.Defaults` `:502-529`, `.Value` `:539-558`, `.DefaultOf` `:568-587` | stated → sized → default, per parameter |
| `SizingOverlay.IsProvisional` `:71-72` | is this placeholder still a placeholder |
| each sizer's `CanSize` + `Provisional` (`PipeSizer.cs:51-56`, `ValveSizer.cs:61-65`, Pump, Thermal, Exchanger) | what goes in before anything is known |
| `SolvedStates.Parameter/Parameters` `:252,276` | promoted or own |
| `SolveExplanation.OperatingPoints`, local `Parameter` `:746-747` | promoted or own, again |
| `ModelContractBuilder.Parameters` `:254-325` | stated > solved > sized > default, four loops into one map |
| `HydraulicPartition.Stated` (29 callers) | stated only; the one shared primitive, and every row above bypasses it |

`WellPosedness.Candidates` (`:785-1021`, 211 lines, 30 branches) is the visible scar. It is the
ordered walk that answers "which free parameter absorbs this constraint": a mixed inlet takes the
split at either end of the coil's own branch, then any reaching split (`S-56`); a node temperature
the same (`S-45`, `S-48`); a fixed flow takes the owner's own `power` if the constraint is not itself
a flow, then a pump on the owner's branch, then any pump in the hydraulic, then the first unstated
`kv` along the owner's branch. Two of those steps build the same "elements on a branch through the
owner" set by walking `graph.Branches` (`:832-844`, `:880-901`). Component kinds are matched by string
literal at `:847`, `:905`, `:915`, `:1013`, while `ValveSizer.CanSize` matches the same kinds by
type. Every branch is correct and every branch cites the row that placed it. That is what "patched,
not designed" looks like: the order is right because five defects made it right, and
[`23`](20-core-domain/23-topology-and-graph.md)'s promotion table states the *pairings* but nowhere
states the *ordering principle* those defects converged on.

Everything else the review found is size debt, not a wound. `BindingRun` is a 175-member type over
six partial files, but the language register is short and its tests do not reach its fields.
`EquationSystem.Build` feeds sixteen parallel arrays into an eighteen-parameter constructor, and has
been stable since `S-2x`. Those are worth doing when a feature next needs to touch them, not before.

Two findings are live today and are not size debt:

- **A pump's head is derived three ways.** The residual (`Components/Pump.cs:247-250`) solves with
  the mean of inlet and outlet density. The report (`SolveExplanation.cs:660-671`) uses inlet density
  and special-cases a stated `dp` (`C-109`). The wire contract (`ModelContractBuilder.cs:452-457`)
  uses inlet density from the solved pressures and never special-cases a stated `dp`. By definition
  head is `Δp / (ρ g)`; the three agree only when the pump runs at speed 1 and inlet density equals
  the mean, which for water across a pump is true to four figures, so nobody has seen it. The
  divergence is in *provenance*, and it will print two heads for one solve the first time a
  `speed` or a density step crosses a pump.
- **The layout ring forms roll back differently, verified.** `Open` (`LayoutEngine.cs:1323-1339`)
  snapshots `_groups.Count`, `_placed` and `_side` and restores all three on decline. `Closed`
  (`:977-992`) removes its groups and restores one `_inline` flag. `Loop` (`:751`; declines at
  `:802`, `:826`, `:843`) removes its groups inline and restores neither `_placed`, which it clears
  for unit members at `:811` and sets through `Place` at `:815`, nor `_side`. `Solve` clears
  `_placed` once per fragment (`:161`), not per form, and `Loop` is the first form tried
  (`:167-170`), so what it placed before declining is still marked placed when C20, C19 and C18 run.
  **Measured in R5:** with every form restoring one snapshot, no ladder step, audit count or
  golden moved; the gap was real and not load-bearing.

## The principle the rewrite is made against

A rule table for `Candidates` only helps if the order in it is a rule. Today it is a history. The
five defects that shaped it agree on one rule, and the rewrite is made against that rule rather than
against the branches. It is `D-130`:

> **A stated constraint is absorbed by the first free actuator in kind order and, within a kind, the
> nearest first. A pinned flow asks the owner's own duty, then a pump's head (own branch before the
> rest of the hydraulic), then a valve's `kv` on its own branch; a mixed inlet or node temperature asks
> only a mixing split, the one at its own branch first. An actuator is claimed once, first come in
> constraint order.**

The first draft of this document put "nearest" above "kind": a valve on the owner's own branch before
a pump elsewhere in the hydraulic. That is not what the code does and not what `C-91` decided. With
one header pump serving two rings, the first ring's constraint takes the pump's head and the second
falls to its own valve; nearest-first would give both rings their valves and leave the pump to the
sizing rule. Both are square, and the second is a different plant. The order is by actuator kind
first, because the pump is the plant's driver and the index-circuit rule sets it from the first pinned
circuit, then by nearness within a kind (`S-45`, `S-56`). Which circuit is the index is declaration
order today, which `D-130` records as not decided.

`C-111`'s second half is not answered by this: the balancing valve practice puts in a mixing valve's
bypass is not in the script, so no ownership rule can size it. It stays open on the user's language
decision.

## The target shapes

Each shape below is named once here and referenced from the packages. None changes a solved number.

### `ParameterState` and one resolver

```
enum ParameterState { Stated, Defaulted, SizedFinal, SizedProvisional, Promoted, Free }

static ParameterState Ownership.Of(IFlowComponent component, string parameter,
                                   CircuitGraph graph, CountingTable? promotions)
```

Precedence is the one [`15`](10-language/15-semantic-model.md) and `D-02`/`D-32` already state:
stated beats everything; a decided default is a constraint too; a sized value is final unless the
graph's `ProvisionalParameters` carries its key (`D-96`); a promoted parameter is an unknown; anything
else is free. `IsFree` becomes `Of(...) is Free or SizedProvisional`; `Claimed` becomes
`Of(...) is not Free`; `ComponentFactory.Value` reads the value the state names. The
`"{name}.{parameter}"` key is built in one place. `HydraulicPartition.Stated` stays and is called from
the resolver, not around it.

### `FreeActuators` and `ReachingElements`

```
static IEnumerable<IFlowComponent> Reach.ElementsOnBranchesThrough(CircuitGraph graph, IFlowComponent owner)
static IEnumerable<(string Component, string Parameter)> Actuators.Free(CircuitGraph graph,
        HydraulicComponent hydraulic, IFlowComponent owner, ConstraintKind kind)
```

`Free` yields in the principle's order and is the only place that order is written. `Candidates`
becomes a `switch` on `ConstraintKind` that filters `Free` by which parameters can answer that kind:
a mixed inlet or node temperature takes `position` on a three-way valve; a fixed flow takes `power`
on its owner (never on a flow constraint, `S-72`), then `head` on a pump with no stated rise
(`C-109`), then `kv` on a valve or three-way valve. Kinds are matched by type, never by string.

### `SolvedStates`, one view per component

`SolvedStates.Exchanger` (`Solvers/SolvedStates.cs:310`, `L-59`) is already this shape for one kind:
the solved view computed once and read by both renderers. It grows to every kind:

```
static PumpView     SolvedStates.Pump(SolvedPort inlet, SolvedPort outlet, Pump pump, double? promotedHead)
static ValveView    SolvedStates.Valve(...)            // kv, position, authority, basis
static ExchangerView SolvedStates.Exchanger(...)        // exists
```

`PumpView.Head` is computed once, from the solved port pressures and the density convention the
residual used, with a `Basis` string ("curve at 0.42 kg/s", "promoted", "rise stated, 30 kPa at
speed 1"). `SolveExplanation.OperatingPoints`, `.Ratings`, `.HeatBalance` and
`ModelContractBuilder.State` consume the views and keep only formatting. The two hand-rolled
promoted-or-own lookups go with them.

### Hydrostatic and volume-flow helpers

`x / (ρ g)` and its inverse are written at twelve sites in seven files. One pair,
`Hydrostatic.Head(dp, density)` and `Hydrostatic.Rise(head, density)`, both in SI, both documented
with sign and unit. `SizingContext.VolumeFlow(density)` in m³/s replaces the three sizer-local
recomputations at `ValveSizer.cs:107,209`, `PipeSizer.cs:75,90`, `PumpSizer.cs:71,85`, two of which
multiply by 1000 and one of which does not.

### `PassOutcome` for the outer loop

`OuterLoop.RunAsync` (`:326-548`) has five early returns, each repeating the diagnostics `AddRange`
chain (`:495-513`, `:527-547`). One `RunPass` returning a `PassOutcome` record (converged, declined,
unsized, non-finite, cancelled, with the pass's notes and histories) and one `Close(outcome)` that
appends diagnostics in one order. `MarkProvisional`'s two copies (`Declined :1051-1074`,
`Unsized :1123-1154`) merge.

### `AssembleRing` and a rollback scope

The three ring forms run the same tail: `Ranges`/`Cycle` → `Block` per range → items → `Top` →
`Corners` → `Bottom` → `Close` → `_groups.Insert(mark, ("loop", members, true))` → `Assign(Walk)`
per run. One private `AssembleRing(...)` parameterised on what differs: the source-side anchors, the
tag, whether a chain hangs off the source. Around it a `readonly struct RingScope` that snapshots
`_groups.Count`, `_placed` and `_side` on construction and restores all three unless `Commit()` was
called, so every form rolls back the same way and the `Loop` gap closes as a side effect. `Open`'s
own rules (`D-115` boundary substitution, the two-path merge `:1287-1297`, the C12 rail correction
`:1460-1484`) stay in `Open`, before the shared tail.

### `Segment`

Six independent segment-geometry sites use the same `Math.Abs(a - b) < Eps` idiom:
`LayoutEngine.Crossings :2985-3017`, `.SignalCrosses :3353-3400`, `SceneAudit.Shared :288-308`,
`.Crosses :470-485`, `.Collinear :610`, `.Overlaps :262-285`, and `OrthogonalRouter`'s
`ParallelBand`/`Step`/`OwnZone` `:504-539`. `OrthogonalRouter.Pipe` (pre-normalised into
`Vertical`/`Line`/`From`/`To`) is the mature form and becomes the shared `Segment` with
`IsVertical`, `Overlap`, `Crosses`, `Contains`.

### Phase records for the binder

`BindingRun` shares about 27 mutable fields across six partial files; the phase order
(`Partition → CollectCurves → CollectDeclarations → Evaluate → Review* → BindTopology → Publish`)
lives in comments (`BindingRun.Topology.cs:40-58`). Each phase takes the previous phase's result
record and returns its own. Inference rules I1 and I7 (`Binder.cs:375-467`), I2 and I3
(`BindingRun.Topology.cs:460-554`) and I6 (inside `BindConnections :212-256`) become one
`InferenceRule(Id, Apply)` record each, run from one ordered list per phase. I4 and I5
([`11`](10-language/11-language-overview.md)) are not code and are not in the list. The phases are
not merged; their order is load-bearing.

## The packages

Ordered so that each makes the next reviewable. Effort and risk use
[`08`](08-implementation-sequence.md)'s vocabulary. "Measured on" names what is run before and after,
and the exit criterion is always the same: **byte-identical output on it, or a listed and explained
golden re-baseline**.

### R0 — Seams and dead code (effort small, risk low)

No logic changes; the purpose is that R2 to R5 produce diffs a reader can review.

1. Split `LayoutEngine.cs` into partials along its own groupings: form selection (`Head`,
   `Fragments`, `Loop`, `Closed`, `Open`, `Ring`, `Tried`, `Decline`, ~1,400 lines), placement
   primitives (`Block`, `Close`, `Top`, `Single`, `Corners`, `Bottom`, `Slide`, `Hang`, `Ranges`,
   `Extend`, `UnitOf`, `Column`, ~900), routing and anchors (`AnchorOf`, `AnchorOffset`, `PlaceFrom`,
   `PlaceNode`, `OnRail`, `Walk`, `Assign`, `Follow`, `Cycle`, ~500), post-processing (`Connect`,
   `PlaceInstruments`, `ComputeHops`, `AlignBoundaries`, `Nudge`, `ToScene`, ~600).
2. Move `SolutionSeed.Field` (611 nested lines) to `Solvers/DivergenceFreeFlowField.cs`.
3. Delete `SceneAudit.Inside` (`SceneAudit.cs:547-566`): added in `af0604f`, never called since,
   verified by `git log -S` and by reference search.
4. Split `LayoutHintsDerivation.ThermalStages` (`:517-829`) into `CollapseVertices`,
   `ClassifyPivots`, `PropagateRoles`, `Rank`, `FillNeutral`, passing the vertex arrays explicitly.
5. Merge the two `component is Tank` casts in `ModelContractBuilder.Ports` (`:363-364`).

Measured on: the ladder (`LayoutLadderTests`, SVG and `.solve.txt` goldens under
`diagnostics/layout-ladder/`), `LayoutHintsTests`, `HintDumpTests`, the Api goldens. Nothing may move.

### R1 — Small shared helpers (effort small, risk low)

`Hydrostatic.Head/Rise` at the twelve sites; `SizingContext.VolumeFlow` at the six;
`ComponentFactory`'s six identical parameter-map initializer blocks (`:358-363`, `:395-399` and
four more) through one helper; `BindingRun.RegisterComponent` at the three list-plus-dictionary
registrations (`Binder.cs:412-413`, `:550-551`, `BindingRun.Topology.cs:438-440`); the
`("loop", members, true)` tuple at its four sites through `LoopGroup(members)`.

Measured on: `CircuitDiagnostics` over `diagnostics/scratch/`, the corpus (`CorpusStatusTests`), the
Api goldens. Nothing may move: every helper is a pure extraction and a changed digit is a bug in the
extraction, not a re-baseline.

### R2 — The solved view (effort medium, risk low-med)

`SolvedStates.Pump` and `.Valve` beside `.Exchanger`; `OperatingPoints`, `Ratings`, `HeatBalance`
and `ModelContractBuilder.State` rewritten to consume them; the promoted-or-own lookups removed.

The pump head is where a number can move. Before the change, run every corpus script and record the
report's head, the contract's head and `(p_out − p_in)/(ρ̄ g)` from the solved ports; the three
should agree within 1e-6 m on every current script because none states `speed` or crosses a density
step. After the change all three are one number. If any script disagrees before the change, that is a
defect row filed first and the re-baseline is explained by it.

Measured on: `SolveExplanationTests`, `HeaderSeedTests`, `OuterLoopTests`, the ladder's `.solve.txt`
goldens, `ModelContractBuilderTests`, `ModelContractJsonTests`, the Api goldens. Expected
re-baseline: none, or the pump-head digits only, listed.

### R3 — Parameter ownership and promotion (effort big, risk med)

The wound. In order, each green before the next:

1. The `D-` for the actuator principle and its paragraph in `23` (`D-130`).
2. Direct tests for `ComponentFactory.Value`, `.Defaults`, `.Sized` and `PipeSizer.CanSize`, which
   today are reached only transitively; they pin the current precedence before anything moves.
3. `Ownership.Of` and `ParameterState`; every site in the diagnosis table calls it; the string key
   is built once.
4. `Reach.ElementsOnBranchesThrough` replaces the two walks in `Candidates`.
5. `Actuators.Free` in the principle's order; `Candidates` becomes the per-kind filter over it;
   kinds matched by type.
6. `WellPosedness.Constraints` (`:428-588`) rewritten as a per-kind classifier returning a record,
   unit-tested per kind.
7. `Promote`'s first-come rule unchanged and asserted by a test that names it.
8. `C-111`'s balancing valve, now on the constraint's path, sized by `ValveSizer.AtStatedDrop`'s
   sibling rule for a pinned flow, closing the row's second half.

Measured on: `WellPosednessTests`, `PromotionLocalityTests` (`S-45`), `SurvivingProvisionalTests`
(`D-96`), `StatedRiseTests` (`C-109`), `ThermalSizerTests`, `ExchangerSizerTests`, the corpus, and
the five reference circuits of [`01`](00-foundation/01-vision-and-scope.md) run through
`CircuitDiagnostics` with the whole `SolveExplanation` read, not grepped. The counting line
(`unknowns/equations/rank`) of every script is the acceptance number and may not move. The
series-loop probes from `C-111` (two, four and mixed rings) are re-run and their outcome recorded on
the row.

### R4 — The outer loop and the seed (effort medium, risk med)

`RunPass`/`PassOutcome`/`Close`; `MarkProvisional` merged; the three promoted-declaration walks in
`SolutionSeed` (`Parameters :217-254`, `Promoted :583-615`, `PromotesHead :617-622`) merged;
`SolutionSeed.Integrate` (`:294-510`) re-expressed over a `WalkState` record and not otherwise
changed; `NewtonSolver.Solve`'s nine termination checks extracted in their current order.

Measured on: `OuterLoopTests`, `CorpusStatusTests`, `RatedExchangerSolveTests`,
`DeferredEvaluationTests`, the corpus with the whole report read. The pass count and the last
residual norm of every script may not move.

### R5 — The layout ring forms (effort medium, risk med)

`RingScope` first, on all three forms, as one change; the ladder re-run. If a golden moves here, it
is the `Loop` gap made visible (open question 2) and is filed as a row before the golden is
re-baselined. Then `AssembleRing`, as one change, never form by form. Then `Segment` across
`LayoutEngine`, `SceneAudit` and `OrthogonalRouter`, and the two "free side" orders
(`FreeSide :2295-2323`, `Open.Finish :1537-1551`) reconciled or the difference documented at both.

Measured on: the ladder (every step's SVG, `SceneText` and audit counts), `LayoutPredicateTests`,
`SceneAuditTests`, `diagnostics/layout/`. Hard and soft audit counts may not move.

### R6 — The binder and the equation system (effort big, risk med; deferred)

Phase records for `BindingRun`, starting with `BindingRun.Heights.cs` (two methods), then
`Curves`, then `Topology`; `InferenceRule` records; `ComponentKindInfo.ResolveMember<T>` after the
`ResolvePort` bounds-check difference (`:336-340` versus `:273-277`) is reconciled by hand;
`EquationSystem.Build` behind a builder and `Constraints` behind a resolver table with named
factories. Taken only when P5.13b or M4 next opens these files; they are stable and well pinned.

### Proposed position in `08`

After P5.13a and before P5.13b. P5.13b moves the model under the binder (`in.p` bound to the node,
`vflow` at the port's state, the keyed spellings on the wire), which is exactly the code R2 and R3
rewrite; doing P5.13b first patches the old shape once more, doing it after patches the new one
once. M4's controllers promote the same actuators R3 names, and should be specified against
`Actuators.Free`, not against `Candidates`. `08` decides; this is the proposal.

## What is deliberately not rewritten

`ValveSizer.Size`, `PipeSizer.Size`, `ThermalSizer.Size`: large, but already one named helper per
physical case. `BranchFlows.Propagate`, `HydraulicBlocks.Build` (Tarjan): real algorithms.
`LayoutEngine.Solve`'s `Tried` chain: a clean strategy list. `ComponentRegistry`: metadata mirrored
to `22` by a test; its 148-line `Verify` runs once over compiled-in data. `Binder.Lookup`: single and
well factored. `Valve`/`ThreeWayValve`: two similar shapes, a judgment call left until a third
appears.

## Invariants

- **A refactoring package changes no solved number, no printed line and no drawn point except by a
  listed, explained re-baseline.** The list is in the commit and in the package's row in
  [`09`](09-project-state.md); an unlisted golden change fails the package.
- **No `D-` is reversed or edited.** A refactoring that needs a decision changed writes a new `D-`
  first and is then a feature package, not a refactoring one.
- **Every register row cited at a rewritten site is cited at the new site.** The rewrite moves the
  reason with the code; a site that loses its `S-45` comment loses the only record of why the order
  is what it is.
- **A defect found during a rewrite is filed, not fixed, unless the fix is the rewrite.** The two
  live findings above (pump head, `Loop` rollback) are the exception written into R2 and R5.
- **Each package is one review scope.** Its diff is reviewable by `dotnet-review` against the
  standards in one pass; a package too large for that is two packages.
- **`EvaluateResiduals` still allocates nothing** after R2 and R4; the solved views are built after
  convergence, never inside a Newton iteration.

## Error cases

| Case | Handling |
|---|---|
| A golden moves in R0 or R1 | The extraction is wrong. Revert, find the digit, fix the extraction; never re-baseline. |
| The three pump heads disagree on a current script before R2 | A defect row in `20-core-domain/defects.md` first; R2's re-baseline cites it. |
| The counting line moves in R3 | The rewrite changed a promotion. Compare `Actuators.Free`'s yield order against the old `Candidates` on that script; the principle or the code is wrong, and the row says which. |
| A ladder step moves under `RingScope` | The `Loop` gap was load-bearing. File it (`28`/`29`), show the two pictures, decide which is right, then re-baseline. |
| A rewrite exposes an undocumented rule | It goes into the owning plan document before the code, with `C-`/`S-`/`L-` if it was hiding a defect. |
| The review scope of a package exceeds one pass | Split the package; do not skip the review. |

## Worked example

`ParameterState` on the simple loop with `PU1 pump head=15`, the `C-75` case
[`23`](20-core-domain/23-topology-and-graph.md) records. The ring needs 5.28 m; the surplus must be
taken by the valve.

| Parameter | Today's route | `Ownership.Of` |
|---|---|---|
| `PU1.head` | `HydraulicPartition.Stated` → true | `Stated`: 15 m is a constraint (`D-02`), the pump row of the promotion table cannot fire |
| `TV1.kv` after bootstrap lowering | `IsFree`: not stated, not defaulted, sized, and `ProvisionalParameters` holds `TV1.kv` → free | `SizedProvisional`: the catalogue's largest Kv is a placeholder (`D-96`) |
| `HE1.power`, `HE1.out` stated | constraint list: `FixedFlow` on `HE1`'s branch | unchanged |
| `Actuators.Free(HE1, FixedFlow)` | `Candidates`: `power` (stated, skip), local pump `head` (stated, skip), distant pump (none), branch `kv` → `TV1.kv` | yields `TV1.kv` first, because it is the first free actuator on the owner's path |
| `TV1.kv` after promotion | `Claimed` → promoted set contains it | `Promoted`: an unknown; the sizer skips it and reports no authority |
| Solved | Kv 0.77 | Kv 0.77, byte-identical report |

The same walk on the two-ring header (`C-91`): the first ring's flow constraint claims `PU1.head`,
the second ring's finds it `Promoted`, walks on, and takes its own `kv`. That is first-come, and the
test for R3 step 7 asserts it by name.

## Acceptance criteria

1. R0 through R5 each land as one package with a row in `09`, and each package's measured set is
   byte-identical or its re-baseline is listed and explained in the commit.
2. After R3, the ownership question -- which of the three maps holds a parameter -- is asked only
   through `Ownership`; what may still read a map is a value read of a named stated parameter,
   `HydraulicPartition.Stated`, the components and the factory that build the maps, the contract
   builder's serialisation and `DeferredEvaluation.SolvedScope`, as `D-131` lists. Verified on
   2026-09-21 by `get_references` on the three `IComponent` map properties and on
   `UnitTable.StandardGravity` (four readers, all in `Hydrostatic`), after `reload_workspace` --
   the first walk, before the reload, still showed sites two commits gone.
3. After R3, the string `"three_way_valve"` appears in `WellPosedness.cs` only in diagnostics text.
4. After R2, one method computes a pump's head for the report and the contract, and
   `UnitTable.StandardGravity` is referenced from `Hydrostatic` and the unit table only.
5. After R5, every ring form declines through `RingScope` and a test declines `Loop` after `Place`
   and asserts `_placed` and `_side` are as before.
6. The actuator principle is a `D-` (`D-130`) and a paragraph in `23` before R3 step 3 is merged.
7. `plan/20-core-domain/defects.md`, `plan/30-solver/defects.md` and this document's `09` rows
   record what each package found, in the register's form.

## Open questions

1. **Where does the refactoring sit in `08`?** Proposed after P5.13a and before P5.13b, for the
   reason given above; `08` owns the answer and this document only argues for one.
