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
    /// <remarks>Empty for fixed-port kinds; <c>tank</c> declares <c>in{n}</c>/<c>out{n}</c> (`D-32`).</remarks>
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
| Controller | `controller` | `pi`, `pid`, `p`, `thermostat` |

**`control` was an alias and has been removed, because `D-40` made it a reserved word.** A reserved
word cannot appear in `kind-name` position ([`12-grammar`](12-grammar.md)'s word classification), so
the alias was unwriteable: `TC1 control` lexes as a keyword and never reaches kind resolution. `pid`
and `p` join `pi` in its place, so every algorithm spelling a user might reach for resolves to the one
`controller` kind — which is what keeps one `TagCode` and one `/docs` page covering all of them.

This is the general hazard of reserving a word: it silently invalidates any alias equal to it. The
registry-build check that asserts no two kinds share a normalised alias (invariant 9) must also assert
that **no alias equals a reserved word**, or the next reserved word repeats this.

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
| Suggestion floor | **0.60** | Below this a failed match carries no suggestion at all |
| Tie-break | none — ambiguity is reported | See below |

**The ambiguity margin is not optional, and it is the part that makes this specifiable at all.**
`4_way_valve` — a real device this version does not model — normalises to `4wayvalve`, which is
exactly one substitution from both `2wayvalve` and `3wayvalve` and therefore scores **0.889 against
`valve` and 0.889 against `three_way_valve`**. Picking the higher of two equal candidates is a coin
flip that produces a silently wrong circuit. Below the margin the input is `FS1513` with both
candidates ranked, which is a question the user answers in one keystroke.

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
    /// <summary>Every circuit in the script, in declaration order (`D-33`).</summary>
    /// <remarks>
    /// Non-empty for any script with a <c>circuit</c> header. A script with none binds a single
    /// implicit circuit named for the file, so consumers never special-case an empty collection.
    /// </remarks>
    public required ImmutableArray<CircuitSymbol> Circuits { get; init; }

    /// <summary>File-wide settings from the <c>project</c> directive (`D-37`).</summary>
    public required ProjectSettings Project { get; init; }

    /// <summary>Controller bindings, in declaration order (`D-40`).</summary>
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
    /// <summary>The identifier written in the header. Also the source of <see cref="Role"/>.</summary>
    public required string Name { get; init; }

    /// <summary>The circuit's designation, used as the leading part of every tag it owns.</summary>
    /// <value>As written, or resolved by the binder as the lowest unused multiple of 100 in
    /// declaration order when the header omitted it (`D-33`).</value>
    public required int Number { get; init; }

    /// <summary>Whether the number was written or resolved.</summary>
    /// <remarks>The printer needs this to reproduce the source byte for byte, and the canvas uses it
    /// to render a resolved number at reduced emphasis, as it does any inferred value (`P3`).</remarks>
    public required bool NumberIsExplicit { get; init; }

    public required string Substance { get; init; }

    /// <summary>Steady or transient for this circuit, after applying `D-37`'s precedence.</summary>
    /// <remarks>The circuit's own <c>fluid dynamic|static</c> wins; otherwise the project default;
    /// otherwise <see cref="FluidMode.Static"/>. A circuit contradicting the project gets
    /// <c>FS1517</c> and keeps its own value.</remarks>
    public required FluidMode Mode { get; init; }

    /// <summary>The circuit's role, resolved from <see cref="Name"/> through the role registry (`D-35`).</summary>
    /// <value><c>Neutral</c> when the name matches no role — never an error.</value>
    public required CircuitRole Role { get; init; }

    /// <summary>The circuit both attachments resolve into, or null when this circuit stands alone.</summary>
    /// <remarks>Derived, not written: it is the circuit owning <see cref="Supply"/>'s and
    /// <see cref="Return"/>'s resolved components, which must be the same one (`FS1526`).</remarks>
    public string? ParentCircuit { get; init; }

    /// <summary>Where this circuit takes flow from its parent, or null when it stands alone (`D-33`).</summary>
    public AttachmentSymbol? Supply { get; init; }

    /// <summary>Where this circuit returns flow to its parent, or null when it stands alone.</summary>
    public AttachmentSymbol? Return { get; init; }

    public required TextSpan DeclarationSpan { get; init; }
}

