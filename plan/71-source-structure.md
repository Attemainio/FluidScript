---
id: 71-source-structure
title: Source structure and abstraction plan
tier: plan
status: draft
owns: [the folder and namespace layout of FluidScript.Core and its test mirror, when a class is split into partial files and when those files get a folder of their own, the one-type-per-file rule, how base and derived types are named, which component and sizer families share an abstract base, the order the restructure is taken in]
depends_on: [03-repository-layout, 04-engineering-standards, 06-decision-log, 08-implementation-sequence, 70-core-refactoring, 22-component-model, 24-auto-sizing, 27-component-catalog, 62-testing-strategy]
traces_to: [R-16, R-17]
open_questions: 0
last_review_pass: 0
---

# Source structure and abstraction plan

## Purpose

**Status (2026-09-23).** Decided (`D-147`, `D-148`). **Shipped: S0–S5, all six packages.** Ran before
P6.3, the user's call.

`FluidScript.Core` is 204 files and 58 400 lines in **sixteen flat folders**. Only `Syntax/Ast` has a
subfolder; `Diagnostics/` holds 28 files and `Solvers/` 27 side by side, so a folder listing says what
files exist and nothing about how they relate. There is **one** abstract class in the assembly
(`SubstanceBase`, declared inside `Water.cs`), while seven components re-declare the same eleven
members. Thirteen files are over 1000 lines, and thirty hold more than one type — `ModelContract.cs`
holds forty-one.

`03` already said where some of this should be: its tree puts the lexer, parser, AST, binder and
printer under `Language/`. The code grew `Syntax/` and `Binding/` beside it instead. So part of this
plan is not new structure but the structure that was decided and drifted.

This document says what the tree becomes, what the conventions are, which families get a base class
and which do not, what it costs in lines, and in what order it is done so that behaviour never moves.

## Responsibilities

**Owns.** The target tree; the namespace rule; the partial-file rule; one type per file; base and
derived naming; the `ComponentBase`, `ValveComponentBase` and `SizerBase<TComponent>` shapes and the
pipe-catalogue builder; the package order; the line budget.

**Does not own.** What any component computes (`22`), what a sizer chooses (`24`), what a catalogue
row contains (`27`), the seams `70` rewrote (`D-130`, `D-131`) — a package here moves and splits
those, never changes them.

## Where the conventions come from

The user's PandaAI repository, `docs/folder-structure-guidelines-core.md` and its `Utilities/`
tree, read 2026-09-23, is the model. Four patterns carry it:

1. **Domain folders, recursively.** `Solvers/` → `Base/`, `Evolutionary/`, `Random/`, `SolverPool/`.
   No top-level `Interfaces/`, `Enums/` or `BaseClasses/` buckets.
2. **A large class is one class across dot-named partial files** — `SolverBase.Execution.cs`,
   `ValueIndicator.Arithmetic.cs` — and in the code those files sit in a folder named after the class.
3. **Layered abstraction where implementations share behaviour:** `IIndicator` → `IndicatorBase<T>` →
   `BitMaskIndicator<T>` → `ValueIndicator<T>` / `BooleanIndicator`.
4. **The derived type carries the family's name:** `EvolutionarySolver : SolverBase`.

Where the PandaAI guideline and the PandaAI code disagree, the user chose (2026-09-23), and the
choice is recorded in `D-147`:

| Question | Chosen | Why |
|---|---|---|
| Namespaces | **Mirror the folders**, to any depth | The `dotnet-toolkit` naming standard (`naming.md`, *Namespaces & folders*) says so, and reviewers check against it. PandaAI keeps one namespace per domain root; that is what its code does and what the standard would flag. FluidScript already mirrors today, one to one. |
| A large class's partials | **A folder named after the class once it has three or more partials**; siblings below that | PandaAI's code (`ValueIndicator/`, `SolverBase/`, `KlineSeries/`). Its guideline says "a CLASS, not a folder", which the code does not follow. |
| Base-class naming | **Suffix: `ComponentBase`** | PandaAI's own `IndicatorBase`, `SolverBase`, `TimeSeriesBase`, and this repository's `SubstanceBase`. |
| Pipe catalogues | **One shared builder**, not a base class | Their variation is data, not behaviour — PandaAI's anti-pattern list names "base classes created without a real polymorphism problem to solve". |

