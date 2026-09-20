---
id: 62-testing-strategy
title: Testing strategy
tier: 60-docs-and-devex
status: reviewed
owns: [test project layout, test tiers and traits, golden files, physical validation cases, assertion tolerances, frontend testing]
depends_on: [03-repository-layout, 07-quality-attributes, 18-script-compatibility, 36-numerics-and-convergence, 61-documentation-plan]
traces_to: [R-17, R-38, R-39, R-40, R-41, R-42, R-43, R-44, R-45, R-46, R-47, R-48, R-50]
open_questions: 0
last_review_pass: 6
---

# Testing strategy

## Purpose

`R-17` asks for extensive unit tests on Core. The word that needs unpacking is "extensive": a
thermodynamic solver can have a thousand tests that all assert its own output and prove nothing about
whether the physics is right. This document separates the kinds of test that answer different
questions, and states which numbers are checkable against an external truth.

## Responsibilities

**Owns.** Test project layout, tiers and traits, golden files, physical validation cases, assertion
tolerances, and frontend testing.

**Explicitly does not own.** Numerical tolerances ([`36-numerics-and-convergence`](../30-solver/36-numerics-and-convergence.md)
— cited here, not restated), CI ([`63-ci-and-repo-hygiene`](63-ci-and-repo-hygiene.md)),
documentation-example testing ([`61-documentation-plan`](61-documentation-plan.md)).

## Framework

**xUnit v3**, per the brief. Assertions with xUnit's built-ins plus `Verify.Xunit` for golden files.
No fluent-assertion library: one assertion vocabulary is enough, and the failure messages from a
well-named test are clearer than from a chained expression.

**Do not mock types the project owns** (`testing.md`). The one boundary worth a test double is the
property backend, and it exists for speed rather than isolation — see below.

## Layout

