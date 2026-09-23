using System.Collections.Immutable;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout;

internal sealed partial class LayoutEngine
{
    /// <summary>The unit around a ring's consumer (C11): the consumer alone, or the block of the inner loop through it that avoids <paramref name="avoid"/>; with the range of ring members the unit replaces.</summary>
    private (int Start, int End, Unit? Unit) UnitOf(List<Member> cycle, int consumerAt, HashSet<int> avoid, Direction outlet)
    {
        var (start, end, inner) = Column(cycle, consumerAt, avoid);
        return (start, end, inner is null ? Single(cycle[consumerAt]) : Block(inner, cycle[start], cycle[end], avoid, outlet));
    }

    /// <summary>
    /// The junctions that take a ring's bottom corners (C10): on the right, the one next to the unit under an outlet
    /// that faces down or right; on the left, when the ring is a block whose outlet faces left, the one nearest the
    /// left side -- so the block's outlet faces its parent beside its inlet. One junction takes one corner.
    /// </summary>
    private (Member? Left, Member? Right) Corners(List<Member> bottomMembers, Unit unit, bool leftFirst)
    {
        var left = leftFirst && bottomMembers.Count > 0 && Wildcard(bottomMembers[^1].Component) ? bottomMembers[^1] : (Member?)null;
        var under = unit.Out.Outward == Direction.Down || unit.Out.Outward == Direction.Right;
        var right = under && bottomMembers.Count > (left is null ? 0 : 1) && Wildcard(bottomMembers[0].Component) ? bottomMembers[0] : (Member?)null;
        return (left, right);
    }

    /// <summary>One member as a ring's right side, placed with its inlet at the local origin: at the corner if it can turn the flow from leftward to downward (C9), else taking the flow from above.</summary>
    private Unit? Single(Member m)
    {
        List<Transform> candidates;

        if (Wildcard(m.Component))
        {
            candidates = [Transform.Identity];
        }
        else
        {
            bool Faces(Transform t, Direction inward) => Outward(m.Component, m.InPort, t) == inward && Outward(m.Component, m.OutPort, t) == Direction.Down;

            // Any arrangement the symbol offers may turn the corner, the default first (A9, D-112).
            var turning = Admitted(m.Component).Where(t => Faces(t, Direction.Left)).ToList();
            candidates = turning.Count > 0 ? turning : Admitted(m.Component).Where(t => Faces(t, Direction.Up)).ToList();
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var (_, h) = candidates[0].Size(_symbol[m.Component]);
        var inOffset = Wildcard(m.Component) ? new Point(0, h / 2) : AnchorOffset(m.Component, m.InPort, candidates[0])!.Value.Offset;
        Place(m.Component, candidates[0], new Point(-inOffset.X, -inOffset.Y), "C11", "a single member as the unit, its inlet at the origin, turned to face the top rail (A9)");

        if (Wildcard(m.Component))
        {
            _side[(m.Component, m.InPort)] = Direction.Up;
            _side[(m.Component, m.OutPort)] = Direction.Down;
        }

        return new Unit([m.Component], AnchorOf(m.Component, m.InPort), AnchorOf(m.Component, m.OutPort), m, InnerOf(m.Component).Y, []);
    }

    /// <summary>
    /// An inner loop laid out as its own clockwise ring at the local origin (C11): its consumer's unit on the right,
    /// the first member after it that turns the flow from upward to rightward at the top-left corner with the block's
    /// inlet facing out to the left, the members between them on the bottom rail and the rest on the top. The
    /// block's outlet -- its junction's free port -- faces <paramref name="outlet"/>: left, beside the inlet, when the
    /// parent's return goes back that way; right when the parent continues on to the next member.
    /// </summary>
    /// <param name="path">The inner loop in flow order from its consumer.</param>
    /// <param name="entry">The member and port the outer loop enters the block by.</param>
    /// <param name="exit">The member and port the outer loop leaves the block by.</param>
    /// <param name="avoid">The enclosing rings' sources and corner members, which a nested unit's search must not pass.</param>
    /// <param name="outlet">Which way the block's outlet faces.</param>
    /// <param name="deeper">How much lower than its own need the block's bottom rail lies, so that its outlet meets a taller neighbour's rail level (C12); zero for its own need.</param>
    /// <returns>The block as a unit, or <see langword="null"/> when no member can take the corner or the entry and exit do not face the ring.</returns>
    private Unit? Block(List<Member> path, Member entry, Member exit, HashSet<int> avoid, Direction outlet, double deeper = 0)
    {
        var cornerAt = -1;
        var tc = Transform.Identity;

        for (var k = 1; k < path.Count && cornerAt < 0; k++)
        {
            var m = path[k];

            if (Wildcard(m.Component))
            {
                continue;
            }

            var external = m.Component == entry.Component ? entry.InPort : -1;
            var turning = Admitted(m.Component).Where(t => Outward(m.Component, m.InPort, t) == Direction.Down && Outward(m.Component, m.OutPort, t) == Direction.Right && (external < 0 || Outward(m.Component, external, t) == Direction.Left)).ToList();

            if (turning.Count > 0)
            {
                cornerAt = k;
                tc = turning[0];
            }
        }

        if (cornerAt < 0)
        {
            return null;
        }

        var corner = path[cornerAt];
        var mark = _groups.Count;

        // The block is laid out on its own (C11): nothing placed so far is an obstacle to it, and it comes back unplaced, to be slid in by its parent.
        var placed = (bool[])_placed.Clone();
        void Restore() => placed.CopyTo(_placed, 0);
        Array.Clear(_placed);
        var (_, innerEnd, unit) = UnitOf(path, 0, [.. avoid, corner.Component], Direction.Left);

        if (unit is null || innerEnd >= cornerAt)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            Restore();
            return null;
        }

        foreach (var i in unit.Members)
        {
            _placed[i] = false;
        }

        var bottomMembers = path.GetRange(innerEnd + 1, cornerAt - innerEnd - 1);
        var items = path.GetRange(cornerAt + 1, path.Count - cornerAt - 1).Select(static m => new Item(m, null)).ToList();
        Place(corner.Component, tc, new Point(0, 0), "C9", "the corner member at the origin, in the arrangement that turns the pipe");
        var cOut = AnchorOf(corner.Component, corner.OutPort);
        var cIn = AnchorOf(corner.Component, corner.InPort);
        var (left, right) = Corners(bottomMembers, unit, leftFirst: outlet == Direction.Left);
        var runs = new List<(Member From, List<Point> Points)>();
        var hangers = new List<Hanger>();

        if (Top(cOut, [cOut.At], corner, items, path.Select(static m => m.Component).ToHashSet(), null, avoid, runs, hangers) is not { } top)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            Restore();
            return null;
        }

