using System.Collections.Immutable;

using FluidScript.Core.Units;

namespace FluidScript.Core.Language;

/// <summary>Describes a component kind to the binder: its keyword, ports, and parameters.</summary>
/// <remarks>
/// The binder needs no behaviour, only shape. A registry entry is metadata; the class that implements
/// the physics is resolved later, at lowering, and nothing here reaches
/// <c>FluidScript.Core.Components</c> — which is why the registry can exist a whole phase before any
/// component does.
/// </remarks>
public sealed record ComponentKindInfo
{
    /// <summary>Gets the canonical script keyword, in lower_snake_case.</summary>
    /// <value>
    /// The one spelling <c>/docs</c>, the model contract, and the printer use. Everything a user may
    /// type resolves to this (<c>D-15</c>); nothing else is ever emitted.
    /// </value>
    public required string Keyword { get; init; }

    /// <summary>Gets the additional spellings that resolve to this kind, curated per kind (<c>D-15</c>).</summary>
    /// <value>
    /// Matched after normalisation, so <c>3_way_valve</c> covers <c>3WayValve</c> and <c>3 way valve</c>
    /// too, and only genuinely different words need listing. An alias is never printed, never appears in
    /// the model contract, and never appears in <c>/docs</c> except on the kind's own page.
    /// </value>
    public required ImmutableArray<string> Aliases { get; init; }

    /// <summary>Gets the ports in declaration order. Unqualified connections bind to these in order.</summary>
    public required ImmutableArray<PortInfo> Ports { get; init; }

    /// <summary>Gets the indexed port families materialized from qualified endpoints or matching parameters.</summary>
    /// <value>Empty for fixed-port kinds; <c>tank</c> declares <c>in{n}</c>/<c>out{n}</c> (<c>D-32</c>).</value>
    public required ImmutableArray<PortFamilyInfo> PortFamilies { get; init; }

    /// <summary>Gets the patterned parameter families, such as tank layer temperatures and port elevations.</summary>
    public required ImmutableArray<IndexedParameterFamilyInfo> IndexedParameterFamilies { get; init; }

    /// <summary>Gets whether this kind can contribute net hydraulic head and satisfy <c>FS2214</c>.</summary>
    /// <value>Explicit registry metadata; never inferred from a residual implementation (<c>D-30</c>).</value>
    public required bool DrivesFlow { get; init; }

    /// <summary>Gets whether this kind accepts any number of unnamed connections.</summary>
    /// <value>
    /// True for <c>node</c> and nothing else: the junction is the one component whose port count is
    /// not fixed. Everything else binds connections to <see cref="Ports"/> in order.
    /// </value>
    /// <remarks>
    /// Registry data rather than a keyword the binder tests for. <c>15</c>'s record had no field for
    /// it, which left the binder needing to know that the kind called <c>node</c> is special — the one
    /// thing the registry exists to prevent, and the reason a second unlimited-port kind could not be
    /// added without editing the binder.
    /// </remarks>
    public bool HasUnlimitedPorts { get; init; }

    /// <summary>Gets the letter code used in this kind's equipment tag — <c>PU</c>, <c>HE</c>, <c>TV</c> (<c>D-34</c>).</summary>
    /// <value>
    /// <see langword="null"/> for a kind that carries no tag. <c>node</c> and <c>pipe</c> are null
    /// deliberately: they are mostly inferred, they outnumber every other kind, and no plant schedule
    /// tags them.
    /// </value>
    /// <remarks>
    /// Registry data rather than a hard-coded table, so a new kind ships its own code and a house
    /// convention that writes <c>LP</c> for a pump instead of <c>PU</c> is a data change. A code must
    /// not make any tag lex as a quantity literal, which
    /// <see cref="ComponentRegistry"/> asserts when it is built.
    /// </remarks>
    public string? TagCode { get; init; }

    /// <summary>Gets every parameter this kind accepts, keyed by canonical script name.</summary>
    public required ImmutableDictionary<string, ParameterInfo> Parameters { get; init; }

    /// <summary>Gets the properties referenceable as <c>Name.property</c>.</summary>
    /// <remarks>
    /// Separate from <see cref="Parameters"/> although the two overlap: <c>power</c> is both something
    /// you may set and something you may read. Keeping them apart lets a kind expose a read-only
    /// property such as <c>dp</c> that is not settable, and a parameter that is not meaningful to read
    /// back.
    /// </remarks>
    public required ImmutableDictionary<string, PropertyInfo> Properties { get; init; }

