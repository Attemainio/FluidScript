using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Binding;
using FluidScript.Core.Components;
using FluidScript.Core.Language;
using FluidScript.Core.Model;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Layout;

internal sealed partial class LayoutEngine
{
    // ---- loops ----------------------------------------------------------------------------------------------------

    /// <summary>One member of a flow loop: the component, the port the loop enters it by, and the port it leaves by.</summary>
    private readonly record struct Member(int Component, int InPort, int OutPort);

    /// <summary>
    /// The consumer side of a ring, laid out at a provisional place: one member, or a whole inner loop as a
    /// block (A8, C11). The ring slides it into place and shifts its runs with it.
    /// </summary>
    /// <param name="Members">The boxed components the unit moves as one.</param>
    /// <param name="In">Where the ring's top rail enters, facing left (a corner) or up (from the rail above).</param>
    /// <param name="Out">Where the ring's right side leaves, facing down or right.</param>
    /// <param name="OutFrom">The member and port the leaving run walks from.</param>
    /// <param name="Bottom">The lowest inner-box edge, provisional units.</param>
    /// <param name="Runs">The unit's own runs, provisional units, assigned by the ring after the shift.</param>
    private sealed record Unit(List<int> Members, PlacedAnchor In, PlacedAnchor Out, Member OutFrom, double Bottom, List<(Member From, List<Point> Points)> Runs);

    /// <summary>One thing on a rail: a member placed by <see cref="OnRail"/>, or a block laid out as a unit (C11).</summary>
    private readonly record struct Item(Member? Member, Unit? Unit);

    /// <summary>A branch block hanging between the rails (C14): placed under its top junction, its return still to be laid to its bottom junction, which stands under the top one.</summary>
    private sealed record Hanger(PlacedAnchor Out, Member OutFrom, int Top, int Bottom, int BottomPort);

    private bool Loop(int source)
    {
        if (!IsSource(source))
        {
            return Decline("the head is not a source (a positive duty)");
        }

        if (Cycle(source) is not { } cycle)
        {
            return Decline("no cycle of boxed members returns to the source");
        }

        var s = cycle[0];
        var ts = Admitted(s.Component).Where(t => t.Arrangement == "default" && Outward(s.Component, s.OutPort, t) == Direction.Up && Outward(s.Component, s.InPort, t) == Direction.Down).ToList();

        if (ts.Count == 0)
        {
            return Decline("the source has no default arrangement with its outlet up and its inlet down");
        }

        _loopCentre = new Point(0, 0);

        foreach (var member in cycle)
        {
            _loop[member.Component] = true;
        }

        var ring = cycle.Select(static m => m.Component).ToHashSet();
        HashSet<int> avoid = [source];
        var mark = _groups.Count;

        // C11: every inner loop along the ring is a block. The last in flow order is the ring's right side with its outlet facing back; the others stand on the top rail with their outlets facing on, so a chain steps from block to block. Without any, the consumer stands on the right alone.
        var ranges = Ranges(cycle, 1, avoid);

        Unit? unit;
        int unitStart;
        int unitEnd;
        var items = new List<Item>();

        if (ranges.Count > 0)
        {
            var last = ranges[^1];
            unitStart = last.Start;
            unitEnd = last.End;
            unit = Block(last.Inner, cycle[unitStart], cycle[unitEnd], avoid, Direction.Left);
            var next = 1;

            foreach (var (start, end, inner) in ranges.SkipLast(1))
            {
                items.AddRange(cycle.GetRange(next, start - next).Select(static m => new Item(m, null)));
                var block = Block(inner, cycle[start], cycle[end], avoid, Direction.Right);

                if (block is null)
                {
                    unit = null;
                    break;
                }

                items.Add(new Item(null, block));
                next = end + 1;
            }

            if (unit is not null)
            {
                items.AddRange(cycle.GetRange(next, unitStart - next).Select(static m => new Item(m, null)));
            }
        }
        else
        {
            unitStart = unitEnd = ConsumerOf(cycle, 1);
            unit = unitStart < 0 ? null : Single(cycle[unitStart]);

            if (unit is not null)
            {
                items.AddRange(cycle.GetRange(1, unitStart - 1).Select(static m => new Item(m, null)));
            }
        }

        if (unit is null)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            return Decline("no consumer unit was found on the cycle");
        }

        // Every unit stands at a provisional place until it is slid in; none is an obstacle before that.
        foreach (var u in items.Where(static i => i.Unit is not null).Select(static i => i.Unit!).Append(unit))
        {
            foreach (var i in u.Members)
            {
                _placed[i] = false;
            }
        }

