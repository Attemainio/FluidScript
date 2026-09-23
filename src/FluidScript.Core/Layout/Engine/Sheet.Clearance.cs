using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout.Engine;

internal sealed partial class Sheet
{
    // ---- the one clearance test (28 E3, D-153) --------------------------------------------------------------------

    /// <summary>What an element occupies where it would stand: its box and its instruments' bubbles, and the stubs out of its connected ports.</summary>
    /// <param name="Boxes">The inner box first, then each bubble.</param>
    /// <param name="Stubs">The first margin of pipe out of each connected port.</param>
    public sealed record Footprint(List<Box> Boxes, List<(Point From, Point To)> Stubs);

    /// <summary>An element's footprint under a transform at a centre (<c>D-151</c>): an inline point occupies nothing.</summary>
    public Footprint FootprintOf(int c, Transform t, Point centre)
    {
        if (View.IsInline(c))
        {
            return new Footprint([], []);
        }

        var (w, h) = t.Size(View.Symbols[c]);
        List<Box> boxes = [Box.Around(centre, w, h), .. Bubbles(c, t, centre)];
        var stubs = new List<(Point, Point)>();

        foreach (var p in View.Connected(c))
        {
            if (AnchorOffset(c, p, t) is { } anchor)
            {
                var at = centre.Offset(anchor.Offset.X, anchor.Offset.Y);
                stubs.Add((at, at.Towards(anchor.Outward, Margin)));
            }
        }

        return new Footprint(boxes, stubs);
    }

    /// <summary>
    /// Whether an element standing at <paramref name="centre"/> would break the clearance (H2) against anything on the
    /// canvas: a box or bubble of either within a margin of one of the other's, or a stub of either through the other's
    /// bubble's clearance (<c>D-151</c>). Every placement asks this one question.
    /// </summary>
    public bool Clashes(int c, Transform t, Point centre)
    {
        var mine = FootprintOf(c, t, centre);

        for (var i = 0; i < View.Count; i++)
        {
            if (!Placed[i] || i == c || View.IsInline(i))
            {
                continue;
            }

            var theirs = FootprintOf(i, Transform[i], Centre[i]);

            if (Clash(mine, theirs))
            {
                return true;
            }
        }

        return false;
    }

    private bool Clash(Footprint mine, Footprint theirs)
    {
        foreach (var a in mine.Boxes)
        {
            foreach (var b in theirs.Boxes)
            {
                if (b.Intersects(a.Grow(Margin)) || a.Intersects(b.Grow(Margin)))
                {
                    return true;
                }
            }
        }

        return Crosses(mine.Stubs, theirs.Boxes.Skip(1)) || Crosses(theirs.Stubs, mine.Boxes.Skip(1));
    }

    /// <summary>Whether any segment runs through the clearance of any of the bubbles.</summary>
    private bool Crosses(List<(Point From, Point To)> segments, IEnumerable<Box> bubbles)
    {
        var outers = bubbles.Select(b => b.Grow(Margin)).ToList();
        return outers.Count > 0 && segments.Any(s => outers.Any(o => Passes(o, s.From, s.To)));
    }

    /// <summary>
    /// The nearest point at least <paramref name="minimum"/> (and a margin) from <paramref name="origin"/> along
    /// <paramref name="along"/> where the element, its centre <paramref name="delta"/> from that point, clears the
    /// canvas; stepped by a tenth of a unit, so slack goes into the pipe (H2).
    /// </summary>
    /// <returns>The point the pipe ends at: the element's inner anchor.</returns>
    public Point Clear(int c, Transform t, Point origin, Direction along, Point delta, double minimum = 0)
    {
        var gap = Math.Max(Margin, minimum);
        var at = origin.Towards(along, gap);

        while (Clashes(c, t, at.Offset(delta.X, delta.Y)))
        {
            gap += 0.1;
            at = origin.Towards(along, gap);
        }

        return at;
    }

    /// <summary>How far right a set of elements moved by (dx, dy) must go before none of their boxes is within a margin of a placed box: 0 when clear.</summary>
    public double Shift(IReadOnlyList<int> members, double dx, double dy)
    {
        var shift = 0.0;

        foreach (var j in members)
        {
            if (View.IsInline(j))
            {
                continue;
            }

            var inner = InnerOf(j).Offset(dx, dy);
            var outer = inner.Grow(Margin);

            for (var i = 0; i < View.Count; i++)
            {
                if (!Placed[i] || View.IsInline(i) || members.Contains(i))
                {
                    continue;
                }

                var other = InnerOf(i);

                if (other.Intersects(outer) || inner.Intersects(other.Grow(Margin)))
                {
                    shift = Math.Max(shift, other.Grow(Margin).Right - inner.X);
                }
            }
        }

        return shift;
    }

    /// <summary>Whether a set of elements moved by (dx, dy) clears the canvas by the one test: boxes, bubbles and stubs.</summary>
    public bool ClearAt(IReadOnlyList<int> members, double dx, double dy)
    {
        foreach (var j in members)
        {
            var mine = FootprintOf(j, Transform[j], Centre[j].Offset(dx, dy));

            for (var i = 0; i < View.Count; i++)
            {
                if (Placed[i] && !View.IsInline(i) && !members.Contains(i) && Clash(mine, FootprintOf(i, Transform[i], Centre[i])))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether a vertical pipe at <paramref name="x"/> between two heights stays out of every placed box's clearance, the members in <paramref name="except"/> aside.</summary>
    public bool Free(double x, double y0, double y1, IReadOnlyList<int> except)
    {
        for (var i = 0; i < View.Count; i++)
        {
            if (!Placed[i] || View.IsInline(i) || except.Contains(i))
            {
                continue;
            }

            var box = InnerOf(i).Grow(Margin);

            if (box.X < x && x < box.Right && box.Y < y1 && y0 < box.Top)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a level or vertical move from one point to another crosses a placed box's clearance, <paramref name="self"/>'s aside.</summary>
    public bool Obstructed(Point from, Point to, int self)
    {
        for (var i = 0; i < View.Count; i++)
        {
            if (i != self && Placed[i] && !View.IsInline(i) && Passes(InnerOf(i).Grow(Margin), from, to))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether an orthogonal segment runs through a box's interior.</summary>
    public static bool Passes(Box box, Point a, Point b) =>
        Math.Abs(a.X - b.X) < Eps
            ? a.X > box.X + Eps && a.X < box.Right - Eps && Math.Max(Math.Min(a.Y, b.Y), box.Y) < Math.Min(Math.Max(a.Y, b.Y), box.Top) - Eps
            : a.Y > box.Y + Eps && a.Y < box.Top - Eps && Math.Max(Math.Min(a.X, b.X), box.X) < Math.Min(Math.Max(a.X, b.X), box.Right) - Eps;
}
