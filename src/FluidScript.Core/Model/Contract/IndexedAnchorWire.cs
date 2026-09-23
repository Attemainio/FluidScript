using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>An anchor rule for an indexed port family.</summary>
public sealed record IndexedAnchorWire
{
    /// <summary>The port name prefix, <c>in</c> or <c>out</c>.</summary>
    public required string Prefix { get; init; }

    /// <summary>The box side the family sits on.</summary>
    public required string Side { get; init; }

    /// <summary>The outward unit vector every anchor of the family leaves along, <c>[dx, dy]</c>.</summary>
    public required ImmutableArray<double> Direction { get; init; }

    /// <summary>Which port field gives the position along that side.</summary>
    public required string VerticalCoordinate { get; init; }

    /// <summary>The smallest index the rule covers.</summary>
    public required int MinIndex { get; init; }

    /// <summary>The largest index the rule covers.</summary>
    public required int MaxIndex { get; init; }
}
