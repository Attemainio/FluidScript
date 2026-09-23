using System.Collections.Immutable;

namespace FluidScript.Core.Language.Registry;

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
