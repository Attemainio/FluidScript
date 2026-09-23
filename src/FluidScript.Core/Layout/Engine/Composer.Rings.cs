using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Engine.Structures;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout.Engine;

internal sealed partial class Composer
{
    private readonly Dictionary<int, double> _columnFloor = [];
    private string? _declined;
    private bool _floorRaised;

    /// <summary>Lays the body the plan names, each form that fits the plan in turn until one draws it; with none, the head stands at the origin for the chain rules (C1).</summary>
    private void Form(FragmentPlan plan, ImmutableArray<int> members, string subject)
    {
        var drawn = plan.Kind switch
        {
            FragmentKind.Sourced => Tried(subject, "C2 sourced loop", () => Settled(() => Sourced(plan)))
                || Tried(subject, "C18 unsourced ring", () => Settled(() => Unsourced(plan, members))),
            FragmentKind.SelfLoop => Tried(subject, "C20 ring of one", () => RingOfOne(plan)),
            FragmentKind.Open => Tried(subject, "C19 open supply-to-return", () => Settled(() => Open(plan)))
                || Tried(subject, "C18 unsourced ring", () => Settled(() => Unsourced(plan, members))),
            FragmentKind.Unsourced => Tried(subject, "C18 unsourced ring", () => Settled(() => Unsourced(plan, members))),
            _ => false,
        };

        if (!drawn)
        {
            _sheet.Note(subject, "form", "chain (C1): no ring, loop or open form drew the fragment");
            _sheet.Place(plan.Head, Transform.Identity, new Point(0, 0), "C1", "the fragment's head at the origin in its drawn default");
        }
    }

    /// <summary>
    /// Runs a ring form until its columns stand over their merges (C14): a pass that finds a merge the bottom rail put
    /// right of its column raises that column's floor, and the form is laid again from the state it started in. Three
    /// passes at most; the last stands as drawn.
    /// </summary>
    private bool Settled(Func<bool> form)
    {
        _columnFloor.Clear();

        for (var pass = 0; ; pass++)
        {
            var start = new Attempt(this);
            _floorRaised = false;

            if (!form())
            {
                return false;
            }

            if (!_floorRaised || pass == 2)
            {
                return true;
            }

            start.Restore();
        }
    }

    /// <summary>Runs one form and notes whether it drew the fragment, with the reason it declined when it did not.</summary>
    private bool Tried(string subject, string form, Func<bool> attempt)
    {
        _declined = null;
        var drawn = attempt();
        _sheet.Note(subject, "form", drawn ? $"{form}: drawn" : $"{form}: declined -- {_declined ?? "not built for this fragment"}");
        return drawn;
    }

    /// <summary>A form's attempt on the sheet: the state as it stood, put back if the form declines. Runs are laid only once a form has drawn, so they need no restoring; the trace is kept (<c>C-107</c>).</summary>
    private sealed class Attempt
    {
        private readonly Composer _composer;
        private readonly int _groups;
        private readonly bool[] _placed;
        private readonly bool[] _stood;
        private readonly bool[] _onLoop;
        private readonly Dictionary<(int Component, int Port), Direction> _side;
        private readonly Point _loopCentre;

        public Attempt(Composer composer)
        {
            _composer = composer;
            var sheet = composer._sheet;
            _groups = sheet.Groups.Count;
            _placed = (bool[])sheet.Placed.Clone();
            _stood = (bool[])sheet.Stood.Clone();
            _onLoop = (bool[])composer.OnLoop.Clone();
            _side = new Dictionary<(int Component, int Port), Direction>(sheet.Side);
            _loopCentre = composer.LoopCentre;
        }

        /// <summary>Puts the sheet back as the attempt found it, then declines with the reason.</summary>
        public bool Decline(string reason)
        {
            Restore();
            _composer._declined = reason;
            return false;
        }

