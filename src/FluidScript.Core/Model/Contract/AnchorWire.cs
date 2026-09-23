using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>Where a port meets its symbol, and which way a connection leaves it.</summary>
public sealed record AnchorWire
{
    /// <summary>The point on the box edge, <c>[x, y]</c> in symbol units.</summary>
    public required ImmutableArray<double> At { get; init; }

    /// <summary>
    /// The outward unit vector a connection leaves along, <c>[dx, dy]</c> with <c>y</c> up (<c>28</c> A1); rotates with
    /// the box. Absent for the wildcard anchor, whose direction the layout chooses.
    /// </summary>
    [AbsentWhenNull]
    public ImmutableArray<double>? Direction { get; init; }
}
