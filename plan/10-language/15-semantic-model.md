---
id: 15-semantic-model
title: Semantic model and binding
tier: 10-language
status: reviewed
owns: [binder, symbol table, semantic model types, component registry, kind resolution and aliases, lowering boundary]
depends_on: [12-grammar, 13-type-and-unit-system, 14-expressions-and-references]
traces_to: [R-01, R-02, R-06, R-16, R-45, R-46, R-47, R-49]
open_questions: 0
last_review_pass: 6
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
    /// <remarks>
    /// Empty for fixed-port kinds; <c>tank</c> declares <c>in[n]</c>/<c>out[n]</c> (`D-32`, `D-120`).
    /// The first member of a family is a fixed row in <see cref="Ports"/> (<c>in</c>, keyed
    /// <c>in1</c>) and the family proper starts at 2, so an unqualified endpoint binds <c>in</c> by
    /// the same order rule as on any other kind.
    /// </remarks>
    public required ImmutableArray<PortFamilyInfo> PortFamilies { get; init; }

    /// <summary>Patterned parameter families such as tank layer temperatures and port elevations.</summary>
    public required ImmutableArray<IndexedParameterFamilyInfo> IndexedParameterFamilies { get; init; }

    /// <summary>Whether this kind can contribute net hydraulic head and satisfy FS2214.</summary>
    /// <remarks>Explicit registry metadata; never inferred from residual implementation (`D-30`).</remarks>
    public required bool DrivesFlow { get; init; }

    /// <summary>Whether this kind accepts any number of unnamed connections.</summary>
    /// <remarks>
    /// True for <c>node</c> and nothing else. Without it the binder has to know that the kind spelled
    /// <c>node</c> is special, which is the one thing the registry exists to prevent: a second
    /// unlimited-port kind could not then be added without editing the binder.
    /// </remarks>
    public bool HasUnlimitedPorts { get; init; }

    /// <summary>Letter code used in this kind's equipment tag — <c>PU</c>, <c>HE</c>, <c>TV</c> (`D-34`).</summary>
    /// <value>
    /// Null for a kind that carries no tag. <c>node</c> and <c>pipe</c> are null deliberately: they
    /// are mostly inferred, they outnumber every other kind, and no plant schedule tags them.
    /// </value>
    /// <remarks>
    /// Registry data rather than a hard-coded table, so a new kind ships its own code and a house
    /// convention that writes <c>LP</c> for a pump instead of <c>PU</c> is a data change. A code must
    /// not make any tag lex as a quantity literal, which is asserted against the unit-symbol table
    /// when the registry is built (invariant 16).
    /// </remarks>
    public string? TagCode { get; init; }

    /// <summary>Every parameter this kind accepts, keyed by <see cref="ParameterInfo.Key"/>.</summary>
    /// <remarks>
    /// The key is the model's identifier -- what a stated parameter, a sizing decision and the wire
    /// carry; the script spelling is <see cref="ParameterInfo.Name"/>, reached through
    /// <c>ResolveParameter</c> (`D-120`). The two coincide for every parameter without a port index.
    /// </remarks>
    public required ImmutableDictionary<string, ParameterInfo> Parameters { get; init; }

    /// <summary>Properties referenceable as <c>Name.property</c>.</summary>
    public required ImmutableDictionary<string, PropertyInfo> Properties { get; init; }
}

public sealed record ParameterInfo
{
    /// <summary>The script spelling: <c>power</c>, <c>in.t</c>, <c>secondary.in.flow</c> (`D-120`, `D-179`).</summary>
    public required string Name { get; init; }

    /// <summary>The model's identifier, which the bound symbol, the sizes and the wire carry: <c>in2</c> for <c>secondary.in.t</c>, <c>flow2</c> for <c>secondary.in.flow</c>. Defaults to <see cref="Name"/>.</summary>
    public string Key { get; init; }


    /// <summary>Curated input spellings. Binding stores <see cref="Key"/>; printing preserves source.</summary>
    public required ImmutableArray<string> Aliases { get; init; }

    /// <summary>What shape of value this parameter accepts.</summary>
    /// <remarks>
    /// <see cref="ParameterValueKind.Quantity"/> for everything dimensioned;
    /// <see cref="ParameterValueKind.Symbol"/> for a closed set of names such as a valve's
    /// <c>characteristic</c>; <see cref="ParameterValueKind.Reference"/> for a controller's
    /// <c>reads</c> and <c>moves</c>, which name another component's property or parameter
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

