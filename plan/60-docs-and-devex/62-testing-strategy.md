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
| `Category=Unit` | < 2 s total | Everything with a fake property backend | Constantly |
| `Category=Property` | < 20 s | Real SharpProp property accuracy | Pre-commit |
| `Category=Validation` | < 60 s | Physical validation cases | Pre-commit |
| `Category=Golden` | < 10 s | Parser, printer, model-contract snapshots | Pre-commit |
| `Category=Api` | < 30 s | Endpoint and contract tests | Pre-commit |

`dotnet test --filter Category=Unit` must stay under two seconds. That is what makes tests something
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

**V8 is the strongest test in the suite.** Two solvers sharing no numerical code arriving at the same
answer is evidence neither is systematically wrong, which no amount of unit testing provides. It is
also the test most likely to be skipped as slow, and it must not be.

**V11 is the one that catches a discretization bug.** A scheme with a sign error can look plausible at
`nodes=4` and diverge as the mesh refines; asserting that refinement *converges* catches it.

**V15–V17 keep `D-32`'s tank model honest.** V15 has a closed form, V16 checks the conservative finite-
volume accounting with simultaneous branches, and V17 checks both the layer-order algorithm and the
claim that more layers improve rather than merely change the answer. The V17 reference table is
generated by an independently reviewed control-volume calculation, not by the production solver.

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
| Unit | Vitest | Layout engine, unit formatting, frame reconstruction, colour scales |
| Component | Vitest + Testing Library | Editor integration, hover, log reconciliation |
| Visual | Playwright screenshots | Canvas rendering, both themes |
| End to end | Playwright | Type a script → see a diagram → edit on canvas → see the script change |

**The layout engine is unit-testable and must be**: given hints, assert placements. It is the most
algorithmically complex frontend code and the least suited to visual-only testing.

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

1. `Category=Unit` completes in under two seconds.
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
| [`22`](../20-core-domain/22-component-model.md) | enthalpy difference, real backend | 0.2394 kg/s | The physical answer |
| [`14`](../10-language/14-expressions-and-references.md) | 4180 (written in the script) | 0.2392 kg/s | An expression uses the number the user wrote |

A unit test that asserted 0.2394 against `FakeWater` would be asserting the real backend's answer
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

- [ ] `dotnet test --filter Category=Unit` runs in under two seconds.
- [ ] `dotnet test` runs everything with no arguments (`R-17`).
- [ ] V1–V17 are present and pass against the applicable real backend and independent oracle.
- [ ] Every governing equation has a hand-checked unit test.
- [ ] Every diagnostic code has a triggering test.
- [ ] The fuzz corpus produces no exception from any pipeline stage.
- [ ] The end-to-end write-back test passes in Playwright.
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
