# FluidScript abstraction architecture review

Reviewed the complete current C# solution on 2026-09-05 using three disjoint dotnet-toolkit review-agent scopes. This is an architecture and abstraction analysis, not a request to modify source. It evaluates concrete duplication, responsibility boundaries, substitution needs, and whether inheritance or composition actually pays for itself.

## Executive conclusion

FluidScript does **not** lack abstraction completely. It already has several sound seams (`ICatalog<TSpec>`, `ISubstance`, `IComponentRegistry`, `IComponentFactory`, `IBoreLookup`, `SyntaxNode`, and the separate solver layout types). The main architectural weakness is different: several large orchestration classes retain too many responsibilities, while a few stable data/policy shapes are repeated across implementations.

Recommended outcome:

- **8 abstractions/refactorings should be introduced now.** Five are responsibility extractions, two are shared value/helper abstractions, and one narrows an over-general solver contract.
- **4 abstractions should be deferred** until a second implementation or measurable variation exists.
- **16 tempting abstractions should be avoided** because the existing composition is already sufficient or inheritance would hide domain distinctions.

The dotnet-toolkit standards do not recommend interfaces or base classes by default. They say an abstraction earns its cost through real duplication, substitution, or testability; two duplicate blocks are a judgment call, while three or more normally justify extraction. They also prefer composition over inheritance unless the types are genuinely substitutable.

## Workspace verdict

`FluidScript.slnx` was fully loaded: 192 indexed C# files, 413 types, five projects, and no workspace failures. Semantic callers and type relationships were therefore trusted.

## The pipe-catalog question

### Decision: keep the current generic composition

The premise that the two pipe catalogs have no common abstraction is not accurate in the current code:

- `CopperEn1057` at `src/FluidScript.Core/Catalogs/CopperEn1057.cs:25` and `SteelEn10255` at `src/FluidScript.Core/Catalogs/SteelEn10255.cs:26` are static data/provenance providers.
- Both publish `ICatalog<PipeSpec>` instances at `CopperEn1057.cs:70` and `SteelEn10255.cs:148`.
- Shared selection, validation, indexing, and catalog behavior lives in `Catalog<TSpec>` at `Catalog.cs:93`.
- `PipeCatalogs.Default` and `PipeCatalogs.All` traffic in `ICatalog<PipeSpec>` at `PipeCatalogs.cs:35-38`.
- `CatalogBoreLookup` accepts `ICatalog<PipeSpec>` at `PipeCatalogs.cs:112`.
- The planned contract in `plan/20-core-domain/27-component-catalog.md` is also `ICatalog<TSpec>`, so implementation and plan agree.

The remaining duplicated-looking code is catalog **data construction**, and its axes differ: copper uses OD/wall tuples with outside-diameter identity; steel includes DN and generates DN designations using nominal-size identity. That is data-policy variation, not shared runtime behavior.

Do not add `IPipeCatalog`: it would merely rename `ICatalog<PipeSpec>` without narrowing or adding a needed capability. Do not add `PipeCatalogBase`: the static providers are not polymorphic objects, and inheritance would couple distinct source/provenance mapping policies. Continue composing `Catalog<PipeSpec>`.

## A — introduce now

### 1. Split `BindingRun` into composed binding phases

- **Severity/aspect:** 🟡 `[correctness/architecture]`
- **Evidence:** `BindingRun` begins at `src/FluidScript.Core/Binding/Binder.cs:64` and spans that file plus `BindingRun.Components.cs`, `BindingRun.Curves.cs`, and `BindingRun.Topology.cs`: over 2,500 lines and 129 members. `Execute` at `Binder.cs:86` coordinates declaration collection, evaluation, component review, curve processing, topology binding, and model publication through shared mutable fields.
- **Why now:** Partial files improve navigation but provide no ownership boundary; every phase can mutate every piece of run state. `Binder.Bind` is the sole caller, so the public blast radius is small.
- **Shape:** Keep `BindingRun` as an orchestrator. Introduce an internal `BindingContext` and concrete `DeclarationBindingPhase`, `CurveBindingPhase`, and `TopologyBindingPhase`. Do not add phase interfaces until substitution exists.
- **Migration:** Introduce the context; extract topology first, then curves, then declaration/component work; retain final `SemanticModel` assembly in the orchestrator.
- **Verification:** Existing binder, curve, topology, reference-circuit, and property tests should remain unchanged.

