using System.Collections.Immutable;

namespace FluidScript.Core.Layout.Drawing;

/// <summary>One connection's path, in world units.</summary>
/// <param name="ConnectionId"><c>c{n}</c>, or a non-flow element's id for a signal line.</param>
/// <param name="Kind"><c>pipe</c> for a flow connection, <c>signal</c> for an instrument's leader line.</param>
/// <param name="Points">The polyline, orthogonal segment by segment and normalised (<c>28</c> §20); the first and last points are the inner anchors.</param>
/// <param name="Layer">The draw order (<c>28</c> C16): <c>supply</c> in front, <c>return</c> behind it, <c>signal</c> behind everything.</param>
/// <param name="Hops">Where this route passes behind another it crosses; the picture breaks this route around each.</param>
public sealed record Route(string ConnectionId, string Kind, string Layer, ImmutableArray<Point> Points, ImmutableArray<Point> Hops)
{
    /// <summary>Gets the total length, world units.</summary>
    public double Length
    {
        get
        {
            var length = 0.0;
            for (var i = 1; i < Points.Length; i++)
            {
                length += Points[i - 1].ManhattanTo(Points[i]);
            }

            return length;
        }
    }

    /// <summary>Gets the interior points where the direction changes.</summary>
    public IEnumerable<Point> Bends
    {
        get
        {
            for (var i = 1; i + 1 < Points.Length; i++)
            {
                var a = Points[i - 1];
                var b = Points[i];
                var c = Points[i + 1];
                var horizontalIn = Math.Abs(b.Y - a.Y) < 1e-9;
                var horizontalOut = Math.Abs(c.Y - b.Y) < 1e-9;

                if (horizontalIn != horizontalOut)
                {
                    yield return b;
                }
            }
        }
    }
}
