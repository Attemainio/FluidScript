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

    /// <summary>Gets the patterned parameter families, such as tank layer temperatures and port levels.</summary>
    public required ImmutableArray<IndexedParameterFamilyInfo> IndexedParameterFamilies { get; init; }

    /// <summary>Gets the patterned property families, such as a tank's per-layer and per-port temperatures.</summary>
    /// <value>Empty for a kind whose readable properties are all fixed names.</value>
    /// <remarks>
    /// <strong>Separate from <see cref="IndexedParameterFamilies"/> for the same reason
    /// <see cref="Properties"/> is separate from <see cref="Parameters"/>, and the tank shows why.</strong>
    /// Its <c>t{index}</c> exists on both sides and means two things: as a parameter it is an initial
    /// condition the script may state, and as a property it is the solved layer temperature. Its
    /// <c>in{index}_t</c> is a property with no parameter behind it at all, and its
    /// <c>in{index}_level</c> a parameter that is not meaningful to read back.
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

    /// <summary>Gets every parameter this kind accepts, keyed by <see cref="ParameterInfo.Key"/>.</summary>
    /// <remarks>
    /// The key is the model's identifier, which is what a stated parameter, a sizing decision and the
    /// wire carry; the script spelling is <see cref="ParameterInfo.Name"/>, reached through
    /// <see cref="ResolveParameter"/> (<c>D-120</c>). The two coincide for every parameter that has no
    /// port index.
    /// </remarks>
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
    public PropertyInfo? ResolveProperty(string written) => ResolveProperty(written, out _);

    /// <summary>Resolves a property name, saying whether it was written in a spelling <c>D-120</c> retired.</summary>
    /// <param name="written">The property name as the reference wrote it.</param>
    /// <param name="suggestion">
    /// The current spelling when <paramref name="written"/> is a legacy one — <c>in[2].t</c> for
    /// <c>t_in2</c> — else <see langword="null"/>. What <c>FS1536</c> offers as its quick fix.
    /// </param>
    /// <returns>
    /// The property, with <see cref="PropertyInfo.Name"/> and <see cref="PropertyInfo.Key"/> set to
    /// the member's own for a family member, or <see langword="null"/> when this kind has no such
    /// property.
    /// </returns>
    /// <remarks>
    /// <c>in[1].t</c> and <c>in.t</c> are one port written two ways, so the bracketed first index is
    /// folded before matching and draws no suggestion.
    /// </remarks>
    public PropertyInfo? ResolveProperty(string written, out string? suggestion)
    {
        ArgumentNullException.ThrowIfNull(written);
        suggestion = null;

        // With the quantity by its symbol and `[1]` folded first, then as written: `in[1].t` is the
        // fixed `in.t` once folded, and `layer[1].t` is a family member only as written, since its
        // family keeps its index.
        var folded = IndexedName.FoldFirstIndex(PropertyTable.Canonical(written));

        foreach (var name in string.Equals(folded, written, StringComparison.Ordinal) ? [written] : new[] { folded, written })
        {
            if (Properties.TryGetValue(name, out var exact))
            {
                return exact;
            }

            foreach (var property in Properties.Values)
            {
                if (property.LegacySpellings.Contains(name, StringComparer.Ordinal))
                {
                    suggestion = property.Name;
                    return property;
                }
            }

            foreach (var family in IndexedPropertyFamilies)
            {
                var legacy = false;

                if (!IndexedName.Matches(family.Pattern, name, out var index)
                    && !(legacy = family.LegacyPattern is { } old && IndexedName.Matches(old, name, out index)))
                {
                    continue;
                }

                if (index < family.MinIndex || (family.MaxIndex is { } max && index > max))
                {
                    continue;
                }

                var member = family.Element with
                {
                    Name = IndexedName.Spell(family.Pattern, index),
                    Key = IndexedName.Spell(family.KeyPattern, index),
                };

                suggestion = legacy ? member.Name : null;
                return member;
            }
        }

        return null;
    }

    /// <summary>Resolves a parameter name as a declaration wrote it: by name, alias, legacy spelling, or indexed family (<c>D-120</c>).</summary>
    /// <param name="written">The name as written: <c>power</c>, <c>in[2].t</c>, <c>layer[3].t</c>, or the old <c>in2</c>, <c>t3</c>.</param>
    /// <param name="suggestion">The current spelling when <paramref name="written"/> is a legacy one, else <see langword="null"/>.</param>
    /// <param name="outsideFamily">The family the name belongs to when its index is out of the family's fixed range, else <see langword="null"/>.</param>
    /// <returns>
    /// The parameter, with <see cref="ParameterInfo.Name"/> and <see cref="ParameterInfo.Key"/> set
    /// to the member's own for a family member, or <see langword="null"/> when nothing matches
    /// exactly — similarity is the binder's, since it reports.
    /// </returns>
    /// <remarks>
    /// A family bounded by a <em>parameter</em> (a tank's <c>layers</c>) has no fixed maximum to check
    /// here, so <c>layer[9].t</c> on a five-layer tank resolves and is caught where the count is known.
    /// </remarks>
    public ParameterInfo? ResolveParameter(string written, out string? suggestion, out IndexedParameterFamilyInfo? outsideFamily)
    {
        ArgumentNullException.ThrowIfNull(written);
        suggestion = null;
        outsideFamily = null;

        // `in[2].temperature` is `in[2].t` (the table's name for its symbol), then `[1]` folds.
        var folded = IndexedName.FoldFirstIndex(PropertyTable.Canonical(written));

        foreach (var name in string.Equals(folded, written, StringComparison.Ordinal) ? [written] : new[] { folded, written })
        {
            foreach (var candidate in Parameters.Values)
            {
                if (string.Equals(candidate.Name, name, StringComparison.Ordinal)
                    || candidate.Aliases.Contains(name, StringComparer.Ordinal))
                {
                    return candidate;
                }

                if (candidate.LegacySpellings.Contains(name, StringComparer.Ordinal))
                {
                    suggestion = candidate.Name;
                    return candidate;
                }
            }

            foreach (var family in IndexedParameterFamilies)
            {
                var legacy = false;

                if (!IndexedName.Matches(family.Pattern, name, out var index)
                    && !(legacy = family.LegacyPattern is { } old && IndexedName.Matches(old, name, out index)))
                {
                    continue;
                }

                if (index < family.MinIndex || (family.MaxIndex is { } max && index > max))
                {
                    outsideFamily = family;
                    return null;
                }

                suggestion = legacy ? IndexedName.Spell(family.Pattern, index) : null;
                return family.Element with
                {
                    Name = IndexedName.Spell(family.Pattern, index),
                    Key = IndexedName.Spell(family.KeyPattern, index),
                };
            }
        }

        return null;
    }

    /// <summary>Resolves a port as an endpoint wrote it to the key the model knows it by (<c>D-120</c>).</summary>
    /// <param name="written">The port name as written: <c>in</c>, <c>in[2]</c>, <c>b</c>, or the old <c>in2</c>.</param>
    /// <param name="suggestion">The current spelling when <paramref name="written"/> is a legacy one, else <see langword="null"/>.</param>
    /// <param name="outsideFamily">The family the name belongs to when its index is out of range, else <see langword="null"/>.</param>
    /// <returns>The key — <c>in2</c>, <c>in1</c>, <c>b</c> — or <see langword="null"/> when this kind has no such port.</returns>
    /// <remarks>
    /// The fixed ports first, so a family's first member — a tank's <c>in</c>, keyed <c>in1</c> — is
    /// the fixed row and the family proper starts above it; <c>in[1]</c> folds to <c>in</c> before
    /// matching, as everywhere. A kind with unlimited unnamed ports has no port to resolve and
    /// answers <see langword="null"/>; the caller knows that case already.
    /// </remarks>
    public string? ResolvePort(string written, out string? suggestion, out PortFamilyInfo? outsideFamily)
    {
        ArgumentNullException.ThrowIfNull(written);
        suggestion = null;
        outsideFamily = null;

        var folded = IndexedName.FoldFirstIndex(written);

        foreach (var name in string.Equals(folded, written, StringComparison.Ordinal) ? [written] : new[] { folded, written })
        {
            foreach (var port in Ports)
            {
                if (string.Equals(port.Name, name, StringComparison.Ordinal))
                {
                    return port.Key;
                }

                if (port.LegacySpellings.Contains(name, StringComparer.Ordinal))
                {
                    suggestion = port.Name;
                    return port.Key;
                }
            }

            foreach (var family in PortFamilies)
            {
                var legacy = false;

                if (!IndexedName.Matches(family.Prefix + "[{index}]", name, out var index)
                    && !(legacy = IndexedName.Matches(family.Prefix + "{index}", name, out index)))
                {
                    continue;
                }

                if (index < family.MinIndex || index > family.MaxIndex)
                {
                    outsideFamily = family;
                    return null;
                }

                suggestion = legacy ? family.Name(index) : null;
                return family.Key(index);
            }
        }

        return null;
    }

    /// <summary>Spells a parameter's key the way a script writes it: <c>in2</c> as <c>in[2].t</c>, <c>t3</c> as <c>layer[3].t</c>.</summary>
    /// <param name="key">The key a stated value is stored under.</param>
    /// <returns>The script spelling, or the key itself when nothing respelled it.</returns>
    public string ParameterName(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        foreach (var parameter in Parameters.Values)
        {
            if (string.Equals(parameter.Key, key, StringComparison.Ordinal))
            {
                return parameter.Name;
            }
        }

        foreach (var family in IndexedParameterFamilies)
        {
            if (IndexedName.Matches(family.KeyPattern, key, out var index))
            {
                return IndexedName.Spell(family.Pattern, index);
            }
        }

        return key;
    }

    /// <summary>Spells a port's key the way a script writes it: <c>in2</c> as <c>in[2]</c>, a tank's <c>in1</c> as <c>in</c>.</summary>
    /// <param name="key">The key the model knows the port by.</param>
    /// <returns>The script spelling, or the key itself when nothing respelled it.</returns>
    public string PortName(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        foreach (var port in Ports)
        {
            if (string.Equals(port.Key, key, StringComparison.Ordinal))
            {
                return port.Name;
            }
        }

        foreach (var family in PortFamilies)
        {
            if (IndexedName.Matches(family.Prefix + "{index}", key, out var index))
            {
                return family.Name(index);
            }
        }

        return key;
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
    private readonly string? _key;

    /// <summary>Gets the canonical name: the spelling a script writes, <c>/docs</c> teach and a diagnostic quotes.</summary>
    /// <value><c>power</c>, or a port's quantity in <c>D-120</c>'s form — <c>in[2].t</c>.</value>
    public required string Name { get; init; }

    /// <summary>Gets the identifier binding stores the value under, which is what Core reads.</summary>
    /// <value>
    /// <see cref="Name"/> unless <c>D-120</c> respelled the surface: an exchanger's <c>in[2].t</c> is
    /// stored as <c>in2</c>, the key every sizer, seed and residual has read since before the
    /// spelling changed. The split keeps the language free to change its surface without a rename
    /// through the physics.
    /// </value>
    public string Key
    {
        get => _key ?? Name;
        init => _key = value;
    }

    /// <summary>Gets the curated input spellings.</summary>
    /// <value>
    /// Binding stores <see cref="Key"/>; printing preserves whatever the source said, so
    /// <c>T1 tank v=300</c> keeps its <c>v</c> (<c>D-32</c>). An alias is silent: <c>in[1].t</c> for
    /// <c>in.t</c> is one port written two ways, not an old form.
    /// </value>
    public ImmutableArray<string> Aliases { get; init; } = [];

    /// <summary>Gets the spellings a script wrote before <c>D-120</c>, read for one language major.</summary>
    /// <value>
    /// <c>in</c> for <c>in.t</c>, <c>flow2</c> for <c>in[2].flow</c>. Each binds exactly as
    /// <see cref="Name"/> does and raises <c>FS1536</c> with the new spelling as its suggestion, so
    /// the editor's quick fix rewrites the line and the documentation teaches one form (<c>18</c>
    /// retires them at the next major).
    /// </value>
    public ImmutableArray<string> LegacySpellings { get; init; } = [];

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
    private readonly string? _key;

    /// <summary>Gets the port's name, as a qualified endpoint writes it: <c>in</c>, <c>in[2]</c>, <c>ab</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the identifier the model, the wire and the symbol anchors know the port by.</summary>
    /// <value>
    /// <see cref="Name"/> unless <c>D-120</c> respelled it: <c>in[2]</c> is the port <c>in2</c> to
    /// everything downstream of the binder, which is what keeps the scene's anchors and the frontend
    /// unaware that the script's spelling changed.
    /// </value>
    public string Key
    {
        get => _key ?? Name;
        init => _key = value;
    }

    /// <summary>Gets the spellings an endpoint wrote before <c>D-120</c>, read for one language major with <c>FS1536</c>.</summary>
    public ImmutableArray<string> LegacySpellings { get; init; } = [];

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
    /// <summary>Gets the family's name, such as <c>in</c>: the word before the index.</summary>
    /// <remarks>
    /// A member is written <c>in[n]</c>, with the bare word standing for <c>in[1]</c> (<c>D-120</c>);
    /// it is stored as <c>in{n}</c>, the key the model has always used; and before <c>D-120</c> it was
    /// written as that key. <see cref="Name"/>, <see cref="Key"/> and <see cref="LegacyName"/> are the
    /// three spellings of one port.
    /// </remarks>
    public required string Prefix { get; init; }

    /// <summary>Gets the script spelling of a member with one <c>{index}</c> placeholder: <c>in[{index}]</c>.</summary>
    /// <remarks>The same shape an indexed parameter family's <see cref="IndexedParameterFamilyInfo.Pattern"/> has, so a reader of either can spell a member one way.</remarks>
    public string Pattern => Prefix + "[{index}]";

    /// <summary>Gets the script spelling of one member.</summary>
    /// <param name="index">The member's index.</param>
    /// <returns><c>in</c> for index 1, else <c>in[n]</c>.</returns>
    public string Name(int index) =>
        index == 1 ? Prefix : string.Create(CultureInfo.InvariantCulture, $"{Prefix}[{index}]");

    /// <summary>Gets the model's identifier for one member.</summary>
    /// <param name="index">The member's index.</param>
    /// <returns><c>in1</c>, <c>in2</c>, …</returns>
    public string Key(int index) => string.Create(CultureInfo.InvariantCulture, $"{Prefix}{index}");

    /// <summary>Gets the spelling a script used before <c>D-120</c>, which is the key.</summary>
    /// <param name="index">The member's index.</param>
    /// <returns><c>in1</c>, <c>in2</c>, …</returns>
    public string LegacyName(int index) => Key(index);

    /// <summary>Gets the lowest index that exists.</summary>
    public required int MinIndex { get; init; }

    /// <summary>Gets the highest index that may be materialized.</summary>
    public required int MaxIndex { get; init; }

    /// <summary>Gets the nominal direction of flow through ports of this family.</summary>
    public required PortRole Role { get; init; }

    /// <summary>Gets the associated normalized-height parameter suffix.</summary>
    /// <value><c>_level</c> for a tank; <see langword="null"/> where a family has no height.</value>
    public required string? LevelParameterSuffix { get; init; }
}

