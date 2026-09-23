using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout;

internal sealed partial class LayoutEngine
{
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
        var attempt = new Attempt(this);
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
            return attempt.Decline("no member besides the head to take the consumer's seat");
        }

        if (Cycle(consumer) is not { } cycle)
        {
            return attempt.Decline("no cycle of boxed members returns to the consumer");
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
        var mark = _groups.Count;
        var ring = cycle.Select(static m => m.Component).ToHashSet();
        // The unit search must not find the ring itself: it avoids the corner, or with a bare bend the first member on the top rail.
        HashSet<int> avoid = corner is { } c1 ? [c1.Component] : [cycle[split].Component];

        if (corner is { } c3)
        {
            _inline[c3.Component] = false;
        }

        var (_, innerEnd, unit) = UnitOf(cycle, 0, avoid, Direction.Left);
        var end = cornerAt >= 0 ? cornerAt : split;

        if (unit is null || innerEnd >= end)
        {
            return attempt.Decline(unit is null ? "no unit was found past the consumer" : "the unit reaches the corner and leaves nothing for the rails");
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
            return attempt.Decline("the top rail could not be laid");
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
            return attempt.Decline("the ring could not be closed along the bottom rail");
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

        _groups.Insert(mark, LoopGroup(cycle.Select(static m => m.Component).ToList(), true));

        AssignRuns(runs);

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
        var attempt = new Attempt(this);
        if (fragment.Any(i => i != head && !Inline(i)))
        {
            return attempt.Decline("the fragment has a boxed member besides the head");
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

            _groups.Add(LoopGroup(members, true));
            Assign(walk, Normalise(points));
            return true;
        }

        return attempt.Decline("no walk from the head's outlet returns to another of its ports");
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
}