### 2. Split `ComponentRegistry` data, validation, and runtime lookup

- **Severity/aspect:** 🟡 `[correctness/architecture]`
- **Evidence:** `ComponentRegistry` at `src/FluidScript.Core/Language/ComponentRegistry.cs:66` is an 869-line sealed class. Runtime lookup/indexing begins around line 91, invariant validation at line 149, and built-in component declarations/helper DSL at line 297.
- **Why now:** Built-in metadata, invariant policy, and runtime resolution change for different reasons but currently share one type.
- **Shape:** `BuiltInComponentKinds.Create()` owns declarations; `ComponentRegistryValidator.Validate(...)` owns invariants; `ComponentRegistry` retains immutable indexing and the existing public `IComponentRegistry` surface. Use concrete internal helpers, not new interfaces or a component-kind hierarchy.
- **Migration:** Extract the validator, then built-in construction, leaving indexing/resolution in the registry.
- **Verification:** Preserve `ComponentRegistryTests` and `RegistryMatchesTheComponentModelTests`; add direct validator cases if internals are exposed to tests.

### 3. Centralize normalized alias-index construction

- **Severity/aspect:** 🔵 `[cleanup]`
- **Evidence:** The same canonical-name/alias normalization and immutable-dictionary construction occurs in `ComponentRegistry.BuildIndex` (`ComponentRegistry.cs:126`), `CircuitRoleRegistry.BuildIndex` (`CircuitRoleRegistry.cs:88`), and `ScheduleRoleRegistry.BuildIndex` (`ScheduleRoleRegistry.cs:79`).
- **Why now:** Three copies meet the toolkit threshold, and normalization/collision behavior can drift.
- **Shape:** Add an internal generic `NameResolution.BuildIndex<T>(entries, canonicalName, aliases)` with explicit ordinal comparison and duplicate behavior. Keep each registry's distinct `Resolve` policy local.
- **Migration:** Add focused helper tests, migrate the two role registries, then the component registry.

### 4. Split `WellPosedness` behind its existing façade

- **Severity/aspect:** 🟡 `[correctness/architecture]`
- **Evidence:** `src/FluidScript.Core/Topology/WellPosedness.cs:44` defines a 1,239-line static class combining relation construction (`Count` at line 92 and `Relations` at 223), constraint ownership/promotion (`Constraints` at 370, `Promote` at 537), and diagnostic analysis (`ReportDatums` at 674 through balance reporting near 1187). `Check` at line 64 has 20 callers/21 call sites and orchestrates 17 callees.
- **Why now:** Counting, promotion, and diagnostics are independently changing policies hidden behind one façade.
- **Shape:** Preserve `WellPosedness.Check`. Extract concrete internal `EquationCounter`, `ConstraintPromotion`, and `TopologyDiagnostics` collaborators with explicit immutable inputs. Do not introduce interfaces yet.
- **Migration:** Extract relation/counting logic, then promotion, then reporting; retain `Check` as orchestration.
- **Verification:** Add focused tests for each pure group and equivalence tests for deterministic diagnostics across representative graphs.

### 5. Compose the repeated component-parameter triplet

- **Severity/aspect:** 🔵 `[cleanup]`
- **Evidence:** `StatedParameters`, `SizedParameters`, and `DefaultParameters` are repeated across eight component implementations: `Pipe.cs:87`, `Pump.cs:113`, `Tank.cs:130`, `Valve.cs:51`, `Valve.cs:226` (`ThreeWayValve`), `CircuitNode.cs:84`, `PlacedSensor.cs:63`, and `HeatExchanger.cs:96`. Their consumers overlap across component creation, lowering, sizing, seed construction, and well-posedness.
- **Why now:** Twenty-four repeated declarations carry one stable domain concept. A base class would impose inheritance merely to remove storage boilerplate.
- **Shape:** Prefer an immutable composed value such as `ComponentParameters(Stated, Sized, Defaults)` with a shared `Empty` value, exposed as `IComponent.Parameters`.
- **Migration:** Add the value object; add `Parameters`; migrate all eight components and consumers; remove the flattened members before compatibility obligations form.
- **Verification:** Test empty state, precedence categories, factory population, observer output, and unchanged solve/residual determinism.

### 6. Narrow `ISolver` to the steady-state contract it actually represents