**The naming, looked up.** Microsoft's Framework Design Guidelines, *Names of Classes, Structs, and
Interfaces*: "✔️ CONSIDER ending the name of derived classes with the name of the base class …
However, it is important to use reasonable judgment … the `Button` class is a kind of `Control`
event, although `Control` doesn't appear in its name." And *Base Classes for Implementing
Abstractions*: "❌ AVOID naming base classes with a 'Base' suffix if the class is intended for use in
public APIs", with "✔️ CONSIDER making base classes abstract even if they don't contain any abstract
members." Core's public surface is consumed by this repository's own `FluidScript.Api` and nothing
else, so the public-API caution is weak here and the suffix is taken for consistency with
`SubstanceBase`. The guideline's other warning stands and shapes the list below: a base class
"should be avoided if [it provides] value only to the implementers", and delegation considered
instead — which is why the diagnostics families and the explanations get none.

**The `…Component` suffix is this project's call, not a rule.** It follows the guideline's
*consider* and the families that already carry theirs (`PipeSizer`, `PipeSpec`, `*Diagnostics`), and
it separates the solver's `PipeComponent` from the script's `pipe` and the catalogue's `PipeSpec`.

## Conventions

1. **Namespace = folder path** from the project root. `Solvers/Transient/TransientSolver.cs` is
   `FluidScript.Core.Solvers.Transient`. **A class folder is transparent** (`D-148`): the partials in
   `Layout/LayoutEngine/` declare `FluidScript.Core.Layout`. Enforced by
   `ArchitectureTests.EveryCoreNamespaceIsItsFolder` rather than `IDE0130`, which cannot express the
   exception.