    /// <summary>What omission means: size the value, apply a visible default, or report it.</summary>
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
public enum ParameterOmissionBehavior { Size, Default, Require }   // Require: D-64

public sealed record PortInfo
{
    public required string Name { get; init; }
    public required PortRole Role { get; init; }        // Inlet | Outlet | Bidirectional
    /// <summary>Whether the port may be left unconnected without inference rule I3 firing.</summary>
    public required bool IsOptional { get; init; }
}

public sealed record PortFamilyInfo
{
    /// <summary>The word before the index, such as <c>in</c>. A member is written <c>in[n]</c> (<see cref="Name"/>), keyed <c>in{n}</c> (<see cref="Key"/>).</summary>
    public required string Prefix { get; init; }
    /// <summary>The script spelling with one <c>{index}</c> placeholder: <c>in[{index}]</c>.</summary>
    public string Pattern { get; }
    public required int MinIndex { get; init; }
    public required int MaxIndex { get; init; }
    public required PortRole Role { get; init; }
    /// <summary>Associated normalized-height parameter key suffix; <c>_level</c> for a tank. The script writes <c>in[n].level</c>.</summary>
    public required string? LevelParameterSuffix { get; init; }
}

public sealed record IndexedParameterFamilyInfo
{
    /// <summary>Canonical pattern with one <c>{index}</c> placeholder, as a script writes it.</summary>
    /// <value><c>layer[{index}].t</c>, <c>in[{index}].level</c>, or <c>out[{index}].level</c>.</value>
    public required string Pattern { get; init; }
    /// <summary>The pattern of the key a member is stored under: <c>t{index}</c>, <c>in{index}_level</c>.</summary>
    public required string KeyPattern { get; init; }
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
| Controller | `controller` | `pi`, `pid`, `p`, `thermostat` |

**`pi`, `pid` and `p` are aliases of `controller`**, so every algorithm spelling a user might reach for
resolves to the one `controller` kind — which is what keeps one `TagCode` and one `/docs` page covering
all of them. The algorithm itself is the declaration's `type` (`D-168`).

No alias can collide with a keyword: the lexer reserves nothing, and a statement word is recognised
only at a line's start, where a kind never stands ([`19`](19-fluidscript-2.md) §Lines, blocks and
names). The registry-build check asserts that no two kinds share a normalised alias (invariant 9).

`controller` is in the table although [`22-component-model`](../20-core-domain/22-component-model.md)
describes six *flow-component families*: a controller is a registry kind with no ports, declared like any
other component and excluded from the flow graph
([`34-controllers`](../30-solver/34-controllers.md)).

**Aliases are curated, not generated**, and the list above is the whole of it. Each entry is a word a
designer actually uses for that component, so `radiator` and `boiler` reaching `heat_exchanger` is
information the registry carries rather than a coincidence of edit distance —
The canonical `heat_exchanger` name is fixed for v1; aliases keep familiar domain spellings explicit
without multiplying component physics.

`D-91` gives the heat-exchanger role aliases one deliberate boundary meaning without creating new
physics kinds. `load`, `cooler`, `radiator`, and `chiller` read `power` as a positive capacity and
lower it to negative side-1 heat flow; `heater` and `boiler` lower it to positive heat flow. The
neutral `heat_exchanger`, `exchanger`, and `hx` spellings remain signed. `WrittenKind` is therefore
not presentation-only for this family: lowering must retain it until the signed core duty is formed.

`fan` and `duct` are deliberately absent (`D-28`). A pump/pipe alias would accept an air-side script
while omitting humidity balance, condensation, leakage, fan curves, and compressibility. Unknown air
kinds therefore fail clearly instead of producing a hydronic answer wearing air-side names.

`tank` also has the curated **parameter** alias `v` → `volume` (`D-32`). Parameter aliases obey the
same rule as kind aliases: the semantic map and metadata use `volume`; the lossless printer leaves
`v` exactly as written. Similarity is scored only after exact canonical/alias lookup.

### Stage 3 — similarity, which suggests and never binds

An input that matches nothing exactly is scored against every normalised keyword and alias. **It never
binds** (`D-170`, which supersedes `D-15`'s third stage; `FS1512`, the note a near binding once
carried, is retired): a near kind is `FS1502` and a near parameter `FS1503`, both
errors, and a near circuit role is `FS1519`, placed neutrally; the spelling it was near is the message's
suggestion and its one-click fix.
Measured 2026-09-25, before `D-170`: `PU1 pmp haed=15` bound as a pump with `head = 15`, and
`RAD radiators power=30` as a neutral exchanger that put 30 kW *into* the water.

| Rule | Value | Reasoning |
|---|---|---|
| Score | `1 − damerau_levenshtein(a, b) / max(len(a), len(b))` | Normalised edit distance with transposition, because `pmup` for `pump` is one keystroke, not two |
| Threshold | **0.70** | `pmp`→`pump` scores 0.75 and is offered as the fix; `valve`→`pipe` scores 0.20 and is not |
| Ambiguity margin | **0.05** | If the runner-up is within this of the winner, both are named (`FS1513`) and neither is the fix |
| Suggestion floor | **0.60** | Below this a failed match carries no suggestion at all |
| Tie-break | none — ambiguity is reported | See below |

**The ambiguity margin is not optional, and it is the part that makes this specifiable at all.**
`4_way_valve` — a real device this version does not model — normalises to `4wayvalve`, which is
exactly one substitution from both `2wayvalve` and `3wayvalve` and therefore scores **0.889 against
`valve` and 0.889 against `three_way_valve`**. Picking the higher of two equal candidates is a coin
flip that would offer a wrong fix as the one-click answer. Below the margin the input is `FS1513` with
both candidates ranked, which is a question the user answers in one keystroke.

*(This example previously read "`valv` scores 0.80 against `valve` and 0.78 against a normalised
`3_way_valve` prefix". No prefix is computed anywhere: under the formula above, `valv` scores 0.80
against `valve` and 0.44 against `3wayvalve`, so it resolves cleanly and was never the ambiguous
case. The margin is still needed — `4_way_valve` is what needs it.)* Without the margin the rule is "resolve to the best match", which is not deterministic in
any useful sense — it depends on the alias list's contents, so adding an alias for one component could
silently change how a *different* script resolves.

**A failed match below the suggestion floor carries no suggestion, and `FS1502` has a second message
for that.** The floor exists because `fan` is two edits from `tank` in four characters and scores
exactly 0.50: `D-28` wants an air-side kind to fail clearly rather than be nudged toward a hydronic
one, and *"There is no 'fan'. Did you mean 'tank'?"* is worse than saying only that there is no `fan`.
Above the floor a suggestion still earns its place — `exchan` scores 0.67 against `exchanger`, too far
to act on and plainly aimed at it.

**A near kind or parameter is an error, not a note.** `D-15` bound it and said so as information, which the
log hides by default; a typo in a parameter's name then became a constraint the user never stated, which is a wrong
answer that compiles. The fix the user accepts in one click costs a second (`D-170`).

### What resolution does not do

- **It never invents a kind.** Anything but an exact or curated spelling binds with `Kind = Unknown`
  and the script continues (P4).
- **It never runs on component *names*.** `PU1` and `PUI` are different components, and a typo in a
  name must produce a dangling reference, not a silent merge. Similarity suggests for `kind-name`,
  `parameter` names (`FS1503`'s suggestion) and circuit roles — all closed sets the registry owns.
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
    /// <summary>Every circuit in the script, in declaration order (`D-33`).</summary>
    /// <remarks>
    /// Non-empty for any script with a <c>circuit</c> block. A script with none binds a single
    /// implicit circuit named for the file, so consumers never special-case an empty collection.
    /// </remarks>
    public required ImmutableArray<CircuitSymbol> Circuits { get; init; }

    /// <summary>File-wide settings from the <c>project</c> block (`D-37`, `D-165`).</summary>
    public required ProjectSettings Project { get; init; }

    /// <summary>Controller bindings, in declaration order (`D-40`, `D-168`).</summary>
    public required ImmutableArray<ControlBindingSymbol> ControlBindings { get; init; }

    /// <summary>Every scheduled change, in declaration order.</summary>
    public required ImmutableArray<DisturbanceSymbol> Disturbances { get; init; }
    public required ImmutableArray<ComponentSymbol> Components { get; init; }
    public required ImmutableArray<ConnectionSymbol> Connections { get; init; }
    public required ImmutableArray<BindingSymbol> Bindings { get; init; }
    public required StyleSettings Style { get; init; }

    /// <summary>Maps a source position back to the symbol it declares or references.</summary>
    /// <remarks>Backs hover, go-to-definition, and canvas write-back's need to find the line
    /// that owns a value (R-25).</remarks>
    public required ISymbolMap SymbolMap { get; init; }
}

public sealed record CircuitSymbol
{
    /// <summary>The title written in the circuit's head, or <c>circuit 1</c>, <c>circuit 2</c>, … in file
    /// order when none is written (`19`).</summary>
    public required string Name { get; init; }

    /// <summary>The circuit's designation, used as the leading part of every tag it owns.</summary>
    /// <value>As written, or resolved by the binder as the lowest unused multiple of 100 in
    /// declaration order when the block states no <c>number</c> (`D-33`).</value>
    public required int Number { get; init; }

    /// <summary>Whether the number was written or resolved.</summary>
    /// <remarks>The printer needs this to reproduce the source byte for byte, and the canvas uses it
    /// to render a resolved number at reduced emphasis, as it does any inferred value (`P3`).</remarks>
    public required bool NumberIsExplicit { get; init; }

    public required string Substance { get; init; }

    /// <summary>Steady or transient for this circuit.</summary>
    /// <remarks>Static as bound: the design solve is an equilibrium. A run projects the model and makes every
    /// circuit dynamic except those its <c>steady</c> list names (`D-169`).</remarks>
    public required FluidMode Mode { get; init; }