/// <summary>One end of a subcircuit's attachment to its parent (`D-33`).</summary>
/// <remarks><see cref="ParentComponent"/> is resolved during connection binding, so it is null
/// while the named node does not exist; that condition is <c>FS1518</c>, not an exception. A name that
/// resolves to this circuit's own component is <c>FS2217</c> instead — the two never both fire.</remarks>
public sealed record AttachmentSymbol(
    string ParentComponentName,
    ComponentSymbol? ParentComponent,
    TextSpan Span);

/// <summary>A circuit's thermal classification, feeding `D-31` staging.</summary>
/// <remarks>Registry data, not a closed set in the language: adding a role is a registry change,
/// never a grammar change (`D-35`).</remarks>
public sealed record CircuitRole(string CanonicalName, ThermalStageRole Stage);

/// <summary>File-wide settings from the <c>project</c> directive (`D-37`).</summary>
/// <remarks>
/// Spacing is deliberately <b>not</b> here. `D-37` puts it in <see cref="StyleSettings"/>, the
/// presentation payload Core already carries without interpreting, and a second home on this record
/// would create two paths for one value — the one that gets serialized and the one that does not.
/// </remarks>
public sealed record ProjectSettings(
    string? Name,
    FluidMode? DefaultMode);

/// <summary>A controller bound to what it drives and what it reads (`D-40`).</summary>
/// <remarks>
/// Every field is resolved from a named argument, so a transposition is a binding error rather than
/// a silent reversal. <see cref="Setpoint"/> lives here rather than on a sensor because `D-23`
/// defers persistent sensor components.
/// </remarks>
public sealed record ControlBindingSymbol
{
    /// <summary>The controller component named by <c>by=</c>.</summary>
    public required ComponentSymbol Controller { get; init; }

    /// <summary>The settable parameter named by <c>actuate=</c>, such as <c>TV1.position</c>.</summary>
    /// <remarks>Always qualified as <c>component.parameter</c>; a bare component name is <c>FS1515</c>
    /// (`D-43`). There is no per-kind default actuator to fall back on, deliberately.</remarks>
    public required ParameterReference Actuator { get; init; }

    /// <summary>The property named by <c>measure=</c>, such as <c>N2.t</c>.</summary>
    public required PropertyReference Measurement { get; init; }

    /// <summary>The target value named by <c>setpoint=</c>, in the measurement's dimension.</summary>
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

/// <summary>Presentation values Core carries and never interprets.</summary>
/// <remarks>
/// <see cref="Tokens"/> holds the <c>style</c> directive's positional tokens verbatim.
/// <see cref="Spacing"/> is the <c>spacing</c> directive's value in world units, or null when the
/// script states none, in which case the renderer's own default applies (`D-37`).
/// </remarks>
public sealed record StyleSettings(
    ImmutableArray<StyleTokenSyntax> Tokens,
    double? Spacing);

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
dictionary is unambiguous; the kind registry then selects sizing or a binding visible default under
its omission policy (`D-02`, `D-32`).

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
motivates it — a supply and a return sensor per branch — needs the sensors `D-23` defers.

**A tag must never lex as a quantity.** `400PU01` is safe because rule 3 of
[`12-grammar`](12-grammar.md) matches a whole word against `number , unit-symbol` and `PU01` is not a
unit symbol. A tag code that made one — a hypothetical `W` — would produce a tag the language reads as
a power. A test runs every registered tag code against the unit-symbol table and fails on a collision,
because this is a data change that breaks the language silently.

## Circuit role resolution

`circuit AHU 101`'s role comes from the header name through the **same three stages** kind resolution
uses (`D-15`): normalise, exact match against canonical names and curated aliases, then similarity.
`AHU`, `ahu` and `air_handling_unit` all reach one role; `radiators` reaches `radiator` by similarity.

An unresolved name is **not an error**. The circuit gets `ThermalStageRole.Neutral` and `FS1519`
(info), exactly as an unresolved component kind still produces a component (`P4`). A plant is full of
circuits whose function has no registry entry, and refusing to bind one would make the language
useless for the plant it is describing.

Reusing `D-15`'s stages rather than writing a second matcher is deliberate: two similarity
implementations drift, and a user who learns that `3WayValve` finds `three_way_valve` reasonably
expects `AirHandlingUnit` to find `ahu`.

## Binding order

0. **Partition into circuits and read the file-wide settings.** Each `CircuitHeaderSyntax` opens a
   circuit that owns every statement until the next header; a script with no header gets one implicit
   circuit named for the file (`FS1508`). `project` binds into `ProjectSettings`; `spacing` binds into
   `StyleSettings`, not `ProjectSettings` — one value, one path (`D-37`).
   Circuit numbers are assigned here — stated ones kept, omitted ones filled with the lowest unused
   multiple of 100 in declaration order — and a collision is `FS1524`. Roles resolve through the role
   registry (`FS1519`), and each circuit's `Mode` is settled by `D-37`'s precedence (`FS1517`).
0b. **Collect curves and the design point** (`D-57`, `D-58`). Each `curve` header and its rows become
   a `CurveSymbol`: a sorted table of `(x, y)` bare doubles, an end rule (clamped or extrapolated), and
   a driver name. Rows arriving out of order are sorted by `x`; two rows with the same `x` are
   `FS1529`, information rather than an error, and the later row wins — a step is a legitimate thing
   to write. Drivers resolve in step 4 with everything else, because a curve may name a curve declared
   below it. `design` binds into `ProjectSettings`, one file-wide home, per `D-37`'s "one value, one
   path".
1. **Collect declarations.** Every `ComponentDeclarationSyntax` and `LetBindingSyntax` enters the
   symbol table. The table is one per model, not one per circuit (`D-41`), and its circuit is recorded
   on the symbol. Duplicates → `FS1501` / `FS1401`.
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
4. **Build the dependency graph** over bindings, parameters, referenced properties, and curves. A
   curve is a node like any other: `power=heating` depends on `heating`, which depends on its driver,
   which may be another curve. A cycle among curves is `FS1402`, the same code and the same
   depth-first sort that already reports one among `let` bindings.
5. **Evaluate** in topological order; defer what depends on solved values. **A curve reference is an
   ordinary value source**, which is the whole reason the feature costs so little here: `heating`
   resolves through `IValueScope.Lookup` exactly as a `let` does, yields a **bare** number, and
   `D-14`'s rule reinterprets it in the target parameter's canonical unit at assignment. That is what
   makes one curve drive a power, a percentage and a temperature without being told which.

