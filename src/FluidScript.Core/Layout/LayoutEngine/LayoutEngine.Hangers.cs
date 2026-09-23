using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout;

internal sealed partial class LayoutEngine
{
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
}