/// <summary>A family of indexed parameters, such as a tank's per-layer temperatures.</summary>
public sealed record IndexedParameterFamilyInfo
{
    private readonly string? _keyPattern;

    /// <summary>Gets the canonical pattern with one <c>{index}</c> placeholder, as a script writes it.</summary>
    /// <value><c>layer[{index}].t</c>, <c>in[{index}].level</c>, or <c>out[{index}].level</c>.</value>
    public required string Pattern { get; init; }

    /// <summary>Gets the pattern of the key a member is stored under.</summary>
    /// <value><see cref="Pattern"/> unless <c>D-120</c> respelled the surface: <c>t{index}</c>, <c>in{index}_level</c>.</value>
    public string KeyPattern
    {
        get => _keyPattern ?? Pattern;
        init => _keyPattern = value;
    }

    /// <summary>Gets the pattern a script wrote before <c>D-120</c>, read for one language major with <c>FS1536</c>.</summary>
    /// <value><see langword="null"/> for a family that never changed spelling.</value>
    public string? LegacyPattern { get; init; }

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
    private readonly string? _keyPattern;

    /// <summary>Gets the canonical pattern with one <c>{index}</c> placeholder, as a reference writes it.</summary>
    /// <value><c>layer[{index}].t</c>, <c>in[{index}].t</c>, or <c>out[{index}].t</c>.</value>
    public required string Pattern { get; init; }

