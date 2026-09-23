using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>The <c>show</c> directive resolved (<c>57</c>).</summary>
public sealed record VisualizationWire
{
    /// <summary>The property the colour scale follows.</summary>
    public required string Active { get; init; }

    /// <summary>The properties the switcher offers.</summary>
    public required ImmutableArray<string> Available { get; init; }

    /// <summary>The scale for <see cref="Active"/>. The same as <c>Scales[Active]</c>.</summary>
    public required ScaleWire Scale { get; init; }

    /// <summary>A scale per available property (<c>D-117</c>), each with its own domain, so switching needs no recompile (<c>57</c> invariant 6).</summary>
    public required IReadOnlyDictionary<string, ScaleWire> Scales { get; init; }
}