Mirrors the source tree, folder for folder
([`03-repository-layout`](../00-foundation/03-repository-layout.md)'s invariant 3):

```
tests/
├── FluidScript.Core.Tests/
│   ├── Language/      Units/      Diagnostics/
│   ├── Fluids/        Components/ Topology/
│   ├── Sizing/        Solvers/    Model/
│   ├── Validation/    ← physical validation. Not a mirror of anything.
│   └── Golden/        ← .verified.txt files
├── FluidScript.Api.Tests/
└── FluidScript.Fixtures/    shared sample scripts and expected outputs
```

`Validation/` is deliberately not a mirror: it is organised by *physical claim* rather than by class,
because a claim like "energy balances around a loop" is not about one type.

## Tiers

Traits, so a fast subset can run constantly and the whole suite before a commit.

| Trait | Target | Contains | Runs |
|---|---|---|---|
| `Category=Unit` | < 2 s executing | Everything with a fake property backend | Constantly |
| `Category=Property` | < 20 s | Real SharpProp property accuracy | Pre-commit |
| `Category=Validation` | < 60 s | Physical validation cases | Pre-commit |
| `Category=Golden` | < 10 s | Parser, printer, model-contract snapshots | Pre-commit |
| `Category=Api` | < 30 s | Endpoint and contract tests | Pre-commit |
| `Category=Docs` | < 5 s | The documentation gate: registry, reserved words and diagnostic codes against `/docs` | Pre-commit, and its own CI check |
| `Category=Diagnostic` | *(none — the duration is the measurement)* | Performance harnesses that write a timing report into `diagnostics/` | **Never in a gate**; on request |

**`Category=Diagnostic` is in the table but is not a test tier in the same sense** (`T-1`). It has no
pass criterion and no budget, because the thing it measures *is* its duration — `P3.1` added it for the
property-performance harness, which writes a timing report rather than asserting anything. A row with a
budget would be a contradiction, and leaving it out of the table entirely is what let it be mistaken for
an omission. It is excluded from every gate deliberately: a run whose duration is the result cannot also
be a run that fails when it is slow.

The consequence to hold on to is that **a regression here is invisible to CI by design**, so it is found
by reading the reports. `F-19` is what that looks like when nobody does — a property-call cost 20 000×
the figure the latency budget was set against, sitting in `diagnostics/` and contradicting `07`.

### FluidScript experiment loop

Every investigation of a circuit follows the same loop:

1. Execute the actual FluidScript under investigation through the solution pipeline.
2. Read its complete `SolveExplanation` report. A passing diagnostic harness means only that the report
   was written; it does not mean the circuit converged.
3. Trace counting, constraints and promotions, the iteration trajectory, seeded and solved values with
   their seed bases, the state in °C and kPa and each branch's direction, the heat balance, the operating
   points, every residual, sizing choices and notes, warnings, and rank and conditioning together. The
   largest final residual may be a symptom of an earlier bad seed, sizing choice, bound, or flow
   direction, and the iteration table is where a failed solve says which step it went wrong on.
4. Change only what the complete report supports.
5. Rerun the script and repeat from step 2.

Do not diagnose a circuit from a grep excerpt, its termination headline, or only the largest residual.

The same loop for a picture: a variant dropped in `diagnostics/scratch/` is drawn by `LayoutDiagnostics`
as `28` §31 text and SVG beside the samples, and the whole `SceneText` -- components, connections,
placement trace, raster, validation, interference -- is read before a rule is touched. One scratch
folder serves both harnesses, so one file gives a variant its solve report and its picture.
The report is one causal record and must be read as one.

**The budget is execution time, not what `dotnet test` prints.** Measured on the reference
environment, a run matching *no* tests still reports between 0.6 s and 1.3 s: that is host start,
JIT and Microsoft.Testing.Platform discovery, and it varies by more than the entire budget from run
to run. A criterion asserting the printed figure would therefore be measuring the runner, and would
fail on a cold cache with nothing wrong. Subtract the empty-run floor, measured on the same machine
in the same session, before comparing.

The floor also puts a hard bound on what the tier can ever be worth: no subset of a .NET test suite
runs in less than about half a second, so "run it after every edit" means one to three seconds in
practice, not zero.

`dotnet test --filter-trait Category=Unit` must stay under two seconds of execution. (The flag is
`--filter-trait` rather than `--filter`: .NET 10 drives xunit v3 through Microsoft.Testing
Platform, opted into by the repository's `global.json`, and VSTest's `--filter` is gone.) That is what makes tests something
run after every edit rather than before every commit, and it is achievable only because of the fake
property backend.

## The fake property backend

An `ISubstance` implementation with incompressible-water constants (ρ = 998, cp = 4182, μ = 1.002e-3),
analytic and instant.

**It exists for speed, not isolation.** Component and solver tests are about equations and iteration,
not about whether CoolProp is right; running them through real property evaluation makes the unit tier
tens of seconds instead of one. The real backend is exercised by the `Property` tier, which is where
"is CoolProp right" is actually asked.

**The risk it introduces is real**: a component that works with constant properties and breaks with
temperature-dependent ones. Mitigated by a second fake with *linear* temperature dependence, and by
every `Validation` case running against the real backend.

## Kinds of test, and what each proves

The distinction this document exists for.

| Kind | Asserts against | Proves | Failure means |
|---|---|---|---|
| **Unit** | Hand-computed expected values | The code does what was intended | A logic error |
| **Property accuracy** | Published reference data | The property backend is right | Wrong physics at the source |
| **Physical validation** | Conservation laws and analytic solutions | The model is physically consistent | Wrong physics in the model |
| **Golden** | A recorded previous output | Nothing changed unintentionally | A behaviour change, intended or not |
| **Property-based** | An invariant over random inputs | No input breaks the invariant | An edge case |

**Golden tests prove only stability, never correctness.** A golden file records what the code produced,
which is worth a great deal for detecting unintended change and worth nothing for detecting a wrong
answer that has always been wrong. Every golden file's *initial* value must be justified by a
hand-check or a validation case — a golden file accepted because "that's what it printed" bakes in
whatever bug existed that day.

## Memory

Two tests, of different kinds, because `C-76` passed every test the suite had: a property read that
cloned a 540 KB native CoolProp state and dropped it leaked half a megabyte the managed heap never
saw, and the suite's allocation-free assertions (`EvaluateResiduals` allocates nothing) were all
true while the process grew to 31 GB.

- **`Fluids/NativeMemoryTests`**, unit tier: two thousand state reads per substance must not grow
  the working set by more than 64 MB. Working set rather than GC statistics, and no forced
  collection, because a solve collects nothing between residual sweeps either; the leaking backend
  measured 1 059 MB on this test, the fixed one under 10. It runs in under a second.
- **`Performance/MemoryFootprintDiagnostics`**, diagnostic tier: per sample, managed allocation for
  bind, prepare and solve, what the process still holds after ten repeated solves, and that as a
  per-solve slope; fails above 8 MB kept per solve. `SolverScaleDiagnostics` reports the same two
  columns up the unknown-count ladder.

The rule the two encode: **a resource assertion measures the resource, not a proxy for it.** An
allocation counter is a proxy for memory; the working set is the memory.

## Physical validation cases

The tests that would catch a wrong model. Each asserts something true of reality, not of the code.

| # | Claim | Assertion | Tolerance |
|---|---|---|---|
| V1 | Mass is conserved | Σ flows at every node = 0 | 1e-9 relative |
| V2 | Energy is conserved | Heat in = heat out at steady state | 1e-6 relative |
| V3 | Loop closure | Σ Δp around any closed loop = 0 | 1e-6 relative |
| V4 | Water properties | Against CoolProp/IAPWS reference at 5 states | `07` validity matrix, per property |
| V5 | Humid air | Against psychrometric-chart values at 5 states | `07` validity matrix, per property |
| V6 | Friction factor | Against Colebrook–White, Re 4e3…1e8 | 0.01 % |
| V7 | Analytic single-pipe | Δp of one pipe against the hand-computed Darcy result | 0.1 % |
| V8 | Two solvers agree | Transient run to steady state = Newton's answer | Solver tolerance |
| V9 | Transient at rest | No disturbance ⇒ no drift over 600 s | absolute-plus-relative conservation bounds in `07`; each state remains within its property-specific validity tolerance from the initial value |
| V10 | Reversibility | Heating then cooling by the same duty returns the original state | 1e-6 |
| V11 | Discretization convergence | Doubling `nodes` changes the outlet temperature by less each time | monotone |
| V12 | Catalogue plausibility | Every DN entry: OD > 2×wall, monotone OD | exact |
| V13 | Pressure reference | Gauge/absolute conversion at declared atmosphere returns the same absolute state | 1 Pa |
| V14 | Humid-air basis | Enthalpy and humidity ratio use kg dry air consistently through adapter and model contract | 0.5 % |
| V15 | Mixed tank | `layers=1` step response against `T(t)=T_in+(T0−T_in)e^(−ṁt/m)` | 0.1 K |
| V16 | Tank conservation | Integrated external mass/enthalpy equals stored change for multi-inlet/outlet storage header | conservation row in `07` |
| V17 | Stratified tank | Layer refinement converges monotonically to an independently tabulated plug-displacement case; inversion remix preserves mass/enthalpy | monotone; conservation row in `07` |
| V18 | Circuit partitioning | The distribution header (`01`) binds three circuits; each subcircuit's flow sums to the parent's at the attachment nodes | conservation row in `07` |
| V19 | Tag determinism | Tags are a function of declaration order alone: permuting connections changes no tag; inserting a declaration renumbers only its own `(circuit, code)` sequence and changes no identifier | exact |
| V20 | Tag/quantity collision | No registered tag code produces a tag that lexes as a quantity literal | exact |
| V21 | Two-sided ownership | The substation exchanger's owning circuit is unchanged when the two circuit blocks are swapped in the source | exact |
| V22 | Spacing isolation | Across two `spacing` values: everything Core computes — solved state, parameters with `source`/`basis`, graph, and all of `layout` — is byte-identical with `style` excluded, while `style.spacing` and the resulting placements both differ | byte-exact outside `style` |
| V23 | Detached run continuity | Detaching a run for 200 frames and reattaching yields contiguous `sequence`, no second `base`, and correct backward scrubbing | exact |
| V24 | Corner rule | Across every sample, reference circuit and the supported 200-component fixture, no placement's bounding box contains any route direction change that is not a branch; a deliberately crowded fixture reflows rather than violating it, and one crowded past the limit reports `FS5002` (`D-44`) | exact |

**V8 is the strongest test in the suite.** Two solvers sharing no numerical code arriving at the same
answer is evidence neither is systematically wrong, which no amount of unit testing provides. It is
also the test most likely to be skipped as slow, and it must not be.

**V11 is the one that catches a discretization bug.** A scheme with a sign error can look plausible at
`nodes=4` and diverge as the mesh refines; asserting that refinement *converges* catches it.

**V15–V17 keep `D-32`'s tank model honest.** V15 has a closed form, V16 checks the conservative finite-
volume accounting with simultaneous branches, and V17 checks both the layer-order algorithm and the
claim that more layers improve rather than merely change the answer. The V17 reference table is
generated by an independently reviewed control-volume calculation, not by the production solver.

## Governing-equation coverage (`R-17`)

The M2a audit of 2026-09-14, over `src/FluidScript.Core/Components` and `/Solvers`. Every equation the
code writes, with the test that checks it against something other than the code — a hand number, a
published correlation, a closed form — and the test that pins it against regression. "Hand" means the
expected value was computed outside the residual under test; a test that only asserts the function
against itself is listed as a regression, and a row with no hand entry is a gap. The kinds of test
[above](#kinds-of-test-and-what-each-proves) say why the distinction matters.

| Equation | Where | Hand-checked | Regression |
|---|---|---|---|
| Darcy–Weisbach, Δp = (f·L/D + K)·ρv²/2 | `Pipe.PressureDrop` | `FlowComponentTests.APipesPressureDropMatchesAHandComputedCase` (V7) | `OuterLoopTests.TheSimpleLoopReproducesTheWorkedExampleEndToEnd` (99.8 Pa/m) |
| Friction factor: laminar 64/Re, Colebrook–White via Serghide, blended transition | `Pipe.FrictionFactor` | `FlowComponentTests.TheFrictionFactorMatchesColebrookWhite` (V6, iterated Colebrook as the oracle) | `.TheLaminarTurbulentTransitionIsContinuousInValueAndSlope`, `.APipeAtRestHasNoPressureDropAndAFiniteSlope`, `.ReversedFlowOpposesItself` |
| Minor loss, K·ρv²/2 | `Pipe.PressureDrop` | `OuterLoopTests.AStatedMinorLossAddsExactlyKTimesTheVelocityHeadAndAnOmittedOneAddsNothing` (K = 5 at 0.2392 kg/s, by hand) | same |
| Static head along a pipe, ρgΔz, and along a bare link | `Pipe.PressureDrop`, `EquationSystem` link row | `FlowComponentTests.ElevationCostsRhoGH`; `OuterLoopTests.ABareLinkDownFromTheRoofCarriesTheStaticHeadAndSizingCountsIt` | `OuterLoopTests.ALoadOnTheRoofCostsThePumpNothing…`, `.AnOpenRiserSpendsTheStaticHeadFirst…` |
| Potential-energy injection of a rising pipe, ṁ·g·Δz split by flow direction | `Pipe.EvaluateEnergyInjection` | `EnergyInjectionTests.TheShareSumsToWhatTheRiseCostsAtEveryFlow`, `.ARisingPipeTakesEnergyOutOfTheNodeAtTheTop` | `.TheSplitIsSmoothThroughZeroFlow`, `.ALevelPipeContributesNothingAtAll` |
| Pump curve, Δp = −ρg(n²H₀ − k·ṁ²) | `Pump.Head`, `Pump.EvaluateResiduals` | `ValveAndPumpTests.ThePumpCurveDistributesSpeedSquaredOverBothTerms`, `.TheDefaultCurvePassesThroughItsDutyPoint` | `.APumpRaisesPressureSoItsDropIsNegative`, `.AStoppedPumpIsAPureResistanceAndItsResidualIsFinite` |
| Shaft power, ṁ·Δp/(ρη) | `Pump.ShaftPower` | `ValveAndPumpTests.ShaftPowerIsPositiveDespiteTheNegativeDrop` | same |
| Kv relation in SI, ṁ = (ρ/3600)·Kv·√(Δp/(10⁵·ρ/ρ_w)), regularised below `ValveRegularizationDrop` | `ValveLaw.MassFlow`, `.RequiredKv` | `ValveAndPumpTests.TheKvFormAndTheSiFormAgree` (the m³/h form as the oracle), `.AStraightLineThroughTheOriginWouldHaveMissedTheSlopeByAFactorOfTwo` | `.TheValveLawIsOdd`, `.TheValveLawIsContinuousInValueAndSlopeAtTheRegularizationJoin`, `.AClosedValveEvaluatesAFiniteResidualAndAFiniteSlope` |
| Characteristics: linear, equal-percentage with rangeability 50 | `ValveLaw.Opening` | `ValveAndPumpTests.TheCharacteristicsFollowTheirDefinitions`, `.AClosedEqualPercentageValveStillPassesTwoPercentOfItsKv` | same |
| Three-way valve: two Kv legs on complementary openings, one mass balance | `ThreeWayValve.EvaluateResiduals` | `ValveAndPumpTests.AThreeWayValvesBypassTakesTheComplementaryOpening` | `.AThreeWayValveBalancesMassWhicheverWayItIsWired`, `TwoWayConfigurationTests.*`, `ValveLegsTests.*` |
| Exchanger duty into the discharging node, ṁ·Δh = P, split smoothly through reversal | `HeatExchanger.EvaluateEnergyInjection`, `EquationSystem` | `HeatExchangerTests.ThirtyKilowattsAcrossThirtyKelvinImpliesTheExpectedFlow` (V2's hand form), `EquationSystemTests.AnExchangersDutyAppearsInTheEnergyBalanceOfTheNodeItDischargesInto` | `.TheWholeDutyLandsOnTheSideItDischargesThrough`, `.TheDutySplitIsSmoothThroughAReversal`, `EnergyInjectionTests.ACoupledExchangerTakesOutOfOneStreamWhatItPutsIntoTheOther` |
| Exchanger pressure drop, Δp = Δp_rated·(ṁ/ṁ_rated)² | `HeatExchanger.EvaluateResiduals` | `HeatExchangerTests.TheDropFollowsTheSquareOfTheFlowRatio` (25/100/6.25 kPa at ½/1/¼) | `.AnIdealBlockHasNoPressureDrop`, `.AReversedFlowLosesPressureInTheDirectionItIsGoing`, `OuterLoopTests.EveryExchangerResistsFlowAndSaysAtWhatFlowItWasMeasured` |
| Stated port temperature as a constraint row, in kelvin | `EquationSystem.Constraints` | `EquationSystemTests.AStatedPressureIsMetExactlyWhenTheIterateCarriesIt`, `.AStatedBoundaryFlowReachesTheBalanceItNames` | `EquationLayoutTests.*`, `EquationRowReconciliationTests.*` |
| Node mass balance, Σṁ = 0 | `CircuitNode`, `EquationSystem` | `FlowComponentTests.ANodesMassResidualIsTheSumOfItsFlows`; `ConservationTests.MassIsConservedAtEveryJunctionByDirectSummation` (V1, by hand over the branch table, so the dropped redundant row is covered) | `SolutionSeedTests.EveryNodeMassBalanceCloses`, `EquationSystemTests.AtRestEveryBalanceIsZeroExceptWhereAComponentAddsEnergy` |
| Node energy balance with upwinded inflow enthalpy | `CircuitNode`, `Smoothing.Upwind` | `FlowComponentTests.ANodeMixesTwoStreamsByMassWeightedEnthalpy` | `.UpwindingIsContinuousAndSmoothThroughZeroFlow`, `ConservationTests.EveryBalanceMeetsTheConservationRowUnscaled` (V2) |
| Junction-element mixing, Σṁᵢhᵢ/Σṁᵢ over inflows (`S-58`) | `EquationSystem.Arriving` | `OuterLoopTests.AMixingValveDeliversTheInflowWeightedEnthalpyAndNotTheAverage` (50 °C by hand from 0.1914 kg/s at 60 and 0.0957 at 30; the old average rule gives 45) | `.TheDistributionHeaderReproducesTheVisionsFiguresEndToEnd` |
| Ideal link, p_from − p_to − ρ̄gΔz = 0 | `EquationSystem` | `EquationSystemTests.AnIdealLinkAssertsThatTwoNodesAreOnePressure` (5 kPa in, 5 kPa out) | `ConservationTests` pressure rows (V3) |
| Loop closure, Σ Δp round every loop = 0 | nodal pressures, so every pressure row | `ConservationTests.EveryBalanceMeetsTheConservationRowUnscaled` (V3, unscaled, 1e-6 of the pressure span) | `CorpusStatusTests` |
| Tank, steady: one mixed enthalpy to every outflow, pressure equalities without hydrostatics | `Tank.EvaluateResiduals` | `TankTests.ASteadyTankGivesOneMixedEnthalpyToEveryOutflow`, `.APressureEqualityIsTheDifferenceFromTheFirstPort` | `.TheSteadyAnswerDoesNotDependOnTheLayerCount`, `.AnInletWithReverseFlowDrawsFromItsLayer`, `.ThePressureEqualitiesCarryNoHydrostaticTerm` |
| Tank, transient (V15–V17) | `33` — not built | — | — (M4) |
| LU with partial pivoting | `DenseLu` | `DenseLuTests.ItSolvesASystemWhoseAnswerIsKnownByHand`, `.ItPivotsRatherThanDividingByAZeroDiagonal` | `.ALargerSystemRoundTripsThroughItsOwnMultiplication`, singular-column naming |
| Newton with line search, domain guard, box projection | `NewtonSolver` | `NewtonSolverTests.TheSimpleLoopConvergesToTheFlowItsDutyImplies` (the duty's ṁ = P/(c_p·ΔT), by hand); `DenseLuTests` for the direction | `.APromotedParameterIsHeldInsideItsPhysicalRange`, `.AParameterThatRunsOutOfRoomIsNamed…`, `.AnIterateOutsideTheFluidDomainStopsWithTheNodeNamed`, `.TheSameInputProducesTheSameIterateEveryTime`, `.EveryTerminationCarriesTheCodeThatExplainsIt` |
| Row and column scaling | `ResidualScales`, `UnknownScales` | `ResidualScalesTests.APressureRowIsScaledByThePressureReference`, `.AnEnergyRowIsScaledByAFlowTimesAnEnthalpy`; `EquationSystemTests.ScalingDividesEachRowByItsOwnReferenceAndNothingElse` | `UnknownScalesTests.ScalingBringsTheSeedsMagnitudesWithinTwoOrdersOfEachOther`, `.EveryScaleIsPositiveOnEverySample` |
| Singular-Jacobian diagnosis | `NullDirection` | `NullDirectionTests.ARowTheOthersAlreadyImplyIsNamed…`, `.TwoIdenticalColumnsAreNamedAsOneCombination…` (hand-built matrices) | `.ShareBelowTheSignificanceCutIsNotNamed`, `.ADirectionIsFoundWhereAPivotCollapsesRelativeToTheLargest…` |
| Seed: flows from stated duties, spanning-forest propagation, temperature anchoring | `SolutionSeed` | `SolutionSeedTests.AStatedDutyFixesItsBranchFlowDirectly`, `.AThreeWayValvePartitionsTheCommonDutyFlowAcrossItsInletLegs` | `.EveryDrivenBranchIsSeededAwayFromRest`, `.NoBranchStandsStillWhereAnotherSpanningForestWouldHaveMovedIt`, `SeedPropagationTests.*` |
| Resistance along a run, by each element's own law | `BranchResistance.Along` | `PumpSizerTests.TheSimpleLoopsPumpTakesTheHeadTheWorkedExampleGives` (`24`'s 51.67 kPa ring), `.ANetRiseAroundTheLoopClampsToZeroRatherThanToANegativeHead` | `OuterLoopTests.ABareLinkDownFromTheRoofCarriesTheStaticHeadAndSizingCountsIt`, `ValveSizerTests.TheChosenValveDropsWhatTheLawSaysItDrops` |
| Outer loop: size, re-lower, re-solve to a fixed point; a sized parameter is never promoted | `OuterLoop` | `OuterLoopTests.TheSimpleLoopReproducesTheWorkedExampleEndToEnd` | `.TheLoopSettlesInFarFewerPassesThanItsCap`, `.ASizedParameterIsNeverAlsoPromoted`, `.SizingNeverOverridesAStatedValue`, `CorpusStatusTests` |
| Every residual allocation-free and deterministic | all `EvaluateResiduals` | — (a property, not a physics claim) | `*.EvaluateResidualsAllocatesNothing`, `ResidualDeterminismTests.*` |

**Two rows are weaker than the rest, and are accepted for M2a.** Newton has no hand check on a
system built outside the graph — `EquationSystem` is constructed from a `CircuitGraph`, so a two-line
analytic system cannot be handed to it without a fake, and the plugin's rule against mocking owned
types applies; the simple loop's duty-implied flow is the closed form it is checked against instead,
and `DenseLu` carries the hand-built matrices. `BranchResistance` has no test of its own and is
checked through the pump and valve sizers, which are its only two callers besides the seed; a change
that broke it would fail `24`'s 51.67 kPa ring before anything else. The transient tank rows are
M4's and stay empty until `33` is built. The `Validation/` folder the layout above promises does not
exist: the validation cases live beside the class they exercise (`ConservationTests` under
`Solvers/`, the property oracles under `Fluids/`), and the case number (`V1`…) is cited in the
test's comment instead. That is a layout drift worth knowing about, not one worth a move.

## Assertion tolerances

Distinct from solver tolerances ([`36`](../30-solver/36-numerics-and-convergence.md)) — a test asserts
what a user would call correct, which is looser than what the solver converges to.

| Compared | Tolerance | Reason |
|---|---|---|
| Property vs reference | [`07`](../00-foundation/07-quality-attributes.md)'s validity matrix | One release source of truth |
| Solved state vs hand calculation | Capability row in `07` | Correlation accuracy dominates |
| Conservation laws | Absolute-plus-relative bounds in `07` | Remains meaningful near zero flow/duty |
| Two solvers | The solver tolerance | They should agree to convergence |
| Sized values | Exact for catalogue entries; 1 % for continuous | Discrete is discrete |
| Temperatures | Never `==` | [`36`](../30-solver/36-numerics-and-convergence.md)'s rule 2 |

## Golden files

| What | Why golden rather than asserted |
|---|---|
| Parse tree for each sample | Structure is large; a diff is the readable failure |
| Printer output | Round-trip is asserted; the golden catches formatting drift |
| Model contract JSON | Large, and its shape is the contract with three consumers |
| Diagnostic lists for broken samples | Codes, spans, and order all matter |
| Layout hints | Ordering is an invariant that a diff shows clearly |

Committed, reviewed on change. **A golden-file diff in a PR is a behaviour change and must be explained
in the PR description** — an unexplained one is a silent contract change.

## Property-based tests

For invariants that must hold over inputs nobody would think to enumerate:

| Invariant | Generator |
|---|---|
| `Parse` never throws | Random bytes, and mutations of the sample corpus |
| `Print(Parse(x)) == x` | Random valid scripts |
| Unit conversion round-trips | Random values × every unit |
| `Quantity` arithmetic preserves dimensions | Random quantity pairs |
| Graph counting balances | Random connected graphs |
| An edit changes only its span | Random edits on random scripts |
| Draft edits cannot mutate a run snapshot | Random edit sequences while a deterministic run advances |

FsCheck, or a hand-rolled generator if FsCheck's C# ergonomics prove awkward. The corpus-mutation fuzz
is worth more than the pure random generator here: mutations of real scripts hit near-valid inputs,
which is where a parser actually breaks.

## Frontend testing

| Layer | Tool | Covers |
|---|---|---|
| Unit | Vitest | Layout engine, unit formatting, frame reconstruction, colour scales; the design system's contrast, palette and literal scan (P5.3, `src/design/*.test.ts`) |
| Component | Vitest + Testing Library | Editor integration, hover, log reconciliation. P5.3's theme switch renders with `react-dom` and `act` alone; Testing Library comes in with the first component that needs queries by role |
| Visual | Playwright screenshots | Canvas rendering, both themes |
| End to end | Playwright | Type a script → see a diagram → edit on canvas → see the script change |

**Keystroke to visible diagnostic is a Playwright measurement, not a server-side one** (`D-48`). It is
the normative interactive gate in `07`, and it is the one budget that cannot be measured from either
end alone: a server-side timer misses the debounce, which measurement says is the dominant term, and a
client-side timer around `fetch` misses the decoration commit. The benchmark drives a real keypress,
waits for the diagnostic decoration for that edit to be present in the DOM, and records the interval —
against the M1 syntax tour and the 200-declaration reference script, both recorded as baselines.

Three component figures are recorded alongside it, because a regression in the sum is unactionable
without them: the debounce actually in force, the server's compile time, and the payload's
serialize-plus-parse cost. They are diagnostic, not gates — `07` states which one governs.

**As built (P5.5, 2026-09-18):** the benchmark is `frontend/e2e/latency.bench.ts` under
`playwright.config.ts`, run by `npm run bench`, never in a gate; the config starts the Api host and
the Vite dev server itself. The editor pane exposes a dev-only hook on `window` (`setText`, `text`,
`timings`, `debounceMs`) that the benchmark drives, because a synthesized keypress through
CodeMirror's contenteditable is the flakiest part of any editor benchmark and the hook dispatches the
same transaction a key does. It appends an unknown parameter to the first declaration, waits for the
error squiggle whose text starts with that parameter, and writes ten samples per script as median,
p95 and max to `diagnostics/keystroke-latency.md` with the debounce and the host's compile time
alongside. **Not yet run:** the environment the package was built in has no browser that can
launch (`U-4`), so the numbers are not recorded and the debounce is still `51`'s provisional
300 ms.

**The layout engine is unit-testable and must be**: given hints, assert placements. It is the most
algorithmically complex frontend code and the least suited to visual-only testing. The section below
states how.

### Layout verification

**Since `D-103` the prepared scene is Core's `layout` on the model contract, and every tier below is
a Core test** (`FluidScript.Core.Tests/Layout`), run by `dotnet test` and read through the layout
report; the frontend tiers reduce to "the renderer draws what it is given". The paragraphs below were
written when the scene was built in the browser; their targets and predicates are unchanged.

**The ladder gate (`D-107`, 2026-09-16).** While the layout rules are being established one step
at a time ([`29`](../20-core-domain/29-layout-ladder.md)), `LayoutLadderTests` is the layout gate:
for every `tests/FluidScript.Core.Tests/Layout/Ladder/step-NN-*.fluid` it writes the scene's SVG
and its `28` A10 text to `diagnostics/layout-ladder/` *first*, then asserts that every component is
placed and `SceneAudit` finds no hard finding. The seven layout samples' routing and audit
assertions in `LayoutSolverTests` run over `Reached`, the samples the ladder has drawn (the simple
loop, the substation, the cooling loop, the distribution header and, as of step 9, the storage
header, 2026-09-17), and
`LayoutTimingTests` is live since step 8 reached headers; the rest join `Reached` sample by sample.
A second ladder theory, `EveryStepSolvesAndSettles`, runs every step through the outer loop and
writes its `SolveExplanation` beside the picture: a script beginning `# fragment` is skipped, one
beginning `# does not settle: S-nn` is expected to stall until that defect closes, one beginning
`# does not bind: C-nn` is expected to be refused until that defect closes, and every other step
must settle -- a ladder script is a circuit the solver accepts before its picture is judged
(`C-91` and `S-63` were found this way). A sample carries the same kind of marker for the seed:
one whose first line begins `# does not seed: S-nn` is expected to seed a rated exchanger backwards
in `SolutionSeedTests` until that defect closes, and the test fails the moment it seeds forwards
with the marker still on (`S-64`, the syntax tour).

**The target is the prepared scene, not the SVG (`D-71`).** A placement reaches the DOM as a transform
string composed with the root Y-flip, a symbol's geometry lives inside a normalized unit box, and a
route is a `d` attribute; asking geometric questions of that requires the test to rebuild the
renderer's own transform stack, which either repeats its mistake and agrees or differs and fails on
correct output. The prepared scene ([`53`](../50-frontend/53-canvas-renderer.md)) is already the single
artefact the canvas and the exporter both consume, it carries resolved geometry, and it builds
headlessly — no DOM, no font, no browser.

Four tiers, with the assertions concentrated in the first:

| Tier | Input | Answers | Tool |
|---|---|---|---|
| 1 · Scene predicates | hints + graph → `PreparedScene` | Everything the layout engine decides | Vitest, headless |
| 2 · SVG geometry | rendered SVG, six references | Only what tier 1 cannot express: transform composition, the Y-flip, port anchors landing where layout thought, DOM/navigation order, export-vs-canvas identity | Vitest + jsdom |
| 3 · Screenshots | pixels, both themes | Stroke weights, fills, fonts, symbol shapes — appearance, not geometry | Playwright |
| 4 · Golden corpus | committed scenes for the six references | "A designer would draw it this way", frozen after human sign-off | Byte comparison |

**Tier 2 is deliberately small — roughly eight assertions.** It earns its place on exactly one class of
defect: correct geometry pushed through a wrong transform, which passes every scene predicate and
renders upside down. Every other question belongs one tier up, where a failure names the step instead
of the pipeline.

**Tier 3 stays small for the opposite reason.** A pixel diff says a diagram changed and never why, and
nobody reads one for a 200-component drawing; left as the primary gate it degrades into a blanket
re-baseline.

#### The predicate sweep

One loop: every predicate against every fixture. Adding a fixture tests it against all nineteen
predicates, and adding a predicate applies it to every fixture already there — which is what makes this
a sweep rather than a list of per-sample expectations that grows one assertion at a time.

| # | Predicate | Enforces | Where it stands (2026-09-18, P5.1d-3) |
|---|---|---|---|
| L1 | The scene is byte-identical across builds for one graph, hints and spacing | `53` inv 1, 10 | `LayoutPredicateTests` L1: the report, ten builds, byte for byte, on every fixture |
| L2 | Drawn edges are a bijection with graph edges, compared **port for port** | inv 4c | L2: every connection's route starts and ends on the port the connection names; the audit's H4 holds the ends |
| L3 | Symbol bounding boxes pairwise disjoint after mandatory collapse | inv 3 | The audit, H1 and H2 |
| L4 | Label boxes disjoint from each other, from non-owner symbols, and from non-leader routes | inv 3a | The scene carries a label anchor, not a box (`D-73` is the renderer's metric table): P5.7 |
| L5 | No placement contains a corner, computed **per point**: two incident run-ends on different axes | inv 4a, `D-44` | The audit, H3 and H6; `TheRingKeepsItsCornersBare` on the loop samples |
| L6 | Every junction element sits at its junction | inv 4b | By construction (`28` A6: every node is placed on its run); H4 holds its ends |
| L7 | Segments axis-aligned; none zero-length or reversing; bends within preference; length within its factor of Manhattan distance | inv 4e | L7: axis-aligned, no zero-length, no collinear or backtracking point; the length factor is the report's `length-ratio`, trended |
| L8 | `metrics.symbolCrossings` zero on samples and references | inv 3b | The audit, H3 |
| L9 | Components on a run in traversal order, none drawn between two directly connected | inv 4d | By construction (`28` A5: inline elements spread along their run in order) |
| L10 | Every component's orientation matches its kind's rule | inv 8 | L10: a `standing` kind turns 0 or 180, an `upright` kind 0, the rest freely (`28` A4, `D-108`) |
| L11 | Every arrow agrees with the sign of the solved flow | inv 9 | The renderer reads the arrow from the port flow on the wire: P5.7 |
| L12 | Every route endpoint coincides exactly with its port's anchor | inv 4 | The audit, H4 |
| L13 | Thermal-stage bands at monotonically increasing X, ranks consumed unchanged | `D-31` | Withdrawn as a scene predicate: the ladder engine (`D-107`) places by `28` C and H10 carries the direction heat takes; `D-31`'s ranks stay in the hints |
| L14 | Two spacing values change placements and change nothing Core computes | inv 1b, `D-37` | L14: the cooling loop at 0.5 and 1.0, boxes move, the solve report is byte for byte |
| L15 | No DOM key, selection key, or export id contains an equipment tag | inv 1c, `D-34` | P5.7 |
| L16 | `metrics.reflowIterations` under half of `D-72`'s cap | `D-72` | Withdrawn: there is no reflow under `D-107`; the engine is constructive and a form that cannot finish backtracks whole |
| L17 | No route segment is shared by a supply route and a return route | `53` H8, `D-100` | The audit, H7 |
| L18 | Header members with equal branch shapes have congruent relative geometry -- equal widths, aligned columns, equal rail distances | `53` *equivalent assemblies*, `D-100` | L18 on the 200-component header: eighteen branches of one kind sequence, one width, one height, members at the same places relative to their block (`BranchShapes` itself was withdrawn by `D-107`; the block is the shape) |
| L19 | Edit stability: adding an observer moves no process symbol; adding a component to a branch moves only that branch and what it pushes; an edit in one circuit leaves every other circuit identical up to translation | `53` *edit stability*, `D-100` | Three L19 tests: step 10 against step 4, a valve into the header's first branch, a valve into the second of two circuits |

**L2 is the one to keep if only one survives.** Every other predicate protects legibility; L2 protects
correctness, and its breach is the only one on this list that a reader cannot see. A scene can be
disjoint, corner-free, deterministic and beautifully routed while connecting the wrong ports.

**Fixtures.** Every sample script; the supported 200-component fixture (generated, not checked in:
`FluidScript.Fixtures.ReferenceModels.DistributionHeader(18)`, `01`'s header with eighteen pumped
consumers -- a file that size would be solved by every corpus test); and six ladder steps chosen
for their shapes (a valve on a loop, a ring with a branch, the mixed header, instruments, two
circuits, the tour's loops). The crowded fixture that was to force reflow went with L16; an
explicitly over-limit fixture is not built. Plus, still to build, **corpus mutation over the sample
scripts** — the same argument this document already makes for the parser fuzz applies here:
mutations of real scripts produce near-valid topologies, which is where a layout engine breaks, and a
pure random graph generator produces shapes no plant has.

Cost is not a concern at this scale. 200 components is 19,900 symbol pairs, and roughly 250 routes of
four segments against 200 boxes is about 200,000 segment-box tests — naive `O(n²)` in JavaScript, well
under a second. No spatial index.

#### The layout report

`SolveExplanation` is what made the solver debuggable from a terminal, and the layout engine gets the
same instrument (`D-100`). **It is one text, `28` A10's, and it is `SceneText` in Core** (`D-108`
item 4; the move from Core.Tests is `C-89`): every component with its group, transform, arrangement,
inner and outer boxes and each port's inner anchor, outer anchor and flow vector; every route with
its points, length, bends, crossings, hops and envelope; the groups; a character raster of the
arrangement; every hard constraint and soft class of `28` B with its count; every finding. The
ladder writes it per step and the sample gates per sample to `diagnostics/`, *before* any
assertion, and the experiment protocol in `CLAUDE.md` applies to it unchanged: run the fixture,
read the whole text, change only what it supports. It is the reason a session with no canvas can
look at a layout, and **a session checks a layout from this text and never from the SVG or a PNG**
-- the afternoon P5.1d-2 spent reading rendered pictures is recorded in `20`'s observations as the
drift this sentence exists to stop. Shipped 2026-09-18: `SceneText` is in Core with the raster and
the metrics, and `C-89` is closed.

The earlier `LayoutExplanation` -- a second, columnar text with the `L1`–`L19` verdicts -- is
withdrawn as a separate artefact: the verdicts are `28` B's constraints and the audit's counts in
the same text, and the raster the columnar form was to carry is A10's. Its form stays
`SolveExplanation`'s where a section is a table (one line per element, names in a header), and the
raster is the one part that is a picture rather than a table.

#### Metrics, which are trended rather than gated

The report carries them per fixture: the crossing count, `length-ratio` (the pipes' length over
their ends' Manhattan distance), `area-utilisation` (symbol area over the extent's) and `aspect`.
`symbolCrossings` is H3 and hard; `labelCollisions` and `reflowIterations` wait on P5.7 and went
with L16. `diagnostics/` is regenerated rather than committed, so a number that moves shows in the
ladder's log when a step is redrawn, not in a diff; a committed metrics file is not built.

**This is the part that answers "is the diagram any good", and the honest answer is that it cannot be
asserted.** What can be done is to make degradation visible: a refactor that raises mean route length
by 40 % or drops area utilisation by half has made every diagram worse, breaks no invariant, and shows
up as a committed number moving. Nobody diffs screenshots; everybody notices a number.

#### The golden corpus, and the human in it

Predicates reach what can be named. "A designer would not draw it that way" cannot be named, and it is
the failure that makes a generated diagram look generated.

So the six reference circuits' prepared scenes are rendered, **looked at once by a person**, and
committed as goldens; thereafter they are guarded byte for byte and a legitimate change requires a
fresh look rather than a re-baseline. That review is a scheduled step in P5.7, not an informal one —
it is the only instrument that reaches the undecidable half, and it costs an afternoon at the single
moment when it is cheap.

The storage-header layout fixture asserts source/storage/consumer X bands, parallel stacking, tank
port elevation anchors, and stable placement across reversed transient flows. A screenshot alone is
insufficient: the unit test compares stage ranks and coordinates, while the visual test checks routes.

The same Core-to-canvas golden suite owns all three protected thermal-stage shapes from `25`: the cooling loop is
one rank-0 Neutral stage; the substation orders its source circuit, exchanger Conversion, then heating
circuit; and the storage header uses Source/Storage/Consumer ranks 0/1/2 with equal-rank parallel
groups. Each fixture compares serialized roles,
ranks, component order, prepared-scene X bands, and export output across 100 repeated builds. A solved
flow or duty reversal may change arrows and state, but must leave every stage byte-identical (`D-31`).

The supported 200-component fixture applies mandatory initial collapse and asserts pairwise-disjoint
symbol bounds after deterministic reflow. A separate explicitly over-limit fixture is allowed to
overlap only when the scene emits `FS5001` and sets `degraded: true`; injecting the same overlap into a
supported fixture must fail the renderer invariant test rather than pass as degraded.

**One end-to-end test carries disproportionate weight** — the write-back round trip
([`54-interaction-and-writeback`](../50-frontend/54-interaction-and-writeback.md)) crosses every layer,
and if it works, most of the system works.

### Isolation, worker, file, and accessibility tests

- Use deterministic barriers to hold a backend transient worker between frames while draft compile,
  edit, Save, and Open actions execute. Assert the run's snapshot hash, sequence, and worker lifetime
  do not change.
- Record backend thread ids and browser worker/main-thread markers. Fail if integration, frame decode,
  delta reconstruction, colour mapping, layout, routing, or geometry preparation executes on the UI
  thread. Measure DOM commits and long tasks against `07` rather than inferring responsiveness from fps.
- Fault-inject snapshot/checksum/base-frame mismatch, NaN, worker exit, watchdog expiry, channel
  overflow, and frontend worker crash. Each must stop the run, retain the last verified frame, report
  one stable reason, and leave no worker alive.
- Exercise every input/resource limit at limit−1, limit, and limit+1, including long transient frame
  retention and checkpoint compaction.
- Run `18` compatibility fixtures for current, migratable-old, unsupported-future, missing-version,
  and unavailable-catalogue files. Save/recovery/conflict tests run through both File System Access and
  upload/download paths with storage and permission failures injected.
- Playwright runs axe plus keyboard-only, screen-reader smoke, 200% zoom, reduced-motion, focus order,
  and non-colour-cue scenarios. The structured canvas table must expose the same state, provenance, and
  diagnostics as pointer hover.

## Invariants

1. `Category=Unit` completes in under two seconds of execution — the reported duration minus the
   empty-run floor measured on the same machine.
2. Every governing equation has a unit test with hand-computed values.
3. Every validation case V1–V17 runs against the real property backend where applicable; V4, V5,
   V13, V14, V15, and V17 use an independent published, analytic, or separately tabulated oracle,
   never production-backend/solver output as expected data.
4. No test asserts an absolute enthalpy against a textbook value
   ([`21-fluid-and-state`](../20-core-domain/21-fluid-and-state.md) — reference states differ).
5. Every golden file's initial value is justified by a hand-check or a validation case.
6. No test depends on execution order or shared mutable state.
7. Every diagnostic code has a test that triggers exactly it.
8. Test names state scenario and expected result.

## Error cases

| Situation | Handling |
|---|---|
| A test needs the network | Not allowed. Fixtures are committed |
| A test is flaky | Deleted or fixed within one working session — never retried into passing |
| A validation case fails after a change | Blocks the change. These encode reality, not decisions |
| A golden file differs | Reviewed and explained, or the change is wrong |
| A test takes > 5 s | Moved out of `Unit`, and justified |

## Worked example

The M2 demo circuit, at three levels:

**Unit** — the heat exchanger's energy balance, fake backend:

```csharp
[Fact]
[Trait("Category", "Unit")]
public void EvaluateResiduals_StatedPowerAndTemperatures_ImpliesExpectedMassFlow()
{
    var hx = new HeatExchanger(power: Power.FromKilowatts(30),
                               inlet: Temperature.FromCelsius(20),
                               outlet: Temperature.FromCelsius(50));
    // 30 000 W / (4182 J/(kg K) x 30 K) = 0.23912 kg/s, with FakeWater's declared cp.
    var context = SolveContext.ForSingleComponent(FakeWater.Instance, massFlow: 0.2391);

    Span<double> residuals = stackalloc double[hx.EquationCount];
    hx.EvaluateResiduals(context, residuals);

    Assert.Equal(0.0, residuals[0], tolerance: 1e-6);
}
```

0.2391 kg/s is hand-computed from Q/(cp·ΔT) = 30 000 / (4182 × 30) = 0.23912, stated in the test's
comment, using **`FakeWater`'s own cp of 4182** — the value the fake declares, so the test's arithmetic
and the code under test agree exactly. It runs in microseconds because `FakeWater` is arithmetic.

**The three figures for this one duty are all correct and must not be reconciled**, which is worth a
comment in the test so nobody "fixes" it:

| Where | cp used | Flow | Why |
|---|---|---|---|
| This unit test | 4182 (the fake's constant) | 0.2391 kg/s | Asserts the *equation*, against a fake with declared properties |
| [`22`](../20-core-domain/22-component-model.md) | enthalpy difference, real backend | 0.2392 kg/s | The physical answer |
| [`14`](../10-language/14-expressions-and-references.md) | 4180 (written in the script) | 0.2392 kg/s | An expression uses the number the user wrote |

A unit test that asserted 0.2392 against `FakeWater` would be asserting the real backend's answer
against a fake that cannot produce it — which is exactly the confusion between "did I write what I
meant" and "is it physically true" that the table above this section exists to prevent.

**Validation** — V2 and V3 on the whole circuit, real backend:

```csharp
[Fact]
[Trait("Category", "Validation")]
public void CoolingLoop_Solved_ConservesEnergyAndClosesEveryLoop()
{
    var solution = Solve(Fixtures.Script("m2-cooling-loop.fluid"));

    Assert.True(solution.Converged);
    AssertEnergyBalance(solution, relativeTolerance: 1e-6);       // V2
    AssertLoopClosure(solution, relativeTolerance: 1e-6);         // V3
    AssertMassBalance(solution, relativeTolerance: 1e-9);         // V1
}
```

This asserts nothing about specific numbers — it asserts that the answer is *physically consistent*,
which is true of the right answer and of no wrong one produced by a sign error, a unit slip, or a
dropped term.

**Golden** — the model contract:

```csharp
[Fact]
[Trait("Category", "Golden")]
public Task CoolingLoop_Serialized_MatchesContract()
    => Verify(Serialize(Solve(Fixtures.Script("m2-cooling-loop.fluid"))));
```

One line, and it pins every field of the payload three consumers depend on. Its initial value is
justified by the validation case above having passed on the same circuit — invariant 5 satisfied.

Three tests, three different questions: *did I write what I meant*, *is it physically true*, *did it
change*. All three are needed, and confusing them is how a project accumulates a thousand tests that
prove one of the three.

## Acceptance criteria

- [ ] `dotnet test --filter-trait Category=Unit` runs in under two seconds of execution, measured
      against a same-session empty-filter run rather than as raw wall clock.
- [ ] `dotnet test` runs everything with no arguments (`R-17`).
- [ ] Keystroke to visible diagnostic is measured through the browser and meets `D-48` on both the
      syntax tour and the 200-declaration reference script, with the debounce, compile and payload
      components recorded beside it.
- [ ] V1–V17 are present and pass against the applicable real backend and independent oracle.
- [ ] Every governing equation has a hand-checked unit test. — The M2a rows are audited above; two are
      accepted weaker, the transient tank rows wait for M4.
- [ ] Every diagnostic code has a triggering test.
- [ ] The fuzz corpus produces no exception from any pipeline stage.
- [ ] The end-to-end write-back test passes in Playwright.
- [ ] All nineteen layout predicates run against every fixture in the sweep, and adding a fixture
      requires no new assertion.
- [ ] The prepared scene builds with no DOM, no font and no browser, and is byte-identical across 100
      builds (`D-71`).
- [ ] `SceneMetrics` is recorded and committed per fixture, and a deliberate layout regression that
      breaks no invariant is visible as a metric moving.
- [ ] The six reference goldens are signed off by a person before they are committed, and a change to
      any of them fails until re-reviewed.
- [ ] No test performs network I/O — asserted by running with networking disabled.
- [ ] Golden files exist for every sample's parse tree, printer output, model contract, and diagnostics.
- [ ] All `07` resource/performance boundaries and stop conditions have boundary or fault-injection tests.
- [ ] Draft edit/save/open actions run concurrently with a transient without changing its snapshot.
- [ ] Worker instrumentation and accessibility scenarios above pass in the reference environment.

## Open questions

None. Coverage percentage is reported but is not a gate; validation, diagnostic, and boundary-case
completeness are the gates. Property expected values are transcribed with citations from independent
IAPWS releases and ASHRAE reference examples, reviewed once against a second independent calculation,
and never generated by SharpProp/CoolProp. Short deterministic transient cases run on every change;
the full 600-s V8/V9 and resource-soak cases run nightly and before release.

## Pipeline timings

`PipelineTimingDiagnostics` (`Category=Diagnostic`) writes `diagnostics/pipeline-timings.md`: parse,
bind, lower and solve per sample, and inside one Newton step the cost of a residual evaluation, the
`N+1` of them a finite-difference Jacobian needs, and a dense LU of the same order. It runs the whole
corpus twice, once on constant properties and once on the real backend, because the difference between
those two columns is the answer to almost every performance question this project has.

**Its run counts are set by the property call, not by the stopwatch.** One real `Water` state costs
about 2.2 ms (`fluid-state-timings.md`), so a residual evaluation costs milliseconds. A first version
used 2000 evaluations per sample per substance — sized for a microsecond call — and was killed by the
test timeout twice before anyone did the arithmetic. It now takes 5.5 s for the corpus. **A benchmark
whose loop counts were not derived from a measured per-call cost is a benchmark that has not been
sized**, and the failure mode is a timeout that looks like a hang.
