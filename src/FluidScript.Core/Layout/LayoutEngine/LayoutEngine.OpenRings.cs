using FluidScript.Core.Components;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout;

internal sealed partial class LayoutEngine
{
    /// <summary>C19: a supply boundary feeding two paths to one return boundary is the open supply-to-return form. The supply is the left end of the top rail and the return, directly under it, the left end of the bottom rail: the first path with no inner loop hangs straight down between them, and the other path is the ring's right side, fed level from the supply's right and returning along the bottom into the return's right.</summary>
    /// <param name="supply">The fragment's head, which must be a supply boundary.</param>
    /// <param name="fragment">The fragment's members.</param>
    /// <returns>Whether the form was laid out.</returns>
    /// <remarks>Not built: more than two paths, a chain path that turns level, a path to a second return (that one is left to the chain rule off the supply's remaining sides, up then left).</remarks>
    private bool Open(int supply, List<int> fragment)
    {
        var attempt = new Attempt(this);
        if (_graph.Components[supply] is not NodeComponent { Boundary: BoundaryRole.Inlet } || !Wildcard(supply))
        {
            return attempt.Decline("the head is not a supply boundary");
        }

        // D-115: a boundary has one connection, so the rail's left end is the junction after the inlet and the inlet hangs off that junction's left side. An inlet wired to several paths (FS2205) is still drawn, as the junction itself.
        var inlet = -1;
        var inletPort = -1;
        var ports = _graph.Components[supply].Ports;
        var linked = Enumerable.Range(0, ports.Length).Where(p => _links.Any(l => (l.From == supply && l.FromPort == p) || (l.To == supply && l.ToPort == p))).ToList();

        if (linked.Count == 1)
        {
            var (junction, back) = Follow(supply, linked[0]);

            if (junction < 0 || !Wildcard(junction) || _inline[junction] || _graph.Components[junction] is not NodeComponent { Boundary: BoundaryRole.Interior })
            {
                return attempt.Decline("the supply does not feed an interior junction node");
            }

            inlet = supply;
            inletPort = back;
            supply = junction;
            ports = _graph.Components[supply].Ports;
            linked = Enumerable.Range(0, ports.Length).Where(p => p != back && _links.Any(l => (l.From == supply && l.FromPort == p) || (l.To == supply && l.ToPort == p))).ToList();
        }

        List<List<Member>>? paths = null;
        var ret = -1;
        var outletPort = -1;

        foreach (var candidate in Ordered(fragment).Where(i => i != supply && i != inlet && _graph.Components[i] is NodeComponent { Boundary: BoundaryRole.Outlet }))
        {
            var found = new List<List<Member>>();

            foreach (var p in linked)
            {
                var path = new List<Member>();
                HashSet<int> visited = inlet < 0 ? [supply] : [supply, inlet];

                if (Extend(supply, -1, p, candidate, path, visited))
                {
                    found.Add(path);
                }
            }

            if (found.Count is not (1 or 2))
            {
                continue;
            }

            paths = found;
            ret = candidate;

            // Both paths ending on one junction: it is the bottom rail's left end, and the outlet hangs off its left side.
            if (found.Count == 2 && found[0].Count > 1 && found[1].Count > 1 && found[0][^1].Component == found[1][^1].Component && Wildcard(found[0][^1].Component) && !_inline[found[0][^1].Component])
            {
                ret = found[0][^1].Component;
                outletPort = found[0][^1].OutPort;

                foreach (var path in found)
                {
                    path[0] = path[0] with { InPort = path[^1].InPort };
                    path.RemoveAt(path.Count - 1);
                }
            }

            break;
        }

        if (paths is null)
        {
            return attempt.Decline("the junction's paths do not both reach one return boundary");
        }

        HashSet<int> avoid = [supply];
        List<Member> CycleOf(List<Member> path) => [new Member(supply, -1, path[0].OutPort), .. path.Skip(1)];
        var chainAt = paths.FindIndex(path => Ranges(CycleOf(path), 1, avoid).Count == 0);
        var ringAt = paths.FindIndex(path => Ranges(CycleOf(path), 1, avoid).Count > 0);

        // One path with no inner loop is the chain alone: the outlet stands at its foot and there are no rails (C19, step 11b).
        var chainOnly = ringAt < 0 && paths.Count == 1;

        if (ringAt < 0 && !chainOnly)
        {
            ringAt = chainAt == 0 ? 1 : 0;
        }

        var chain = chainAt >= 0 && chainAt != ringAt ? paths[chainAt] : null;
        var cycle = chainOnly ? [new Member(supply, -1, chain![0].OutPort)] : CycleOf(paths[ringAt]);
        var ring = cycle.Select(static m => m.Component).ToHashSet();
        var mark = _groups.Count;

        // The ring path's unit and top-rail items, as a ring's (C11).
        var (unit, _, unitEnd, items, unitRange, unitGroups) = RailItems(cycle, avoid, mark);

        if (unit is null && !chainOnly)
        {
            return attempt.Decline("no unit was found on the ring path");
        }

        Unplace(items, unit);

        _loopCentre = new Point(0, 0);
        Place(supply, Transform.Identity, new Point(0, 0), "C19", "the supply boundary at the left end of the top rail");
        _side[(supply, cycle[0].OutPort)] = Direction.Right;
        var half = Transform.Identity.Size(_symbol[ret]).Height / 2;
        var natural = InnerOf(supply).Y - (2 * _margin) - half;
        var runs = new List<(Member From, List<Point> Points)>();
        Member chainEnd = default;
        PlacedAnchor chainCursor = default;

        if (chain is not null)
        {
            // The chain hangs straight down under the supply, each member placed from the one before.
            _side[(supply, chain[0].OutPort)] = Direction.Down;
            var cursor = AnchorOf(supply, chain[0].OutPort);
            var previous = new Member(supply, -1, chain[0].OutPort);
            var pending = new List<Point> { cursor.At };

            foreach (var m in chain.Skip(1))
            {
                if (OnRail(cursor, m, m.InPort, m.OutPort) is not { } points)
                {
                    return attempt.Decline("a chain member could not be laid on the rail");
                }

                pending.AddRange(points.Skip(1));
                runs.Add((previous, pending));
                cursor = AnchorOf(m.Component, m.OutPort);
                previous = m;
                pending = [cursor.At];
            }

            if (cursor.Outward != Direction.Down || Math.Abs(cursor.At.X) > Eps)
            {
                return attempt.Decline("the chain does not end pointing down on the supply's axis");
            }

            chainEnd = previous;
            chainCursor = cursor;
            natural = Math.Min(natural, cursor.At.Y - _margin - half);
        }

        if (chainOnly)
        {
            // No ring path: the outlet stands at the chain's foot, directly under the junction.
            Place(ret, Transform.Identity, new Point(0, natural), "C19", "no ring path: the return at the chain's foot, directly under the junction");
            _side[(ret, chain![0].InPort)] = Direction.Up;
            runs.Add((chainEnd, [chainCursor.At, AnchorOf(ret, chain[0].InPort).At]));
            Finish();
            return true;
        }

        if (unit is null)
        {
            return attempt.Decline("no unit was found on the ring path");
        }

        // C12 for the open form: a block standing on both rails whose outlet sits above the chain's level would put a step in the return, so the block is rebuilt deeper and its outlet meets the rail.
        if (unitRange is { } range && unit.In.Outward == Direction.Left && unit.Out.Outward == Direction.Left)
        {
            var outletAt = AnchorOf(supply, cycle[0].OutPort).At.Y + unit.Out.At.Y - unit.In.At.Y;

            if (outletAt > natural + Eps)
            {
                _groups.RemoveRange(mark, unitGroups);
                var at = _groups.Count;
                unit = Block(range.Inner, cycle[range.Start], cycle[range.End], avoid, Direction.Left, outletAt - natural);

                if (unit is null)
                {
                    return attempt.Decline("the ring's block could not be laid as a unit");
                }

                var created = _groups.GetRange(at, _groups.Count - at);
                _groups.RemoveRange(at, created.Count);
                _groups.InsertRange(mark, created);

                foreach (var i in unit.Members)
                {
                    _placed[i] = false;
                }
            }
        }

        var sOut = AnchorOf(supply, cycle[0].OutPort);
        var bottomMembers = cycle.GetRange(unitEnd + 1, cycle.Count - unitEnd - 1);
        var hangers = new List<Hanger>();

        if (Top(sOut, [sOut.At], cycle[0], items, ring, bottomMembers, avoid, runs, hangers) is not { } top)
        {
            return attempt.Decline("the top rail could not be laid");
        }

        var (_, rightJunction) = Corners(bottomMembers, unit, leftFirst: false);
        var (drop, yBottom) = Bottom(unit, top.End.At.Y, natural, rightJunction?.Component ?? -1);
        yBottom = Math.Min(yBottom, Under(hangers));

        // The return stands directly under the supply at the bottom rail's level, fed by the chain from above and by the rail from the right.
        Place(ret, Transform.Identity, new Point(0, yBottom), "C19", "the return directly under the supply at the bottom rail's level");
        _side[(ret, paths[ringAt][0].InPort)] = Direction.Right;

        if (chain is not null)
        {
            _side[(ret, chain[0].InPort)] = Direction.Up;
            runs.Add((chainEnd, [chainCursor.At, AnchorOf(ret, chain[0].InPort).At]));
        }

        var rIn = AnchorOf(ret, paths[ringAt][0].InPort);

        if (!Close(top, unit, drop, yBottom, bottomMembers, [rIn.At, rIn.Along(_margin)], null, rightJunction, hangers, runs))
        {
            return attempt.Decline("the ring could not be closed along the bottom rail");
        }

        foreach (var m in cycle)
        {
            _loop[m.Component] = true;
        }

        Finish();
        return true;

        void Finish()
        {
            // The inlet and the outlet hang off their junctions' left sides (D-115); the supply's other connections leave by the sides the form leaves free, up first.
            // Up then left is deliberately narrower than `FreeSide`'s right, up, left, down: the supply stands at the top rail's left end, so right and down are the rails' own.
            if (inlet >= 0)
            {
                _side[(supply, inletPort)] = Direction.Left;
            }

            if (outletPort >= 0)
            {
                _side[(ret, outletPort)] = Direction.Left;
            }

            var free = new Queue<Direction>([Direction.Up, Direction.Left]);

            foreach (var p in linked.Where(p => !_side.ContainsKey((supply, p))))
            {
                while (free.Count > 0)
                {
                    var side = free.Dequeue();

                    if (!_side.Any(kv => kv.Key.Component == supply && kv.Value == side))
                    {
                        _side[(supply, p)] = side;
                        break;
                    }
                }
            }

            AssignRuns(runs);
        }
    }

    /// <summary>Whether the walk leaving a member passes a node that is a point on the line -- an inline node the cycle search steps over (C18).</summary>
    private bool PassesNode(Member from)
    {
        foreach (var (k, _) in Walk(from.Component, from.OutPort).Links)
        {
            var link = _links[k];

            if ((Wildcard(link.From) && _inline[link.From]) || (Wildcard(link.To) && _inline[link.To]))
            {
                return true;
            }
        }

        return false;
    }
}
