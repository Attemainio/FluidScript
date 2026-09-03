using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
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

    /// <summary>Gets the patterned property families, such as a tank's per-layer and per-port temperatures.</summary>
    /// <value>Empty for a kind whose readable properties are all fixed names.</value>
    /// <remarks>
    /// <strong>Separate from <see cref="IndexedParameterFamilies"/> for the same reason
    /// <see cref="Properties"/> is separate from <see cref="Parameters"/>, and the tank shows why.</strong>
    /// Its <c>t{index}</c> exists on both sides and means two things: as a parameter it is an initial
    /// condition the script may state, and as a property it is the solved layer temperature. Its
    /// <c>in{index}_t</c> is a property with no parameter behind it at all, and its
    /// <c>in{index}_elevation</c> a parameter that is not meaningful to read back.
    /// </remarks>
    public ImmutableArray<IndexedPropertyFamilyInfo> IndexedPropertyFamilies { get; init; } = [];

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

    /// <summary>Gets the parameter sets one relation ties together, checked for over-determination.</summary>
    /// <value>Empty for a kind whose parameters constrain each other in no way the binder can count.</value>
    public ImmutableArray<ParameterGroupInfo> ParameterGroups { get; init; } = [];

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

    /// <summary>Resolves a property name against this kind's fixed properties and its indexed families.</summary>
    /// <param name="written">The property name as the reference wrote it.</param>
    /// <returns>
    /// The property, with <see cref="PropertyInfo.Name"/> set to the written name for a family member,
    /// or <see langword="null"/> when this kind has no such property.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Here rather than in the binder because the binder is not the only reader: the model contract
    /// reports <c>T1.t3</c> too, and a second copy of this walk is a second place for the fixed and
    /// the indexed halves to disagree.
    /// </para>
    /// <para>
    /// <strong>An index above the family's bound resolves to nothing.</strong> A family bounded by a
    /// <em>parameter</em> — a tank's <c>layers</c> — has no fixed maximum to check here at all, so
    /// <c>T1.t9</c> on a five-layer tank resolves and is caught where the layer count is known.
    /// </para>
    /// </remarks>
    public PropertyInfo? ResolveProperty(string written)
    {
        if (Properties.TryGetValue(written, out var exact))
        {
            return exact;
        }

        foreach (var family in IndexedPropertyFamilies)
        {
            if (IndexedName.Matches(family.Pattern, written, out var index)
                && index >= family.MinIndex
                && (family.MaxIndex is not { } max || index <= max))
            {
                return family.Element with { Name = written };
            }
        }

        return null;
    }

    /// <summary>Gets every name a property reference may write, with each family shown as its pattern.</summary>
    /// <value>Ordered, for a diagnostic that lists the alternatives.</value>
    public IEnumerable<string> ReadableNames =>
        Properties.Keys
            .Concat(IndexedPropertyFamilies.Select(static family => family.Pattern))
            .Order(StringComparer.Ordinal);

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

    /// <summary>Gets the bounds outside which a value is an error rather than a doubt.</summary>
    /// <value>
    /// <see langword="null"/> where the parameter has no hard limit, which is most of them.
    /// </value>
    /// <remarks>
    /// Different in kind from <see cref="UsualRange"/> rather than only in severity. A usual range is a
    /// judgement about units — 30 000 W where kW was meant is <em>implausible</em>, not impossible — so
    /// it warns and the value stands. This is the range in which the parameter means anything at all: a
    /// valve 1.4 open, a pump 130 % efficient, or a port above the top of its own tank has no reading a
    /// solve could take, so it is an error and the value is dropped.
    /// </remarks>
    public ParameterValidity? Validity { get; init; }

    /// <summary>Gets the decimal places write-back formats this parameter to.</summary>
    /// <value>
    /// From <c>22</c>'s convention 5: every parameter declares a display precision, so a sized
    /// <c>kv</c> is written back as <c>12.4</c> rather than <c>12.40000000000001</c>.
    /// </value>
    public required int DisplayPrecision { get; init; }
}

/// <summary>The range a parameter's value must lie in, and the code that says so when it does not.</summary>
/// <remarks>
/// <strong>The descriptor travels with the range because each of these codes reads as its own
/// sentence.</strong> "position must be between 0 and 1" and "layers must be a whole number from 1 to
/// 100" are not one message with a substitution in it, and flattening them into one would produce the
/// generic bounds message every parameter already has in <c>FS1306</c>. One check site renders all of
/// them, so a newly bounded parameter is a registry row and a descriptor rather than another branch in
/// the binder.
/// </remarks>
public sealed record ParameterValidity
{
    /// <summary>Gets the inclusive bounds, in SI.</summary>
    public required Range<double> Range { get; init; }

    /// <summary>Gets the code raised for a value outside the range.</summary>
    /// <value>
    /// Rendered with <c>name</c>, <c>parameter</c>, <c>value</c>, <c>low</c> and <c>high</c> available;
    /// a template uses the ones its sentence needs and the rest are ignored.
    /// </value>
    public required DiagnosticDescriptor Descriptor { get; init; }

    /// <summary>Gets whether a fractional value is an error too.</summary>
    /// <value>
    /// <see langword="true"/> for a tank's <c>layers</c>, which is a count of things and not a size.
    /// </value>
    public bool RequiresWholeNumber { get; init; }
}