    /// <summary>The circuit's role, resolved from its <c>role</c> setting through the role registry (`D-35`).</summary>
    /// <value><c>Neutral</c> when none is written or the one written matches no role exactly (`FS1519`) — never an error.</value>
    public required CircuitRole Role { get; init; }

    public required TextSpan DeclarationSpan { get; init; }
}

// A circuit is wired to its parent by connections: there is no attachment statement (`D-166`), and a
// circuit's parent is derived from the nodes its links reach (`25`).

/// <summary>A circuit's thermal classification, feeding `D-31` staging.</summary>
/// <remarks>Registry data, not a closed set in the language: adding a role is a registry change,
/// never a grammar change (`D-35`).</remarks>
public sealed record CircuitRole(string CanonicalName, ThermalStageRole Stage);

/// <summary>File-wide settings from the <c>project</c> block (`D-37`, `D-165`).</summary>
/// <remarks>
/// Spacing is deliberately <b>not</b> here. `D-37` puts it in <see cref="StyleSettings"/>, the
/// presentation payload Core already carries without interpreting, and a second home on this record
/// would create two paths for one value — the one that gets serialized and the one that does not.
/// </remarks>
public sealed record ProjectSettings(string? Name)
{
    // ... Scenarios (the cases, in the order written) and DesignScenario (the first of them) ...

    /// <summary>Where a run's t = 0 sits on every time curve, from a run's <c>start</c> (`D-149`, `D-169`).</summary>
    /// <value>s since the Unix epoch, or null when the run states none.</value>
    public double? Start { get; init; }
}

/// <summary>A controller bound to what it drives and what it reads (`D-40`, `D-168`).</summary>
/// <remarks>
/// Every field is resolved from a named setting of the controller's one declaration, so a transposition
/// is a binding error rather than a silent reversal. A sensor holds no state at all (`D-61`).
/// </remarks>
public sealed record ControlBindingSymbol
{
    /// <summary>The controller whose declaration this is.</summary>
    public required ComponentSymbol Controller { get; init; }

    /// <summary>The settable parameter named by <c>moves</c>, such as <c>TV1.position</c>.</summary>
    /// <remarks>A bare component means its kind's one actuated parameter (`D-61`; `D-168` superseded
    /// `D-43`'s qualified-only rule); a kind with no single one is <c>FS1531</c>.</remarks>
    public required ParameterReference Actuator { get; init; }

    /// <summary>The property named by <c>reads</c>, such as <c>N2.t</c>.</summary>
    public required PropertyReference Measurement { get; init; }

    /// <summary>The target value named by <c>setpoint</c>, in the measurement's dimension.</summary>
    public required Quantity Setpoint { get; init; }

    public required TextSpan Span { get; init; }
}

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

/// <summary>Presentation values: the project's default style and the spacing.</summary>
/// <remarks>
/// <see cref="Default"/> is the project's <c>style:</c> block classified (`D-104`, `D-171`): stroke, width,
/// corner, pattern and fill, each null when unstated; a circuit's own <c>style:</c> block overrides it for
/// that circuit's components. <see cref="Spacing"/> is the <c>spacing</c> setting in world units -- the
/// layout margin since `D-103` -- or null when the script states none, in which case
/// <c>LayoutSolver.DefaultMargin</c> (0.5) applies. Core resolves; the renderer draws what `26` carries.
/// </remarks>
public sealed record StyleSettings(
    double? Spacing,
    StyleSpec Default);

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

    /// <summary>Name of the circuit this component was declared in (`D-33`).</summary>
    /// <remarks>For a two-sided component this is the owning circuit under `D-36`, which may differ
    /// from the circuit whose block the declaration sits in.</remarks>
    public required string CircuitName { get; init; }