    /// <summary>Gets the pattern of the key a member's value is published under.</summary>
    /// <value><see cref="Pattern"/> unless <c>D-120</c> respelled the surface: <c>t{index}</c>, <c>in{index}_t</c>.</value>
    public string KeyPattern
    {
        get => _keyPattern ?? Pattern;
        init => _keyPattern = value;
    }

    /// <summary>Gets the pattern a reference wrote before <c>D-120</c>, read for one language major with <c>FS1536</c>.</summary>
    /// <value><see langword="null"/> for a family that never changed spelling.</value>
    public string? LegacyPattern { get; init; }

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

    /// <summary>Writes one member of a pattern's family.</summary>
    /// <param name="pattern">A pattern with one <c>{index}</c> placeholder.</param>
    /// <param name="index">The member's index.</param>
    /// <returns><c>in[2].level</c> for <c>in[{index}].level</c> and 2.</returns>
    public static string Spell(string pattern, int index)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        return pattern.Replace(
            Placeholder, index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>Folds a bracketed first index away: <c>in[1].t</c> reads as <c>in.t</c>, <c>in[1]</c> as <c>in</c> (<c>D-120</c>).</summary>
    /// <param name="written">A name as the script wrote it.</param>
    /// <returns>The name with every <c>[1]</c> removed; unchanged when there is none.</returns>
    /// <remarks>
    /// The bare word and the first index are one port, so they are one spelling to the registry, and
    /// neither draws a suggestion. Only <c>[1]</c> folds: <c>in[2]</c> is a different port.
    /// </remarks>
    public static string FoldFirstIndex(string written)
    {
        ArgumentNullException.ThrowIfNull(written);

        return written.Contains("[1]", StringComparison.Ordinal)
            ? written.Replace("[1]", string.Empty, StringComparison.Ordinal)
            : written;
    }
}

/// <summary>One property readable as <c>Name.property</c>.</summary>
public sealed record PropertyInfo
{
    private readonly string? _key;

    /// <summary>Gets the property's name, as a reference writes it: <c>dp</c>, <c>in[2].t</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the identifier a solved value is published under, which is what a deferred reference is keyed by.</summary>
    /// <value><see cref="Name"/> unless <c>D-120</c> respelled it: <c>in[2].t</c> is published as <c>t_in2</c>.</value>
    public string Key
    {
        get => _key ?? Name;
        init => _key = value;
    }

    /// <summary>Gets the spellings a reference used before <c>D-120</c>, read for one language major with <c>FS1536</c>.</summary>
    public ImmutableArray<string> LegacySpellings { get; init; } = [];

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