- **Severity/aspect:** 🟡 `[correctness/architecture]`
- **Evidence:** `ISolver` at `src/FluidScript.Core/Solvers/ISolver.cs:98` claims to cover Newton, transient integration, and optimization, but `SolveAsync` requires `EquationSystem` and `StateVector` (`:121-125`). `SolveResult` contains residual norm, worst equations, and iteration count (`:57-90`); `SolveProgress` exposes a line-search step (`:50-54`). It has only one implementation, `NewtonSolver`, and no interface consumer yet.
- **Why now:** The abstraction is broader in name than in semantics. Transient and optimization implementations would have to overload steady-state concepts.
- **Shape:** Rename/narrow to `ISteadyStateSolver`, with `SteadySolveResult` and `SteadySolveProgress` if these public records remain. Do not introduce a universal solver base contract.
- **Migration:** Rename the contract/results; update `NewtonSolver`; make the P3.7 selector depend on the narrow interface; define independent transient/optimization contracts when implemented.
- **Verification:** Preserve Newton tests and add selector compatibility tests around `CanSolve`.

### 7. Extract `EquationSystemBuilder` from runtime evaluation

- **Severity/aspect:** 🟡 `[correctness/architecture]`
- **Evidence:** `EquationSystem` at `src/FluidScript.Core/Solvers/EquationSystem.cs:38` spans about 963 lines and 48 members. `Build` and construction helpers occupy roughly lines 205-503; hot runtime residual evaluation/cache/state logic begins at `TryEvaluateResiduals` around line 514 and continues to the end.
- **Why now:** Construction/resolution and repeated evaluation have different lifecycles. `Build` currently has only two test-helper callers, while evaluation has ten callers, so extraction can preserve the hot API.
- **Shape:** Add an internal `EquationSystemBuilder` that produces completed mappings, layouts, buffers, and scales. Keep `EquationSystem` as the assembled immutable description plus evaluator/cache owner. Retain `EquationSystem.Build` as a forwarding façade initially.
- **Migration:** Move pure build helpers/data; add an internal completed-build constructor; preserve the façade until P3.7 settles.
- **Verification:** Assert identical layouts, scales, dropped rows, and initial residuals across every sample.

### 8. Consolidate solver test lowering/assembly fixtures

- **Severity/aspect:** 🔵 `[cleanup]`
- **Evidence:** Similar source-reading and graph-lowering helpers occur in `PortMapTests.cs:219`, `SolutionSeedTests.cs:219`, `SystemLayoutTests.cs:104`, `EquationLayoutTests.cs:237`, `ResidualScalesTests.cs:162`, and `EquationRowReconciliationTests.cs:98`; related assembly pipelines occur in `NewtonSolverTests.cs:260` and `EquationSystemTests.cs:291`. The six lowering helpers serve 23 test callers and the assembly helpers another 19.
- **Why now:** The copies have already diverged in accepted inputs and returned auxiliary data.
- **Shape:** Add test-only `SolverFixture` operations such as `ReadSampleOrSource`, `LowerGraph`, and `LowerAndCheck`. Keep scenario-specific seeds local.
- **Migration:** Extract source resolution/lowering, migrate exact copies, then inline-or-sample variants. Do not merge the real sizing seed with deliberately at-rest test seeds.
- **Verification:** Add direct sample-path and inline-script equivalence checks; the existing solver suite verifies migration.

## B — defer until there is evidence

### 1. `IRefrigerant`

`VapourCompressionCycle` accepts concrete `Refrigerant` at `VapourCompressionCycle.cs:145,203,237,309`. A cycle-specific contract may eventually be useful because `SaturationEnthalpies` is not part of `ISubstance`, but today there is one implementation and no deterministic fake. Do not widen to `ISubstance`, which would admit substances lacking the needed capability. Introduce `IRefrigerant : ISubstance` only when a second implementation or cycle fake is required, and include only capabilities the cycle actually consumes.

### 2. Per-component builder strategies

`ComponentFactory.Create` at `src/FluidScript.Core/Topology/ComponentFactory.cs:104-136` switches over six closed, centrally registered component kinds. Six `IComponentBuilder` classes would presently redistribute a cohesive switch and duplicate keyword ownership. Revisit when external component registration exists or one construction policy changes independently.

### 3. `ILinearSystemSolver`

