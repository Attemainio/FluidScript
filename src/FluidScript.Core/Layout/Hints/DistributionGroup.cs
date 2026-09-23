using System.Collections.Immutable;

namespace FluidScript.Core.Layout.Hints;

/// <summary>Circuits sharing one supply/return pair, in stacking order (<c>D-38</c>).</summary>
public sealed record DistributionGroup
{
    /// <summary>The circuit that owns the two header lines.</summary>
    public required string ParentCircuit { get; init; }

    /// <summary>The member circuits, at least two, in declaration order.</summary>
    public required ImmutableArray<string> Members { get; init; }
}