   What a curve evaluates *at* is `D-58`: in a static circuit, the `design` value for its driver, with
   the driver's own curve short-circuited; in a dynamic circuit, the current time, deferred like a
   solved property. A curve whose driver has neither is `FS1528` — an error naming the driver, never a
   default, because guessing zero puts a number in front of an engineer that nothing chose.
6. **Materialize indexed ports.** A tank starts with `in1` and `out1`. A qualified endpoint or an
   elevation parameter creates the named port after validating its 1…16 index. Ports not evidenced by
   source do not exist in the bound model or model contract.
7. **Bind connections.** Each endpoint resolves to a component symbol and a port. An unqualified tank
   endpoint on the right of `-` takes the next free inlet; one on the left takes the next free outlet.
   Multiple-port tank examples qualify every endpoint so source reordering cannot change intent. This
   is where the inference rules fire.
8. **Apply inference rules** I1, I2, I3 in that order — order matters, since I2 can only run once I1
   has created the undeclared nodes, and I3 can only run once every connection has claimed its port.
9. **Bind attachments, control bindings, and the schedule.** Each `supply`/`return` endpoint resolves against the
   the model's single symbol table (`D-41`) — unresolved is `FS1518`, and resolving to a component of
   the *same* circuit is `FS2217`, owned by topology because that is where circuit membership is
   final. A lone `supply` or `return` is `FS1520`. **Both must resolve into the same circuit**, which
   is then this circuit's `ParentCircuit`; resolving into two different circuits is `FS1526`, because
   the model carries one parent and a subcircuit fed by one circuit and draining into another is a
   topology the user should state as connections rather than as an attachment. Each `control` line resolves its
   named arguments — `by=` to a controller component (`FS1523`), `actuate=` to a settable parameter
   (`FS1522`), `measure=` to a property reference, `setpoint=` to a quantity — with a missing required
   argument reported as `FS1521`. Each `schedule` entry resolves its `component.parameter` target the
   same way `actuate=` does, and evaluates its times against `Time` and its values against the
   target parameter's dimension, so `at 60 s HE4.power = 45` is forty-five kilowatts by `D-14`'s
   bare-number rule exactly as `power=45` would be.
10. **Validate.** A declared component in no connection is `FS1507`; a *cluster* of two or more
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
exceptions: a `setpoint=` and a schedule's times and values are quantities, and step 9 evaluates them
rather than a third pass existing for four expressions. The split keeps each half testable alone. Step 0 knows about neither, which is what lets circuit partitioning be tested
against a syntax tree with no registry at all.

**Step 11 is last, and its position is a contract rather than a convenience.** A tag is derived from
the finished declaration set, so computing it earlier would make it depend on how much of the file had
been bound. Nothing in steps 0–10 may read `ComponentSymbol.Tag`; an architecture test asserts it,
because a binder stage that resolved a reference by tag would reintroduce exactly the identity `D-34`
removed.

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
2. Every `ComponentSymbol` has a unique `Name` **within the model** (`D-41`). Circuits scope tags, not
   identifiers: two circuits may not both declare a `PU1`, and a bare name resolves the same way from
   anywhere in the script — which is what makes an attachment endpoint and a cross-circuit control
   binding ordinary lookups rather than qualified ones.
3. A parameter absent from `ComponentSymbol.Parameters` was not written by the user. There is no other
   reason for absence.
4. Every `ConnectionSymbol` endpoint resolves to an existing `ComponentSymbol` and one of its ports.
5. Every inferred component has `DeclarationSpan == null` and a non-`Declared` `Origin`, and the
   converse holds.
6. `SymbolMap` resolves every source position within a declaration to that declaration's symbol.
7. The semantic model references no type from `FluidScript.Core.Fluids`, `.Components`, or `.Solvers`.
8. **Kind resolution is deterministic and total**: the same input and the same registry always yield
   the same kind or the same diagnostic, and no input throws.
9. **No two kinds share a normalised keyword or alias, and no keyword or alias equals a reserved
   word.** Asserted when the registry is built: a collision between kinds makes stage 2
   order-dependent and stage 3 permanently ambiguous, and a collision with a reserved word makes the
   spelling unwriteable, since the lexer classifies it as a keyword before resolution is reached.
10. `ComponentSymbol` records the canonical `Keyword`; the alias or misspelling the user wrote survives
    only in the source text and in `DeclarationSpan`.
11. Every materialized indexed port has an index in its family's closed range, and no unevidenced
    indexed port appears in `ComponentSymbol` or the model contract.
12. A registry entry with `OmissionBehavior.Default` has a parseable `DefaultLiteral` and non-empty
    basis; one with `Size` has neither. Defaults never appear in `ComponentSymbol.Parameters`, whose
    presence continues to mean the user wrote the parameter.

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
17. Every `AttachmentSymbol` with a non-null `ParentComponent` names a component of a *different*
    circuit. A circuit never attaches to itself.

Invariant 7 is checkable by an architecture test and should be, since it is the one a well-meaning
refactor breaks first. Invariant 14 needs the same treatment for the same reason: reading a tag during
binding is a natural-looking shortcut whose cost only appears when a user inserts a line.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS1501` | Duplicate component name anywhere in the script | Error | `'{name}' is already declared at line {n}{inCircuit}. Names are unique across the whole file; tags are what distinguish circuits.` |
| `FS1502` | Unknown component kind, closest candidate above the suggestion floor | Error | `There is no '{kind}'. Did you mean '{suggestion}'?` |
| `FS1502` | Unknown component kind, nothing close enough to suggest | Error | `There is no '{kind}'.` |
| `FS1503` | Unknown parameter for the kind | Error | `A {kind} has no '{param}'. It accepts: {list}.` |
| `FS1504` | Endpoint names an unknown component | Error | Handled by I1 unless the name is a declared non-component symbol, then: `'{name}' is a value, not a component.` |
| `FS1505` | Unknown port | Error | `A {kind} has no port '{port}'. Ports: {list}.` |
| `FS1506` | Port connected more than once | Error | `Port '{port}' of '{name}' is already connected at line {n}.` |
| `FS1507` | Component in no connection | Warning | `'{name}' is not connected to anything.` |
| `FS1508` | No `circuit` header | Warning | `No circuit name; using '{filename}'.` |
| `FS1509` | *(retired)* | — | Meant "more than one `circuit` header", which `D-33` makes legal. Retired, not reused; left unallocated. |
| `FS1510` | A component was inferred | Info | `Added {kind} '{name}' ({rule}).` |
| `FS1511` | Graph is disconnected | Warning | `'{name}' and {n} others are not connected to the rest of the circuit.` |
| `FS1512` | A kind, parameter, or property name resolved by similarity (stage 3) | Info | `Read '{written}' as '{canonical}'.` |
| `FS1513` | A kind name is ambiguous within the margin | Error | `'{written}' could be '{a}' or '{b}'. Write one of them.` |
| `FS1514` | A symbol-valued parameter got an unaccepted name | Error | `'{param}' accepts {list}; '{written}' is none of them.` |
| `FS1515` | A reference-valued parameter got something that is not a reference | Error | `'{param}' names a component property, like 'N2.t'.` |
| `FS1516` | An indexed port or parameter lies outside its declared family | Error | `'{written}' is outside {kind}'s supported {min}…{max} range.` |
| `FS1517` | A circuit's `fluid` mode contradicts the project default | Warning | `'{circuit}' is {circuitMode} while the project is {projectMode}; the circuit's own setting is used.` |
| `FS1518` | An attachment names a component no circuit declares | Error | `'{name}' is not declared anywhere. A subcircuit attaches to a node of another circuit.` |
| `FS1519` | A circuit's role name matched no registry entry | Info | `'{name}' is not a known circuit role, so it is placed neutrally. Known roles: {list}.` |
| `FS1520` | A subcircuit declares `supply` without `return`, or the reverse | Warning | `'{circuit}' declares '{present} {node}' and no '{other}'. A subcircuit attaches with both.` |
| `FS1521` | A `control` binding is missing a required argument | Error | `A 'control' line needs {list}. Missing: {missing}.` |
| `FS1522` | A `control` binding's `actuate=` names a parameter that cannot be set | Error | `'{param}' of '{component}' cannot be controlled.` |
| `FS1523` | A `control` binding's `by=` names something that is not a controller | Error | `'{name}' is a {kind}, not a controller.` |
| `FS1524` | Two circuits resolve to the same number | Error | `Circuit '{a}' and '{b}' are both {n}. Give one of them a different number.` |
| `FS1525` | Two circuits share a name | Error | `'{name}' is already a circuit at line {n}. Circuit names identify a circuit and must be unique.` |
| `FS1526` | A subcircuit's `supply` and `return` resolve into different circuits | Error | `'{circuit}' takes flow from '{a}' and returns it to '{b}'. A subcircuit attaches to one parent; write the second link as a connection.` |
| `FS1527` | A curve's driver names no curve, registered role, `design` entry or `time` | Error | `'{driver}' is not something '{curve}' can depend on. Name a curve, a known driver, or 'time'.` |
| `FS1528` | A curve is read in a static circuit and its driver has no `design` value | Error | `'{curve}' depends on '{driver}', which has no value here. Add 'design {driver}=…' or solve in time.` |
| `FS1529` | Two curve rows share an x value | Info | `'{curve}' has two rows at {x}; the later one is used.` |
| `FS1530` | A curve has fewer than two rows | Error | `'{curve}' needs at least two rows to interpolate between.` |
| `FS1531` | A bare `control` endpoint whose kind names no single actuated parameter or measured property | Error | `A {kind} has no single {role} to use here. Write it out, such as '{example}'.` |
| `FS1532` | An `at` clause on a kind that carries flow rather than observing it | Error | `'{name}' is a {kind}, which is not placed with 'at'. Connect it with '-' instead.` |
| `FS1533` | An instrument that was declared and never placed | Warning | `'{name}' observes nothing. Place it with 'at' and the name of a node.` |

**`FS1527` and `D-59`'s permissiveness are reconciled by what a driver is for.** `D-59` says a name
matching no role is not an error, because a plant is full of drivers nobody registered; `FS1527`
says a driver naming nothing is one. Both hold, because a driver has to *supply a number* and there
are exactly three things that can: another curve, the clock, or a `design` line. The registry
decides only what **name** a design value may be written under — it is what makes `design tout=-26`
reach `curve heating outdoor`. So `curve recovery flueTemp` with `design flueTemp=180` binds, and
`curve recovery flueTemp` with nothing behind the name is `FS1527`.

**`FS1532` and `FS1533` were not in this table and are additions, not corrections.** `D-61` settles
what `at` means and says nothing about writing it on a pump, or about an instrument that states no
`at` at all. Both are ordinary user mistakes with no code, and the second is the one that mattered:
an observer is exempt from `FS1507` because it is never connected to anything, so without a code of
its own an unplaced sensor bound in silence.

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
- [ ] Binding a script at the 10 000-statement input limit completes within one debounce interval
      (`D-49`), so a compile is never still running when the next one is due. The debounce is idle
      time, not a compute allowance; the compute budget is `07`'s draft-compile row.

## Open questions

None. `FS1507` and `FS1511` remain warnings on the live compile path and are errors for an explicit
solve (`42`). Core supplies the production component registry to `IBinder.Bind`; tier-10 unit tests
supply a fake registry, preserving the binder's dependency direction without creating a second
manifest format.