`NewtonSolver.cs:155` directly calls `DenseLu.Factor`; there is one production caller and current systems are documented as small. Introduce a strategy only when a sparse implementation exists or measurements cross the dense-LU size budget. Keep pivot details out of any future interface.

### 4. Shared timing-statistics helper

`Microseconds`, `Median`, and `StandardDeviation` are duplicated only in `StateTimingDiagnostics.cs:150-170` and `BackendPairDiagnostics.cs:662-679`. Two copies are a judgment call. Extract `TimingStatistics` when a third diagnostic appears or definitions begin to diverge.

## C — avoid or preserve existing seams

| Candidate | Decision |
|---|---|
| `IPipeCatalog` / `PipeCatalogBase` | Avoid; `ICatalog<PipeSpec>` + `Catalog<PipeSpec>` already provides the shared contract and implementation. |
| Wider `IBoreLookup` | Avoid; its one-operation lowering seam is intentionally narrower than catalog selection. |
| `FlowComponentBase` | Avoid; components share parameter state, not meaningful overridable behavior. Use composition via `ComponentParameters`. |
| Replace `SubstanceBase` | Preserve; five genuine substance subtypes share template behavior for pressure conversion, range diagnostics, formatting, and unsupported operations. |
| Split `Lowering.Build` by size | Avoid until a distinct policy appears; it already encapsulates one stateful lowering pass. |
| Generic `INameResolver<T>` | Avoid; only mechanical index building is shared, while ambiguity/resolution policies differ. |
| Syntax visitor hierarchy | Defer/avoid; `SyntaxNode` already supplies the cross-cutting token seam, and two concrete switches do not justify a 42-node visitor surface. |
| `IDiagnosticProvider` discovery | Avoid; the explicit diagnostic-family catalog is a compile-time ownership list guarded by tests. |
| Wider `IComponentRegistry` or registry base | Avoid; the existing two-member consumer-facing interface is appropriately small. |
| Generic `StageResult<T>` | Avoid; the implementation sequence already records that stages are not consumed uniformly and payload names carry domain meaning. |
| Universal `ISolver` base | Avoid; steady, transient, and optimization workflows do not share a useful result/input contract. |
| `INonlinearSystem` around `EquationSystem` | Avoid until a second algorithmic consumer or test-double need exists. |
| Generic `ILayout` | Avoid; `SystemLayout` columns and `EquationLayout` rows are different domains whose separation prevents interchange. |
| Generic scale provider | Avoid; `UnknownScales` and `ResidualScales` have different owners and physics. |
| Flag-driven residual evaluator | Avoid; distinct raw/scaled and full/single-column methods encode important unit and cache semantics. |
| Documentation-generator base classes | Avoid; generators already compose through `GeneratedRegion`, while their rendering policies differ. |
| Repository/filesystem interfaces for test fixtures | Avoid; these are intentional real-checkout invariant tests, not unit-test dependencies. |
| API service/repository interfaces now | Avoid until P5.2 introduces actual compile/validate/solve/session behavior. |

## Suggested implementation order

1. Narrow `ISolver` before P3.7 adds consumers to the overly broad name.
2. Extract `EquationSystemBuilder` before the sizing outer loop adds another construction caller.
3. Introduce `ComponentParameters` before more component kinds repeat the triplet.
4. Split `WellPosedness` while preserving its façade.
5. Add the shared registry index builder, then split `ComponentRegistry` internals.
6. Extract `BindingRun` incrementally, topology first.
7. Consolidate solver test fixtures after production seams settle.

This sequence favors changes whose future blast radius is currently smallest. It does not require public inheritance hierarchies.

## Totals and standards coverage

- Category A — introduce now: **8**
- Category B — defer: **4**
- Category C — avoid/preserve: **16**
- Review severities for actionable A/B candidates: **5 🟡 architecture warnings**, **7 🔵 cleanup/design suggestions**, **0 🔴 bugs**. These are maintainability findings, not current runtime failures.

All agents loaded the dotnet-toolkit core standards plus `architecture`, `api-design`, and `testing`; applicable scopes also consulted `performance`, `concurrency`, and `error-handling`. `resource-management` was not triggered. No C# source was edited.

## Report-path note

The report is stored under `.claude/dotnet-toolkit/review/`. This directory was previously confirmed not to be covered by the expected dotnet-toolkit ignore rule, so avoid committing it accidentally until the installation is refreshed.