        var natural = cIn.Along(_margin).Y - (left is { } l ? Transform.Identity.Size(_symbol[l.Component]).Height / 2 : 0);
        var (drop, yBottom) = Bottom(unit, top.End.At.Y, natural, right?.Component ?? -1);

        if (deeper > Eps)
        {
            // C12: the bottom rail goes down by the caller's need, whatever set it; a unit hung from above keeps its outlet mid-side.
            yBottom -= deeper;
            drop += unit.In.Outward == Direction.Up ? deeper / 2 : 0;
        }

        if (!Close(top, unit, drop, yBottom, bottomMembers, [cIn.At, cIn.Along(_margin), new Point(cIn.At.X, yBottom)], left, right, hangers, runs))
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            Restore();
            return null;
        }

        if (Wildcard(exit.Component))
        {
            // The outlet leaves by a side the junction's other ports leave free, the parent's way first, then down.
            var used = new HashSet<Direction>();

            for (var p = 0; p < _graph.Components[exit.Component].Ports.Length; p++)
            {
                if (p != exit.OutPort && _side.TryGetValue((exit.Component, p), out var side))
                {
                    used.Add(side);
                }
            }

            Direction[] order = outlet == Direction.Left ? [Direction.Left, Direction.Down, Direction.Right] : [Direction.Right, Direction.Down, Direction.Left];
            _side[(exit.Component, exit.OutPort)] = order.First(d => !used.Contains(d));
        }

        var members = path.Select(static m => m.Component).ToList();
        var entering = AnchorOf(entry.Component, entry.InPort);
        var leaving = AnchorOf(exit.Component, exit.OutPort);

        if (entering.Outward != Direction.Left || leaving.Outward == Direction.Up)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            Restore();
            return null;
        }

        // The block is a group (A8), listed before the blocks it holds.
        _groups.Insert(mark, LoopGroup(members, false));
        Restore();

        return new Unit(members, entering, leaving, new Member(exit.Component, -1, exit.OutPort), members.Min(i => InnerOf(i).Y), runs);
    }

    /// <summary>
    /// How far the unit's inlet drops below the rail it is fed from and where the bottom rail lies: level with an
    /// outlet that faces left; else low enough for the unit, for the stub out of it, and for a corner junction under
    /// its outlet (C10). A unit entered from above with the other side the taller is centred on its side (C12).
    /// </summary>
    private (double Drop, double YBottom) Bottom(Unit unit, double yTop, double natural, int junction)
    {
        var drop = unit.In.Outward == Direction.Up ? _margin : 0;
        var half = junction < 0 ? 0 : Transform.Identity.Size(_symbol[junction]).Height / 2;
        var stub = junction >= 0 && unit.Out.Outward == Direction.Right ? _margin : 0;
        double Low(double dy) => unit.Out.Outward == Direction.Left
            ? unit.Out.At.Y + dy
            : Math.Min(unit.Bottom + dy - _margin, unit.Out.Along(_margin).Y + dy - half - stub);
        var low = Low(yTop - drop - unit.In.At.Y);

        if (unit.In.Outward == Direction.Up && low > natural)
        {
            drop += (low - natural) / 2;
            low = Low(yTop - drop - unit.In.At.Y);
        }

        return (drop, Math.Min(natural, low));
    }

    /// <summary>The bottom rail's highest admissible height under the hanging branches: a margin under the lowest block, and room for the junction each returns to.</summary>
    private double Under(List<Hanger> hangers) =>
        hangers.Count == 0 ? double.MaxValue : hangers.Min(h => h.Out.At.Y - _margin - Transform.Identity.Size(_symbol[h.Bottom]).Height / 2 - _margin / 5);

    /// <summary>
    /// Slides a unit from its provisional place into the layout: its inlet level with <paramref name="yIn"/>, one
    /// margin right of <paramref name="originX"/> and further right until every member clears what is placed (H2)
    /// -- jumping past each obstacle in whole tenths -- and, when <paramref name="clear"/> is given, by tenths
    /// until it holds for the offset. Its runs move with it.
    /// </summary>
    /// <returns>The unit's inlet and outlet where they landed.</returns>
    private (PlacedAnchor In, PlacedAnchor Out) Slide(Unit unit, double originX, double yIn, List<(Member From, List<Point> Points)> runs, Func<double, double, bool>? clear = null)
    {
        var dx = originX + _margin - Math.Min(unit.In.At.X, unit.Out.At.X);
        var dy = yIn - unit.In.At.Y;

        foreach (var i in unit.Members)
        {
            _placed[i] = false;
        }

        while (true)
        {
            var shift = unit.Members.Max(i => Shift(i, _transform[i], _centre[i].Offset(dx, dy)));

            if (shift > 0)
            {
                // The same lattice as stepping by tenths from the start, in one jump.
                dx += Math.Ceiling(shift * 10 - 1e-6) / 10;
                continue;
            }

            if (clear is not null && !clear(dx, dy))
            {
                dx += 0.1;
                continue;
            }

            break;
        }

        foreach (var i in unit.Members)
        {
            _centre[i] = _centre[i].Offset(dx, dy);
            _placed[i] = true;
            Note(_graph.Components[i].Name, "C11", $"slid in as a unit member by ({dx:0.##}, {dy:0.##}) to ({_centre[i].X:0.##}, {_centre[i].Y:0.##}), a margin right of its origin and past every obstacle (H2)");
        }

        foreach (var (walkFrom, points) in unit.Runs)
        {
            runs.Add((walkFrom, points.Select(p => p.Offset(dx, dy)).ToList()));
        }

        return (unit.In with { At = unit.In.At.Offset(dx, dy) }, unit.Out with { At = unit.Out.At.Offset(dx, dy) });
    }

    /// <summary>How far right a member at <paramref name="centre"/> must move to clear every placed member (H2): its largest overlap with any of their margins, 0 when it is clear.</summary>
    private double Shift(int j, Transform t, Point centre)
    {
        var (w, h) = t.Size(_symbol[j]);
        var inner = Box.Around(centre, w, h);
        var outer = inner.Grow(_margin);
        var shift = 0.0;

        for (var i = 0; i < _n; i++)
        {
            if (!_placed[i] || i == j)
            {
                continue;
            }

            var other = InnerOf(i);

            if (other.Intersects(outer) || inner.Intersects(other.Grow(_margin)))
            {
                shift = Math.Max(shift, other.Grow(_margin).Right - inner.X);
            }
        }

        return shift;
    }

    /// <summary>Whether a vertical pipe at <paramref name="x"/> between two heights stays out of every placed member's outer box (B: a pipe never enters a box), the members in <paramref name="except"/> aside.</summary>
    private bool Free(double x, double y0, double y1, IReadOnlyList<int> except)
    {
        for (var i = 0; i < _n; i++)
        {
            if (!_placed[i] || except.Contains(i))
            {
                continue;
            }

            var box = InnerOf(i).Grow(_margin);

            if (box.X < x && x < box.Right && box.Y < y1 && y0 < box.Top)
            {
                return false;
            }
        }

        return true;
    }
}
