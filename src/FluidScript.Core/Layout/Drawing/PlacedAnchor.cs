using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout.Drawing;

/// <summary>Where a port meets its placed symbol, in world units, which way a pipe leaves it and which way the fluid flows (<c>28</c> §2, §5).</summary>
/// <param name="At">The inner anchor: the point on the inner box's edge.</param>
/// <param name="Direction">The outward unit vector, one of the four axis directions; the stub to the outer anchor runs along it.</param>
/// <param name="Flow">The flow vector: <see cref="Direction"/> for an outlet, its opposite for an inlet.</param>
public readonly record struct PlacedAnchor(Point At, Point Direction, Direction Flow)
{

    /// <summary>The point <paramref name="distance"/> along the outward direction; at the margin it is the outer anchor.</summary>
    /// <param name="distance">World units.</param>
    /// <returns>The point.</returns>
    public Point Along(double distance) => At.Offset(Direction.X * distance, Direction.Y * distance);

    /// <summary>Gets the outward direction as a <see cref="FluidScript.Core.Layout.Routing.Direction"/>.</summary>
    public Direction Outward => FluidScript.Core.Layout.Routing.Direction.Of(Direction) ?? FluidScript.Core.Layout.Routing.Direction.Right;
}
