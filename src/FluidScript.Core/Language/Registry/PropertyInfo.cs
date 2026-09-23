using System.Collections.Immutable;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Registry;

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
