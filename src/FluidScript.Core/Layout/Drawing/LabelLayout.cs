using System.Collections.Immutable;

namespace FluidScript.Core.Layout;

/// <summary>Lays out the labels: every tag gets a box of its own that no symbol, no other label and no line runs through (<c>53</c> label geometry, <c>C-84</c>).</summary>
/// <remarks>
/// <para>
/// <strong>A label was a point.</strong> <c>LabelAt</c> sat a fixed distance outside its owner's box on
/// the side the symbol's label anchor names, chosen without looking at what was there, so on the
/// substation a label crossed a route and on the header two labels of a stacked pair touched. At two
/// hundred components the label layer holds more boxes than the symbol layer does, and a drawing
/// whose symbols are clear and whose labels collide is the ordinary way a generated P&amp;I diagram
/// becomes unreadable.
/// </para>
/// <para>
/// <strong>The box comes from a declared metric, never from a font</strong> (<c>D-73</c>): the canvas
/// label is 11 px at 60 px to the world unit, and each character advances 0.62 em, so a tag of
/// <c>n</c> characters reserves <c>0.62 × n × 11/60</c> by <c>11/60</c> world units. The resolved font
/// is only required to fit inside it; a wider fallback overflows its own box and moves nothing, which
/// is what keeps a placement the same on every machine.
/// </para>
/// <para>
/// <strong>Placement is a search over a short candidate list, in a fixed order.</strong> First the side
/// the symbol names, then the opposite side, then the other two; on each side, centred first and then
/// displaced along the edge a quarter unit at a time, either way, out to a unit. The first candidate
/// whose box overlaps no other symbol's box, no label already placed and no route wins. Labels are
/// placed in scene order, so the result is deterministic (<c>28</c> A9). A label that cannot be placed
/// clear takes the least-collided candidate and is marked, and the renderer draws a leader from it to
/// its owner rather than dropping it: a symbol whose tag is invisible is worse than a busier drawing,
/// because the tag is what a reader matches against the equipment schedule.
/// </para>
/// <para>
/// Labels do not move symbols or routes. An inline element's label -- a pipe's, a two-port node's -- is
/// shown only above the canvas's 3× level of detail and is not placed here; an instrument's label sits
/// inside its own bubble and is not placed here either.
/// </para>
/// </remarks>
public static class LabelLayout
{
    /// <summary>The label's height, world units: the canvas label's 11 px at 60 px per world unit (<c>55</c>, <c>D-73</c>).</summary>
    public const double Size = 11.0 / 60.0;

    /// <summary>The advance per character, in em (<c>55</c>: the proportional canvas label's reservation).</summary>
    public const double Advance = 0.62;

    /// <summary>The clear gap between a label's box and its owner's box, world units.</summary>
    public const double Gap = 0.15 - (Size / 2);

    /// <summary>The step a label is displaced along its owner's edge when the centred position collides, world units.</summary>
    public const double Step = 0.25;

    /// <summary>How far along the edge the search goes, in steps either way.</summary>
    public const int Reach = 4;