    /// <summary>Gets the one parameter a controller may move at runtime (<c>D-61</c>).</summary>
    /// <value>
    /// <c>position</c> for a valve, <c>speed</c> for a pump, <see langword="null"/> for a kind nothing
    /// actuates. Always a key of <see cref="Parameters"/> when it is not null.
    /// </value>
    /// <remarks>
    /// This is what makes <c>control TV1 with TE1 by PID1</c> unambiguous without writing
    /// <c>.position</c>. <c>D-43</c> refused a bare actuator because "a valve has more than one thing
    /// that could move", which was right about parameters and wrong about actuators: of
    /// <c>position</c>, <c>kv</c> and <c>authority</c>, only <c>position</c> moves during a solve.
    /// Where the registry names exactly one, the bare form is safe by construction; where it names
    /// none, the bare form is <c>FS1531</c> and the qualified form is required.
    /// </remarks>
    public string? ActuatedParameter { get; init; }

    /// <summary>Gets the one property an instrument reads (<c>D-61</c>).</summary>
    /// <value>
    /// <c>t</c> for a temperature sensor, <see langword="null"/> for a kind that is not an instrument.
    /// Always a key of <see cref="Properties"/> when it is not null.
    /// </value>
    /// <remarks>
    /// The measurement half of the same rule: a sensor measures exactly one quantity, so <c>TE1</c>
    /// alone is unambiguous and <c>.t</c> never needs writing.
    /// </remarks>
    public string? MeasuredProperty { get; init; }

    /// <summary>Gets whether this kind observes a node rather than carrying flow.</summary>
    /// <value><see langword="true"/> for the three instrument kinds.</value>
    /// <remarks>
    /// An observer is attached with <c>at</c> and stays out of the hydraulic graph entirely. A
    /// pass-through instrument would carry two ports, gain an inserted node from rule I2, and
    /// contribute equations that are all identities — a hundred sensors would double the solve to
    /// compute nothing.
    /// </remarks>
    public bool IsObserver { get; init; }
}

/// <summary>One parameter a component kind accepts.</summary>
public sealed record ParameterInfo
{
    /// <summary>Gets the canonical name, which is what binding stores.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the curated input spellings.</summary>
    /// <value>
    /// Binding stores <see cref="Name"/>; printing preserves whatever the source said, so
    /// <c>T1 tank v=300</c> keeps its <c>v</c> (<c>D-32</c>).
    /// </value>
    public ImmutableArray<string> Aliases { get; init; } = [];

    /// <summary>Gets what shape of value this parameter accepts.</summary>
    /// <value>
    /// <see cref="ParameterValueKind.Quantity"/> for everything dimensioned;
    /// <see cref="ParameterValueKind.Symbol"/> for a closed set of names such as a valve's
    /// <c>characteristic</c>; <see cref="ParameterValueKind.Reference"/> for a controller's
    /// <c>measure</c> and <c>actuate</c>, which name another component's property rather than a value.
    /// All three are the same syntax, and only this says how to bind it.
    /// </value>
    public required ParameterValueKind ValueKind { get; init; }

    /// <summary>Gets the dimension, for a <see cref="ParameterValueKind.Quantity"/> parameter.</summary>
    /// <value><see cref="Dimension.Dimensionless"/> for the other kinds.</value>
    public required Dimension Dimension { get; init; }

    /// <summary>Gets the accepted names, for a <see cref="ParameterValueKind.Symbol"/> parameter.</summary>
    /// <value>Empty for the other kinds. Resolved by the same normalisation as a kind name.</value>
    public ImmutableArray<string> AcceptedSymbols { get; init; } = [];

    /// <summary>Gets what omission means: size the value, or apply an explicit visible default.</summary>
    public required ParameterOmissionBehavior OmissionBehavior { get; init; }

    /// <summary>Gets the canonical source literal for a defaulted parameter.</summary>
    /// <value><see langword="null"/> for a sized one. Written as a user would write it, unit included.</value>
    public string? DefaultLiteral { get; init; }

    /// <summary>Gets the user-facing reason for a default.</summary>
    /// <value><see langword="null"/> for a sized parameter.</value>
    public string? DefaultBasis { get; init; }

    /// <summary>Gets the plausibility bounds in SI, used for <c>FS1306</c>.</summary>
    /// <value><see langword="null"/> disables the check.</value>
    public Range<double>? UsualRange { get; init; }

    /// <summary>Gets the decimal places write-back formats this parameter to.</summary>
    /// <value>
    /// From <c>22</c>'s convention 5: every parameter declares a display precision, so a sized
    /// <c>kv</c> is written back as <c>12.4</c> rather than <c>12.40000000000001</c>.
    /// </value>
    public required int DisplayPrecision { get; init; }
}