        Place(s.Component, ts[0], new Point(0, 0), "C2", "the loop's source at the origin, outlet up and inlet down");
        var sOut = AnchorOf(s.Component, s.OutPort);
        var sIn = AnchorOf(s.Component, s.InPort);
        var yTop = sOut.Along(_margin).Y;
        var bottomMembers = cycle.GetRange(unitEnd + 1, cycle.Count - unitEnd - 1);
        var runs = new List<(Member From, List<Point> Points)>();
        var hangers = new List<Hanger>();
        List<Point> topStart = [sOut.At, new Point(sOut.At.X, yTop)];

        if (Top(Anchor(topStart[^1], Direction.Right, Direction.Right), topStart, s, items, ring, bottomMembers, avoid, runs, hangers) is not { } top)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            return Decline("the top rail could not be laid");
        }

        var (_, rightJunction) = Corners(bottomMembers, unit, leftFirst: false);
        var (drop, yBottom) = Bottom(unit, top.End.At.Y, sIn.Along(_margin).Y, rightJunction?.Component ?? -1);
        yBottom = Math.Min(yBottom, Under(hangers));

        // C12: a member on a side with slack sits at the side's middle. The source moves down by half the excess of the rails' span over its own.
        var slack = sIn.Along(_margin).Y - yBottom;
        Place(s.Component, ts[0], new Point(0, -slack / 2), "C12", $"the source at the middle of its side, half the rails' slack ({slack:0.##}) down");
        sOut = AnchorOf(s.Component, s.OutPort);
        sIn = AnchorOf(s.Component, s.InPort);
        topStart[0] = sOut.At;

        if (!Close(top, unit, drop, yBottom, bottomMembers, [sIn.At, sIn.Along(_margin), new Point(sIn.At.X, yBottom)], null, rightJunction, hangers, runs))
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            return Decline("the ring could not be closed along the bottom rail");
        }

        // The ring is a group (A8), listed before the blocks it holds.
        _groups.Insert(mark, ("loop", cycle.Select(static m => m.Component).ToList(), true));

        foreach (var (from, points) in runs)
        {
            Assign(Walk(from.Component, from.OutPort), Normalise(points));
        }

        return true;
    }

    /// <summary>The member that takes a ring's right side: the standing consumer of the largest duty from <paramref name="from"/> on, else the first member the flow leaves the ring by.</summary>
    private int ConsumerOf(List<Member> cycle, int from)
    {
        var consumerAt = -1;

        for (var k = from; k < cycle.Count; k++)
        {
            if (Duty(cycle[k].Component) is { } duty && (consumerAt < 0 || duty < Duty(cycle[consumerAt].Component)))
            {
                consumerAt = k;
            }
        }

        for (var k = from; k < cycle.Count && consumerAt < 0; k++)
        {
            // No standing consumer: the member the loop's flow leaves by -- a valve or a junction with an off-loop outlet -- takes the right side.
            if (LeavesLoop(cycle[k]))
            {
                consumerAt = k;
            }
        }

        return consumerAt;
    }

    /// <summary>A consumer's duty, negative (W): its stated power, or a nominal amount for an exchanger whose power is sized but which is written as a load or states an inlet above its outlet; <see langword="null"/> for anything else.</summary>
    private double? Duty(int component) => _graph.Components[component] switch
    {
        HeatExchanger { Power: < 0 } h => h.Power,
        HeatExchanger { Power: 0 } when Written(component) is "load" or "radiator" or "cooler" or "chiller" => -double.Epsilon,
        HeatExchanger { Power: 0 } h when h.StatedParameters.TryGetValue("in", out var inlet) && h.StatedParameters.TryGetValue("out", out var outlet) && inlet.SiValue > outlet.SiValue => -double.Epsilon,
        _ => null,
    };

    /// <summary>Whether an exchanger is a heat source: its stated power positive, or its power sized but written as a heater or boiler, or stating an outlet above its inlet.</summary>
    private bool IsSource(int component) => _graph.Components[component] switch
    {
        HeatExchanger { Power: > 0 } => true,
        HeatExchanger { Power: 0 } when Written(component) is "heater" or "boiler" => true,
        HeatExchanger { Power: 0 } h when h.StatedParameters.TryGetValue("in", out var inlet) && h.StatedParameters.TryGetValue("out", out var outlet) && outlet.SiValue > inlet.SiValue => true,
        _ => false,
    };

    /// <summary>The kind the script wrote for a component (<c>load</c>, <c>heater</c>, …), lower-cased; <see langword="null"/> for one the language inferred.</summary>
    private string? Written(int component)
    {
        var name = _graph.Components[component].Name;

        foreach (var c in _model.Components)
        {
            if (string.Equals(c.Name, name, StringComparison.Ordinal))
            {
                return c.WrittenKind.ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// C18: a closed loop with no heat source is still a ring. Its consumer of the largest duty takes the right
    /// side; the top-left corner is the first boxed node after it in flow order -- in from below, out to the
    /// right -- or, where the loop's nodes are points on the line, a bare bend before the first member the flow
    /// reaches past such a point; the members between consumer and corner lie on the bottom rail, the rest on
    /// the top, and the left side is the bare vertical up into the corner. A loop none of whose members has a
    /// known duty -- an exchanger neither stated nor solved, which a script under editing is most of the time --
    /// is still a ring (<c>C-100</c>): its first exchanger takes the consumer's seat, so the loop reads as a
    /// loop before it solves.
    /// </summary>
    private bool Closed(List<int> fragment, int head)
    {
        var consumer = fragment.Where(i => Duty(i) is not null).OrderBy(i => Duty(i)).ThenBy(static i => i).FirstOrDefault(-1);

        if (consumer < 0)
        {
            consumer = fragment.Where(i => _graph.Components[i] is HeatExchanger).Order().FirstOrDefault(-1);
        }

        // A ring with no duty and no exchanger -- two pumps, a pump and a valve -- is still a ring (C-102):
        // any member but the head can take the consumer's seat, and the first that is not a pump does, so
        // the pump stays on the top rail; a ring of pumps seats the one that is not the head.
        if (consumer < 0)
        {
            var seats = fragment.Where(i => i != head && !Inline(i) && !Wildcard(i)).Order().ToList();
            consumer = seats.FirstOrDefault(i => _graph.Components[i] is not Pump, seats.FirstOrDefault(-1));
        }

        if (consumer < 0)
        {
            return Decline("no member besides the head to take the consumer's seat");
        }

        if (Cycle(consumer) is not { } cycle)
        {
            return Decline("no cycle of boxed members returns to the consumer");
        }

        var cornerAt = -1;
        var split = -1;

        for (var k = 1; k < cycle.Count && cornerAt < 0; k++)
        {
            if (Wildcard(cycle[k].Component))
            {
                cornerAt = k;
            }
            else if (split < 0 && PassesNode(cycle[k - 1]))
            {
                split = k;
            }
        }

        if (cornerAt < 0 && split < 0)
        {
            split = 1;
        }

        var corner = cornerAt >= 0 ? cycle[cornerAt] : (Member?)null;
        var wasInline = corner is { } c0 && _inline[c0.Component];
        var mark = _groups.Count;
        var ring = cycle.Select(static m => m.Component).ToHashSet();
        // The unit search must not find the ring itself: it avoids the corner, or with a bare bend the first member on the top rail.
        HashSet<int> avoid = corner is { } c1 ? [c1.Component] : [cycle[split].Component];

        bool Fail(string reason)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);

            if (corner is { } c2)
            {
                _inline[c2.Component] = wasInline;
            }

            return Decline(reason);
        }

        if (corner is { } c3)
        {
            _inline[c3.Component] = false;
        }

        var (_, innerEnd, unit) = UnitOf(cycle, 0, avoid, Direction.Left);
        var end = cornerAt >= 0 ? cornerAt : split;

        if (unit is null || innerEnd >= end)
        {
            return Fail(unit is null ? "no unit was found past the consumer" : "the unit reaches the corner and leaves nothing for the rails");
        }

        foreach (var i in unit.Members)
        {
            _placed[i] = false;
        }

        foreach (var member in cycle)
        {
            _loop[member.Component] = true;
        }

        var bottomMembers = cycle.GetRange(innerEnd + 1, end - innerEnd - 1);

        // C9, mirrored to the ring's bottom-left corner (C-105): the last bottom member that can turn the flow
        // from leftward to upward takes the corner, and the left side rises out of it instead of a bare bend
        // beside it -- the case C18 had recorded as not built. A junction there is C10's (Corners), not this.
        (Member Member, Transform Transform)? leftTurner = null;

        if (corner is not null && bottomMembers.Count > 0 && !Wildcard(bottomMembers[^1].Component) && !_inline[bottomMembers[^1].Component])
        {
            var last = bottomMembers[^1];
            var turning = Admitted(last.Component)
                .Where(t => Outward(last.Component, last.InPort, t) == Direction.Right && Outward(last.Component, last.OutPort, t) == Direction.Up)
                .ToList();

            if (turning.Count > 0)
            {
                leftTurner = (last, turning[0]);
                bottomMembers = bottomMembers.GetRange(0, bottomMembers.Count - 1);
            }
        }

        var items = cycle.GetRange(end + (cornerAt >= 0 ? 1 : 0), cycle.Count - end - (cornerAt >= 0 ? 1 : 0)).Select(static m => new Item(m, null)).ToList();
        PlacedAnchor cOut;
        PlacedAnchor cIn;
        Member previous;

        if (corner is { } c4)
        {
            Place(c4.Component, Transform.Identity, new Point(0, 0), "C18", "the unsourced ring's corner node at the origin");
            _side[(c4.Component, c4.InPort)] = Direction.Down;
            _side[(c4.Component, c4.OutPort)] = Direction.Right;
            cOut = AnchorOf(c4.Component, c4.OutPort);
            cIn = AnchorOf(c4.Component, c4.InPort);
            previous = c4;
        }
        else
        {
            // A bare bend at the origin: the run that turns it belongs to the last bottom member (or the consumer), and its two halves are joined below.
            cOut = Anchor(new Point(0, 0), Direction.Right, Direction.Right);
            cIn = Anchor(new Point(0, 0), Direction.Down, Direction.Up);
            previous = cycle[end - 1];
        }

        var (_, right) = Corners(bottomMembers, unit, leftFirst: false);
        var runs = new List<(Member From, List<Point> Points)>();
        var hangers = new List<Hanger>();

        if (Top(cOut, [cOut.At], previous, items, ring, bottomMembers, avoid, runs, hangers) is not { } top)
        {
            return Fail("the top rail could not be laid");
        }

        var (drop, yBottom) = Bottom(unit, top.End.At.Y, cIn.Along(_margin).Y, right?.Component ?? -1);
        yBottom = Math.Min(yBottom, Under(hangers));

        if (leftTurner is { } lt)
        {
            // The rail is where the turner's inlet lies; its outlet's tip must sit a margin under the corner's inlet.
            var dIn = AnchorOffset(lt.Member.Component, lt.Member.InPort, lt.Transform)!.Value.Offset;
            var dOut = AnchorOffset(lt.Member.Component, lt.Member.OutPort, lt.Transform)!.Value.Offset;
            yBottom = Math.Min(yBottom, cIn.Along(_margin).Y - (dOut.Y - dIn.Y));
        }

        if (!Close(top, unit, drop, yBottom, bottomMembers, [cIn.At, cIn.Along(_margin), new Point(cIn.At.X, yBottom)], null, right, hangers, runs, leftTurner))
        {
            return Fail("the ring could not be closed along the bottom rail");
        }

        // C8 reads a junction's free side against the loop's centre. This ring is built from its corner at the
        // origin, so the centre had stayed there and the corner's own free port -- at zero distance from it --
        // fell to the default order and went up; an open end there stands off the ring's side, level (C-105).
        _loopCentre = new Point(cycle.Average(m => _centre[m.Component].X), cycle.Average(m => _centre[m.Component].Y));

        if (corner is null)
        {
            // The bare corner's two halves are one walk: the bottom rail's last run ends at the origin, the top rail's first starts there.
            var halves = runs.Where(r => r.From.Equals(previous)).ToList();
            var into = halves.FirstOrDefault(r => r.Points[^1].ManhattanTo(cOut.At) < Eps);
            var outOf = halves.FirstOrDefault(r => r.Points[0].ManhattanTo(cOut.At) < Eps);

            if (into.Points is not null && outOf.Points is not null && !ReferenceEquals(into.Points, outOf.Points))
            {
                runs.Remove(into);
                runs.Remove(outOf);
                runs.Add((previous, [.. into.Points, .. outOf.Points.Skip(1)]));
            }
        }

        _groups.Insert(mark, ("loop", cycle.Select(static m => m.Component).ToList(), true));

        foreach (var (from, points) in runs)
        {
            Assign(Walk(from.Component, from.OutPort), Normalise(points));
        }

        return true;
    }

    /// <summary>
    /// C20 (<c>C-101</c>): a component connected to itself is a ring of one. Neither C2 nor C18 can seat it -- a pump is
    /// no source and no consumer -- and the chain rule would put its inferred node one row under it and route the
    /// outlet back through the member's own box. The member stands at the origin in its drawn default; the pipe
    /// leaves the outlet by its outward margin, which puts it on the outer box's edge, walks that edge clockwise (H9)
    /// round to the inlet's stub and enters by its margin, so a pump's return runs under it and an exchanger's, out at
    /// the bottom and in at the top, runs up its left side; the inline elements between -- the inferred node the
    /// syntax gives <c>PU1 - PU1</c> -- are cut into the return (A5). Only a fragment whose one boxed member is the
    /// head is a ring of one; anything larger is another rule's, and a member whose stubs do not end on its outer
    /// box is left to them too.
    /// </summary>
    private bool Ring(int head, List<int> fragment)
    {
        if (fragment.Any(i => i != head && !Inline(i)))
        {
            return Decline("the fragment has a boxed member besides the head");
        }

        var ports = _graph.Components[head].Ports;

        for (var p = 0; p < ports.Length; p++)
        {
            if (Enters(head, p, ports[p].Role))
            {
                continue;
            }

            var walk = Walk(head, p);

            if (walk.Far != head || walk.FarPort == p)
            {
                continue;
            }

            Place(head, Transform.Identity, new Point(0, 0), "C20", "a ring of one: the head at the origin, the walk returning to it");
            var outlet = AnchorOf(head, p);
            var inlet = AnchorOf(head, walk.FarPort);
            var (width, height) = SizeOf(head);

            if (Clockwise(outlet.Along(_margin), inlet.Along(_margin), (width / 2) + _margin, (height / 2) + _margin) is not { } around)
            {
                _placed[head] = false;
                continue;
            }

            List<Point> points = [outlet.At, .. around, inlet.At];
            var members = new List<int> { head };
            members.AddRange(walk.Inline.Select(static e => e.Element));

            foreach (var i in members)
            {
                _loop[i] = true;
            }

            _groups.Add(("loop", members, true));
            Assign(walk, Normalise(points));
            return true;
        }

        return Decline("no walk from the head's outlet returns to another of its ports");
    }

    /// <summary>The clockwise walk along the edge of the box <c>[-x, x] × [-y, y]</c> about the origin from <paramref name="from"/> to <paramref name="to"/>, both on that edge, through the corners between.</summary>
    /// <param name="from">Where the walk starts, on the edge.</param>
    /// <param name="to">Where it ends, on the edge.</param>
    /// <param name="x">The box's half width.</param>
    /// <param name="y">The box's half height.</param>
    /// <returns>The points from <paramref name="from"/> to <paramref name="to"/> inclusive, or null where either is off the edge or they coincide.</returns>
    private static List<Point>? Clockwise(Point from, Point to, double x, double y)
    {
        // Edges clockwise with y up: top (rightwards), right (downwards), bottom (leftwards), left (upwards); the corner each edge ends at.
        Point[] corners = [new(x, y), new(x, -y), new(-x, -y), new(-x, y)];

        static int EdgeOf(Point p, double x, double y) =>
            Math.Abs(p.Y - y) < Eps ? 0 : Math.Abs(p.X - x) < Eps ? 1 : Math.Abs(p.Y + y) < Eps ? 2 : Math.Abs(p.X + x) < Eps ? 3 : -1;

        static bool Ahead(int edge, Point at, Point p) => edge switch
        {
            0 => p.X >= at.X - Eps,
            1 => p.Y <= at.Y + Eps,
            2 => p.X <= at.X + Eps,
            _ => p.Y >= at.Y - Eps,
        };

        var edge = EdgeOf(from, x, y);

        if (edge < 0 || EdgeOf(to, x, y) < 0 || from.ManhattanTo(to) < Eps)
        {
            return null;
        }

        List<Point> points = [from];
        var at = from;

        for (var step = 0; step <= 4; step++)
        {
            if (EdgeOf(to, x, y) == edge && Ahead(edge, at, to))
            {
                points.Add(to);
                return points;
            }

            at = corners[edge];
            points.Add(at);
            edge = (edge + 1) % 4;
        }

        return null;
    }

    /// <summary>C19: a supply boundary feeding two paths to one return boundary is the open supply-to-return form. The supply is the left end of the top rail and the return, directly under it, the left end of the bottom rail: the first path with no inner loop hangs straight down between them, and the other path is the ring's right side, fed level from the supply's right and returning along the bottom into the return's right.</summary>
    /// <param name="supply">The fragment's head, which must be a supply boundary.</param>
    /// <param name="fragment">The fragment's members.</param>
    /// <returns>Whether the form was laid out.</returns>
    /// <remarks>Not built: more than two paths, a chain path that turns level, a path to a second return (that one is left to the chain rule off the supply's remaining sides, up then left).</remarks>
    private bool Open(int supply, List<int> fragment)
    {
        if (_graph.Components[supply] is not CircuitNode { Boundary: BoundaryRole.Inlet } || !Wildcard(supply))
        {
            return Decline("the head is not a supply boundary");
        }

        // D-115: a boundary has one connection, so the rail's left end is the junction after the inlet and the inlet hangs off that junction's left side. An inlet wired to several paths (FS2205) is still drawn, as the junction itself.
        var inlet = -1;
        var inletPort = -1;
        var ports = _graph.Components[supply].Ports;
        var linked = Enumerable.Range(0, ports.Length).Where(p => _links.Any(l => (l.From == supply && l.FromPort == p) || (l.To == supply && l.ToPort == p))).ToList();

        if (linked.Count == 1)
        {
            var (junction, back) = Follow(supply, linked[0]);

            if (junction < 0 || !Wildcard(junction) || _inline[junction] || _graph.Components[junction] is not CircuitNode { Boundary: BoundaryRole.Interior })
            {
                return Decline("the supply does not feed an interior junction node");
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

        foreach (var candidate in Ordered(fragment).Where(i => i != supply && i != inlet && _graph.Components[i] is CircuitNode { Boundary: BoundaryRole.Outlet }))
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
            return Decline("the junction's paths do not both reach one return boundary");
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
        var placed = (bool[])_placed.Clone();
        var sides = new Dictionary<(int Component, int Port), Direction>(_side);
        var mark = _groups.Count;

        bool Fail(string reason)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            placed.CopyTo(_placed, 0);
            _side.Clear();

            foreach (var (key, value) in sides)
            {
                _side[key] = value;
            }

            return Decline(reason);
        }

        // The ring path's unit and top-rail items, as a ring's (C11).
        var ranges = Ranges(cycle, 1, avoid);
        Unit? unit;
        int unitEnd;
        var items = new List<Item>();
        (int Start, int End, List<Member> Inner)? unitRange = null;
        var unitGroups = 0;

        if (ranges.Count > 0)
        {
            var last = ranges[^1];
            unitEnd = last.End;
            unit = Block(last.Inner, cycle[last.Start], cycle[last.End], avoid, Direction.Left);
            unitRange = last;
            unitGroups = _groups.Count - mark;
            var next = 1;

            foreach (var (start, end, inner) in ranges.SkipLast(1))
            {
                items.AddRange(cycle.GetRange(next, start - next).Select(static m => new Item(m, null)));
                var block = Block(inner, cycle[start], cycle[end], avoid, Direction.Right);

                if (block is null)
                {
                    unit = null;
                    break;
                }

                items.Add(new Item(null, block));
                next = end + 1;
            }

            if (unit is not null)
            {
                items.AddRange(cycle.GetRange(next, last.Start - next).Select(static m => new Item(m, null)));
            }
        }
        else
        {
            var consumerAt = ConsumerOf(cycle, 1);
            unitEnd = consumerAt;
            unit = consumerAt < 0 ? null : Single(cycle[consumerAt]);

            if (unit is not null)
            {
                items.AddRange(cycle.GetRange(1, consumerAt - 1).Select(static m => new Item(m, null)));
            }
        }

        if (unit is null && !chainOnly)
        {
            return Fail("no unit was found on the ring path");
        }

        foreach (var u in items.Where(static i => i.Unit is not null).Select(static i => i.Unit!).Concat(unit is null ? [] : [unit]))
        {
            foreach (var i in u.Members)
            {
                _placed[i] = false;
            }
        }

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
                    return Fail("a chain member could not be laid on the rail");
                }

                pending.AddRange(points.Skip(1));
                runs.Add((previous, pending));
                cursor = AnchorOf(m.Component, m.OutPort);
                previous = m;
                pending = [cursor.At];
            }

            if (cursor.Outward != Direction.Down || Math.Abs(cursor.At.X) > Eps)
            {
                return Fail("the chain does not end pointing down on the supply's axis");
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
            return Fail("no unit was found on the ring path");
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
                    return Fail("the ring's block could not be laid as a unit");
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
            return Fail("the top rail could not be laid");
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
            return Fail("the ring could not be closed along the bottom rail");
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

            foreach (var (from, points) in runs)
            {
                Assign(Walk(from.Component, from.OutPort), Normalise(points));
            }
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