2. **One top-level type per file**, the file named for the type (the plugin's `styling.md`). A nested
   private type stays with its owner.
3. **Split a class at about 500 lines**, by concern, into `Class.Concern.cs` partials. At three or more
   partials they move into `Class/`. `#region` is not a substitute.
4. **Pick the smallest ensemble template** (PandaAI's *Type 1* / *Type 2*): an interface only for real
   polymorphism, a base only for several implementations sharing behaviour, a `Models/` or
   `Primitives/` subfolder only when three or more supporting types need one. No folder for a single
   file.
5. **Folder names**: plural for "a bunch of X" (`Sizers/`, `Descriptors/`), singular for a subsystem
   (`Binding/`, `Layout/`) — `naming.md`.
6. **Base types are abstract and named `…Base`; derived types carry the family noun.**
7. **Abstraction is priced by path temperature** (PandaAI's policy). Anything inside a Newton
   iteration is hot: an abstraction there must be dispatch- and allocation-neutral, and S4 measures it.
8. **Tests mirror the tree folder for folder** — `03`'s invariant 3, unchanged — and move in the same
   commit as the code they test.

## The target tree

Sixteen top-level folders become nine. Every current file has a destination; the ones not named sit
in the domain folder's root.

```
FluidScript.Core/
├── CoreAssembly.cs
├── Primitives/       Result, Unit                                    (from Fluids/ — used everywhere)
├── Language/
│   ├── Syntax/
│   │   ├── Text/     SourceText, LinePosition, TextEdit
│   │   ├── Lexing/   Lexer, LexResult, Token, TokenKind, Trivia, TriviaKind, ReservedWord, ReservedWords
│   │   ├── Parsing/  FluidScriptParser, ParseResult, LineParser (class folder, S3)
│   │   ├── Printing/ Formatter, SyntaxPrinter
│   │   └── Ast/      SyntaxNode · Statements/ · Expressions/           (one node per file, S2)
│   ├── Binding/      Binder, SemanticModel, DependencyGraph, ExpressionEvaluator, ScenarioProjection,
│   │   │             HeightMap, StyleSpec, NamedColours, Constants
│   │   ├── BindingRun/  the BindingRun partials                  (class folder: namespace …Binding)
│   │   └── Symbols/  SymbolMap, CurveSymbols, TopologySymbols
│   ├── Registry/     ComponentRegistry, ComponentKindInfo, CircuitRoleRegistry, ScheduleRoleRegistry,
│   │                 PropertyTable, NameResolution, InputLimits, Range
│   └── Compatibility/ ScriptCompatibility
├── Physics/
│   ├── Units/        unchanged contents
│   ├── Fluids/       FluidState, ISubstance, PropertyBackend, SubstanceRegistry, If97Saturation
│   │   └── Substances/ Water, HumidAirSubstance, Refrigerant, RefrigerantKind, TestSubstances
│   └── Cycles/       VapourCompressionCycle
├── Components/       IComponent, IFlowComponent, ComponentBase, PipeComponent, PumpComponent,
│   │                 TankComponent, NodeComponent, ParameterOwnership, Smoothing, SolveContext   (S4)
│   ├── Valves/       ValveComponentBase, ValveComponent, ThreeWayValveComponent, ValveLaw
│   ├── Exchangers/   HeatExchangerComponent, Effectiveness, LogMeanTemperatureDifference, ExchangerRating,
│   │                 ExchangerArrangement
│   └── Observation/  Observers, ModelObservers, PlacedSensor
├── Catalogs/         Catalog, CatalogEntry
│   ├── Pipes/        PipeSpec, MaterialRoughness, PipeCatalogs, SteelEn10220, SteelEn10255, CopperEn1057
│   └── Valves/       ValveSpec, ValveKvR5
├── Topology/
│   ├── Graph/        CircuitGraph, Branch, GraphNode, PortAdjacency
│   ├── Construction/ Lowering/ (class folder, S3), ComponentFactory, ScheduledChange, Setpoint
│   ├── Counting/     WellPosedness/ (class folder, S3), CountingTable, Assignment, Reach
│   └── Hydraulics/   HydraulicBlocks, HydraulicComponent, FillPressure
├── Solvers/          ISolver, Tolerances
│   ├── Equations/    EquationSystem, EquationLayout, SystemLayout, PortMap, ResidualScales,
│   │                 UnknownScales, StateVector
│   ├── Steady/       NewtonSolver, NewtonSettings, DenseLu, NullDirection
│   ├── Seeding/      SolutionSeed/ (class folder, S3), WarmStart
│   ├── Passes/       OuterLoop/ (class folder, S3), DeferredEvaluation
│   ├── Transient/    ITransientSolver, TransientSolver, TransientSettings, TransientFrame, RunSnapshot,
│   │                 Stratification
│   └── Results/      SolvedStates, BranchResistance, ValveLegs
├── Sizing/           ISizer, SizingDefaults, SizingOverlay               (SizerBase, S5)
│   ├── Sizers/       PipeSizer, PumpSizer, ValveSizer, ThermalSizer, ExchangerSizer
│   ├── Flows/        BranchFlows, BypassBalance
│   └── Scenarios/    ScenarioSizing, ScenarioEnvelope
├── Layout/           LayoutSolver
│   ├── LayoutEngine/ the LayoutEngine partials                    (class folder: namespace …Layout)
│   │                 -- deleted at P6.10's switch (`D-153`)
│   ├── Engine/       the rebuilt engine, P6.10 (`D-153`, `28` part E): CircuitView, Decomposition and
│   │                 its structures, Occupancy, Composer, RunBuilder       (namespace …Layout.Engine)
│   ├── Routing/      OrthogonalRouter, Segments, Direction
│   ├── Hints/        LayoutHints, LayoutHintsDerivation/ (class folder, S3)
│   └── Drawing/      Scene, SceneAudit, SceneText, LabelLayout
├── Model/            ModelContractBuilder/ (class folder, S3), ModelContractInput, ScaleDomain, Styles, SymbolCatalog
│   └── Contract/     ModelContract and its forty types                (one per file, S2)
└── Diagnostics/      Diagnostic, DiagnosticDescriptor, DiagnosticSeverity, DiagnosticArea,
    │                 DiagnosticArgument, DiagnosticRegistry, RelatedLocation, RetiredDiagnostic,
    │                 Suggestion, TextSpan
    ├── Descriptors/  the sixteen *Diagnostics families
    └── Explanations/ SolveExplanation/ (class folder, S3), ScenarioExplanation
```

**Folder names were chosen so no namespace segment is also the name of a type in it** (`D-148`):
`Topology/Construction/` rather than `Lowering/`, `Counting/` rather than `WellPosedness/`,
`Solvers/Passes/` rather than `OuterLoop/`, `Layout/Drawing/` rather than `Scene/`,
`Components/Observation/` rather than `Observers/`. A namespace `…Topology.Lowering` holding
`class Lowering` makes the compiler read `Lowering` as the namespace wherever the parent is imported.

`FluidScript.Api` is 19 files in five folders and already reads by domain; it takes S2's one-type-per-
file rule (`MetadataWire.cs` holds sixteen) and nothing else.

## The abstractions

### `ComponentBase` — seven implementers, measured

Every `IFlowComponent` implementer declares `Name`, `Mode`, `StatedParameters`, `SizedParameters`,
`DefaultParameters`, an `_equations` field, `DeclareEquations`, and usually `DeclareUnknowns => []`,
plus the constructor's `ThrowIfNull(name)` and `Name = name`. Measured per member with its XML doc
and trailing blank: **150 lines across the seven**.

As shipped in S4 (the shape first written here took the equations as a constructor argument; see
the S4 row for why it does not):

```csharp
public abstract class ComponentBase : IFlowComponent
{
    protected ComponentBase(string name);

    public string Name { get; }
    public abstract string Kind { get; }
    public virtual string? Mode => null;
    public ImmutableDictionary<string, Quantity> StatedParameters { get; init; } = …Empty;
    public ImmutableDictionary<string, Quantity> SizedParameters { get; init; } = …Empty;
    public ImmutableDictionary<string, Quantity> DefaultParameters { get; init; } = …Empty;
    public abstract ImmutableArray<Port> Ports { get; }
    public abstract ImmutableArray<int> FlowGroups { get; }
    public abstract int EquationCount { get; }
    protected ImmutableArray<EquationDeclaration> Equations { get; init; } = [];
    protected ImmutableArray<UnknownDeclaration> Unknowns { get; init; } = [];
    public ImmutableArray<UnknownDeclaration> DeclareUnknowns() => Unknowns;
    public ImmutableArray<EquationDeclaration> DeclareEquations() => Equations;
    public abstract void EvaluateResiduals(in SolveContext context, Span<double> residuals);
    public virtual bool InjectsEnergy => false;                                   // the interface's
    public virtual void EvaluateEnergyInjection(in SolveContext c, Span<double> i) => i.Clear(); // defaults,
    public virtual ImmutableArray<ResolvedParameter> Resolvable => [];            // restated
}
```

**Hot path.** `EvaluateResiduals` is dispatched through `IFlowComponent` today and will be through a
virtual slot or the same interface after — neither allocates, and the invariant that it allocates
nothing is untouched. It is still measured: the solver-scale timing baseline before and after S4,
and S4 does not ship if it moves beyond its run-to-run noise.

`IFlowComponent` stays. `EquationSystem`, the sizers and the tests program against it, and a test
double does not have to inherit.

### `ValveComponentBase` — two implementers, decided rather than measured

`ValveComponent` and `ThreeWayValveComponent` share `Kv`, `Position`, `Characteristic`, `KvIndex`,
`PositionIndex` and the shape of `Resolvable`. The line saving is small — about ten net — and `70`
deferred this base "until a third appears". `D-147` takes it anyway, for the reason that is not about
lines: **`ValveSizer` handles exactly these two types and nothing names that set.** Today it tests
`component is Valve or ThreeWayValve` in `CanSize`, and every rule that asks "is this a control valve"
repeats the pair. A base names the concept once.

### `SizerBase<TComponent>` — five implementers

```csharp
public abstract class SizerBase<TComponent> : ISizer where TComponent : IFlowComponent
{
    public abstract ImmutableArray<string> Parameters { get; }
    public virtual ImmutableDictionary<string, Quantity> Provisional => …Empty;
    public virtual bool CanSize(IFlowComponent component) => component is TComponent;
    public Result<SizingResult> Size(IFlowComponent component, in SizingContext context) =>
        component is TComponent typed ? Size(typed, context) : Refused(component);
    protected abstract Result<SizingResult> Size(TComponent component, in SizingContext context);
}
```

`ValveSizer` becomes `SizerBase<ValveComponentBase>` and loses its hand-written wrong-type refusal;
the others lose their `CanSize` type tests and their casts. **Roughly line-neutral** — the base costs
what it saves. It is here for the typed `Size`: a sizer cannot be handed the wrong component and
cast it.

### The pipe-catalogue builder — data, not a base

`SteelEn10220`, `SteelEn10255` and `CopperEn1057` each build `Catalog<PipeSpec>` with the same
twenty-odd lines; only the row tuple, the provenance, the roughness and the series differ. One
`PipeCatalogBuilder.Build(id, version, standard, rows, sources, roughness, series)` replaces the three
builders. About **30 lines** net. The three stay static classes with an `Instance`, so no caller
changes.

### What gets no base, and why

- **The sixteen `*Diagnostics` families.** Static descriptor tables; a static class cannot inherit,
  and the only shared member is `All`.
- **`SolveExplanation`, `ScenarioExplanation`, `SceneText`.** Three reports with three audiences;
  what they share is `StringBuilder`, and the guideline's "value only to the implementers" applies.
- **`ISubstance`.** Already has `SubstanceBase`; it moves to its own file.

## The line budget, measured 2026-09-23

The user asked how many lines this saves. **It does not save lines; it adds about 3 %.** The
restructure is for finding things, and the few hundred lines the abstractions remove are outweighed
by the file headers that one type per file and the partial splits create.

| Package | Lines | How measured |
|---|---|---|
| S1 moves | ≈ 0 | a `namespace` line edited per file, `using` lines adjusted |
| S2 one type per file | **≈ +1 500 estimated; +748 measured** (Core +696, Api +52) | 279 types left 69 files. The estimate assumed each new file kept its source's `using`s; pruning to what each file needs halved it |
| S3 partial splits | **≈ +400 estimated; +678 measured** (Core only) | 22 classes split into 63 new partial files. Each new file carries its source's `using`s until pruned, the namespace, and one class header — two for a nested class, which is wrapped in its outer class |
| S4 `ComponentBase` | **−150**, base +60 | counted per member with docs, across the seven components |
| S4 measured | **−98** (Core; tests renamed, net 0) | both bases and the renames together, against −100 estimated for the two S4 rows |
| S4 `ValveComponentBase` | ≈ −10 | shared members less the base's own |
| S5 `SizerBase<T>` | ≈ 0 | `CanSize` and guards removed, the base added |
| S5 pipe-catalogue builder | ≈ −30 | three builders of 23, 23 and 32 lines to calls of ~6, plus a ~30-line builder |
| S5 measured | **+53** | the sizers and catalogues lost 104 lines net of their edits; `SizerBase` (71) and the builder (86) cost more, most of it the XML docs a public base carries |
| **Net** | **≈ +1 700 of 58 400** | |
| **Net measured** | **+1 491 of 58 387** (Core, 204 → 513 files; Api +55) | S0's tree against S5's, every `.cs` line counted |

Where a real reduction would come from is **duplicated logic**, not boilerplate — `70`'s method, a
review that finds one question answered several ways. `70`'s R6 (the binder's phase records,
`EquationSystem`'s builder) is that kind of work and S3 absorbs it where it splits those files. A
line-count target should be set against such a review, not against this plan.

## The packages

Each package is behaviour-preserving and is closed on the same evidence: **every golden
byte-identical** (report, contract, token, scene and export goldens), the full Core, Api and frontend
suites green, the build at zero warnings, and the plan checker at its baseline.

| # | What | Effort | Risk |
|---|---|---|---|
| S0 | `D-147`; this document; the conventions in [`04`](00-foundation/04-engineering-standards.md); `03`'s tree | small | low |
| S1 | **Moves only**, shipped 2026-09-23 as two commits rather than one per domain: every `git mv` in one commit with no content edit, which compiles as it stands because no file's text changed and keeps `git log --follow` intact; then the namespaces, the `using`s (rebuilt from a type map — a file gains a `using` for a type only if it could already see that type's old namespace), 149 unused `using`s removed, and three test literals that named a folder or a namespace. Tests moved with their code | medium | low — the compiler finds every missed `using` |
| S2 | One type per file, shipped 2026-09-23: 279 types out of 69 files (Core and Api), nine files named for no type removed; `Ast/Statements/`, `Ast/Expressions/` and `Components/Declarations/` created; a type split out of a class folder goes to its parent so the folder stays one class's. A doc comment attaches across a blank line, and the splitter follows it | medium | low |
| S3 | Concern partials, shipped 2026-09-23: every Core file over 600 lines split by concern, members moved whole and unedited — 22 classes, 63 new files, eight new class folders. Fields and initialised auto-properties stay in the core file, because C# does not order static initialisers across partial files. A nested class splits as a nested partial inside its outer partial (`Lowering.Build.*`, `SolutionSeed.Field.*`); primary-constructor parameters are visible in every part. Two static `char[]`/`string[]` fields moved beside their only readers, because `CA1870`'s analyzer crashes (`AD0001`, "Syntax node is not within syntax tree") when the array and its use sit in different files. `70`'s R6 was **not** taken: it changes how the binder and the equation system are built, and a package whose evidence is "members moved, nothing edited" is the wrong carrier for it | big | low–med: moving members between partials cannot change behaviour, but a private helper's accessibility can |
| S4 | `ComponentBase`, `ValveComponentBase`, and the `…Component` renames through `rename_symbol`, shipped 2026-09-23. Three departures from the sketch above, each for a reason found while writing it. **The equations are a protected `init` property, not a constructor argument**: the tank and the exchanger build theirs from validated state over several statements, and a base-constructor argument would have forced each into a static helper. **The interface's default members are restated as virtuals** (`InjectsEnergy`, `EvaluateEnergyInjection`, `Resolvable`): a derived member the base did not declare would not implement the interface — the base's mapping to the interface default would win, and the pipe's rise would drop out of the energy balance with nothing failing to compile. **`CircuitNode` became `NodeComponent`**, which `71` had not named: the kind is `node`, and the glossary derives the type from the keyword. `EquationCount` stays abstract, because the node's and the exchanger's explain why their count is what it is. Solver-scale timing at 861 unknowns, two runs each, debug build: 5.50 and 5.63 s before, 5.58 and 5.52 s after; 1 597 MB allocated both times | medium | **med** — the only package that touches the hot path |
| S5 | `SizerBase<TComponent>` and `PipeCatalogBuilder`, shipped 2026-09-23. The five sizers take their family as the type argument and lose their `CanSize` type test, their re-test and cast, and their hand-written refusal; each keeps its refusal wording as a `Refusal` pair, so no message changed. The thermal rule narrows `CanSize` to an exchanger with a rating and refuses the rest itself. With the valve helpers typed to `ValveComponentBase`, `CA1859` asked for exactly that, and the two rules that listed `ValveComponent or ThreeWayValveComponent` now name the base. The builder has two overloads — nominal-size rows under one provenance (the two steels), and rows carrying their own designation and provenance (copper). **Found while closing it:** S1 moved the token goldens and `tokenizer.test.ts` still read the old folder, so one frontend file had failed since S1 — every package from S1 to S4 ran the .NET suites and not the frontend's, which this document's closing evidence names. Fixed here; `03`'s example paths, which had also gone stale, with it | small | low |

S1 before S2 before S3 so a file moves once and splits in its final folder. S4 after S3 so the
renames run over files already in place. Each package updates this document's status line, `09`, and
the tier registers of whatever it found.

## What is deliberately not changed

- **Behaviour, numbers, diagnostics, the wire.** Api DTOs are mapped from Core types (`D-101`), so a
  Core rename or namespace change reaches no payload; `metadata.json` names codes and areas, not types.
- **`IFlowComponent`, `ISizer`, `ISubstance`, `ICatalog<T>`.** The bases implement them; nothing that
  consumes them changes.
- **The frontend and `docs/`.** No user-facing page names a C# type's namespace.
- **Closed register rows and `09`'s history** that cite `File.cs:line`. They record what was true;
  open rows citing a moved file are updated in the package that moves it.

## Invariants

1. Every package leaves every golden byte-identical.
2. After S1, every file's namespace equals its folder path, a class folder being transparent, and a test enforces it.
3. After S2, no file under `src/` declares more than one top-level type.
4. After S3, no Core class file is over ~800 lines without a register row saying why. The one today is
   `BinderDiagnostics` (933 lines, 77 descriptors and the `All` list that reads them): a table, not a
   class, and one that cannot split, since `All`'s initialiser would then read properties another file
   initialises in an order C# does not define.
5. The test tree mirrors the source tree folder for folder at every commit.
6. No package changes a public member's behaviour or a residual's arithmetic.

## Error cases

| Situation | What happens | Why it matters |
|---|---|---|
| A moved file's `using` is missed | The build fails (`CS0246`) | The cheap failure; S1 relies on it |
| A move and an edit share a commit | `git log --follow` loses the file's history | Rows cite files; their history is the evidence |
| A golden moves in a structural package | The package does not ship until the cause is found | A structural change that moves a number is a behaviour change in disguise |
| S4 moves the solver-scale timing | S4 does not ship; the dispatch is reconsidered | The residual path runs N+1 times per Newton iteration |
| A split makes a private helper `internal` | Review flags it; prefer a partial over widening | Accessibility is part of the surface |

## Worked example

`Components/Valve.cs` today: 359 lines, two classes, namespace `FluidScript.Core.Components`.

- **S1** — `git mv` to `Components/Valves/Valve.cs`; next commit sets
  `namespace FluidScript.Core.Components.Valves;` and adds that `using` in every file that names
  `Valve` — `get_references` gives the list, and the build confirms it.
- **S2** — `ThreeWayValve` leaves for `Components/Valves/ThreeWayValve.cs`; `ValveCharacteristic` and
  `ValveArrangement` leave `ValveLaw.cs` for files of their own in the same folder.
- **S4** — `ValveComponentBase.cs` added, holding `Kv`, `Position`, `Characteristic` and the indices;
  `rename_symbol` turns `Valve` into `ValveComponent` (28 sites in 17 files) and `ThreeWayValve`
  into `ThreeWayValveComponent` (69 in 26); `git mv` renames the files to match.
- **S5** — `ValveSizer : SizerBase<ValveComponentBase>`; its `CanSize` line and its wrong-type refusal
  block are deleted.

Goldens unchanged at every step: the kind string is `"valve"` in the registry, not the type name.

## Acceptance criteria

1. The tree matches *The target tree*, and a file not named there sits by the conventions.
2. `EveryCoreNamespaceIsItsFolder` passes and the build is clean.
3. No `src/` file declares two top-level types.
4. `ComponentBase`, `ValveComponentBase`, `SizerBase<TComponent>` and `PipeCatalogBuilder` exist and
   every family member uses them.
5. Every golden is byte-identical to the one before S1; the suites and the plan checker are at their
   baselines.
6. The solver-scale timing is within its noise across S4.

## Open questions

None. The four choices this plan turned on were the user's, 2026-09-23, and are `D-147`.