/// <summary>What shape of value a parameter accepts.</summary>
public enum ParameterValueKind
{
    /// <summary>A number with a dimension, bare or with a unit.</summary>
    Quantity = 1,

    /// <summary>One name from a closed set, such as a valve characteristic.</summary>
    Symbol,

    /// <summary>Another component's property, such as a controller's measurement point.</summary>
    Reference,
}

/// <summary>What the absence of a parameter means (<c>D-02</c>).</summary>
public enum ParameterOmissionBehavior
{
    /// <summary>Sizing decides the value, and reports what it decided.</summary>
    Size = 1,

    /// <summary>An explicit, visible default applies, with a stated basis.</summary>
    Default,
}

/// <summary>One named port of a component kind.</summary>
public sealed record PortInfo
{
    /// <summary>Gets the port's name, as a qualified endpoint writes it.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the nominal direction of flow through the port.</summary>
    public required PortRole Role { get; init; }

    /// <summary>Gets whether the port may be left unconnected without inference rule I3 firing.</summary>
    public required bool IsOptional { get; init; }
}

/// <summary>Which way flow nominally runs through a port.</summary>
/// <remarks>
/// Nominal, not solved: a negative solved flow is a legal answer (<c>22</c>'s convention 2), and a
/// three-way valve's ports are all bidirectional precisely because mixing and diverting arrangements
/// are both real.
/// </remarks>
public enum PortRole
{
    /// <summary>Flow nominally enters here.</summary>
    Inlet = 1,

    /// <summary>Flow nominally leaves here.</summary>
    Outlet,

    /// <summary>Either, decided by the solve.</summary>
    Bidirectional,
}

/// <summary>A family of indexed ports, such as a tank's inlets.</summary>
public sealed record PortFamilyInfo
{
    /// <summary>Gets the canonical prefix before a positive decimal index, such as <c>in</c>.</summary>
    public required string Prefix { get; init; }

    /// <summary>Gets the lowest index that exists.</summary>
    public required int MinIndex { get; init; }

    /// <summary>Gets the highest index that may be materialized.</summary>
    public required int MaxIndex { get; init; }

    /// <summary>Gets the nominal direction of flow through ports of this family.</summary>
    public required PortRole Role { get; init; }

    /// <summary>Gets the associated normalized-height parameter suffix.</summary>
    /// <value><c>_elevation</c> for a tank; <see langword="null"/> where a family has no height.</value>
    public required string? ElevationParameterSuffix { get; init; }
}

/// <summary>A family of indexed parameters, such as a tank's per-layer temperatures.</summary>
public sealed record IndexedParameterFamilyInfo
{
    /// <summary>Gets the canonical pattern with one <c>{index}</c> placeholder.</summary>
    /// <value><c>t{index}</c>, <c>in{index}_elevation</c>, or <c>out{index}_elevation</c>.</value>
    public required string Pattern { get; init; }

    /// <summary>Gets the lowest index the family accepts.</summary>
    public required int MinIndex { get; init; }

    /// <summary>Gets the fixed maximum index.</summary>
    /// <value><see langword="null"/> when <see cref="MaxIndexParameter"/> supplies it instead.</value>
    public int? MaxIndex { get; init; }

    /// <summary>Gets the canonical integer parameter controlling the maximum, such as <c>layers</c>.</summary>
    /// <value><see langword="null"/> when <see cref="MaxIndex"/> is fixed.</value>
    public string? MaxIndexParameter { get; init; }

    /// <summary>Gets the shape of one member of the family.</summary>
    public required ParameterInfo Element { get; init; }
}

/// <summary>One property readable as <c>Name.property</c>.</summary>
public sealed record PropertyInfo
{
    /// <summary>Gets the property's name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the dimension of the value read back.</summary>
    public required Dimension Dimension { get; init; }

    /// <summary>Gets the earliest stage at which the property has a value.</summary>
    public required PropertyAvailability Availability { get; init; }

    /// <summary>Gets the unit the value is reported in on the model contract.</summary>
    public required string CanonicalUnit { get; init; }
}

/// <summary>When a property becomes readable.</summary>
public enum PropertyAvailability
{
    /// <summary>Available as soon as the script is bound, because the user stated it.</summary>
    Declared = 1,

    /// <summary>Available once sizing has run.</summary>
    Sized,

    /// <summary>Available only after a solve.</summary>
    Solved,
}