        /// <summary>Puts the sheet back as the attempt found it.</summary>
        public void Restore()
        {
            var sheet = _composer._sheet;
            sheet.Groups.RemoveRange(_groups, sheet.Groups.Count - _groups);
            _placed.CopyTo(sheet.Placed, 0);
            _stood.CopyTo(sheet.Stood, 0);
            _onLoop.CopyTo(_composer.OnLoop, 0);
            sheet.Side.Clear();

            foreach (var (key, value) in _side)
            {
                sheet.Side[key] = value;
            }

            _composer.LoopCentre = _loopCentre;
        }
    }

    // ---- C2: the sourced ring --------------------------------------------------------------------------------------

    /// <summary>
    /// C2: the ring through the heat source. The source stands at the origin, outlet up and inlet down; the top rail
    /// runs right from its outlet through the ring's members and blocks, the last loop in flow order (or, with none,
    /// the consumer of the largest duty) takes the right side, and the bottom rail returns to the inlet. A header's
    /// other branches hang between the rails (C14).
    /// </summary>
    private bool Sourced(FragmentPlan plan)
    {
        var attempt = new Attempt(this);

        if (plan.Body is null || RingPath(plan.Body, plan.Cut) is not { } path)
        {
            return attempt.Decline("the ring could not be read from the decomposition");
        }

        var cycle = path.Members;
        var s = cycle[0];
        var ts = _sheet.Admitted(s.Component).Where(t => t.Arrangement == "default" && _sheet.Outward(s.Component, s.OutPort, t) == Direction.Up && _sheet.Outward(s.Component, s.InPort, t) == Direction.Down).ToList();

        if (ts.Count == 0)
        {
            return attempt.Decline("the source has no default arrangement with its outlet up and its inlet down");
        }

        LoopCentre = new Point(0, 0);

        foreach (var member in cycle)
        {
            OnLoop[member.Component] = true;
        }

        var mark = _sheet.Groups.Count;
        var (unit, _, unitEnd, items, _, _) = RailItems(path, mark);

        if (unit is null)
        {
            return attempt.Decline("no consumer unit was found on the cycle");
        }

        Unplace(items, unit);

        _sheet.Place(s.Component, ts[0], new Point(0, 0), "C2", "the loop's source at the origin, outlet up and inlet down");
        var sOut = _sheet.AnchorOf(s.Component, s.OutPort);
        var sIn = _sheet.AnchorOf(s.Component, s.InPort);
        var yTop = sOut.Along(_margin).Y;
        var bottomMembers = cycle.GetRange(unitEnd + 1, cycle.Count - unitEnd - 1);
        var runs = new List<(Member From, List<Point> Points)>();
        var hangers = new List<Hanger>();
        List<Point> topStart = [sOut.At, new Point(sOut.At.X, yTop)];

        if (Top(Sheet.Anchor(topStart[^1], Direction.Right, Direction.Right), topStart, s, items, path, bottomMembers, runs, hangers, sIn.At.X) is not { } top)
        {
            return attempt.Decline("the top rail could not be laid");
        }

        var (_, rightJunction) = Corners(bottomMembers, unit, leftFirst: false, hangers);
        var (drop, yBottom) = Bottom(unit, top.End.At.Y, sIn.Along(_margin).Y, rightJunction?.Component ?? -1);
        yBottom = Math.Min(yBottom, Under(hangers));

        // C12: a member on a side with slack sits at the side's middle. The source moves down by half the excess of the rails' span over its own.
        var slack = sIn.Along(_margin).Y - yBottom;
        _sheet.Place(s.Component, ts[0], new Point(0, -slack / 2), "C12", $"the source at the middle of its side, half the rails' slack ({slack:0.##}) down");
        sOut = _sheet.AnchorOf(s.Component, s.OutPort);
        sIn = _sheet.AnchorOf(s.Component, s.InPort);
        topStart[0] = sOut.At;

        if (!Close(top, unit, drop, yBottom, bottomMembers, [sIn.At, sIn.Along(_margin), new Point(sIn.At.X, yBottom)], null, rightJunction, hangers, runs))
        {
            return attempt.Decline("the ring could not be closed along the bottom rail");
        }

        // The ring is a group (A8), listed before the blocks it holds.
        _sheet.Groups.Insert(mark, (cycle.Select(static m => m.Component).ToList(), true));
        AssignRuns(runs);
        return true;
    }

