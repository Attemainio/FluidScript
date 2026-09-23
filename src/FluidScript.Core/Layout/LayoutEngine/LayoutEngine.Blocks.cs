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

    /// <summary>
    /// Lays a ring's top rail rightwards from its start: each member by <see cref="OnRail"/>, each block by
    /// <see cref="Slide"/> with the rail continuing from its outlet, and under each junction whose free port leads
    /// to a bottom-rail member, the branch's block hanging between the rails (C14). Appends the runs so far.
    /// </summary>
    /// <returns>Where the rail ends, its pending points and the member it leaves; <see langword="null"/> when a member cannot be placed.</returns>
    private (PlacedAnchor End, List<Point> Pending, Member Previous)? Top(PlacedAnchor cursor, List<Point> pending, Member previous, List<Item> items, IReadOnlySet<int> ring, List<Member>? bottomMembers, HashSet<int> avoid, List<(Member From, List<Point> Points)> runs, List<Hanger> hangers)
    {
        foreach (var item in items)
        {
            if (item.Unit is { } u)
            {
                var (uIn, uOut) = Slide(u, cursor.At.X, cursor.At.Y, runs);
                pending.Add(uIn.At);
                runs.Add((previous, pending));
                cursor = uOut;
                pending = [uOut.At];
                previous = u.OutFrom;
                continue;
            }

            var m = item.Member!.Value;

            if (OnRail(cursor, m, m.InPort, m.OutPort) is not { } points)
            {
                return null;
            }

            pending.AddRange(points.Skip(1));
            runs.Add((previous, pending));
            cursor = AnchorOf(m.Component, m.OutPort);
            previous = m;

            if (bottomMembers is not null && Wildcard(m.Component) && Hang(m, cursor.At.Y, ring, bottomMembers, avoid, runs, hangers) is { } moved)
            {
                cursor = moved;
            }

            pending = [cursor.At];
        }

        return (cursor, pending, previous);
    }

    /// <summary>
    /// C14: a branch off a top-rail junction that reaches a bottom-rail member hangs between the rails. The branch
    /// is a chain of blocks like a rail (C11): its first block hangs one margin under the junction with its inlet
    /// one margin right of it, the junction moved along its rail to stand over the inlet so the drop from its free
    /// port is one bend; each further block steps on from the previous block's outlet, and the last faces back to
    /// the left. The return is laid once the bottom rail exists (<see cref="Close"/>).
    /// </summary>
    /// <returns>The junction's outlet where the rail continues from, or <see langword="null"/> when nothing hangs.</returns>
    private PlacedAnchor? Hang(Member j, double yTop, IReadOnlySet<int> ring, List<Member> bottomMembers, HashSet<int> avoid, List<(Member From, List<Point> Points)> runs, List<Hanger> hangers)
    {
        var ports = _graph.Components[j.Component].Ports;

        for (var p = 0; p < ports.Length; p++)
        {
            if (p == j.InPort || p == j.OutPort)
            {
                continue;
            }

            foreach (var b in bottomMembers)
            {
                var path = new List<Member>();
                var visited = new HashSet<int>(ring) { j.Component };
                visited.Remove(b.Component);

                if (!Extend(j.Component, -1, p, b.Component, path, visited) || path.Count < 2)
                {
                    continue;
                }

                var branch = path.GetRange(1, path.Count - 1);
                var bottomPort = path[0].InPort;
                HashSet<int> branchAvoid = [.. avoid, .. ring];
                var ranges = Ranges(branch, 0, branchAvoid);

                if (ranges.Count == 0)
                {
                    continue;
                }

                var mark = _groups.Count;
                var units = new List<Unit>();
                var items = new List<Item>();
                var next = 0;

                foreach (var (start, end, inner) in ranges)
                {
                    var block = Block(inner, branch[start], branch[end], branchAvoid, end == ranges[^1].End ? Direction.Left : Direction.Right);

                    if (block is null)
                    {
                        break;
                    }

                    if (units.Count > 0)
                    {
                        items.AddRange(branch.GetRange(next, start - next).Select(static m => new Item(m, null)));
                        items.Add(new Item(null, block));
                    }

                    units.Add(block);
                    next = end + 1;
                }

                if (units.Count < ranges.Count)
                {
                    _groups.RemoveRange(mark, _groups.Count - mark);
                    continue;
                }

                foreach (var i in units.SelectMany(static u => u.Members))
                {
                    _loop[i] = true;
                }

                var first = units[0];
                var feed = runs.Count - 1;
                var rise = first.Members.Max(i => InnerOf(i).Top) - first.In.At.Y;
                var half = Transform.Identity.Size(_symbol[j.Component]).Height / 2;
                var (uIn, uOut) = Slide(first, _centre[j.Component].X, yTop - half - _margin - rise, runs);
                var jx = uIn.At.X - _margin;

                if (jx > _centre[j.Component].X)
                {
                    _centre[j.Component] = new Point(jx, _centre[j.Component].Y);
                    Note(_graph.Components[j.Component].Name, "C14", $"the junction moved right to ({jx:0.##}, {_centre[j.Component].Y:0.##}) so its hanger's inlet clears it by a margin");
                    runs[feed].Points[^1] = AnchorOf(j.Component, j.InPort).At;
                }

                _side[(j.Component, p)] = Direction.Down;
                var free = AnchorOf(j.Component, p);
                runs.Add((new Member(j.Component, -1, p), [free.At, new Point(free.At.X, uIn.At.Y), uIn.At]));

                // The rest of the chain steps on from the first block's outlet, as a rail does.
                if (Top(uOut, [uOut.At], first.OutFrom, items, ring, null, branchAvoid, runs, hangers) is not { } chain)
                {
                    return null;
                }

                hangers.Add(new Hanger(chain.End, chain.Previous, j.Component, b.Component, bottomPort));
                return AnchorOf(j.Component, j.OutPort);
            }
        }

        return null;
    }

    /// <summary>The runs of consecutive members that the inner loops along a ring or branch take (C11), in flow order from <paramref name="from"/>, each with its loop.</summary>
    private List<(int Start, int End, List<Member> Inner)> Ranges(List<Member> cycle, int from, HashSet<int> avoid)
    {
        var ranges = new List<(int Start, int End, List<Member> Inner)>();

        for (var k = from; k < cycle.Count; k++)
        {
            var (start, end, inner) = Column(cycle, k, avoid);

            if (inner is not null)
            {
                ranges.Add((start, end, inner));
                k = end;
            }
        }

        return ranges;
    }

    /// <summary>
    /// Closes a ring whose top rail is laid: the bottom rail rightwards from its start against the flow, the corner
    /// junctions (C10), the unit slid into the right side at the top rail's end, and the hanging branches' returns
    /// down into their bottom junctions. Appends the ring's remaining runs.
    /// </summary>
    private bool Close((PlacedAnchor End, List<Point> Pending, Member Previous) top, Unit unit, double drop, double yBottom, List<Member> bottomMembers, List<Point> bottomStart, Member? leftJunction, Member? rightJunction, List<Hanger> hangers, List<(Member From, List<Point> Points)> runs, (Member Member, Transform Transform)? leftTurner = null)
    {
        var (topEnd, topPending, topPrevious) = top;

        // The bottom rail, built left to right against the flow, each member placed by its outlet facing the left side.
        var cursor = Anchor(bottomStart[^1], Direction.Right, Direction.Right);
        var pending = bottomStart;
        var bottomRuns = new List<(Member From, List<Point> Points)>();
        var first = bottomMembers.Count - 1;

        if (leftJunction is { } lj)
        {
            // The junction nearest the left side takes the bottom-left corner: in from the rail, out up the left side; its free port is the block's outlet, facing out beside the inlet.
            Place(lj.Component, Transform.Identity, bottomStart[^1], "C14", "the junction nearest the left side takes the bottom-left corner");
            _side[(lj.Component, lj.OutPort)] = Direction.Up;
            _side[(lj.Component, lj.InPort)] = Direction.Right;
            pending.RemoveAt(pending.Count - 1);
            pending.Add(AnchorOf(lj.Component, lj.OutPort).At);
            bottomRuns.Add((lj, pending));
            cursor = AnchorOf(lj.Component, lj.InPort);
            pending = [cursor.At];
            first--;
        }
        else if (leftTurner is { } lt)
        {
            // A member that turns the flow from leftward to upward takes the bottom-left corner (C9 mirrored): its
            // inlet on the rail facing right, its outlet on the left side's line facing up, so the side's run ends on
            // it and the rail starts from it.
            var dIn = AnchorOffset(lt.Member.Component, lt.Member.InPort, lt.Transform)!.Value.Offset;
            var dOut = AnchorOffset(lt.Member.Component, lt.Member.OutPort, lt.Transform)!.Value.Offset;
            var at = bottomStart[^1];
            Place(lt.Member.Component, lt.Transform, new Point(at.X - dOut.X, at.Y - dIn.Y), "C9", "the left turner takes the bottom-left corner, mirrored (C-105)");
            pending.RemoveAt(pending.Count - 1);
            pending.Add(AnchorOf(lt.Member.Component, lt.Member.OutPort).At);
            bottomRuns.Add((lt.Member, pending));
            cursor = AnchorOf(lt.Member.Component, lt.Member.InPort);
            pending = [cursor.At];
        }

        for (var k = first; k >= 0; k--)
        {
            var m = bottomMembers[k];

            if (hangers.Find(h => h.Bottom == m.Component) is { } hanging)
            {
                // C14: the junction a branch returns to stands directly under the one that feeds it -- as a loop's supply and return nodes align -- and never nearer the outlet than a margin, so the return is one bend; the rail runs on to it.
                var half = Transform.Identity.Size(_symbol[m.Component]).Width / 2;
                var x = Math.Min(_centre[hanging.Top].X, hanging.Out.At.X - _margin) - half - _margin;

                if (x > cursor.At.X)
                {
                    cursor = Anchor(new Point(x, cursor.At.Y), Direction.Right, Direction.Right);
                }
            }

            if (OnRail(cursor, m, m.OutPort, m.InPort) is not { } points)
            {
                return false;
            }

            pending.AddRange(points.Skip(1));
            bottomRuns.Add((m, pending));
            cursor = AnchorOf(m.Component, m.InPort);
            pending = [cursor.At];
        }

        // The unit: as far right as the longer rail needs, its inlet level with the top rail's end (or a drop under it).
        var origin = Math.Max(topEnd.At.X, cursor.At.X);

        if (rightJunction is { } j0)
        {
            // The junction moves under the unit's outlet (C10), so its rail box is not in the way: the unit needs only to put that outlet at or beyond the junction's packed rail position.
            _placed[j0.Component] = false;
            origin = Math.Max(topEnd.At.X, _centre[j0.Component].X - (unit.Out.Along(_margin).X - unit.In.At.X) - _margin);
        }

        // A left-facing outlet descends to the bottom rail one margin out; the descent must clear the boxes too.
        Func<double, double, bool>? clear = rightJunction is null && unit.Out.Outward == Direction.Left
            ? (dx, dy) => Free(unit.Out.Along(_margin).X + dx, yBottom, unit.Out.At.Y + dy, unit.Members)
            : null;
        var (uIn, uOut) = Slide(unit, origin, topEnd.At.Y - drop, runs, clear);

        if (rightJunction is { } j1)
        {
            _placed[j1.Component] = true;
        }

        if (uIn.Outward == Direction.Up)
        {
            topPending.Add(new Point(uIn.At.X, topEnd.At.Y));
        }

        topPending.Add(uIn.At);
        runs.Add((topPrevious, topPending));
        var outOuter = uOut.Along(_margin);

        if (rightJunction is { } j)
        {
            // The junction slides right along its rail until it clears the unit, under the outlet stub; the descent lands on it from above.
            var jx = outOuter.X;
            var jy = _centre[j.Component].Y;

            while (Clashes(j.Component, Transform.Identity, new Point(jx, jy)))
            {
                jx += 0.1;
            }

            _centre[j.Component] = new Point(jx, jy);
            Note(_graph.Components[j.Component].Name, "C14", $"the hanger's bottom junction slid right along its rail to ({jx:0.##}, {jy:0.##}), under the unit's outlet stub and clear of it");
            _side[(j.Component, j.InPort)] = Direction.Up;
            var reaching = bottomRuns.FindIndex(r => r.From.Component == j.Component);
            bottomRuns[reaching].Points[^1] = AnchorOf(j.Component, j.OutPort).At;
            pending = [AnchorOf(j.Component, j.InPort).At, new Point(jx, uOut.At.Y), uOut.At];
        }
        else
        {
            pending.AddRange([new Point(outOuter.X, yBottom), outOuter, uOut.At]);
        }

        bottomRuns.Add((unit.OutFrom, pending));

        foreach (var (walkFrom, points) in bottomRuns)
        {
            points.Reverse();
            runs.Add((walkFrom, points));
        }

        // C14: each hanging branch returns from its outlet, level to above its bottom junction and down into it.
        foreach (var h in hangers)
        {
            _side[(h.Bottom, h.BottomPort)] = Direction.Up;
            var up = AnchorOf(h.Bottom, h.BottomPort);
            runs.Add((h.OutFrom, [h.Out.At, new Point(up.At.X, h.Out.At.Y), up.At]));
        }

        return true;
    }

    /// <summary>
    /// The run of consecutive ring members around <paramref name="consumerAt"/> -- any member of the ring -- that an
    /// inner loop through it avoiding <paramref name="avoid"/> passes through (C11), and that inner loop in flow
    /// order from its consumer.
    /// </summary>
    /// <remarks>
    /// With no such inner loop, or one that leaves the ring through a boxed member -- a second branch, not a
    /// recirculation -- the run is the consumer alone and the path is <see langword="null"/>.
    /// </remarks>
    private (int Start, int End, List<Member>? Inner) Column(List<Member> cycle, int consumerAt, HashSet<int> avoid)
    {
        var c = cycle[consumerAt];
        var path = new List<Member>();
        var visited = new HashSet<int>(avoid) { c.Component };

        if (!Extend(c.Component, -1, c.OutPort, c.Component, path, visited))
        {
            return (consumerAt, consumerAt, null);
        }

        // The search may start anywhere on the inner loop; the block wants it in flow order from its consumer.
        var at = ConsumerOf(path, 0);

        if (at < 0)
        {
            return (consumerAt, consumerAt, null);
        }

        path = [.. path.Skip(at), .. path.Take(at)];
        var inner = path.Select(static m => m.Component).ToHashSet();
        var start = consumerAt;
        var end = consumerAt;

        while (start > 0 && !avoid.Contains(cycle[start - 1].Component) && inner.Contains(cycle[start - 1].Component))
        {
            start--;
        }

        while (end + 1 < cycle.Count && !avoid.Contains(cycle[end + 1].Component) && inner.Contains(cycle[end + 1].Component))
        {
            end++;
        }

        foreach (var m in path)
        {
            var index = cycle.FindIndex(x => x.Component == m.Component);

            if (index < start || index > end)
            {
                return (consumerAt, consumerAt, null);
            }
        }

        return (start, end, path);
    }

    /// <summary>A loop member placed on a rail from the cursor: a component by <see cref="PlaceFrom"/> through the port facing the cursor, a junction by <see cref="PlaceNode"/> with its other loop port turned on along the rail.</summary>
    /// <param name="cursor">Where the rail has reached.</param>
    /// <param name="m">The member.</param>
    /// <param name="facing">Its port towards the cursor.</param>
    /// <param name="onward">Its other loop port.</param>
    /// <returns>The pipe from the cursor to the facing port, or null when nothing admitted fits.</returns>
    private ImmutableArray<Point>? OnRail(PlacedAnchor cursor, Member m, int facing, int onward)
    {
        if (!Wildcard(m.Component))
        {
            return PlaceFrom(cursor, m.Component, facing);
        }

        var points = PlaceNode(cursor, m.Component, facing);
        _side[(m.Component, onward)] = cursor.Outward;
        return points;
    }

    /// <summary>Whether a loop member has a connection off the loop the fluid leaves by.</summary>
    private bool LeavesLoop(Member m)
    {
        var ports = _graph.Components[m.Component].Ports;

        for (var p = 0; p < ports.Length; p++)
        {
            if (p != m.InPort && p != m.OutPort && !Enters(m.Component, p, ports[p].Role) && _links.Any(l => (l.From == m.Component && l.FromPort == p) || (l.To == m.Component && l.ToPort == p)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A junction's side for a port no rule has placed: one no other port uses, the side facing away from the loop's centre first for a loop member -- vertical where the rail is level, level where it is vertical -- else the first free from right, up, left, down.</summary>
    private Direction FreeSide(int i)
    {
        var used = new HashSet<Direction>();

        for (var p = 0; p < _graph.Components[i].Ports.Length; p++)
        {
            if (_side.TryGetValue((i, p), out var s))
            {
                used.Add(s);
            }
        }

        var preferred = new List<Direction>();

        if (_loop[i])
        {
            var away = _centre[i].Offset(-_loopCentre.X, -_loopCentre.Y);
            var level = used.Contains(Direction.Left) || used.Contains(Direction.Right);
            var vertical = used.Contains(Direction.Up) || used.Contains(Direction.Down);
            var levelAway = away.X >= 0 ? Direction.Right : Direction.Left;
            var verticalAway = away.Y >= 0 ? Direction.Up : Direction.Down;

            // On a level rail the free port leaves vertically; on a vertical side, level. At a corner (C10) it leaves level, so an open end there lines up with the loop's other open ends (C7).
            preferred.AddRange(level && !vertical ? [verticalAway, levelAway] : [levelAway, verticalAway]);
        }

        preferred.AddRange([Direction.Right, Direction.Up, Direction.Left, Direction.Down]);
        return preferred.First(d => !used.Contains(d));
    }

    /// <summary>The simple flow loop through <paramref name="source"/>: from each component the first connected port the fluid leaves by, through inline nodes, until the walk returns; null where it reaches a junction, a boundary or a dead end first.</summary>
    /// <param name="source">Where the walk starts.</param>
    /// <returns>The members in flow order, the source first, or null.</returns>
    private List<Member>? Cycle(int source)
    {
        var ports = _graph.Components[source].Ports;

        for (var p = 0; p < ports.Length; p++)
        {
            if (!Enters(source, p, ports[p].Role) && Cycle(source, p) is { } members)
            {
                return members;
            }
        }

        return null;
    }

    /// <summary>The simple flow loop that leaves <paramref name="source"/> by <paramref name="leaving"/>, if that port is on one: a depth-first walk over the ports the fluid leaves by, through inline elements and junctions alike, back to the source.</summary>
    /// <param name="source">Where the walk starts.</param>
    /// <param name="leaving">The source's port to leave by.</param>
    /// <returns>The members in flow order, the source first, or null.</returns>
    private List<Member>? Cycle(int source, int leaving)
    {
        var path = new List<Member>();
        var visited = new HashSet<int> { source };
        return Extend(source, -1, leaving, source, path, visited) ? path : null;
    }

    private bool Extend(int component, int inPort, int outPort, int source, List<Member> path, HashSet<int> visited)
    {
        var (next, nextPort) = Follow(component, outPort);

        if (next < 0)
        {
            return false;
        }

        path.Add(new Member(component, inPort, outPort));

        if (next == source)
        {
            path[0] = path[0] with { InPort = nextPort };
            return true;
        }

        if (visited.Add(next))
        {
            var ports = _graph.Components[next].Ports;

            for (var p = 0; p < ports.Length; p++)
            {
                if (p != nextPort && !Enters(next, p, ports[p].Role) && _links.Any(l => (l.From == next && l.FromPort == p) || (l.To == next && l.ToPort == p)) && Extend(next, nextPort, p, source, path, visited))
                {
                    return true;
                }
            }

            visited.Remove(next);
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    /// <summary>The component and port a connection reaches from a port, passing through inline nodes.</summary>
    /// <param name="component">The component.</param>
    /// <param name="port">Its port.</param>
    /// <returns>The far end, or (-1, -1) where the port has no connection.</returns>
    private (int Component, int Port) Follow(int component, int port)
    {
        var (n, q) = (component, port);

        for (var guard = 0; guard <= _n; guard++)
        {
            var link = _links.FirstOrDefault(l => (l.From == n && l.FromPort == q) || (l.To == n && l.ToPort == q), new Link(-1, -1, -1, -1, -1));

            if (link.Connection < 0)
            {
                return (-1, -1);
            }

            (n, q) = link.From == n && link.FromPort == q ? (link.To, link.ToPort) : (link.From, link.FromPort);

            if (!Inline(n))
            {
                return (n, q);
            }

            var other = _links.First(l => (l.From == n && l.FromPort != q) || (l.To == n && l.ToPort != q));
            q = other.From == n ? other.FromPort : other.ToPort;
        }

        return (-1, -1);
    }



    /// <summary>A port's outward direction under a transform, or null for a wildcard.</summary>
    private Direction? Outward(int component, int port, Transform t) => AnchorOffset(component, port, t)?.Outward;

    /// <summary>C4: a node with a box on the placed port's axis, one clearance out or as far as H2 needs.</summary>
    /// <param name="anchor">The placed port.</param>
    /// <param name="j">The node.</param>
    /// <param name="q">Its port towards the placed one.</param>
    /// <returns>The pipe from the placed port to the node.</returns>
    private ImmutableArray<Point> PlaceNode(PlacedAnchor anchor, int j, int q)
    {
        var d = anchor.Outward;
        var (w, h) = Transform.Identity.Size(_symbol[j]);
        var half = d.Horizontal ? w / 2 : h / 2;
        var delta = new Point(d.X * half, d.Y * half);
        var at = Clear(j, Transform.Identity, anchor.At, d, delta);
        Place(j, Transform.Identity, at.Offset(delta.X, delta.Y), "C4", $"a node one clearance out on the placed port's axis, {Name(d)}");
        _side[(j, q)] = d.Opposite;
        return [anchor.At, at];
    }

    /// <summary>C5: a component placed off a placed port -- in the first admitted transform of its default arrangement whose port faces the pipe, straight along the axis; else, for a standing kind that cannot face a level pipe, below a single turn at the placed port's outer anchor, entered from above (C3); else in an alternative arrangement that faces the pipe. One clearance out or as far as H2 needs.</summary>
    /// <param name="anchor">The placed port.</param>
    /// <param name="j">The component.</param>
    /// <param name="q">Its port on the connection.</param>
    /// <returns>The pipe from the placed port to the component's port, or null when no admitted transform fits.</returns>
    private ImmutableArray<Point>? PlaceFrom(PlacedAnchor anchor, int j, int q)
    {
        var d = anchor.Outward;
        var admitted = Admitted(j).ToList();
        // A level kind (D-113) faces a vertical pipe only as a last resort: the pipe turns level into it first.
        var level = _symbol[j].TransformClass == "level";
        // H10: among the transforms that face the pipe, the one that sends the member's outlet on to the right comes first, mirrored where that is what it takes (step 11b).
        var facing = admitted.Where(t => t.Arrangement == "default" && AnchorOffset(j, q, t) is { } a && a.Outward == d.Opposite && (!level || t.Rotation is 0 or 180)).OrderBy(t => Onward(j, q, t)).ToList();

        if (facing.Count == 0)
        {
            // C3: the pipe turns into a member that cannot face it -- down into a standing kind from a level pipe, rightwards into a level kind from a vertical one.
        var along = d == Direction.Left || d == Direction.Right ? Direction.Down : Direction.Right;
            var turned = admitted.Where(t => t.Arrangement == "default" && AnchorOffset(j, q, t) is { } a && a.Outward == along.Opposite).ToList();

            if (turned.Count > 0)
            {
                var turn = AnchorOffset(j, q, turned[0])!.Value.Offset;
                var corner = anchor.At.Towards(d, _margin);
                var inner = Clear(j, turned[0], corner, along, new Point(-turn.X, -turn.Y));
                Place(j, turned[0], inner.Offset(-turn.X, -turn.Y), "C3", $"the pipe turns {Name(along)} into a member that cannot face it");
                return [anchor.At, corner, inner];
            }

            facing = admitted.Where(t => AnchorOffset(j, q, t) is { } a && a.Outward == d.Opposite).OrderBy(t => Onward(j, q, t)).ToList();
        }

        if (facing.Count == 0)
        {
            return null;
        }

        var offset = AnchorOffset(j, q, facing[0])!.Value.Offset;
        var end = Clear(j, facing[0], anchor.At, d, new Point(-offset.X, -offset.Y));
        Place(j, facing[0], end.Offset(-offset.X, -offset.Y), "C5", $"facing the pipe arriving {Name(d)}, straight along the axis, the arrangement sending its outlet on to the right first (H10)");
        return [anchor.At, end];
    }

    /// <summary>H10 as a sort key for a member placed from a pipe at port <paramref name="q"/>: 0 when a port the flow leaves by faces right under <paramref name="t"/>, so the flow goes on left to right, else 1; the admitted order decides between equals.</summary>
    private int Onward(int j, int q, Transform t)
    {
        var ports = _graph.Components[j].Ports;

        for (var p = 0; p < ports.Length; p++)
        {
            if (p != q && !Enters(j, p, ports[p].Role) && Outward(j, p, t) == Direction.Right)
            {
                return 0;
            }
        }

        return 1;
    }



    private static ImmutableArray<Point> Reversed(ImmutableArray<Point> points) => [.. points.Reverse()];

    /// <summary>The nearest point at least one clearance from <paramref name="origin"/> along <paramref name="along"/> at which a box for <paramref name="j"/>, its centre <paramref name="delta"/> from that point, keeps the clearance (<c>28</c> H2) against everything placed; stepped by a tenth of a unit, so slack goes into the pipe.</summary>
    /// <param name="j">The component.</param>
    /// <param name="t">Its transform.</param>
    /// <param name="origin">Where the pipe starts from.</param>
    /// <param name="along">The axis it runs along.</param>
    /// <param name="delta">The centre relative to the returned point.</param>
    /// <returns>The point the pipe ends at: the component's inner anchor.</returns>
    private Point Clear(int j, Transform t, Point origin, Direction along, Point delta)
    {
        var gap = _margin;
        var at = origin.Towards(along, gap);

        while (Clashes(j, t, at.Offset(delta.X, delta.Y)))
        {
            gap += 0.1;
            at = origin.Towards(along, gap);
        }

        return at;
    }

    /// <summary>The transforms a kind admits (<c>28</c> A4, D-108): a standing kind (an exchanger) has no quarter turn, an upright one (a tank) only the left-right mirror, the rest turn freely. Identity first, so a tie keeps the drawn default (A9).</summary>
    /// <param name="j">The component.</param>
    /// <returns>The admitted transforms in tie-break order.</returns>
    private IEnumerable<Transform> Admitted(int j)
    {
        var symbol = _symbol[j];

        var admitted = symbol.TransformClass switch
        {
            "standing" => Transform.All(symbol).Where(static t => t.Rotation is 0 or 180),
            "upright" => Transform.All(symbol).Where(static t => t.Rotation == 0),
            _ => Transform.All(symbol),
        };

        // A9 (D-109): the default arrangement first, then the smaller turn, then unmirrored before mirrored -- so a symbol
        // reversing on a line is mirrored rather than half-turned and its top (a valve's stem, a pump's badge) stays up.
        // A `level` kind (a pump, D-113) stands vertical only when nothing level fits: its quarter turns come last.
        var level = symbol.TransformClass == "level";
        return admitted.OrderBy(static t => t.Arrangement != "default").ThenBy(t => level && t.Rotation is 90 or 270).ThenBy(static t => t.Rotation).ThenBy(static t => t.Mirrored);
    }

    /// <summary>Whether a box for <paramref name="j"/> at <paramref name="centre"/> would break the clearance (<c>28</c> H2) against anything placed.</summary>
    /// <param name="j">The component.</param>
    /// <param name="t">Its transform.</param>
    /// <param name="centre">Its centre.</param>
    /// <returns>True when its inner box enters a placed outer box or its outer box is entered by a placed inner box.</returns>
    private bool Clashes(int j, Transform t, Point centre)
    {
        var (w, h) = t.Size(_symbol[j]);
        var inner = Box.Around(centre, w, h);
        var outer = inner.Grow(_margin);

        for (var i = 0; i < _n; i++)
        {
            if (!_placed[i] || i == j)
            {
                continue;
            }

            var other = InnerOf(i);

            if (other.Intersects(outer) || inner.Intersects(other.Grow(_margin)))
            {
                return true;
            }
        }

        return false;
    }
}