    /// <summary>The derived equipment tag, such as <c>400PU01</c>, or null when the kind has no
    /// tag code (`D-34`).</summary>
    /// <remarks>
    /// <b>Metadata, never identity.</b> <see cref="Name"/> is what every consumer keys on — selection,
    /// diagnostics, write-back, export. A tag changes whenever a declaration is inserted above this
    /// one; a name does not, and that difference is the whole content of `D-34`. Nothing may index by
    /// this field.
    /// </remarks>
    public string? Tag { get; init; }
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
dictionary is unambiguous; the kind registry then selects sizing, a binding visible default, or a
diagnostic under its omission policy (`D-02`, `D-32`, `D-64`); in a run a stated value holds at
t = 0 and `D-140` says what it means afterwards.

**`Require` is the third policy, and it is deliberately rare** (`D-64`). Absence is a diagnostic
(`FS2117`) rather than a value, and it is right only where every possible substitute would be a guess
about the *plant* rather than about the model — a `inlet`'s temperature is the whole of the current
list. Sizing can choose a pipe bore because a bore is a consequence of the model; nothing can choose
the temperature of water arriving from outside it. A `Require` parameter has no `DefaultLiteral` and
no `DefaultBasis`, exactly as `Size` does not.

**A group may also have a lower bound.** `ParameterGroupInfo.Freedoms` caps how many of a related set
may be stated (`FS2101`); `Minimum` requires how few (`FS2118`), and today only a boundary's
`flow`/`p` pair has one. The two halves are the same rule read from both ends, which is why they are
one registry object rather than a group and a separate requirement list.

**`Origin` is on every component.** The canvas must show which components the user wrote and which the
language created (`R-23`, and principle P3's justification), diagnostics must not point at a span an
inferred component does not have, and write-back must refuse to edit a component with no declaration.
Carrying it as data beats deriving it from a null span.

## Tag derivation

Every device gets a tag of the form `<circuit><code><ordinal>` — `400PU01` — computed after binding
and carried in the model contract (`D-34`).

| Part | Rule |
|---|---|
| `<circuit>` | The owning circuit's `Number`. Ownership is declaration for a one-sided component, `D-36`'s enthalpy-losing side for a two-sided one. |
| `<code>` | `ComponentKindInfo.TagCode` ([`22-component-model`](../20-core-domain/22-component-model.md)). A kind with no code — `node`, `pipe` — gets no tag. |
| `<ordinal>` | Two digits from `01`, counted per `(circuit, code)` **in declaration order**, zero-padded and widening past 99 rather than wrapping. |

Inferred components are never tagged. They have no declaration to order by, their count changes with
unrelated edits, and tagging scaffolding the user did not write would put `HE1__3WV` on an equipment
schedule.

**Declaration order, not topological order, and the choice is load-bearing.** A topological ordinal
would put the supply sensor before the return one, which is how a drafter numbers a finished drawing.
But a finished drawing is not edited live: topological ordinals move whenever a *connection* changes,
and a connection edit is the most common edit there is. Declaration order moves a tag only when a
declaration moves, which the user can see on the line they are editing. The cost is accepted and
visible — writing the return line first gives the return the lower ordinal.

The optional `.NN` branch extension (`100TE01.02`) appends a header branch ordinal. The format is
fixed now so it will not change later, and v1 emits it only for devices, because the case that
motivates it — a supply and a return sensor per branch — is expressible from `D-61` onward, and v1
still emits the extension only for devices.

**A tag must never lex as a quantity.** `400PU01` is safe because rule 3 of
[`12-grammar`](12-grammar.md) matches a whole word against `number , unit-symbol` and `PU01` is not a
unit symbol. A tag code that made one — a hypothetical `W` — would produce a tag the language reads as
a power. A test runs every registered tag code against the unit-symbol table and fails on a collision,
because this is a data change that breaks the language silently.

## Circuit role resolution

A circuit's `role = …` resolves through the **same stages** kind resolution uses (`D-15`, `D-170`):
normalise, then exact match against canonical names and curated aliases. `AHU`, `ahu` and
`air_handling_unit` all reach one role; `radiators` does not reach `radiator`, it is `FS1519` with
`radiator` as the fix.

An unresolved name is **not an error**. The circuit gets `ThermalStageRole.Neutral` and `FS1519`
(info), exactly as an unresolved component kind still produces a component (`P4`). A plant is full of
circuits whose function has no registry entry, and refusing to bind one would make the language
useless for the plant it is describing.

Reusing `D-15`'s stages rather than writing a second matcher is deliberate: two similarity
implementations drift, and a user who learns that `3WayValve` finds `three_way_valve` reasonably
expects `AirHandlingUnit` to find `ahu`.

## Binding order

0. **Read the file (`ScriptReader`) and the circuits.** Each `circuit "…":` block is a circuit and
   owns what it holds; a declaration or connection outside every block is `FS1802`, and a script with
   no block still gets one circuit named for the file so the model is never empty (measured 2026-09-26).
   The project block's settings bind into
   `ProjectSettings`, and `spacing` into `StyleSettings`, not `ProjectSettings` — one value, one path
   (`D-37`). Circuit numbers are assigned here — stated ones kept, omitted ones filled with the lowest
   unused multiple of 100 in declaration order — and a collision is `FS1524`. Roles resolve through the
   role registry (`FS1519`), and every circuit binds static; a run sets the modes (`D-169`).
0b. **Collect curves** (`D-57`, `D-167`). Each `curve` header and its rows become
   a `CurveSymbol`: a sorted table of `(x, y)` bare doubles, an end rule (clamped or extrapolated), and
   a driver name. Rows arriving out of order are sorted by `x`; two rows with the same `x` are
   `FS1529`, information rather than an error, and the later row wins — a step is a legitimate thing
   to write. Drivers resolve in step 4 with everything else, because a curve may name a `let` declared
   below it. `cases` binds into `ProjectSettings`, one file-wide home, per `D-37`'s "one value, one
   path".
1. **Collect declarations.** Every `ComponentDeclarationSyntax` and `LetBindingSyntax` enters the
   symbol table. The table is one per model, not one per circuit (`D-41`), and its circuit is recorded
   on the symbol. Duplicates → `FS1501` / `FS1401`. **A one-link connection line carrying pipe
   properties declares here too** (I7, `D-110`, `D-166`): one `pipe` symbol for its link, so that steps
   2 to 5 resolve its kind, bind and evaluate its parameters exactly as a written `P1 pipe length = 25`
   would be; step 7 then wires the connection through it.
2. **Resolve kinds** against the registry — normalise, exact — with similarity only to suggest (`D-170`).
   Unresolved → `FS1502`, with the near kind as its fix; ambiguous → `FS1513`. Either way the component is still created with an
   `Unknown` kind so later stages can skip it without the script collapsing (P4).
3. **Bind parameters.** Each parameter name is looked up in the kind's canonical names and curated
   aliases, by the same normalisation as a kind name. A dotted name is a port's state (`D-120`):
   its quantity steps are folded to the property table's symbols (`in[2].temperature` → `in[2].t`)
   and `[1]` is folded away (`in[1].t` → `in.t`), then the folded form and the written form are
   tried in that order -- the folded first so `in[1].level` reaches the fixed `in.level` row, the
   written second so `layer[1].t` still reaches its family. Indexed families (`layer[1].t`…,
   `in[2].level`…`out[16].level`) are matched against their declared pattern before any suggestion
   is scored, and an index outside the family is `FS1516` rather than a near miss. A pre-`D-120` spelling
   (`in=`, `in2=`, `t3=`) is not read, and `FS1536`, the note it once carried, is retired (`L-79`); a port state on a kind with unlimited unnamed ports is `FS1537`. A dotted name
   whose quantity the property table names but the port does not take is `FS1538`, listing what
   the port takes, and never a similarity match: `in.p` scores 0.75 against `in.t`, and a stated
   pressure was read as a temperature of 300 °C under `FS1512` until `D-124` (P5.13b). The bound
   symbol stores the row's `Key`, never the written text.
   Unknown → `FS1503` listing the accepted names/patterns. The value binds
   according to `ParameterInfo.ValueKind`: a quantity is evaluated, a symbol is matched against
   `AcceptedSymbols` (`FS1514`), and a reference is recorded unevaluated (`FS1515`) for a later stage
   to resolve, because `reads = N2.t` and `reads = TE1.t` alike name a property that does not exist
   until the solve. An instrument resolves to the node it is placed on (`D-61`: `TE1.t` *is* `N2.t`),
   which is what makes the two spellings one evaluation.
4. **Build the dependency graph** over bindings, parameters, referenced properties, and curves. A
   curve is a node like any other: `power = heating` depends on `heating`, which depends on its driver,
   a `let` or `time`. A cycle through a curve is `FS1402`, the same code and the same depth-first sort
   that already reports one among `let` bindings.
5. **Evaluate** in topological order; defer what depends on solved values. **A curve reference is an
   ordinary value source**, which is the whole reason the feature costs so little here: `heating`
   resolves through `IValueScope.Lookup` exactly as a `let` does, yields a **bare** number, and
   `D-14`'s rule reinterprets it in the target parameter's canonical unit at assignment. That is what
   makes one curve drive a power, a percentage and a temperature without being told which.

   What a curve evaluates *at* is its driver's value in each case (`D-167`): the ordinary evaluation is
   the design case's, with each driver at its first element, and each other case re-evaluates, in the
   same dependency order, only the values that read a driver ([`19`](19-fluidscript-2.md) §Drivers and
   cases). A curve of `time` has a value only in a run, where the clock moves it, deferred like a
   solved property; read in the design solve it is `FS1528` — an error naming the driver, never a
   default, because guessing zero puts a number in front of an engineer that nothing chose.

   **A component with a `sized_at` setting reads every curve at its own point** (`D-94`, `D-175`,
   `D-176`). `sized_at.outdoor = -5 C` names a driver `let` by its exact spelling, with a value in the
   `let`'s unit; the point binds under `ValueId.SizingPoint`, and each of the component's parameters is
   ordered after it. A curve reference then asks *whose* parameter is reading: one belonging to such a
   component walks the curve chain afresh with the override, falling back to the case's value for any
   driver the setting did not name, so two components reading one curve at two points each get their
   own number and the curve's stored value never changes. A point none of the component's parameters
   reads is `FS1549`. The published
   `ComponentSymbol.SizingPoint` carries the evaluated point and `ParameterValue.Basis` the sentence
   the solve report shows — *27.174 kW at tout=-5, 0.54 of the 50 kW the design day asks* — from the
   same expression evaluated once more at the file's point.
6. **Materialize indexed ports.** A tank starts with `in1` and `out1`. A qualified endpoint or an
   elevation parameter creates the named port after validating its 1…16 index. Ports not evidenced by
   source do not exist in the bound model or model contract.
7. **Bind connections.** Each endpoint resolves to a component symbol and a port. An unqualified tank
   endpoint on the right of `-` takes the next free inlet; one on the left takes the next free outlet.
   Multiple-port tank examples qualify every endpoint so source reordering cannot change intent. This
   is where the inference rules fire.
8b. **Propagate heights** (`D-70`, `D-95`), after inference and before observers. A union-find over
   connection endpoints: a single-height component's ports share one class, a pipe's two ports are
   two classes, and a bare node-to-node connection joins nothing — it spans heights like a pipe. Each
   class takes the first `elevation` stated in it, in declaration order; a second, different one is
   `FS2219` on that declaration, naming both components; a class with none reads 0. The result is
   `SemanticModel.Heights`, keyed by component name and by `pipe.port`, from which lowering reads a
   node's height and a pipe's rise. The map is derived, never a parameter: nothing here changes what
   the script states, and a script with no height in it reads 0 everywhere and means what it did.
8c. **Propagate port pressures** (`D-124`), after inference for the same reason. Every stated
   `port.p` (key `p_<port>`) is copied onto the `p` of the node that port is wired to — the node the
   script named, the one I2 inserted, or the port's own point on a junction (I9, `D-183`) — as a `ParameterValue` keeping the component's span and a
   `WrittenName` of the form `PU1 out.p`, so a diagnostic about the node's pressure and a write-back
   to it land on the line that stated it, and `FS2210` names `PU1 out.p` rather than `HE1__PU1.p`.
   A node whose `p` is already present — its own, or an earlier port's — is `FS1539` on the later
   statement, agreeing or not. The component keeps its `p_<port>` value too; nothing downstream reads
   it, and the wire's `parameters` shows the line as written. Unlike heights this *is* a statement:
   the node's pressure is exactly as constrained as if the node had written it.
8. **Bind controllers and runs.** A controller block (`19`) binds as one control binding: `moves` resolves
   through the registry's spellings to the key of the parameter it moves (`FS1522` when the kind has none
   of that name, `FS1531` when a bare component has no single one), `reads` to a property the kind has
   (`FS1406`) or to what a sensor measures, `setpoint` to a quantity in the measured dimension, and a
   missing required setting is `FS1521`. A run's events (`at 60 s HE4.power = 45`) resolve their
   `component.parameter` target the same way `moves` does, and evaluate their times against `Time` and
   their values against the target parameter's dimension, so `45` there is forty-five kilowatts by
   `D-14`'s bare-number rule exactly as `power = 45` would be. A circuit is wired to its parent by
   connections, which inference treats like any other (`D-166`).

9. **Apply inference rules** I1, I2, I3, I9 in that order — order matters, since I2 can only run once I1
   has created the undeclared nodes, I3 can only run once every connection has claimed its port, and
   I9 can only count a junction's connections once I2 and I3 have added theirs.
   I7 ran already, in step 1, for the same reason in reverse: a pipe's parameters must exist before
   anything evaluates them, and I2's nodes beside it need the pipe to exist.
10. **Validate.** `FS1507` skips two things on purpose (`L-32`). A kind with **no ports at all** is
    never warned about — a controller appears in no connection by design, because its `moves` and
    `reads` bind it rather than topology, so warning would put a squiggle on every script with a
    controller. A declared **`node`** is skipped for a weaker reason and it is a judgement rather than a
    derivation: a node may legitimately be declared as a datum another circuit's connection is about
    to reach, and a lone declared node is more often a datum the user is about to wire than a mistake. The cost is that
    a genuinely orphaned `node` is silent; `FS2107` catches the dead-end case once topology runs, which
    is where the evidence to tell them apart exists.