    // ---- C18: the unsourced ring -----------------------------------------------------------------------------------

    /// <summary>
    /// C18: a closed loop with no heat source is still a ring. Its consumer takes the right side; the top-left corner
    /// is the first node after it in flow order -- in from below, out to the right -- or, where the loop's nodes are
    /// points on the line, a bare bend before the first member the flow reaches past such a point; the members
    /// between consumer and corner lie on the bottom rail, the rest on the top, and the left side is the bare vertical
    /// up into the corner. A member that turns the flow from leftward to upward takes the bottom-left corner (C9
    /// mirrored, <c>C-105</c>).
    /// </summary>
    private bool Unsourced(FragmentPlan plan, ImmutableArray<int> fragment)
    {
        var attempt = new Attempt(this);
        var consumer = Consumer(fragment, plan.Head);

        if (consumer < 0)
        {
            return attempt.Decline("no member besides the head to take the consumer's seat");
        }

        if (CycleThrough(plan, consumer) is not { } path)
        {
            return attempt.Decline("no cycle of boxed members returns to the consumer");
        }

        var cycle = path.Members;
        var cornerAt = -1;
        var split = -1;

        for (var k = 1; k < cycle.Count && cornerAt < 0; k++)
        {
            if (_view.Wildcard(cycle[k].Component))
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
        var mark = _sheet.Groups.Count;
        var (innerEnd, unit) = UnitOf(path, Direction.Left);
        var end = cornerAt >= 0 ? cornerAt : split;

        if (unit is null || innerEnd >= end)
        {
            return attempt.Decline(unit is null ? "no unit was found past the consumer" : "the unit reaches the corner and leaves nothing for the rails");
        }

        foreach (var i in unit.Members)
        {
            _sheet.Placed[i] = false;
        }

        foreach (var member in cycle)
        {
            OnLoop[member.Component] = true;
        }

        var bottomMembers = cycle.GetRange(innerEnd + 1, end - innerEnd - 1);
        (Member Member, Transform Transform)? leftTurner = null;

        if (corner is not null && bottomMembers.Count > 0 && !_view.Wildcard(bottomMembers[^1].Component))
        {
            var last = bottomMembers[^1];
            var turning = _sheet.Admitted(last.Component)
                .Where(t => _sheet.Outward(last.Component, last.InPort, t) == Direction.Right && _sheet.Outward(last.Component, last.OutPort, t) == Direction.Up)
                .ToList();

            if (turning.Count > 0)
            {
                leftTurner = (last, turning[0]);
                bottomMembers = bottomMembers.GetRange(0, bottomMembers.Count - 1);
            }
        }

        var skip = end + (cornerAt >= 0 ? 1 : 0);
        var items = cycle.GetRange(skip, cycle.Count - skip).Select(static m => new Item(m, null)).ToList();
        PlacedAnchor cOut;
        PlacedAnchor cIn;
        Member previous;

        if (corner is { } c)
        {
            _sheet.Place(c.Component, Transform.Identity, new Point(0, 0), "C18", "the unsourced ring's corner node at the origin");
            _sheet.Side[(c.Component, c.InPort)] = Direction.Down;
            _sheet.Side[(c.Component, c.OutPort)] = Direction.Right;
            cOut = _sheet.AnchorOf(c.Component, c.OutPort);
            cIn = _sheet.AnchorOf(c.Component, c.InPort);
            previous = c;
        }
        else
        {
            // A bare bend at the origin: the run that turns it belongs to the last bottom member (or the consumer), and its two halves are joined below.
            cOut = Sheet.Anchor(new Point(0, 0), Direction.Right, Direction.Right);
            cIn = Sheet.Anchor(new Point(0, 0), Direction.Down, Direction.Up);
            previous = cycle[end - 1];
        }

        var runs = new List<(Member From, List<Point> Points)>();
        var hangers = new List<Hanger>();

        if (Top(cOut, [cOut.At], previous, items, path, bottomMembers, runs, hangers, cIn.At.X) is not { } top)
        {
            return attempt.Decline("the top rail could not be laid");
        }

        var (_, right) = Corners(bottomMembers, unit, leftFirst: false, hangers);
        var (drop, yBottom) = Bottom(unit, top.End.At.Y, cIn.Along(_margin).Y, right?.Component ?? -1);
        yBottom = Math.Min(yBottom, Under(hangers));

        if (leftTurner is { } lt)
        {
            // The rail is where the turner's inlet lies; its outlet's tip must sit a margin under the corner's inlet.
            var dIn = _sheet.AnchorOffset(lt.Member.Component, lt.Member.InPort, lt.Transform)!.Value.Offset;
            var dOut = _sheet.AnchorOffset(lt.Member.Component, lt.Member.OutPort, lt.Transform)!.Value.Offset;
            yBottom = Math.Min(yBottom, cIn.Along(_margin).Y - (dOut.Y - dIn.Y));
        }

        if (!Close(top, unit, drop, yBottom, bottomMembers, [cIn.At, cIn.Along(_margin), new Point(cIn.At.X, yBottom)], null, right, hangers, runs, leftTurner))
        {
            return attempt.Decline("the ring could not be closed along the bottom rail");
        }

        // C8 reads a node's free side against the loop's centre; the ring is built from its corner, so the centre is its members' mean (C-105).
        LoopCentre = new Point(cycle.Average(m => _sheet.Centre[m.Component].X), cycle.Average(m => _sheet.Centre[m.Component].Y));

        if (corner is null)
        {
            // The bare corner's two halves are one run: the bottom rail's last piece ends at the origin, the top rail's first starts there.
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

        _sheet.Groups.Insert(mark, (cycle.Select(static m => m.Component).ToList(), true));
        AssignRuns(runs);
        return true;
    }

    /// <summary>
    /// The member that takes an unsourced ring's right side (C18): the consumer of the largest duty, else the first
    /// exchanger (<c>C-100</c>), else the first boxed member besides the head that is not a pump, else any (<c>C-102</c>).
    /// </summary>
    private int Consumer(ImmutableArray<int> fragment, int head)
    {
        var consumer = fragment.Where(i => _view.Duty(i) is not null).OrderBy(i => _view.Duty(i)).ThenBy(static i => i).FirstOrDefault(-1);

        if (consumer < 0)
        {
            consumer = fragment.Where(i => _view.Graph.Components[i] is HeatExchangerComponent).Order().FirstOrDefault(-1);
        }

        if (consumer < 0)
        {
            var seats = fragment.Where(i => i != head && !_view.IsInline(i) && !_view.Wildcard(i)).Order().ToList();
            consumer = seats.FirstOrDefault(i => _view.Graph.Components[i] is not PumpComponent, seats.FirstOrDefault(-1));
        }

        return consumer;
    }

    /// <summary>The closed path through <paramref name="consumer"/>, started there: the plan's ring when the consumer is on it, else the outermost loop of the body it lies on.</summary>
    private Path? CycleThrough(FragmentPlan plan, int consumer)
    {
        if (plan.Body is null)
        {
            return null;
        }

        if (plan.Kind is FragmentKind.Sourced or FragmentKind.Unsourced && RingPath(plan.Body, plan.Cut) is { } ring)
        {
            var at = ring.Members.FindIndex(m => m.Component == consumer);
            return at < 0 ? null : Rotated(ring, at);
        }

        foreach (var loop in Loops(plan.Body))
        {
            if (InnerPath(loop, consumer) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }

    /// <summary>The loops of a structure, outermost first.</summary>
    private static IEnumerable<LoopStructure> Loops(Structure structure)
    {
        switch (structure)
        {
            case LoopStructure loop:
                yield return loop;

                foreach (var inner in Loops(loop.Forward).Concat(Loops(loop.Back)))
                {
                    yield return inner;
                }

                break;
            case SeriesStructure series:
                foreach (var inner in series.Parts.SelectMany(Loops))
                {
                    yield return inner;
                }

                break;
            case HeaderStructure header:
                foreach (var inner in header.Branches.SelectMany(Loops))
                {
                    yield return inner;
                }

                break;
        }
    }

    /// <summary>Whether the run leaving a member passes a node that is a point on the line (C18).</summary>
    private bool PassesNode(Member from) =>
        _view.RunAt(from.Component, from.OutPort) is { } run && run.Inline.Any(p => _view.Wildcard(p.Element));

    // ---- C19: the open supply-to-return form -----------------------------------------------------------------------

    /// <summary>
    /// C19: a supply boundary feeding two paths to one return is the open supply-to-return form. The supply's junction
    /// is the left end of the top rail and the return's, directly under it, the left end of the bottom rail: the first
    /// path with no loop hangs straight down between them, and the other path is the ring's right side, fed level from
    /// the supply's right and returning along the bottom into the return's right. One path with no loop is the chain
    /// alone, the return at its foot (step 11b). The inlet and the outlet hang off their junctions' left sides (<c>D-115</c>).
    /// </summary>
    private bool Open(FragmentPlan plan)
    {
        var attempt = new Attempt(this);
        var supply = plan.Head;

        if (!_view.IsInlet(supply) || !_view.Wildcard(supply) || plan.Body is null)
        {
            return attempt.Decline("the head is not a supply boundary");
        }

        List<Structure> parts = plan.Body is SeriesStructure series ? [.. series.Parts] : [plan.Body];
        var inlet = -1;
        var inletPort = -1;

        if (parts[0] is RunLeaf lead && _view.Degree(supply) == 1)
        {
            var junction = parts.Count > 1 ? parts[1].From : -1;

            if (junction < 0 || !_view.Wildcard(junction) || _view.IsInline(junction) || _view.IsBoundary(junction))
            {
                return attempt.Decline("the supply does not feed an interior junction node");
            }

            var run = _view.Runs[lead.Run];
            inlet = supply;
            inletPort = lead.WithFlow ? run.End.Port : run.Start.Port;
            supply = junction;
            parts.RemoveAt(0);
        }

        var linked = _view.Connected(supply).Where(p => p != inletPort).ToList();
        List<Path> paths;
        int ret;
        var outletPort = -1;

        if (parts.Count == 2 && parts[0] is HeaderStructure header && header.From == supply && header.Branches.Length == 2 && parts[1] is RunLeaf tail
            && _view.Wildcard(header.To) && !_view.IsInline(header.To))
        {
            // Both paths end on one junction: it is the bottom rail's left end, and the outlet hangs off its left side.
            var run = _view.Runs[tail.Run];
            ret = header.To;
            outletPort = tail.WithFlow ? run.Start.Port : run.End.Port;
            paths = [];

            foreach (var branch in header.Branches)
            {
                if (PathOf([branch], supply) is not { } path)
                {
                    return attempt.Decline("a path could not be read from the decomposition");
                }

                path.Members[0] = path.Members[0] with { InPort = path.EndPort };
                paths.Add(path);
            }

            paths = [.. paths.OrderBy(static p => p.Members[0].OutPort)];
        }
        else if (PathOf(parts, supply) is { } single && parts[^1].To is var end && _view.IsBoundary(end) && !_view.IsInlet(end))
        {
            ret = end;
            single.Members[0] = single.Members[0] with { InPort = single.EndPort };
            paths = [single];
        }
        else
        {
            return attempt.Decline("the junction's paths do not both reach one return boundary");
        }

        // A loop through the supply itself is not a block of the path: only spans past the supply are.
        static bool HasBlocks(Path path) => path.Blocks.Any(static b => b.Start > 0);
        var chainAt = paths.FindIndex(p => !HasBlocks(p));
        var ringAt = paths.FindIndex(HasBlocks);
        var chainOnly = ringAt < 0 && paths.Count == 1;

        if (ringAt < 0 && !chainOnly)
        {
            ringAt = chainAt == 0 ? 1 : 0;
        }

        var chain = chainAt >= 0 && chainAt != ringAt ? paths[chainAt] : null;
        var ringPath = chainOnly ? null : paths[ringAt];
        var mark = _sheet.Groups.Count;
        Unit? unit = null;
        var unitEnd = 0;
        var items = new List<Item>();
        Span? unitRange = null;
        var unitGroups = 0;

        if (ringPath is not null)
        {
            ringPath.Members[0] = ringPath.Members[0] with { InPort = -1 };
            (unit, _, unitEnd, items, unitRange, unitGroups) = RailItems(ringPath, mark);

            if (unit is null)
            {
                return attempt.Decline("no unit was found on the ring path");
            }

            Unplace(items, unit);
        }

        var firstOut = ringPath?.Members[0].OutPort ?? chain!.Members[0].OutPort;
        LoopCentre = new Point(0, 0);
        _sheet.Place(supply, Transform.Identity, new Point(0, 0), "C19", "the supply boundary at the left end of the top rail");
        _sheet.Side[(supply, firstOut)] = Direction.Right;
        var half = Transform.Identity.Size(_view.Symbols[ret]).Height / 2;
        var natural = _sheet.InnerOf(supply).Y - (2 * _margin) - half;
        var runs = new List<(Member From, List<Point> Points)>();
        Member chainEnd = default;
        PlacedAnchor chainCursor = default;

        if (chain is not null)
        {
            // The chain hangs straight down under the supply, each member placed from the one before.
            _sheet.Side[(supply, chain.Members[0].OutPort)] = Direction.Down;
            var cursor = _sheet.AnchorOf(supply, chain.Members[0].OutPort);
            var previous = new Member(supply, -1, chain.Members[0].OutPort);
            var pending = new List<Point> { cursor.At };

            foreach (var m in chain.Members.Skip(1))
            {
                if (OnRail(cursor, m, m.InPort, m.OutPort) is not { } points)
                {
                    return attempt.Decline("a chain member could not be laid on the rail");
                }

                pending.AddRange(points.Skip(1));
                runs.Add((previous, pending));
                cursor = _sheet.AnchorOf(m.Component, m.OutPort);
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
            // No ring path: the return stands at the chain's foot, directly under the junction.
            _sheet.Place(ret, Transform.Identity, new Point(0, natural), "C19", "no ring path: the return at the chain's foot, directly under the junction");
            _sheet.Side[(ret, chain!.Members[0].InPort)] = Direction.Up;
            runs.Add((chainEnd, [chainCursor.At, _sheet.AnchorOf(ret, chain.Members[0].InPort).At]));
            Finish();
            return true;
        }

        var cycle = ringPath!.Members;

        // C12 for the open form: a block standing on both rails whose outlet sits above the chain's level would put a step in the return, so the block is rebuilt deeper and its outlet meets the rail.
        if (unitRange is { } range && unit!.In.Outward == Direction.Left && unit.Out.Outward == Direction.Left)
        {
            var outletAt = _sheet.AnchorOf(supply, cycle[0].OutPort).At.Y + unit.Out.At.Y - unit.In.At.Y;

            if (outletAt > natural + Eps)
            {
                _sheet.Groups.RemoveRange(mark, unitGroups);
                var at = _sheet.Groups.Count;
                unit = Block(range.Loop, cycle[range.Start], cycle[range.End], Direction.Left, outletAt - natural);

                if (unit is null)
                {
                    return attempt.Decline("the ring's block could not be laid as a unit");
                }

                var created = _sheet.Groups.GetRange(at, _sheet.Groups.Count - at);
                _sheet.Groups.RemoveRange(at, created.Count);
                _sheet.Groups.InsertRange(mark, created);

                foreach (var i in unit.Members)
                {
                    _sheet.Placed[i] = false;
                }
            }
        }

        var sOut = _sheet.AnchorOf(supply, cycle[0].OutPort);
        var bottomMembers = cycle.GetRange(unitEnd + 1, cycle.Count - unitEnd - 1);
        var hangers = new List<Hanger>();

        if (Top(sOut, [sOut.At], cycle[0], items, ringPath, bottomMembers, runs, hangers, sOut.At.X) is not { } top)
        {
            return attempt.Decline("the top rail could not be laid");
        }

        var (_, rightJunction) = Corners(bottomMembers, unit!, leftFirst: false, hangers);
        var (drop, yBottom) = Bottom(unit!, top.End.At.Y, natural, rightJunction?.Component ?? -1);
        yBottom = Math.Min(yBottom, Under(hangers));

        // The return stands directly under the supply at the bottom rail's level, fed by the chain from above and by the rail from the right.
        _sheet.Place(ret, Transform.Identity, new Point(0, yBottom), "C19", "the return directly under the supply at the bottom rail's level");
        var ringIn = paths[ringAt].EndPort;
        _sheet.Side[(ret, ringIn)] = Direction.Right;

        if (chain is not null)
        {
            _sheet.Side[(ret, chain.EndPort)] = Direction.Up;
            runs.Add((chainEnd, [chainCursor.At, _sheet.AnchorOf(ret, chain.EndPort).At]));
        }

        var rIn = _sheet.AnchorOf(ret, ringIn);

        if (!Close(top, unit!, drop, yBottom, bottomMembers, [rIn.At, rIn.Along(_margin)], null, rightJunction, hangers, runs))
        {
            return attempt.Decline("the ring could not be closed along the bottom rail");
        }

        foreach (var m in cycle)
        {
            OnLoop[m.Component] = true;
        }

        Finish();
        return true;

        void Finish()
        {
            // The inlet and the outlet hang off their junctions' left sides (D-115); the supply's other connections leave by the sides the form leaves free, up first -- right and down are the rails' own.
            if (inlet >= 0)
            {
                _sheet.Side[(supply, inletPort)] = Direction.Left;
            }

            if (outletPort >= 0)
            {
                _sheet.Side[(ret, outletPort)] = Direction.Left;
            }

            var free = new Queue<Direction>([Direction.Up, Direction.Left]);

            foreach (var p in linked.Where(p => !_sheet.Side.ContainsKey((supply, p))))
            {
                while (free.Count > 0)
                {
                    var side = free.Dequeue();

                    if (!_sheet.Side.Any(kv => kv.Key.Component == supply && kv.Value == side))
                    {
                        _sheet.Side[(supply, p)] = side;
                        break;
                    }
                }
            }

            AssignRuns(runs);
        }
    }

    // ---- C20: the ring of one --------------------------------------------------------------------------------------

    /// <summary>
    /// C20 (<c>C-101</c>): a component connected to itself is a ring of one. It stands at the origin in its drawn
    /// default; the pipe leaves the outlet by its outward margin, walks the outer box's edge clockwise (H9) round to
    /// the inlet's stub and enters by its margin, the inline points between cut into it (A5).
    /// </summary>
    private bool RingOfOne(FragmentPlan plan)
    {
        var attempt = new Attempt(this);
        var head = plan.Head;

        if (plan.Body is not RunLeaf leaf)
        {
            return attempt.Decline("the body is not one run returning to the head");
        }

        var run = _view.Runs[leaf.Run];
        var (outPort, inPort) = _view.Enters(head, run.Start.Port) ? (run.End.Port, run.Start.Port) : (run.Start.Port, run.End.Port);
        _sheet.Place(head, Transform.Identity, new Point(0, 0), "C20", "a ring of one: the head at the origin, the walk returning to it");
        var outlet = _sheet.AnchorOf(head, outPort);
        var inlet = _sheet.AnchorOf(head, inPort);
        var (width, height) = _sheet.SizeOf(head);

        if (Clockwise(outlet.Along(_margin), inlet.Along(_margin), (width / 2) + _margin, (height / 2) + _margin) is not { } around)
        {
            return attempt.Decline("the head's stubs do not end on its outer box");
        }

        List<Point> points = [outlet.At, .. around, inlet.At];
        List<int> members = [head, .. run.Inline.Select(static e => e.Element)];

        foreach (var i in members)
        {
            OnLoop[i] = true;
        }

        _sheet.Groups.Add((members, true));
        AssignRuns([(new Member(head, inPort, outPort), points)]);
        return true;
    }

    /// <summary>The clockwise walk along the edge of the box <c>[-x, x] × [-y, y]</c> about the origin from <paramref name="from"/> to <paramref name="to"/>, both on that edge, through the corners between; null where either is off the edge or they coincide.</summary>
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