/// <summary>A set of parameters one relation ties together, and how many of them are free.</summary>
/// <remarks>
/// <para>
/// An exchanger's <c>power</c>, <c>in</c>, <c>out</c> and <c>flow</c> satisfy one energy balance, so
/// any three fix the fourth and stating all four asserts something the physics need not agree with.
/// <c>ua</c>, <c>area</c> and <c>u</c> are the same shape with one freedom fewer.
/// </para>
/// <para>
/// <strong>Counting is all this supports.</strong> Whether a stated fourth value <em>agrees</em> with
/// the other three is a different question, and answering it needs a fluid — the implied flow is
/// <c>Q / (cp · dT)</c>, and neither the registry nor the binder has a cp.
/// </para>
/// </remarks>
public sealed record ParameterGroupInfo
{
    /// <summary>Gets the canonical parameter names the relation ties together.</summary>
    public required ImmutableArray<string> Parameters { get; init; }

    /// <summary>Gets how many of them may be stated before the group is over-determined.</summary>
    public required int Freedoms { get; init; }

    /// <summary>Gets how many of them must be stated for the group to be determined at all.</summary>
    /// <value>
    /// Zero for a group that is optional as a whole, which is every group but a boundary's. A
    /// <c>supply</c> states exactly one of <c>flow</c> and <c>p</c>: <see cref="Freedoms"/> is what
    /// stops it stating both, and this is what stops it stating neither (<c>D-64</c>).
    /// </value>
    /// <remarks>
    /// Separate from a <see cref="ParameterOmissionBehavior.Require"/> on each member, because the
    /// requirement is on the <em>set</em>: neither <c>flow</c> nor <c>p</c> is individually required and
    /// a rule that made them so would reject every valid boundary there is.
    /// </remarks>
    public int Minimum { get; init; }

    /// <summary>Gets the code raised when more than <see cref="Freedoms"/> of them are stated.</summary>
    /// <value>
    /// Rendered with <c>name</c>, <c>parameters</c> and <c>count</c>, plus every stated member's value
    /// under its own parameter name.
    /// </value>
    public required DiagnosticDescriptor Descriptor { get; init; }

    /// <summary>Gets the code raised when fewer than <see cref="Minimum"/> of them are stated.</summary>
    /// <value>
    /// <see langword="null"/> when <see cref="Minimum"/> is zero, and required when it is not.
    /// Rendered with <c>name</c> and <c>parameters</c>.
    /// </value>
    public DiagnosticDescriptor? MinimumDescriptor { get; init; }
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
/// <remarks>
/// <strong>The set is closed, which is the whole force of <c>D-02</c>.</strong> Every parameter falls
/// into exactly one of these, so "what happens if I leave it out" always has an answer the registry
/// states rather than the code implies.
/// </remarks>
public enum ParameterOmissionBehavior
{
    /// <summary>Sizing decides the value, and reports what it decided.</summary>
    Size = 1,

    /// <summary>An explicit, visible default applies, with a stated basis.</summary>
    Default,

    /// <summary>There is no answer without it, and its absence is a diagnostic (<c>D-64</c>).</summary>
    /// <remarks>
    /// Added for boundaries, and deliberately rare. A required parameter is one where every possible
    /// substitute would be a guess about the plant rather than about the model: a <c>supply</c> with no
    /// temperature has no state to give the fluid entering there, and inventing one produces a solved
    /// circuit whose every downstream temperature is wrong with nothing to show for it.
    /// </remarks>
    Require,
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

/// <summary>A family of indexed properties, such as a tank's solved layer temperatures.</summary>
/// <remarks>
/// The same shape as <see cref="IndexedParameterFamilyInfo"/> and deliberately not shared with it: an
/// element is a <see cref="PropertyInfo"/> here and a <see cref="ParameterInfo"/> there, and the two
/// carry different things — a property has an availability and a reporting unit, a parameter has an
/// omission policy and a range. A common base holding only the pattern and the bounds would save four
/// lines and cost the reader the one distinction that matters.
/// </remarks>
public sealed record IndexedPropertyFamilyInfo
{
    /// <summary>Gets the canonical pattern with one <c>{index}</c> placeholder.</summary>
    /// <value><c>t{index}</c>, <c>in{index}_t</c>, or <c>out{index}_t</c>.</value>
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
    public required PropertyInfo Element { get; init; }
}

/// <summary>Matches an indexed family pattern such as <c>t{index}</c> against a written name.</summary>
/// <remarks>
/// The one implementation of the pattern rule, used by both family kinds and by the binder. It was
/// the binder's private helper first, which is why the parameter path still reaches it through a
/// forwarder rather than calling it directly — there is one rule, in one place, either way.
/// </remarks>
public static class IndexedName
{
    private const string Placeholder = "{index}";

    /// <summary>Tells whether a written name is a member of a pattern's family, and which one.</summary>
    /// <param name="pattern">The canonical pattern, with one <c>{index}</c> placeholder.</param>
    /// <param name="written">The name to test.</param>
    /// <param name="index">The index it carries, or zero when it is not a member.</param>
    /// <returns><see langword="true"/> when the name matches the pattern.</returns>
    /// <remarks>
    /// The index must be digits only: <c>NumberStyles.None</c> rejects <c>t+3</c> and <c>t 3</c>,
    /// which <see cref="int.TryParse(string, out int)"/>'s default would accept.
    /// </remarks>
    public static bool Matches(string pattern, string written, out int index)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(written);

        index = 0;

        var placeholder = pattern.IndexOf(Placeholder, StringComparison.Ordinal);
        if (placeholder < 0)
        {
            return false;
        }

        var prefix = pattern[..placeholder];
        var suffix = pattern[(placeholder + Placeholder.Length)..];

        if (!written.StartsWith(prefix, StringComparison.Ordinal)
            || !written.EndsWith(suffix, StringComparison.Ordinal)
            || written.Length <= prefix.Length + suffix.Length)
        {
            return false;
        }

        var digits = written[prefix.Length..(written.Length - suffix.Length)];

        return int.TryParse(
            digits, NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }
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
