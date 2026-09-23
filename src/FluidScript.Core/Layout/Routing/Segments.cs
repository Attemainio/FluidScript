using FluidScript.Core.Layout.Drawing;

namespace FluidScript.Core.Layout.Routing;

/// <summary>Orthogonal segment geometry the engine and the audit share: where two segments cross, what two collinear ones share, whether three points are in line.</summary>
/// <remarks>
/// Each caller brings its own tolerance: the engine works in world units at <c>1e-9</c>, the audit on
/// rounded output at <c>1e-6</c>, and merging the two would change what each measures. Before <c>70</c>'s R5
/// the engine's <c>Crossings</c> and <c>SignalCrosses</c> and the audit's <c>Crosses</c> and <c>Shared</c>
/// were the same arithmetic written twice.
/// </remarks>
internal static class Segments
{
    /// <summary>Whether a segment is vertical, within the tolerance.</summary>
    /// <param name="a">One end.</param>
    /// <param name="b">The other.</param>
    /// <param name="eps">The tolerance.</param>
    /// <returns><see langword="true"/> when the ends share an x.</returns>
    public static bool IsVertical(Point a, Point b, double eps) => Math.Abs(a.X - b.X) < eps;

    /// <summary>Where two orthogonal segments cross, strictly inside both.</summary>
    /// <param name="a">The first segment's start.</param>
    /// <param name="b">Its end.</param>
    /// <param name="c">The second segment's start.</param>
    /// <param name="d">Its end.</param>
    /// <param name="eps">The tolerance.</param>
    /// <returns>The crossing, or <see langword="null"/> when they are parallel or meet only at an end.</returns>
    public static Point? Crossing(Point a, Point b, Point c, Point d, double eps)
    {
        var abVertical = IsVertical(a, b, eps);
        var cdVertical = IsVertical(c, d, eps);

        if (abVertical == cdVertical)
        {
            return null;
        }

        var (v0, v1, h0, h1) = abVertical ? (a, b, c, d) : (c, d, a, b);
        var x = v0.X;
        var y = h0.Y;
        var inside = x > Math.Min(h0.X, h1.X) + eps && x < Math.Max(h0.X, h1.X) - eps && y > Math.Min(v0.Y, v1.Y) + eps && y < Math.Max(v0.Y, v1.Y) - eps;
        return inside ? new Point(x, y) : null;
    }

    /// <summary>The length two collinear segments share beyond a point, or nothing.</summary>
    /// <param name="a">The first segment's start.</param>
    /// <param name="b">Its end.</param>
    /// <param name="c">The second segment's start.</param>
    /// <param name="d">Its end.</param>
    /// <param name="eps">The tolerance.</param>
    /// <returns>The shared length, or <see langword="null"/> when they are not on one line or touch only at a point.</returns>
    public static double? Shared(Point a, Point b, Point c, Point d, double eps)
    {
        var vertical = IsVertical(a, b, eps);

        if (vertical != IsVertical(c, d, eps))
        {
            return null;
        }

        var (line, otherLine) = vertical ? (a.X, c.X) : (a.Y, c.Y);

        if (Math.Abs(line - otherLine) > eps)
        {
            return null;
        }

        var (a0, a1, c0, c1) = vertical ? (a.Y, b.Y, c.Y, d.Y) : (a.X, b.X, c.X, d.X);
        var low = Math.Max(Math.Min(a0, a1), Math.Min(c0, c1));
        var high = Math.Min(Math.Max(a0, a1), Math.Max(c0, c1));
        return high - low > eps ? high - low : null;
    }

    /// <summary>Whether three points lie on one vertical or one horizontal line.</summary>
    /// <param name="a">The first.</param>
    /// <param name="b">The second.</param>
    /// <param name="c">The third.</param>
    /// <param name="eps">The tolerance.</param>
    /// <returns><see langword="true"/> when they do.</returns>
    public static bool Collinear(Point a, Point b, Point c, double eps) =>
        (Math.Abs(a.X - b.X) < eps && Math.Abs(b.X - c.X) < eps) || (Math.Abs(a.Y - b.Y) < eps && Math.Abs(b.Y - c.Y) < eps);
}