    A declared component in no connection is `FS1507`; a *cluster* of two or more
    connected to each other and to nothing else in their circuit is `FS1511`. The two partition one
    mistake and never both fire for one component, so connectivity is judged on the connections the
    **user wrote**, before inference — after I3 nothing is unconnected and neither code could fire
    again. A degree-one node with no `t`, `p` or `flow` is `FS2107` (owned by
    [`22-component-model`](../20-core-domain/22-component-model.md), raised here because the binder is
    the first stage that can count a degree), **except one inferred by I3**, which *is* the boundary
    that rule created and therefore terminates a port rather than dead-ending on one.
11. **Assign tags.** Per the derivation above, after every declaration is known and ordered. Tags are
    computed last because an ordinal depends on the complete declaration set of its circuit, and
    because nothing in binding may depend on a tag — a stage that read one would make identity
    circular.

Steps 1–5 have no notion of topology, and steps 6–10 have no notion of expressions with two narrow
exceptions: a controller's `setpoint` and a run's event times and values are quantities, and step 9 evaluates them
rather than a third pass existing for four expressions. The split keeps each half testable alone. Step 0 knows about neither, which is what lets circuit partitioning be tested
against a syntax tree with no registry at all.

**Step 11 is last, and its position is a contract rather than a convenience.** A tag is derived from
the finished declaration set, so computing it earlier would make it depend on how much of the file had
been bound. Nothing in steps 0–10 may read `ComponentSymbol.Tag`; an architecture test asserts it,
because a binder stage that resolved a reference by tag would reintroduce exactly the identity `D-34`
removed.

The binder preserves the exact presence of `secondary.in.t`, `secondary.out.t`, `secondary.in.dt`, and `secondary.in.flow`
(keyed `in2`, `out2`, `dt2`, `flow2`) plus secondary-port connections. During lowering, the heat-exchanger factory derives `duty`, `rated`, or `coupled` from
that evidence using `D-19`'s precedence. It must not collapse “secondary ports unconnected” to Duty:
that would erase Rated external-profile designs before Core sees them.

### The role registries

`CircuitRoleRegistry` is **implementation-defined** — this project chooses the entries, no standard
supplies them, and a script that names something else gets a diagnostic rather than a guess. It was
defined in code and enumerated in no document, which is what `L-20` and `L-39` recorded; the set is
below, and this document is now its home.

A role is resolved by **normalised spelling**: the canonical name, or any alias, case-insensitively. An
unknown circuit role is `FS1519`, and its message lists the canonical names only — the aliases exist so a user's first guess lands, not to be memorised.

**Circuit roles** (`D-35`). The default is `neutral`, which claims nothing and lays out in written
order; every other role is a layout hint that [`25-layout-hints`](../20-core-domain/25-layout-hints.md)
maps to a stage.

| Role | Aliases |
|---|---|
| `neutral` | *(the default; also the role of a circuit that names none)* |
| `ahu` | `air_handling_unit`, `ventilation`, `air_handler` |
| `cooling` | `chilled_water`, `cooling_circuit`, `cooling_loop` |
| `district` | `district_heating`, `district_loop` |
| `distribution` | `primary`, `header`, `distribution_header` |
| `ground_loop` | `ground_source`, `borehole`, `brine` |
| `heat_pump` | `heatpump`, `hp` |
| `heating` | `heating_circuit`, `secondary` |
| `hot_water` | `dhw`, `domestic_hot_water`, `tap_water` |
| `radiator` | `radiators`, `radiator_circuit` |
| `solar` | `solar_collector`, `solar_loop` |
| `storage` | `buffer`, `accumulator`, `storage_circuit` |
| `underfloor` | `floor_heating`, `ufh`, `underfloor_heating` |

**A driver is not a role.** It is any `let` whose value is one per case, named as the script names it
(`D-167`), so there is no driver registry: a plant driven by its production rate needs no entry.

**Adding an entry is not a breaking change and removing one is**, which is the asymmetry to hold on to:
a new alias makes a previously rejected script legal, and dropping one makes a legal script fail. The
list is therefore additive-only until a `D-` entry says otherwise.

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

**I7 — implicit pipe** (`D-110`, `D-166`). A one-link connection line that ends in pipe properties
makes one `pipe`, named `{A}__{B}` after the two endpoint identifiers (ports dropped: `PCV -
HX1.secondary.in  12 m` makes `PCV__HX1`), with an ordinal on collision as I2 appends one. The symbol is
`Origin = Inferred(I7, key)` where the key is the line's start offset and the connection's index on
it, so the same line binds to the same pipe on every parse; `WrittenKind = pipe`; its parameters are
the line's, **stated**. It is created in step 1, not step 9, because its parameters must go through
kind resolution, binding and evaluation like any declaration's; step 7 replaces the connection
`A - B` with `A - {A}__{B}.in` and `{A}__{B}.out - B`, and I2 then puts a node on each side of the
pipe whose neighbour is not a node, named `{A}__{B}__in` and `{A}__{B}__out` after the pipe's port it
joins -- so `HE1 - PU1  DN25` yields `HE1__PU1`, `HE1__PU1__in`, `HE1__PU1__out`. A `length` the line
does not state is zero, the pipe's decided default ([`22`](../20-core-domain/22-component-model.md)):
`DN25` alone marks the drawing and the bore and drops nothing. A bare line makes no pipe, and pipe
properties on a longer chain are `FS1803`.

**I8 — implicit sensor** (`D-151`). A controller whose `reads` names a node's `t`, `p` or `flow`
directly -- a node `D-150` admits, with one or two connections -- reads it through a sensor: the
binder adds a `t_sensor`, `p_sensor` or `flow_sensor` named `{Node}__{TagCode}` (`NS__TE`, `NS__PE`,
`NS__FE`) `at` the node, with `Origin = Inferred(I8, name)`, unless one of that kind already stands
there, which is then the one read. It runs in the control binding step, after the observers the
script placed are bound. The user's rule: a sensor is a physical component and is always drawn, and a
controller is joined to a sensor, never to a node. Like every inferred component it is untagged
(`D-34`) and the user promotes it by writing its name down.

**I9 — a port's own point on a junction** (`D-183`). A junction -- a node with three or more
connections -- holds one state, the mix of everything arriving. A component port wired straight to one
has no state of its own: its stated `out.t` lands on the mix (`C-123`), and its hold-up is mixed into
the other streams. So, after I3, every connection between a junction and a port of a component that is
neither a node nor a pipe is split: a node named `{Component}__{Port}` (`PU1__in`, `3WV__b`; an ordinal
on collision, as I2 appends one) goes between them, `Origin = Inferred(I9)`, in the connection's
circuit, and the junction side of it is a zero-length link. The point is a two-connection node, so its
energy balance makes its state the port's stream, and a stated `port.p` (step 8c) lands on it rather
than on the junction. Inlets get one as well as outlets, because a flow can reverse in the solve and an
inlet then discharges into the junction. A **pipe is exempt**: nothing is stated on its ends, it holds
no volume there, and its outlet stream is already reported on its own port (`C-103`), so a point
would cost the wire and the layout and settle nothing. The point is a two-connection node, so `D-150`
admits a sensor on it exactly as on an I2 node -- `TE9 t_sensor at PU1__in` measures the stub between
the junction and the pump (measured 2026-09-28: it binds with no diagnostic) -- and the junction itself
still admits none.

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
2. Every `ComponentSymbol` has a unique `Name` **within the model** (`D-41`). Circuits scope tags, not
   identifiers: two circuits may not both declare a `PU1`, and a bare name resolves the same way from
   anywhere in the script — which is what makes a connection between circuits and a controller moving a
   component of another circuit ordinary lookups rather than qualified ones.
3. A parameter absent from `ComponentSymbol.Parameters` was not written by the user. There is no other
   reason for absence.
4. Every `ConnectionSymbol` endpoint resolves to an existing `ComponentSymbol` and one of its ports.
5. Every inferred component has `DeclarationSpan == null` and a non-`Declared` `Origin`, and the
   converse holds.
6. `SymbolMap` resolves every source position within a declaration to that declaration's symbol.
7. The semantic model references no type from `FluidScript.Core.Fluids`, `.Components`, or `.Solvers`.
8. **Kind resolution is deterministic and total**: the same input and the same registry always yield
   the same kind or the same diagnostic, and no input throws.
9. **No two kinds share a normalised keyword or alias.** Asserted when the registry is built: a
   collision between kinds makes stage 2 order-dependent and stage 3 permanently ambiguous.
10. `ComponentSymbol` records the canonical `Keyword`; the alias or misspelling the user wrote survives
    only in the source text and in `DeclarationSpan`.
11. Every materialized indexed port has an index in its family's closed range, and no unevidenced
    indexed port appears in `ComponentSymbol` or the model contract.
12. A registry entry with `OmissionBehavior.Default` has a parseable `DefaultLiteral` and non-empty
    basis; one with `Size` or `Require` has neither. Defaults never appear in
    `ComponentSymbol.Parameters`, whose presence continues to mean the user wrote the parameter.
12a. A `ParameterGroupInfo` with a non-zero `Minimum` carries a descriptor to report it with, and one
    with a zero `Minimum` carries none — a bound with no message is a check that cannot fire.

13. Every `CircuitSymbol.Number` is unique within the model, and every resolved one is a multiple of
    100 that was unused when it was assigned.
13a. Every `CircuitSymbol.Name` is unique within the model. `ParentCircuit`, `component.circuit` and
    `DistributionGroup.Members` all use the name as an identity, so a duplicate would make each of
    them ambiguous in the same way a duplicate component name would (`FS1525`).
14. `ComponentSymbol.Tag` is read by no binder stage. It is output, never input.
15. A tag's ordinal sequence is contiguous from `01` within each `(circuit, code)` pair, and is a
    function of declaration order alone — permuting statements that do not change declaration order
    leaves every tag unchanged.
16. No registered `TagCode` produces a tag that lexes as a quantity literal (`FS1003`).
17. *(Removed: there is no attachment statement (`D-166`), so there is no `AttachmentSymbol` to hold to it.)*

Invariant 7 is checkable by an architecture test and should be, since it is the one a well-meaning
refactor breaks first. Invariant 14 needs the same treatment for the same reason: reading a tag during
binding is a natural-looking shortcut whose cost only appears when a user inserts a line.

## Scenarios

`D-143`. A file may declare a list of named operating cases, and any parameter may state one value
per case:

```fluidscript
project:
  cases = [winter, summer]

circuit "heating":
  HX1  heat_exchanger  power = [30, 10]
```

**The list binds to the declaration positionally and to nothing else.** Binding a scenario list is
four rules, and each one is a diagnostic rather than a repair:

1. The declared names are the count. A list of another length is `FS1540`, naming what was written,
   how many values it has and what the scenarios are called. **Nothing is padded to fit**: extending
   `[50, 60]` to four by repeating the last value invents a case nobody stated, and `D-60` already
   refused that shape of rule for timestamp formats.
2. A **scalar is not a short list.** It means the same value in every scenario, which is what a
   scalar already means, so `PU1 pump` and `power=30` need no change and no file that exists today
   acquires a length.
3. A list with no `cases` in the project block is `FS1541`, and duplicate names are `FS1544`.
4. The first case is the operating case (`D-175`): the case written first is the one the canvas
   draws, an export carries and a run starts from unless its `from` names another. A `from` naming no
   case is `FS1542`.

**Each element binds exactly as the scalar would.** An element is an ordinary `parameter-value`, so
a curve reference, an expression and a unit suffix all work inside a list, `D-14`'s bare-number rule
applies per element, and a dimension error is reported against the element that caused it rather
than the list.

**What a scenario is not.** It is not a time step and not a sequence: the cases are unordered, and
nothing interpolates between them. It is not a solve mode either — every case is solved steady, and
a run starts from the case its `from` names (`D-169`). What consumes the list is the sizing pipeline in
[`24`](../20-core-domain/24-auto-sizing.md) §Sizing over scenarios.

**A driver** (`D-167`, [`19`](19-fluidscript-2.md) §Drivers and cases). A `let` may be a
list, one value per case, and a curve may name that `let` as its driver. Step 5 evaluates the design case
with each driver at its design element, then walks the same dependency order once for every other case
over only the values that read a driver, and publishes what each parameter came to in
`ParameterValue.Scenarios` — where a list written on the parameter puts it. A parameter pinned to a curve
of a driver is therefore, from sizing onward, indistinguishable from one written as a list.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS1501` | Duplicate component name anywhere in the script | Error | `'{name}' is already declared at line {line}. Names are unique across the whole file; tags are what distinguish circuits.` |
| `FS1502` | Unknown component kind, closest candidate above the suggestion floor | Error | `There is no '{kind}'.` |
| `FS1502` | Unknown component kind, nothing close enough to suggest | Error | `There is no '{kind}'.` |
| `FS1503` | Unknown parameter for the kind | Error | `The {kind} has no '{parameter}'. It accepts: {available}.` |
| `FS1504` | Endpoint names an unknown component | Error | Handled by I1 unless the name is a declared non-component symbol, then: `'{name}' is a value, not a component.` |
| `FS1505` | Unknown port | Error | `The {kind} has no port '{port}'. Ports: {available}.` |
| `FS1506` | Port connected more than once | Error | `Port '{port}' of '{name}' is already connected at line {line}.` |
| `FS1507` | Component in no connection | Warning | `'{name}' is not connected to anything.` |
| `FS1508` | *(retired)* | — | Statements before any circuit, read into an implicit circuit. Retired (L-70, D-174), not reused: a component is declared inside a circuit block, and one outside is FS1802. |
| `FS1509` | *(retired)* | — | Meant "more than one `circuit` header", which `D-33` makes legal. Retired, not reused; left unallocated. |
| `FS1510` | A component was inferred | Info | `Added {kind} '{name}' ({rule}).` |
| `FS1511` | Graph is disconnected | Warning | `'{name}' and {count} others are not connected to the rest of the circuit.` |
| `FS1512` | *(retired)* | — | A name bound to the registered spelling it was near, with a note. Retired (D-170), not reused: only the exact spelling binds, and the near one is offered as the fix. |
| `FS1513` | A kind name is ambiguous within the margin | Error | `'{written}' could be '{first}' or '{second}'. Write one of them.` |
| `FS1514` | A symbol-valued parameter got an unaccepted name | Error | `'{parameter}' accepts {available}, not '{written}'.` |
| `FS1515` | A reference-valued parameter got something that is not a reference | Error | `'{parameter}' names a component property, like 'N2.t'.` |
| `FS1516` | An indexed port or parameter lies outside its declared family | Error | `'{written}' is outside {kind}'s supported {min}…{max} range.` |
| `FS1517` | *(retired)* | — | A circuit's own mode contradicting the project's. Retired (D-169, D-174), not reused: a run states which circuits it holds steady. |
| `FS1518` | *(retired)* | — | An attachment line naming no component. Retired (D-174), not reused: there are no attachment lines. |
| `FS1519` | A circuit's role name matched no registry entry | Info | `'{name}' is not a known circuit role, so it is placed neutrally. Known roles: {available}.` |
| `FS1520` | *(retired)* | — | A circuit with an inlet attachment and no outlet, or the reverse. Retired (D-174), not reused: there are no attachment lines. |
| `FS1521` | A controller declaration is missing `moves` or `reads` | Error | `A controller needs {list}. Missing: {missing}.` |
| `FS1522` | A controller's `moves` names a parameter that cannot be set | Error | `'{param}' of '{component}' cannot be controlled.` |
| `FS1523` | A control binding whose controller is not a `controller`; a controller's own declaration leaves no script that reaches it (`19` §Diagnostics) | Error | `'{name}' is a {kind}, not a controller.` |
| `FS1524` | Two circuits resolve to the same number | Error | `Circuit {number} is already '{owner}'. Every circuit's number is its own.` |
| `FS1525` | Two circuits share a name | Error | `'{name}' is already a circuit at line {line}.` |
| `FS1526` | *(retired)* | — | A circuit attached to two parent circuits. Retired (D-174), not reused: there are no attachment lines. |
| `FS1527` | *(retired)* | — | A curve driven by a name that was no role, curve or design value. Retired (D-167, D-174), not reused: a curve's driver is a let or time, and anything else is FS1811. |
| `FS1528` | A curve is read in the design solve and its driver has no value there: `time`, which only a run has (`D-167`) | Error | `'{curve}' follows '{driver}', which only a run has. Drive the curve by a let with one value per case, and have the run hand that let a curve of time.` |
| `FS1529` | Two curve rows share an x value | Info | `'{curve}' has two rows at {x}; the later one is used.` |
| `FS1530` | A curve has fewer than two rows | Error | `'{curve}' needs at least two rows to interpolate between.` |
| `FS1531` | A controller's `moves` or `reads` names a bare component whose kind has no single actuated parameter or measured property | Error | `The {kind} has no single {role}. Write it out, such as '{example}'.` |
| `FS1532` | An `at` clause on a kind that carries flow rather than observing it | Error | `'{name}' is a {kind}, which is not placed with 'at'. Connect it with '-' instead.` |
| `FS1533` | An instrument that was declared and never placed | Warning | `'{name}' observes nothing. Put it in a chain, such as 'A - {name} - B', or place it with 'at' and the name of a node.` |
| `FS1534` | A time curve's `format=` is not a quoted string, or names no day or no month (`D-60`) | Error | `'{curve}' has a format that cannot read a date: {reason}. Write a quoted .NET pattern with a day and a month, such as format="dd/MM/yyyy HH:mm".` |
| `FS1535` | More curve rows failed to read than are marked one by one; the rest are counted on the header (`L-40`) | Error | `'{curve}': {count} more rows could not be read; the first {shown} are marked. Check the columns and the format.` |
| `FS1536` | *(retired)* | — | A port, parameter or property in the spelling D-120 replaced (in2, t3, HX1.t_in2), bound with a note. Retired (18, L-79), not reused: those spellings are not read. |
| `FS1537` | A port's state on a kind that has one state and no ports: `N1 node in.t=50` (`D-120`) | Error | `The {kind} has one state and no ports: write '{quantity} =' rather than '{written} ='.` |
| `FS1538` | A port's quantity the kind does not take: `PU1 pump in.h=5`. Never a near miss -- `in.p` is one edit from `in.t` and was read as it (`D-124`) | Error | `The {kind}'s '{port}' has no '{quantity}'. It takes: {available}.` |
| `FS1539` | A node's pressure stated twice: on the node and as a port pressure of a component touching it, or by two ports on one node (`D-124`) | Error | `'{written}' states the pressure of '{node}', which '{other}' already states. State it once.` |
| `FS1540` | A scenario list whose length is not the declared count (`D-143`). Never padded | Error | `'{written}' states {given} {values} for {count} case{plural}: {names}. State one per case, or one value for all of them.` |
| `FS1541` | A list where the project block names no `cases` (`D-143`) | Error | `'{written}' states a list of values, but this file declares no cases. Add 'cases = [<name>, <name>]' to the project block.` |
| `FS1542` | A run's `from` names a case that was not declared (`D-143`, `D-169`) | Error | `'{name}' is not a case of this file. It declares: {names}.` |
| `FS1543` | *(retired)* | — | Scenarios with no 'design' line. Retired (D-174), not reused: the first case is the operating one. |
| `FS1544` | Two scenarios declared with one name (`D-143`) | Error | `'{name}' is declared twice. Each case needs its own name.` |
| `FS1545` | A run's `start` is not a time (`D-149`, `D-169`) | Error | `start = {value} is not a time. Write it as a date, such as start = 2026-01-15 06:00.` |
| `FS1546` | A run follows a curve of time and states no `start` (`D-149`, `D-169`) | Warning | `This follows '{curve}', which runs on the clock, and the run does not say where it starts. Add 'start = …' to the run; until then it holds the curve at its design value.` |
| `FS1547` | *(retired)* | — | A project 'start=' with no dynamic circuit to read it. Retired (D-169, D-174), not reused: 'start' is a run setting. |
| `FS1548` | A sensor's `at`, or a controller's `reads` naming a node, reads a node where more than two connections meet (`D-150`) | Error | `'{name}' reads '{node}', where {count} pipes meet, and a junction has no single stream to measure. Put a node on the pipe you mean, next to '{node}', and read that one.` |
| `FS1549` | A component's `sized_at` names a driver none of its parameters read, directly or through a curve (`D-175`) | Warning | `'{component}' is sized at {point}, and none of its parameters read '{driver}', so it changes nothing. Read '{driver}' in a parameter, directly or through a curve, or remove the point.` |

**A curve's driver has to supply a number, and two things can: a `let` or the clock**
(`D-167`). `D-59`'s permissiveness -- a driver name no registry knows is not an error, because a plant is
full of drivers nobody registered -- survives as the `let`: `let flue = [180, 150] C` is a driver of any
name, with one value per case. A curve driven by anything else is `FS1811`, whose fix is that `let`.

**`FS1532` and `FS1533` were not in this table and are additions, not corrections.** `D-61` settles
what `at` means and says nothing about writing it on a pump, or about an instrument that states no
`at` at all. Both are ordinary user mistakes with no code, and the second is the one that mattered:
an observer is exempt from `FS1507` because it is never connected to anything, so without a code of
its own an unplaced sensor bound in silence.

**`FS1534` and `FS1535` are `D-60`'s validation, added 2026-09-19 (`L-40`).** `D-60` said the format is
validated when the curve is bound and that a string with no month or no day is a diagnostic; neither
check existed, a `format=` that was not a quoted string was ignored, and every row of the curve then
failed on its own line -- a year of hourly data was 8 760 `FS1117`s for one mistake on the header.
`FS1534` is that mistake, once, on the argument; when it fires the rows are neither read nor
reported, because they are not the fault. `FS1535` is the cap on `FS1117` when the header is fine
and the rows are not: the first five are marked where they are, the rest are one count on the header.

**`FS1509` is retired, not redefined, and the distinction matters.** It meant "more than one `circuit`
header", a condition `D-33` makes legal. The tempting move is to keep the number for the nearest
surviving error — two circuits claiming one number — on the grounds that both are "a second circuit
where only one may be". [`16-diagnostics`](16-diagnostics.md)'s invariant 7 forbids it: codes are
referenced by scripts, tests, `/docs` pages and agent prompts, and a code that silently changes
meaning makes every one of those references wrong without breaking anything visibly. `FS1509` is
marked retired in the registry and left unallocated; duplicate numbers get `FS1524`.

This is the opposite treatment from `FS1103`, and the difference is the test: `FS1103`'s trigger
*widened* while its meaning held, so it kept its number. `FS1509`'s old condition is now valid input,
which is a change of meaning, not of scope.

`FS1507` and `FS1511` are warnings rather than errors because a partially-written script is the normal
editing state (P4) and erroring would blank the diagram on every keystroke.

**`FS1502` and `FS1513` are the two ends of stage 3.** `FS1502` fires for any kind written in no known
spelling, and carries the top-ranked candidate as its one-click fix when it scored at least the
suggestion floor (0.60). `FS1513` fires when two candidates scored within the margin of each other, and
offers neither. Both are errors; they differ in whether the fix is "X" or the question is "X or Y".

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
- [ ] `pmp` binds nothing: `FS1502` with `pump` as its one-click fix (`D-170`); `xyzzy` binds nothing
      and `FS1502` offers no fix.
- [ ] `pwer=30` binds nothing: `FS1503` with `power` as its one-click fix; `power` is never stated.
- [ ] A deliberately ambiguous input produces `FS1513` naming both candidates and binds
      `Kind = Unknown` — asserted with a fixed registry, so adding an alias later cannot silently turn
      an `FS1513` into a single offered fix.
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
- [x] `T1.in[2]` materializes the port keyed `in2`; `in[17]` produces `FS1516`; unused `in[3]`…`in[16]`
      do not appear in the semantic model. Unqualified `A - T1 - B` binds `in` then `out` (keyed
      `in1`, `out1`). `PortStateSyntaxTests`, `IndexedPropertyTests`.
- [ ] `Origin` round-trips: every component with `DeclarationSpan == null` has an inferred origin.
- [ ] An architecture test asserts no tier-20 type is referenced from the binder's assembly namespace.
- [ ] Every `FS15xx` code has a triggering test.
- [ ] Binding a script at the 10 000-statement input limit completes within one debounce interval
      (`D-49`), so a compile is never still running when the next one is due. The debounce is idle
      time, not a compute allowance; the compute budget is `07`'s draft-compile row.

## Open questions

None. `FS1507` and `FS1511` remain warnings on the live compile path and are errors for an explicit
solve (`42`). Core supplies the production component registry to `IBinder.Bind`; tier-10 unit tests
supply a fake registry, preserving the binder's dependency direction without creating a second
manifest format.
