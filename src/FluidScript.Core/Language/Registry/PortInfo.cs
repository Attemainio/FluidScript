using System.Collections.Immutable;

namespace FluidScript.Core.Language.Registry;

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
