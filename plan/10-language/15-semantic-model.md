---
id: 15-semantic-model
title: Semantic model and binding
tier: 10-language
status: draft
owns: [binder, symbol table, semantic model types, component registry, kind resolution and aliases, lowering boundary]
depends_on: [12-grammar, 13-type-and-unit-system, 14-expressions-and-references]
traces_to: [R-01, R-02, R-06, R-16, R-45]
open_questions: 0
last_review_pass: 0
---

# Semantic model and binding

## Purpose

Turns a syntax tree — names and text spans — into a semantic model: resolved symbols, known component
kinds, typed parameter values, and the distinction between *absent* and *given* that `D-02` depends on.
This is the stage boundary that matters most in the pipeline: above it nothing knows physics, below it
nothing knows a script existed.

## Responsibilities

**Owns.** The binder, the symbol table, the component registry, the semantic model types, and the
contract at the lowering boundary.

**Explicitly does not own.** Syntax ([`12-grammar`](12-grammar.md)), evaluation
([`14-expressions-and-references`](14-expressions-and-references.md)), the graph the model lowers into
([`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md)), component behaviour
([`22-component-model`](../20-core-domain/22-component-model.md)).

## The component registry

The binder does not know what a `heat_exchanger` is. It asks a registry, which is populated by Core's
component library. This indirection is what lets a component be added without touching the language.

```csharp
/// <summary>Describes a component kind to the binder: its keyword, ports, and parameters.</summary>
/// <remarks>
/// The binder needs no behaviour, only shape. A registry entry is metadata; the class that
/// implements the physics is resolved later, at lowering.
/// </remarks>
public sealed record ComponentKindInfo
{
    /// <summary>The canonical script keyword, in lower_snake_case.</summary>
    /// <remarks>
    /// The one spelling <c>/docs</c>, the model contract, and the printer use. Everything a user
    /// may type resolves to this ({D-15}); nothing else is ever emitted.
    /// </remarks>
    public required string Keyword { get; init; }

    /// <summary>Additional spellings that resolve to this kind, curated per kind (D-15).</summary>
    /// <remarks>
    /// Matched after normalisation, so <c>3_way_valve</c> covers <c>3WayValve</c> and
    /// <c>3 way valve</c> too, and only genuinely different words need listing —
    /// <c>mixing_valve</c>, <c>diverting_valve</c>, <c>exchanger</c>, <c>radiator</c>.
    /// An alias is never printed, never appears in the model contract, and never appears in
    /// <c>/docs</c> except on the kind's own page under "also written as".
    /// </remarks>
    public required ImmutableArray<string> Aliases { get; init; }

    /// <summary>Ports in declaration order. Unqualified connections bind to these in order.</summary>
    public required ImmutableArray<PortInfo> Ports { get; init; }

    /// <summary>Indexed port families materialized from qualified endpoints or matching parameters.</summary>
    /// <remarks>Empty for fixed-port kinds; <c>tank</c> declares <c>in{n}</c>/<c>out{n}</c> (`D-32`).</remarks>
    public required ImmutableArray<PortFamilyInfo> PortFamilies { get; init; }

    /// <summary>Patterned parameter families such as tank layer temperatures and port elevations.</summary>
    public required ImmutableArray<IndexedParameterFamilyInfo> IndexedParameterFamilies { get; init; }

    /// <summary>Whether this kind can contribute net hydraulic head and satisfy FS2214.</summary>
    /// <remarks>Explicit registry metadata; never inferred from residual implementation (`D-30`).</remarks>
    public required bool DrivesFlow { get; init; }

    /// <summary>Every parameter this kind accepts, keyed by script name.</summary>
    public required ImmutableDictionary<string, ParameterInfo> Parameters { get; init; }

    /// <summary>Properties referenceable as <c>Name.property</c>.</summary>
    public required ImmutableDictionary<string, PropertyInfo> Properties { get; init; }
}

public sealed record ParameterInfo
{
    public required string Name { get; init; }

    /// <summary>Curated input spellings. Binding stores <see cref="Name"/>; printing preserves source.</summary>
    public required ImmutableArray<string> Aliases { get; init; }

    /// <summary>What shape of value this parameter accepts.</summary>
    /// <remarks>
    /// <see cref="ParameterValueKind.Quantity"/> for everything dimensioned;
    /// <see cref="ParameterValueKind.Symbol"/> for a closed set of names such as a valve's
    /// <c>characteristic</c>; <see cref="ParameterValueKind.Reference"/> for a controller's
    /// <c>measure</c> and <c>actuate</c>, which name another component's property or parameter
    /// rather than a value. All three are the same syntax — an identifier or an expression — and
    /// only this field says how to bind it.
    /// </remarks>
    public required ParameterValueKind ValueKind { get; init; }

    /// <summary>Dimension, for a <see cref="ParameterValueKind.Quantity"/> parameter.</summary>
    /// <value><see cref="Dimension.Dimensionless"/> for the other kinds.</value>
    public required Dimension Dimension { get; init; }

    /// <summary>Accepted names, for a <see cref="ParameterValueKind.Symbol"/> parameter.</summary>
    /// <value>Empty for the other kinds. Resolved by the same normalisation as a kind name.</value>
    public ImmutableArray<string> AcceptedSymbols { get; init; }

    /// <summary>What omission means: size the value, or apply an explicit visible default.</summary>
    public required ParameterOmissionBehavior OmissionBehavior { get; init; }

    /// <summary>Canonical source literal for a Default parameter; null for Size.</summary>
    /// <remarks>Parsed according to <see cref="ValueKind"/> and exposed with <see cref="DefaultBasis"/>.</remarks>
    public string? DefaultLiteral { get; init; }

    /// <summary>User-facing reason for a default; null for Size.</summary>
    public string? DefaultBasis { get; init; }

    /// <summary>Plausibility bounds in SI, used for FS1306. Null disables the check.</summary>
    public Range<double>? UsualRange { get; init; }
}

public enum ParameterValueKind { Quantity, Symbol, Reference }
public enum ParameterOmissionBehavior { Size, Default }

public sealed record PortInfo
{
    public required string Name { get; init; }
    public required PortRole Role { get; init; }        // Inlet | Outlet | Bidirectional
    /// <summary>Whether the port may be left unconnected without inference rule I3 firing.</summary>
    public required bool IsOptional { get; init; }
}

public sealed record PortFamilyInfo
{
    /// <summary>Canonical prefix before a positive decimal index, such as <c>in</c>.</summary>
    public required string Prefix { get; init; }
    public required int MinIndex { get; init; }
    public required int MaxIndex { get; init; }
    public required PortRole Role { get; init; }
    /// <summary>Associated normalized-height parameter suffix; <c>_elevation</c> for a tank.</summary>
    public required string? ElevationParameterSuffix { get; init; }
}

public sealed record IndexedParameterFamilyInfo
{
    /// <summary>Canonical pattern with one <c>{index}</c> placeholder.</summary>
    /// <value><c>t{index}</c>, <c>in{index}_elevation</c>, or <c>out{index}_elevation</c>.</value>
    public required string Pattern { get; init; }
    public required int MinIndex { get; init; }
    /// <summary>Fixed maximum, or null when <see cref="MaxIndexParameter"/> supplies it.</summary>
    public int? MaxIndex { get; init; }
    /// <summary>Canonical integer parameter controlling the maximum, e.g. <c>layers</c>.</summary>
    public string? MaxIndexParameter { get; init; }
    public required ParameterInfo Element { get; init; }
}

public sealed record PropertyInfo
{
    public required string Name { get; init; }
    public required Dimension Dimension { get; init; }
    public required PropertyAvailability Availability { get; init; }
    public required string CanonicalUnit { get; init; }
}

public enum PropertyAvailability { Declared, Sized, Solved }

public interface IComponentRegistry
{
    ImmutableArray<ComponentKindInfo> Kinds { get; }
    KindResolution Resolve(string writtenKind);
}

public abstract record KindResolution
{
    public sealed record Exact(ComponentKindInfo Kind) : KindResolution;
    public sealed record Similar(ComponentKindInfo Kind, double Score) : KindResolution;
    public sealed record Ambiguous(ImmutableArray<ComponentKindInfo> Candidates) : KindResolution;
    public sealed record Unknown(string? SuggestedKeyword) : KindResolution;
}
```

`Parameters` and `Properties` are separate maps even though they overlap: `power` is both something you
may set and something you may read. Keeping them separate lets a component expose a read-only property
(`dp`) that is not settable, and a write-only parameter that is not meaningful to read back.

## Kind resolution

`D-15`. A user should not have to learn a canonical spelling to declare a valve, and an agent
generating a script from a text brief will produce `heat-exchanger`, `HeatExchanger`, `exchanger` and
`heatexchanger` with roughly equal probability. Resolution therefore runs in three stages, and the
first two are exact.

### Stage 1 — normalise

```
normalise(s) = lowercase(s), with every '_' and ' ' removed
```

So `three_way_valve`, `ThreeWayValve`, `THREE_WAY_VALVE` and `three way valve` all normalise to
`threewayvalve`. **Hyphens are not handled here** because they never reach this stage: `-` is an
operator and `3-way-valve` fails in the lexer with `FS1108`
([`12-grammar`](12-grammar.md)), which suggests the underscored form directly.

Normalisation is applied to the canonical keyword and to every alias when the registry is built, so
the lookup is a dictionary hit, not a scan.

### Stage 2 — exact match

The normalised input is looked up against the normalised keywords and aliases. A hit resolves
**silently**: it is a spelling of a name the registry knows, not a guess.

| Kind | Canonical | Curated aliases |
|---|---|---|
| Node | `node` | `point`, `junction` |
| Pipe | `pipe` | `tube` |
| Heat exchanger | `heat_exchanger` | `exchanger`, `hx`, `heater`, `cooler`, `radiator`, `load`, `boiler`, `chiller` |
| Valve | `valve` | `control_valve`, `balancing_valve`, `two_way_valve`, `2_way_valve` |
| Three-way valve | `three_way_valve` | `3_way_valve`, `mixing_valve`, `diverting_valve`, `3wv` |
| Pump | `pump` | `circulator` |
| Tank | `tank` | `container` |
| Controller | `controller` | `pi`, `thermostat`, `control` |

`controller` is in the table although [`22-component-model`](../20-core-domain/22-component-model.md)
describes six *flow-component families*: a controller is a registry kind with no ports, declared like any
other component and excluded from the flow graph
([`34-controllers`](../30-solver/34-controllers.md)).

**Aliases are curated, not generated**, and the list above is the whole of it. Each entry is a word a
designer actually uses for that component, so `radiator` and `boiler` reaching `heat_exchanger` is
information the registry carries rather than a coincidence of edit distance —
The canonical `heat_exchanger` name is fixed for v1; aliases keep familiar domain spellings explicit
without multiplying component physics.

`fan` and `duct` are deliberately absent (`D-28`). A pump/pipe alias would accept an air-side script
while omitting humidity balance, condensation, leakage, fan curves, and compressibility. Unknown air
kinds therefore fail clearly instead of producing a hydronic answer wearing air-side names.

`tank` also has the curated **parameter** alias `v` → `volume` (`D-32`). Parameter aliases obey the
same rule as kind aliases: the semantic map and metadata use `volume`; the lossless printer leaves
`v` exactly as written. Similarity is scored only after exact canonical/alias lookup.

### Stage 3 — similarity

An input that matches nothing exactly is scored against every normalised keyword and alias, and
**resolves when the best score clears the threshold**, with `FS1512` (info) naming what it read.

| Rule | Value | Reasoning |
|---|---|---|
| Score | `1 − damerau_levenshtein(a, b) / max(len(a), len(b))` | Normalised edit distance with transposition, because `pmup` for `pump` is one keystroke, not two |
| Threshold | **0.70** | `pmp`→`pump` scores 0.75 and must resolve; `valve`→`pipe` scores 0.20 and must not |
| Ambiguity margin | **0.05** | If the runner-up is within this of the winner, nothing resolves |
| Tie-break | none — ambiguity is reported | See below |

**The ambiguity margin is not optional, and it is the part that makes this specifiable at all.**
`valv` scores 0.80 against `valve` and 0.78 against a normalised `3_way_valve` prefix; picking the
higher of two near-equal candidates is a coin flip that produces a silently wrong circuit. Below the
margin the input is `FS1513` with both candidates ranked, which is a question the user answers in one
keystroke. Without the margin the rule is "resolve to the best match", which is not deterministic in
any useful sense — it depends on the alias list's contents, so adding an alias for one component could
silently change how a *different* script resolves.

**Every stage-3 resolution emits `FS1512`, always, and it is info rather than warning.** The user gets
their circuit and a line in the log saying what was read. Suppressing it would make the feature invisible
magic — the same argument that makes `FS1510` mandatory for inferred components — and escalating it to a
warning would put an amber squiggle on a script that is doing exactly what its author meant.

### What resolution does not do

- **It never invents a kind.** Below the threshold, or ambiguous, the declaration binds with
  `Kind = Unknown` and the script continues (P4), exactly as before.
- **It never runs on component *names*.** `PU1` and `PUI` are different components, and a typo in a
  name must produce a dangling reference, not a silent merge. Similarity applies to `kind-name`,
  `parameter` names (`FS1503`'s suggestion), and `property-name` — all closed sets the registry owns.
- **It never changes the source text.** The script keeps the user's spelling; the printer round-trips
  it byte for byte (`R-25`); only the model contract and `/docs` carry the canonical keyword. An
  editor may *offer* the canonical form as a quick fix, and `52-editor` owns whether it does.

**This qualifies P6** ("one way to say each thing"), and `D-15` records the trade rather than pretending
otherwise. P6's cost is paid in `/docs` and in the printer, and neither is affected here: `/docs` has
one page per canonical keyword with an "also written as" line, and the printer never emits an alias.
What P6 was protecting against — two scripts that mean the same thing looking different in a diff — is
a real cost and it is accepted, because the alternative is a user who writes `3-way-valve`, gets an
error, and concludes the tool is fussy.

## The semantic model

```csharp
/// <summary>A bound script: every name resolved, every value typed, ready for lowering.</summary>
public sealed record SemanticModel
{
    public required CircuitSymbol Circuit { get; init; }
    public required ImmutableArray<ComponentSymbol> Components { get; init; }
    public required ImmutableArray<ConnectionSymbol> Connections { get; init; }
    public required ImmutableArray<BindingSymbol> Bindings { get; init; }
    public required StyleSettings Style { get; init; }

    /// <summary>Maps a source position back to the symbol it declares or references.</summary>
    /// <remarks>Backs hover, go-to-definition, and canvas write-back's need to find the line
    /// that owns a value (R-25).</remarks>
    public required ISymbolMap SymbolMap { get; init; }
}

public sealed record CircuitSymbol(string Name, string Substance, FluidMode Mode);

public sealed record ConnectionSymbol(
    EndpointSymbol From,
    EndpointSymbol To,
    TextSpan SourceSpan);

public sealed record EndpointSymbol(ComponentSymbol Component, string Port);

public sealed record BindingSymbol(
    string Name,
    ExpressionSyntax Expression,
    ValueId ValueId,
    Quantity? Value,
    TextSpan DeclarationSpan);

public sealed record StyleSettings(ImmutableArray<StyleTokenSyntax> Tokens);

public abstract record Origin
{
    public sealed record Declared : Origin;
    public sealed record Inferred(string Rule, string StableKey) : Origin;
}

public abstract record SymbolReference
{
    public sealed record Circuit(CircuitSymbol Value) : SymbolReference;
    public sealed record Component(ComponentSymbol Value) : SymbolReference;
    public sealed record Binding(BindingSymbol Value) : SymbolReference;
    public sealed record Connection(ConnectionSymbol Value) : SymbolReference;
}

public interface ISymbolMap
{
    SymbolReference? AtOffset(int utf16Offset);
    ImmutableArray<TextSpan> References(SymbolReference symbol);
}

public sealed record ComponentSymbol
{
    /// <summary>The user's identifier, or a generated one for an inferred component.</summary>
    public required string Name { get; init; }

    /// <summary>How this component came to exist.</summary>
    /// <value><see cref="Origin.Declared"/> for a written declaration; the inference rule id
    /// (I1, I2, I3) for one the binder created.</value>
    public required Origin Origin { get; init; }

    public required ComponentKindInfo Kind { get; init; }

    /// <summary>Parameter values, keyed by canonical parameter name. A parameter the user did not write is
    /// absent from this map — it is not present with a default (D-02, principle P2).</summary>
    public required ImmutableDictionary<string, ParameterValue> Parameters { get; init; }

    /// <summary>Source span of the declaration, or null for an inferred component.</summary>
    public TextSpan? DeclarationSpan { get; init; }
}

/// <summary>A parameter the user supplied. Its mere presence is a constraint (D-02).</summary>
public sealed record ParameterValue
{
    /// <summary>The exact parameter spelling in source, retained for lossless write-back.</summary>
    public required string WrittenName { get; init; }

    /// <summary>The evaluated value, or null when the expression was deferred.</summary>
    public Quantity? Value { get; init; }

    /// <summary>The expression, retained for deferred re-evaluation and for write-back.</summary>
    public required ExpressionSyntax Expression { get; init; }

    public required TextSpan Span { get; init; }
}
```

**`Parameters` uses absence, not a nullable value, to mean unresolved.** This is worth stating as an
implementation rule because the natural C# instinct is a `Quantity?` property per parameter, and that
loses the distinction the moment a component gains a legitimately-null parameter. Absence from the
dictionary is unambiguous; the kind registry then selects sizing or a binding visible default under
its omission policy (`D-02`, `D-32`).

**`Origin` is on every component.** The canvas must show which components the user wrote and which the
language created (`R-23`, and principle P3's justification), diagnostics must not point at a span an
inferred component does not have, and write-back must refuse to edit a component with no declaration.
Carrying it as data beats deriving it from a null span.

## Binding order

1. **Collect declarations.** Every `ComponentDeclarationSyntax` and `LetBindingSyntax` enters the
   symbol table. Duplicates → `FS1501` / `FS1401`.
2. **Resolve kinds** against the registry, in the three stages below — normalise, exact, similarity.
   Unresolved → `FS1502`; ambiguous → `FS1513`. Either way the component is still created with an
   `Unknown` kind so later stages can skip it without the script collapsing (P4).
3. **Bind parameters.** Each parameter name is looked up in the kind's canonical names and curated
   aliases, by the same normalisation as a kind name. Indexed tank parameters (`t1`…`tN`,
   `in1_elevation`…`out16_elevation`) are matched against their declared family before similarity.
   Unknown → `FS1503` listing the accepted names/patterns. The value binds
   according to `ParameterInfo.ValueKind`: a quantity is evaluated, a symbol is matched against
   `AcceptedSymbols` (`FS1514`), and a reference is recorded unevaluated (`FS1515`) for a later stage
   to resolve — under `D-23`, a controller's direct `measure=N2.t` names a property that does not exist until the solve.
4. **Build the dependency graph** over bindings, parameters, and referenced properties.
5. **Evaluate** in topological order; defer what depends on solved values.
6. **Materialize indexed ports.** A tank starts with `in1` and `out1`. A qualified endpoint or an
   elevation parameter creates the named port after validating its 1…16 index. Ports not evidenced by
   source do not exist in the bound model or model contract.
7. **Bind connections.** Each endpoint resolves to a component symbol and a port. An unqualified tank
   endpoint on the right of `-` takes the next free inlet; one on the left takes the next free outlet.
   Multiple-port tank examples qualify every endpoint so source reordering cannot change intent. This
   is where the inference rules fire.
8. **Apply inference rules** I1, I2, I3 in that order — order matters, since I2 can only run once I1
   has created the undeclared nodes, and I3 can only run once every connection has claimed its port.
9. **Validate.** Port over-subscription, connections to unknown components, orphaned components.

Steps 1–5 have no notion of topology and steps 6–9 have no notion of expressions; the split keeps each
half testable alone.

The binder preserves the exact presence of `in2`, `out2`, `dt2`, and `flow2` plus secondary-port
connections. During lowering, the heat-exchanger factory derives `duty`, `rated`, or `coupled` from
that evidence using `D-19`'s precedence. It must not collapse “secondary ports unconnected” to Duty:
that would erase Rated external-profile designs before Core sees them.

## Inference, concretely

The rules are stated in [`11-language-overview`](11-language-overview.md); this is how the binder
executes them.

**I1 — undeclared node.** After step 6, any endpoint identifier with no symbol becomes a
`ComponentSymbol` with `Kind = node`, `Origin = Inferred(I1)`, and `DeclarationSpan = null`. Its name
is the user's identifier, so `N1` in the script is `N1` in the model and in hover.

**I2 — implicit intermediate node.** For each connection whose endpoints are both non-node components,
insert a node named `{A}__{B}` and replace the connection with two. Collision (the same pair connected
twice) appends an ordinal: `HE1__3WV`, `HE1__3WV_2`.

**I3 — open-port termination.** After all connections are bound, every non-optional port with no
connection gets a boundary node named `{Component}__{Port}`. What boundary condition it carries is
[`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md)'s decision; the binder records
only that it is a boundary.

Every inferred component gets an info diagnostic (`FS1510`) so the user can see what was created.
These are info-level and off by default in the log ([`56-console-log`](../50-frontend/56-console-log.md)),
because on a large script they would drown everything else — but they must exist, or the inference is
invisible magic.

## The lowering boundary

```csharp
/// <summary>Binds a syntax tree into a semantic model. Never throws.</summary>
public interface IBinder
{
    BindResult Bind(ScriptSyntax syntax, IComponentRegistry registry);
}

public sealed record BindResult(SemanticModel Model, ImmutableArray<Diagnostic> Diagnostics);
```

**The semantic model contains no Core physics types.** No `ISubstance`, no `IComponent`, no solver
type. It names a substance by string and a kind by registry metadata. Lowering
([`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md)) is what turns those names into
objects. That is what keeps tier 10 testable with no CoolProp dependency, which matters practically:
the language test suite must run in milliseconds, and property lookups are not milliseconds.

## Invariants

1. `Bind` never throws, for any syntax tree including one that is entirely `MalformedStatementSyntax`.
2. Every `ComponentSymbol` has a unique `Name` within its circuit.
3. A parameter absent from `ComponentSymbol.Parameters` was not written by the user. There is no other
   reason for absence.
4. Every `ConnectionSymbol` endpoint resolves to an existing `ComponentSymbol` and one of its ports.
5. Every inferred component has `DeclarationSpan == null` and a non-`Declared` `Origin`, and the
   converse holds.
6. `SymbolMap` resolves every source position within a declaration to that declaration's symbol.
7. The semantic model references no type from `FluidScript.Core.Fluids`, `.Components`, or `.Solvers`.
8. **Kind resolution is deterministic and total**: the same input and the same registry always yield
   the same kind or the same diagnostic, and no input throws.
9. **No two kinds share a normalised keyword or alias.** Asserted when the registry is built, because
   a collision makes stage 2 order-dependent and stage 3 permanently ambiguous.
10. `ComponentSymbol` records the canonical `Keyword`; the alias or misspelling the user wrote survives
    only in the source text and in `DeclarationSpan`.
11. Every materialized indexed port has an index in its family's closed range, and no unevidenced
    indexed port appears in `ComponentSymbol` or the model contract.
12. A registry entry with `OmissionBehavior.Default` has a parseable `DefaultLiteral` and non-empty
    basis; one with `Size` has neither. Defaults never appear in `ComponentSymbol.Parameters`, whose
    presence continues to mean the user wrote the parameter.

Invariant 7 is checkable by an architecture test and should be, since it is the one a well-meaning
refactor breaks first.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS1501` | Duplicate component name | Error | `'{name}' is already declared at line {n}.` |
| `FS1502` | Unknown component kind | Error | `There is no '{kind}'. Did you mean '{suggestion}'?` |
| `FS1503` | Unknown parameter for the kind | Error | `A {kind} has no '{param}'. It accepts: {list}.` |
| `FS1504` | Endpoint names an unknown component | Error | Handled by I1 unless the name is a declared non-component symbol, then: `'{name}' is a value, not a component.` |
| `FS1505` | Unknown port | Error | `A {kind} has no port '{port}'. Ports: {list}.` |
| `FS1506` | Port connected more than once | Error | `Port '{port}' of '{name}' is already connected at line {n}.` |
| `FS1507` | Component in no connection | Warning | `'{name}' is not connected to anything.` |
| `FS1508` | No `circuit` header | Warning | `No circuit name; using '{filename}'.` |
| `FS1509` | More than one `circuit` header | Error | `One circuit per file for now.` |
| `FS1510` | A component was inferred | Info | `Added {kind} '{name}' ({rule}).` |
| `FS1511` | Graph is disconnected | Warning | `'{name}' and {n} others are not connected to the rest of the circuit.` |
| `FS1512` | A kind, parameter, or property name resolved by similarity (stage 3) | Info | `Read '{written}' as '{canonical}'.` |
| `FS1513` | A kind name is ambiguous within the margin | Error | `'{written}' could be '{a}' or '{b}'. Write one of them.` |
| `FS1514` | A symbol-valued parameter got an unaccepted name | Error | `'{param}' accepts {list}; '{written}' is none of them.` |
| `FS1515` | A reference-valued parameter got something that is not a reference | Error | `'{param}' names a component property, like 'N2.t'.` |
| `FS1516` | An indexed port or parameter lies outside its declared family | Error | `'{written}' is outside {kind}'s supported {min}…{max} range.` |

`FS1507` and `FS1511` are warnings rather than errors because a partially-written script is the normal
editing state (P4) and erroring would blank the diagram on every keystroke.

**`FS1502` and `FS1513` are the two ends of stage 3.** `FS1502` fires when nothing scored above the
threshold — the input resembles no component — and carries the top-ranked candidate as a suggestion.
`FS1513` fires when two candidates scored within the margin of each other. Both are errors; they differ
in whether the message says "did you mean X" or "X or Y".

## Worked example

Binding the brief's script. Declared: `HE1`, `3WV`, `PU1`. Connections: `N1-N2`, `N2-HE1`, `HE1-3WV`,
`3WV-N2`, `3WV-N3`.

**After step 6 (connections bound, no inference yet):** `N1`, `N2`, `N3` unresolved.

**I1** creates three nodes:

| Name | Kind | Origin |
|---|---|---|
| `N1` | node | Inferred(I1) |
| `N2` | node | Inferred(I1) |
| `N3` | node | Inferred(I1) |

**I2** examines each connection for two non-node endpoints:

| Connection | Both non-node? | Action |
|---|---|---|
| `N1 - N2` | no | unchanged |
| `N2 - HE1` | no | unchanged |
| `HE1 - 3WV` | **yes** | insert node `HE1__3WV`; becomes `HE1 - HE1__3WV` and `HE1__3WV - 3WV` |
| `3WV - N2` | no | unchanged |
| `3WV - N3` | no | unchanged |

**I3** checks unconnected non-optional ports. `HE1` has 2 ports, both connected. `PU1` has 2 ports and
**no connections at all** — it appears in no connection line. Both its ports terminate, and `FS1507`
fires: *"'PU1' is not connected to anything."*

That is a real finding about the brief's example: as written, the pump is declared but never wired
into the loop. The language reports it rather than guessing where it goes, which is P3 working
correctly — inserting a pump into a loop has more than one defensible answer, so the language does not
choose. The example needs one more connection line, and `/docs`'s tutorial should use the corrected
version.

`3WV` has three ports and all are connected: `N2`, `N3`, and the `HE1__3WV` node inserted between it
and `HE1`. I3 therefore adds only the two terminating nodes for `PU1`. Final component count:
3 declared + 3 (I1) + 1 (I2) + 2 (I3) = **9 components**, of which the user wrote 3.

## Acceptance criteria

- [ ] Binding the brief's example produces exactly the nine components tabulated above and six
      `FS1510` diagnostics; `3WV` produces no I3 node because all three ports are connected.
- [ ] `FS1507` fires for `PU1` on the brief's example, unmodified.
- [ ] `three_way_valve`, `ThreeWayValve`, `three way valve`, `3_way_valve`, `mixing_valve` and `3wv`
      all resolve to `ThreeWayValve` with **no** diagnostic — stage 2 is silent.
- [ ] `pmp` resolves to `pump` with exactly one `FS1512`; `xyzzy` resolves to nothing and produces
      `FS1502` naming a candidate.
- [ ] `pwer=30` resolves to canonical parameter `power` with exactly one `FS1512`; a below-threshold
      `pwor=30` produces `FS1503` with `power` only as a suggested edit.
- [ ] A deliberately ambiguous input produces `FS1513` naming both candidates and binds
      `Kind = Unknown` — asserted with a fixed registry, so adding an alias later cannot silently turn
      an `FS1513` into a resolution.
- [ ] Adding an alias to one kind does not change how any `samples/` script resolves — the whole
      corpus is re-bound and compared, because cross-kind interference is the failure mode stage 3
      invites.
- [ ] Similarity is **not** applied to component names: a script declaring `PU1` and referencing `PUI`
      produces a dangling reference, never a match.
- [ ] The printer round-trips an aliased script byte for byte; the model contract carries the canonical
      keyword for the same script.
- [ ] `characteristic=equal_percentage` binds as a symbol; `characteristic=nonsense` produces `FS1514`
      listing the three accepted names.
- [ ] Every alias in the registry appears on its kind's `/docs` page under "also written as", asserted
      by the docs gate.
- [ ] A parameter the user omitted is absent from `Parameters`, verified by a test that asserts
      `ContainsKey` is false rather than that the value is null.
- [ ] Lowering fixtures select Duty with rating-only fields, Rated with a secondary thermal-profile
      field and no connection, and Coupled with both secondary ports connected.
- [ ] `T1 container v=300` binds to canonical kind `tank` and canonical parameter `volume` without a
      diagnostic; source and printer retain `container` and `v` byte for byte.
- [ ] `T1.in2` materializes `in2`; `in17` produces `FS1516`; unused `in3`…`in16` do not appear in the
      semantic model. Unqualified `A - T1 - B` binds `in1` then `out1`.
- [ ] `Origin` round-trips: every component with `DeclarationSpan == null` has an inferred origin.
- [ ] An architecture test asserts no tier-20 type is referenced from the binder's assembly namespace.
- [ ] Every `FS15xx` code has a triggering test.
- [ ] Binding a script of 10 000 statements completes within the editor's debounce budget (300 ms).

## Open questions

None. `FS1507` and `FS1511` remain warnings on the live compile path and are errors for an explicit
solve (`42`). Core supplies the production component registry to `IBinder.Bind`; tier-10 unit tests
supply a fake registry, preserving the binder's dependency direction without creating a second
manifest format.
