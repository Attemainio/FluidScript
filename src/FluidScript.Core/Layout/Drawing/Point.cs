using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout.Drawing;

/// <summary>A point in world units, <c>y</c> up.</summary>
/// <param name="X">Horizontal, growing east.</param>
/// <param name="Y">Vertical, growing north.</param>
public readonly record struct Point(double X, double Y)
{
    /// <summary>This point moved.</summary>
    /// <param name="dx">East.</param>
    /// <param name="dy">North.</param>
    /// <returns>The moved point.</returns>
    public Point Offset(double dx, double dy) => new(X + dx, Y + dy);

    /// <summary>This point moved along a direction.</summary>
    /// <param name="direction">The direction.</param>
    /// <param name="distance">World units; negative moves the other way.</param>
    /// <returns>The moved point.</returns>
    public Point Towards(Direction direction, double distance) => new(X + (direction.X * distance), Y + (direction.Y * distance));

    /// <summary>The Manhattan distance to another point.</summary>
    /// <param name="other">The other point.</param>
    /// <returns><c>|Δx| + |Δy|</c>.</returns>
    public double ManhattanTo(Point other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
}