    /// <summary>The box a label of this text reserves, centred on a point.</summary>
    /// <param name="text">The label's text.</param>
    /// <param name="centre">Where its centre sits.</param>
    /// <returns>The box, world units.</returns>
    public static Box BoxFor(string text, Point centre)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Box.Around(centre, Math.Max(1, text.Length) * Advance * Size, Size);
    }

    /// <summary>Places every label that is placed at all, and boxes the rest where they stand.</summary>
    /// <param name="placements">The scene's placements, in scene order, with each <see cref="Placement.LabelAt"/> at the side its symbol names.</param>
    /// <param name="routes">Every route, pipes and signals; a label keeps clear of all of them.</param>
    /// <param name="textOf">The text a component's label carries: its tag, or its id.</param>
    /// <returns>The same placements with <see cref="Placement.LabelAt"/>, <see cref="Placement.LabelBox"/> and <see cref="Placement.LabelClear"/> set.</returns>
    public static ImmutableArray<Placement> Place(
        IReadOnlyList<Placement> placements, IReadOnlyList<Route> routes, Func<Placement, string> textOf)
    {
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(textOf);

        var result = ImmutableArray.CreateBuilder<Placement>(placements.Count);
        var obstacles = placements.Where(static p => !p.IsInline).ToList();
        var segments = new List<(Point A, Point B)>();

        foreach (var route in routes)
        {
            for (var k = 1; k < route.Points.Length; k++)
            {
                segments.Add((route.Points[k - 1], route.Points[k]));
            }
        }

        var placed = new List<Box>();

        foreach (var placement in placements)
        {
            var text = textOf(placement);

            if (placement.IsInline || placement.Inner.ContainsInterior(placement.LabelAt))
            {
                // Not laid out: an inline element's label is a detail-level annotation on its run, and an
                // instrument's label sits inside its own bubble.
                result.Add(placement with { LabelBox = BoxFor(text, placement.LabelAt), LabelClear = true });
                continue;
            }

            var best = placement.LabelAt;
            var bestBox = BoxFor(text, best);
            var bestCost = int.MaxValue;

            foreach (var candidate in Candidates(placement, text))
            {
                var box = BoxFor(text, candidate);
                var cost = Collisions(box, placement, obstacles, placed, segments);

                if (cost < bestCost)
                {
                    (best, bestBox, bestCost) = (candidate, box, cost);
                }

                if (cost == 0)
                {
                    break;
                }
            }

            placed.Add(bestBox);
            result.Add(placement with { LabelAt = best, LabelBox = bestBox, LabelClear = bestCost == 0 });
        }

        return result.ToImmutable();
    }

    /// <summary>The candidate centres for a label, in the order they are tried.</summary>
    private static IEnumerable<Point> Candidates(Placement placement, string text)
    {
        var box = placement.Inner;
        var half = BoxFor(text, placement.LabelAt);
        var (w, h) = (half.Width / 2, half.Height / 2);
        var first = SideOf(placement);

        var opposite = first.Opposite;

        foreach (var side in new[] { first, opposite }.Concat(Direction.All.Where(d => !d.Equals(first) && !d.Equals(opposite))))
        {
            foreach (var offset in Offsets())
            {
                yield return side.Horizontal
                    ? new Point(side.Equals(Direction.Right) ? box.Right + Gap + w : box.X - Gap - w, box.Centre.Y + offset)
                    : new Point(box.Centre.X + offset, side.Equals(Direction.Up) ? box.Top + Gap + h : box.Y - Gap - h);
            }
        }
    }

    private static IEnumerable<double> Offsets()
    {
        yield return 0;

        for (var i = 1; i <= Reach; i++)
        {
            yield return i * Step;
            yield return -i * Step;
        }
    }

    /// <summary>Which side of its owner a label was first put on, read off where it sits.</summary>
    private static Direction SideOf(Placement placement)
    {
        var box = placement.Inner;
        var at = placement.LabelAt;

        if (at.Y >= box.Top)
        {
            return Direction.Up;
        }

        if (at.Y <= box.Y)
        {
            return Direction.Down;
        }

        return at.X >= box.Right ? Direction.Right : Direction.Left;
    }

    /// <summary>How many things a label box at a candidate would overlap: symbols but its owner, labels already placed, and route segments.</summary>
    private static int Collisions(
        Box box, Placement owner, List<Placement> obstacles, List<Box> placed, List<(Point A, Point B)> segments)
    {
        var count = 0;

        foreach (var other in obstacles)
        {
            if (!ReferenceEquals(other, owner) && other.Inner.Intersects(box))
            {
                count++;
            }
        }

        foreach (var label in placed)
        {
            if (label.Intersects(box))
            {
                count++;
            }
        }

        foreach (var (a, b) in segments)
        {
            if (Crosses(box, a, b))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Whether an orthogonal segment passes through a box's interior.</summary>
    private static bool Crosses(Box box, Point a, Point b)
    {
        const double eps = 1e-9;
        var vertical = Math.Abs(a.X - b.X) < eps;

        return vertical
            ? a.X > box.X + eps && a.X < box.Right - eps && Math.Max(Math.Min(a.Y, b.Y), box.Y) < Math.Min(Math.Max(a.Y, b.Y), box.Top) - eps
            : a.Y > box.Y + eps && a.Y < box.Top - eps && Math.Max(Math.Min(a.X, b.X), box.X) < Math.Min(Math.Max(a.X, b.X), box.Right) - eps;
    }
}
