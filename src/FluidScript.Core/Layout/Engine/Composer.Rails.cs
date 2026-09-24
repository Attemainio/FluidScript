using System.Collections.Immutable;

using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Engine.Structures;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout.Engine;

internal sealed partial class Composer
{
    /// <summary>
    /// A run as a form drew it: the member and port it leaves by, its polyline in that direction, and the rule that
    /// shaped it with why -- what the trace names when the run is laid (<c>28</c> A10).
    /// </summary>
    private sealed record RunDraft(Member From, List<Point> Points, string Rule, string Reason);

    /// <summary>
    /// A ring's right side, laid out at a provisional place: one member, or a whole loop as a block (A8, C11). The ring
    /// slides it into place and shifts its runs with it.
    /// </summary>
    /// <param name="Members">The boxed elements the unit moves as one.</param>
    /// <param name="In">Where the ring's top rail enters, facing left (a corner) or up (from the rail above).</param>
    /// <param name="Out">Where the ring's right side leaves, facing down, right or left.</param>
    /// <param name="OutFrom">The member and port the leaving run starts from.</param>
    /// <param name="Bottom">The lowest inner-box edge, provisional units.</param>
    /// <param name="Runs">The unit's own runs, provisional units, laid by the ring after the shift.</param>
    private sealed record Unit(List<int> Members, PlacedAnchor In, PlacedAnchor Out, Member OutFrom, double Bottom, List<RunDraft> Runs);

    /// <summary>One thing on a rail: a member, or a block laid out as a unit (C11).</summary>
    private readonly record struct Item(Member? Member, Unit? Unit);

    /// <summary>A branch hanging between the rails (C14): where it ends, its return still to be laid to its merge on the bottom rail, which stands under its split -- straight under for a plain column, whose boxed members it names.</summary>
    private sealed record Hanger(PlacedAnchor Out, Member OutFrom, int Top, int Bottom, int BottomPort, bool Straight, List<int>? Members = null);

    /// <summary>A column's boxed members; a block hanger names none.</summary>
    private static List<int> ColumnMembers(Hanger h) => h.Members ?? [];

    /// <summary>
    /// The ring path's unit and top-rail items (C11): the last loop in flow order is the unit on the ring's right side,
    /// every earlier one a block on the top rail; with none, the consumer of the largest duty stands on the right alone.
    /// </summary>
    /// <returns>The unit (null when a block could not be laid), where it starts and ends on the path, the items before it, its span when it is a block, and how many groups the unit's block created.</returns>
    private (Unit? Unit, int Start, int End, List<Item> Items, Span? Range, int UnitGroups) RailItems(Path path, int mark)
    {
        var cycle = path.Members;
        var ranges = path.Blocks.Where(static b => b.Start >= 1).OrderBy(static b => b.Start).ToList();
        var items = new List<Item>();

        if (ranges.Count > 0)
        {
            var last = ranges[^1];
            var unit = Block(last.Loop, cycle[last.Start], cycle[last.End], Direction.Left);
            var unitGroups = _sheet.Groups.Count - mark;
            var next = 1;

            foreach (var range in ranges.SkipLast(1))
            {
                items.AddRange(cycle.GetRange(next, range.Start - next).Select(static m => new Item(m, null)));

                if (Block(range.Loop, cycle[range.Start], cycle[range.End], Direction.Right) is not { } block)
                {
                    unit = null;
                    break;
                }

                items.Add(new Item(null, block));
                next = range.End + 1;
            }

            if (unit is not null)
            {
                items.AddRange(cycle.GetRange(next, last.Start - next).Select(static m => new Item(m, null)));
            }

            return (unit, last.Start, last.End, items, last, unitGroups);
        }

        var consumerAt = ConsumerOf(cycle, 1);
        var single = consumerAt < 0 ? null : Single(cycle[consumerAt]);

        if (single is not null)
        {
            items.AddRange(cycle.GetRange(1, consumerAt - 1).Select(static m => new Item(m, null)));
        }

        return (single, consumerAt, consumerAt, items, null, 0);
    }

    /// <summary>The unit that starts a closed path (C11): the block of the loop its first member begins, else that member alone.</summary>
    /// <returns>Where the unit ends on the path, and the unit (null when it could not be laid).</returns>
    private (int End, Unit? Unit) UnitOf(Path path, Direction outlet)
    {
        foreach (var span in path.Blocks)
        {
            if (span.Start == 0)
            {
                return (span.End, Block(span.Loop, path.Members[0], path.Members[span.End], outlet));
            }
        }

        return (0, Single(path.Members[0]));
    }

    /// <summary>Every unit stands at a provisional place until it is slid in; none is an obstacle before that.</summary>
    private void Unplace(List<Item> items, Unit? unit)
    {
        foreach (var u in items.Where(static i => i.Unit is not null).Select(static i => i.Unit!).Concat(unit is null ? [] : [unit]))
        {
            foreach (var i in u.Members)
            {
                _sheet.Placed[i] = false;
            }
        }
    }

    /// <summary>Lays every run a form drew, each given from the member and port it leaves by.</summary>
    private void AssignRuns(List<RunDraft> runs)
    {
        foreach (var (from, points, rule, reason) in runs)
        {
            if (_view.RunAt(from.Component, from.OutPort) is not { } run)
            {
                continue;
            }

            if (run.Start.Component == from.Component && run.Start.Port == from.OutPort)
            {
                _sheet.Lay(run, points, rule, reason);
            }
            else
            {
                _sheet.LayBackwards(run, points, rule, reason);
            }
        }
    }

    /// <summary>
    /// The member that takes a ring's right side: the standing consumer of the largest duty from <paramref name="from"/>
    /// on -- the duty the ring sees on the side it passes, so a heat source's second side is a consumer (C-132) -- else
    /// the first member the flow leaves the ring by.
    /// </summary>
    private int ConsumerOf(List<Member> cycle, int from, int to = -1)
    {
        var consumerAt = -1;
        to = to < 0 ? cycle.Count : to;

        for (var k = from; k < to; k++)
        {
            if (_view.DutyOn(cycle[k].Component, cycle[k].InPort) is { } duty && (consumerAt < 0 || duty < _view.DutyOn(cycle[consumerAt].Component, cycle[consumerAt].InPort)))
            {
                consumerAt = k;
            }
        }

        for (var k = from; k < to && consumerAt < 0; k++)
        {
            // No standing consumer: the member the loop's flow leaves by -- a valve or a junction with an off-loop outlet -- takes the right side.
            if (LeavesLoop(cycle[k]))
            {
                consumerAt = k;
            }
        }

        return consumerAt;
    }

    /// <summary>
    /// The headers in series before a ring's right side (<c>D-156</c>, <c>C-131</c>): each split on the path whose branch
    /// returns to a merge that is also on the path before <paramref name="unitStart"/>, with a consumer on the spine
    /// between them -- a header whose return feeds on round the ring. Each is a band: its consumer takes the band's right
    /// side and its return is the band's own bottom rail. A branch rejoining the path with no consumer between (a pump
    /// pair) is a row (C14), not a band; a header nested in a band's spine hangs inside it.
    /// </summary>
    /// <returns>Each band's split, consumer and merge, as indices on the path, in flow order.</returns>
    private List<(int Split, int Consumer, int Merge)> SeriesBands(Path path, int unitStart)
    {
        var cycle = path.Members;
        var index = new Dictionary<int, int>();

        for (var i = 0; i < cycle.Count; i++)
        {
            index.TryAdd(cycle[i].Component, i);
        }

        var bands = new List<(int Split, int Consumer, int Merge)>();

        for (var k = 1; k < unitStart; k++)
        {
            if (!path.Branches.TryGetValue(cycle[k].Component, out var branches))
            {
                continue;
            }

            foreach (var branch in branches)
            {
                if (index.TryGetValue(branch.Merge, out var merge) && merge > k && merge + 1 < unitStart && ConsumerOf(cycle, k + 1, merge) is var consumer and >= 0 && _view.DutyOn(cycle[consumer].Component, cycle[consumer].InPort) is not null)
                {
                    bands.Add((k, consumer, merge));
                    k = merge;
                    break;
                }
            }
        }

        return bands;
    }

    /// <summary>The largest half-height of the boxed members of a stretch of rail, world units; zero for none.</summary>
    private double HalfHeight(IEnumerable<Member> members) =>
        members.Where(m => !_view.IsInline(m.Component)).Select(m => Transform.Identity.Size(_view.Symbols[m.Component]).Height / 2).DefaultIfEmpty(0).Max();

    /// <summary>Whether a loop member has a connection off the loop the fluid leaves by.</summary>
    private bool LeavesLoop(Member m) =>
        _view.Connected(m.Component).Any(p => p != m.InPort && p != m.OutPort && !_view.Enters(m.Component, p));

    /// <summary>A loop member placed on a rail from the cursor: a component by C5 through the port facing the cursor, a node by C4 with its other loop port turned on along the rail.</summary>
    /// <returns>The pipe from the cursor to the facing port, or null when nothing admitted fits.</returns>
    private ImmutableArray<Point>? OnRail(PlacedAnchor cursor, Member m, int facing, int onward)
    {
        var length = _view.RunAt(m.Component, facing) is { } run ? _sheet.RunLength(run) : _margin;

        if (!_view.Wildcard(m.Component))
        {
            return PlaceFrom(cursor, m.Component, facing, length);
        }

        var points = PlaceNode(cursor, m.Component, facing, length);
        _sheet.Side[(m.Component, onward)] = cursor.Outward;
        return points;
    }

    // ---- the top rail and what hangs from it (C11, C14) -----------------------------------------------------------

    /// <summary>
    /// Lays a rail rightwards from its start: each member by <see cref="OnRail"/>, each block by <see cref="Slide"/>
    /// with the rail continuing from its outlet, and under each split whose branch returns to a bottom-rail member,
    /// the branch hanging between the rails (C14). Appends the runs so far.
    /// </summary>
    /// <returns>Where the rail ends, its pending points and the member it leaves; null when a member cannot be placed.</returns>
    private (PlacedAnchor End, List<Point> Pending, Member Previous)? Top(PlacedAnchor cursor, List<Point> pending, Member previous, List<Item> items, Path? path, List<Member>? bottomMembers, List<RunDraft> runs, List<Hanger> hangers, double left = double.NegativeInfinity)
    {
        // Branches that rejoin this rail (C14, C-130): laid as a row under the spine once the rail reaches the merge.
        var rows = new List<(Member Split, Branch Branch, List<Member> Members, List<int> Spine)>();

        foreach (var item in items)
        {
            if (item.Unit is { } u)
            {
                foreach (var row in rows)
                {
                    row.Spine.AddRange(u.Members);
                }

                var (uIn, uOut) = Slide(u, cursor.At.X, cursor.At.Y, runs);
                pending.Add(uIn.At);
                runs.Add(new RunDraft(previous, pending, "C2", "along the rail into a unit"));
                cursor = uOut;
                pending = [uOut.At];
                previous = u.OutFrom;
                continue;
            }

            var m = item.Member!.Value;
            var joining = rows.FindIndex(r => r.Branch.Merge == m.Component);
            (PlacedAnchor End, Member Last, List<RunDraft> Runs)? laidRow = null;

            if (joining >= 0 && (cursor.Outward != Direction.Right || Math.Abs(cursor.At.Y - _sheet.Centre[rows[joining].Split.Component].Y) > Eps))
            {
                // The rail turned between the split and the merge: a row under it has no level spine to stand under, so the
                // branch is left to the chain rules as before.
                _parallelMerge.Remove(m.Component);
                rows.RemoveAt(joining);
                joining = -1;
            }

            if (joining >= 0)
            {
                // The merge stands far enough right that the row rises into it in one bend, a stub clear of its last member.
                if (Parallel(rows[joining].Split, rows[joining].Branch, rows[joining].Members, rows[joining].Spine) is not { } row)
                {
                    return null;
                }

                laidRow = row;
                var half = Transform.Identity.Size(_view.Symbols[m.Component]).Width / 2;
                var rise = _view.RunAt(row.Last.Component, row.Last.OutPort) is { } riseRun ? _sheet.RunLength(riseRun) : _margin;
                var onto = _view.RunAt(m.Component, m.InPort) is { } ontoRun ? _sheet.RunLength(ontoRun) : _margin;
                var x = row.End.At.X + Math.Max(_margin, rise) - half - onto;

                if (x > cursor.At.X)
                {
                    cursor = Sheet.Anchor(new Point(x, cursor.At.Y), Direction.Right, Direction.Right);
                }

                rows.RemoveAt(joining);
            }

            if (OnRail(cursor, m, m.InPort, m.OutPort) is not { } points)
            {
                return null;
            }

            pending.AddRange(points.Skip(1));
            runs.Add(new RunDraft(previous, pending, "C2", "along the rail"));
            cursor = _sheet.AnchorOf(m.Component, m.OutPort);
            previous = m;

            foreach (var row in rows)
            {
                row.Spine.Add(m.Component);
            }

            if (laidRow is { } parallel && MergePortOf(m.Component) is { } mergePort)
            {
                _sheet.Side[(m.Component, mergePort)] = Direction.Down;
                var up = _sheet.AnchorOf(m.Component, mergePort);
                runs.AddRange(parallel.Runs);
                runs.Add(new RunDraft(parallel.Last, [parallel.End.At, new Point(up.At.X, parallel.End.At.Y), up.At], "C14", "the parallel row's rise into its merge"));
            }

            if (bottomMembers is not null && path is not null && path.Branches.TryGetValue(m.Component, out var branches))
            {
                foreach (var branch in branches)
                {
                    if (items.Any(i => i.Member is { } later && later.Component == branch.Merge)
                        && PathOf([branch.Body], m.Component) is { Blocks.Count: 0 } plain)
                    {
                        _parallelMerge[branch.Merge] = branch.MergePort;
                        rows.Add((m, branch, plain.Members.Skip(1).ToList(), [m.Component]));
                        continue;
                    }

                    if (bottomMembers.Any(b => b.Component == branch.Merge) && Hang(m, branch, cursor.At.Y, left, runs, hangers) is { } moved)
                    {
                        cursor = moved;
                        break;
                    }
                }
            }

            pending = [cursor.At];
        }

        return (cursor, pending, previous);
    }

    /// <summary>
    /// C14: a header's branch off a top-rail split hangs between the rails and returns to its merge on the bottom
    /// rail. A branch with loops is a chain of blocks like a rail (C11): its first block hangs one margin under the
    /// split with its inlet one margin right of it, the split moved along its rail to stand over the inlet so the drop
    /// is one bend; each further block steps on from the one before, and the last faces back to the left. A branch
    /// with no loop hangs as a column straight down from the split (<see cref="Column"/>).
    /// </summary>
    /// <returns>The split's outlet where the rail continues from, or null when nothing hangs.</returns>
    private PlacedAnchor? Hang(Member j, Branch branch, double yTop, double left, List<RunDraft> runs, List<Hanger> hangers)
    {
        if (PathOf([branch.Body], j.Component) is not { } path)
        {
            return null;
        }

        var members = path.Members.Skip(1).ToList();
        var ranges = path.Blocks.Where(static b => b.Start >= 1).Select(static b => new Span(b.Start - 1, b.End - 1, b.Loop)).OrderBy(static b => b.Start).ToList();

        if (ranges.Count == 0)
        {
            return Column(j, branch, members, left, runs, hangers);
        }

        var mark = _sheet.Groups.Count;
        var units = new List<Unit>();
        var items = new List<Item>();
        var next = 0;

        foreach (var range in ranges)
        {
            if (Block(range.Loop, members[range.Start], members[range.End], range.End == ranges[^1].End ? Direction.Left : Direction.Right) is not { } block)
            {
                break;
            }

            if (units.Count > 0)
            {
                items.AddRange(members.GetRange(next, range.Start - next).Select(static m => new Item(m, null)));
                items.Add(new Item(null, block));
            }

            units.Add(block);
            next = range.End + 1;
        }

        if (units.Count < ranges.Count)
        {
            _sheet.Groups.RemoveRange(mark, _sheet.Groups.Count - mark);
            return null;
        }

        foreach (var i in units.SelectMany(static u => u.Members))
        {
            OnLoop[i] = true;
        }

        var first = units[0];
        var feed = runs.Count - 1;
        var rise = first.Members.Max(i => _sheet.InnerOf(i).Top) - first.In.At.Y;
        var half = Transform.Identity.Size(_view.Symbols[j.Component]).Height / 2;

        // The block hangs under the split's box by a margin, and its bubbles under the rail by a margin (D-151).
        var yIn = Math.Min(yTop - half - _margin - rise, yTop - _margin - (BubbleTop(first) - first.In.At.Y));
        var origin = Math.Max(_sheet.Centre[j.Component].X, _hangFloor.GetValueOrDefault(j.Component, double.NegativeInfinity));
        var (uIn, uOut) = Slide(first, origin, yIn, runs);
        var jx = uIn.At.X - _margin;

        if (jx > _sheet.Centre[j.Component].X)
        {
            _sheet.Centre[j.Component] = new Point(jx, _sheet.Centre[j.Component].Y);
            _sheet.Note(_view.Name(j.Component), "C14", $"the junction moved right to ({jx:0.##}, {_sheet.Centre[j.Component].Y:0.##}) so its hanger's inlet clears it by a margin");
            runs[feed].Points[^1] = _sheet.AnchorOf(j.Component, j.InPort).At;
        }

        _sheet.Side[(j.Component, branch.SplitPort)] = Direction.Down;
        var free = _sheet.AnchorOf(j.Component, branch.SplitPort);
        runs.Add(new RunDraft(new Member(j.Component, -1, branch.SplitPort), [free.At, new Point(free.At.X, uIn.At.Y), uIn.At], "C14", "the split's feed down to its hanging block"));

        // The rest of the chain steps on from the first block's outlet, as a rail does.
        if (Top(uOut, [uOut.At], first.OutFrom, items, null, null, runs, hangers) is not { } chain)
        {
            return null;
        }

        hangers.Add(new Hanger(chain.End, chain.Previous, j.Component, branch.Merge, branch.MergePort, false));
        return _sheet.AnchorOf(j.Component, j.OutPort);
    }

    /// <summary>
    /// A branch with no loop hangs as a column (<c>28</c> C14, P6.10): its members stand one under the other straight
    /// down from the split, laid on a canvas of their own and slid right as one until they clear the ring's left side
    /// and everything placed (H2), the split moved along its rail to stand over them. Its merge on the bottom rail
    /// stands directly under the split, so the return is one straight drop; where the bottom rail cannot put it there,
    /// the form is laid again with the split held over the merge (<see cref="Settled"/>).
    /// </summary>
    /// <param name="j">The split, on the top rail.</param>
    /// <param name="branch">The branch.</param>
    /// <param name="members">The branch's boxed members in flow order, the split and the merge aside.</param>
    /// <param name="left">The x of the ring's left side, which the column's outer boxes stay right of.</param>
    /// <param name="runs">The form's runs so far; the last is the rail's run into the split.</param>
    /// <param name="hangers">Where the column is added.</param>
    /// <returns>The split's outlet where the rail continues from, or null when a member cannot be placed.</returns>
    private PlacedAnchor? Column(Member j, Branch branch, List<Member> members, double left, List<RunDraft> runs, List<Hanger> hangers)
    {
        var feed = runs.Count - 1;
        _sheet.Side[(j.Component, branch.SplitPort)] = Direction.Down;
        var cursor = _sheet.AnchorOf(j.Component, branch.SplitPort);
        var previous = new Member(j.Component, -1, branch.SplitPort);
        var pending = new List<Point> { cursor.At };
        var own = new List<RunDraft>();
        var placed = (bool[])_sheet.Placed.Clone();
        Array.Clear(_sheet.Placed);

        foreach (var m in members)
        {
            if (OnRail(cursor, m, m.InPort, m.OutPort) is not { } points)
            {
                placed.CopyTo(_sheet.Placed, 0);
                return null;
            }

            pending.AddRange(points.Skip(1));
            own.Add(new RunDraft(previous, pending, "C14", "down the column"));
            cursor = _sheet.AnchorOf(m.Component, m.OutPort);
            previous = m;
            pending = [cursor.At];
        }

        placed.CopyTo(_sheet.Placed, 0);
        _sheet.Placed[j.Component] = false;
        var boxed = members.Select(static m => m.Component).ToList();
        var dx = Math.Max(0, _hangFloor.GetValueOrDefault(j.Component, double.NegativeInfinity) - _sheet.Centre[j.Component].X);

        if (boxed.Count > 0)
        {
            dx = Math.Max(dx, left + _margin - boxed.Min(i => _sheet.InnerOf(i).X));
        }

        for (var shift = _sheet.Shift(boxed, dx, 0); shift > 0; shift = _sheet.Shift(boxed, dx, 0))
        {
            dx += Math.Ceiling((shift * 10) - 1e-6) / 10;
        }

        // Past the boxes, by the one clearance test: the column's instrument bubbles and stubs clear too.
        while (!_sheet.ClearAt(boxed, dx, 0))
        {
            dx += 0.1;
        }

        _sheet.Placed[j.Component] = true;
        _sheet.Move([j.Component, .. boxed], [], dx, 0);

        foreach (var i in boxed)
        {
            _sheet.Placed[i] = true;
        }

        if (dx > Eps)
        {
            _sheet.Note(_view.Name(j.Component), "C14", $"the split moved right by {dx:0.##} to ({_sheet.Centre[j.Component].X:0.##}, {_sheet.Centre[j.Component].Y:0.##}) so the column under it clears the ring's left side and what is placed (H2)");
            runs[feed].Points[^1] = _sheet.AnchorOf(j.Component, j.InPort).At;
        }

        runs.AddRange(own.Select(r => r with { Points = r.Points.Select(p => p.Offset(dx, 0)).ToList() }));
        _sheet.Note(_view.Name(j.Component), "C14", $"a plain branch hangs as a column under the split, {members.Count} member(s), its merge {_view.Name(branch.Merge)} to stand under it");
        hangers.Add(new Hanger(_sheet.AnchorOf(previous.Component, previous.OutPort), previous, j.Component, branch.Merge, branch.MergePort, true, boxed));
        return _sheet.AnchorOf(j.Component, j.OutPort);
    }

    /// <summary>
    /// Whether a unit moved by (dx, dy) keeps its pipes and the form's pipes apart from each other's boxes (H2 with the
    /// pipes in it, P6.10 R5): none of its own runs through the clearance of a placed box or bubble, and none of the
    /// runs the form has laid so far through the clearance of its boxes or bubbles, a run that ends on it aside.
    /// </summary>
    private bool PipesClear(Unit unit, double dx, double dy, List<RunDraft> runs)
    {
        var mine = unit.Members.Where(i => !_view.IsInline(i))
            .SelectMany(i => _sheet.FootprintOf(i, _sheet.Transform[i], _sheet.Centre[i].Offset(dx, dy)).Boxes)
            .Select(b => b.Grow(_margin))
            .ToList();
        var theirs = new List<Box>();

        for (var i = 0; i < _view.Count; i++)
        {
            if (_sheet.Placed[i] && !_view.IsInline(i) && !unit.Members.Contains(i))
            {
                theirs.AddRange(_sheet.FootprintOf(i, _sheet.Transform[i], _sheet.Centre[i]).Boxes.Select(b => b.Grow(_margin)));
            }
        }

        // The sensors on pipes the form has drawn are part of what is placed -- a unit's box keeps a margin from their
        // bubbles as from a placed box's; the unit's own, of what it occupies.
        var bubbles = InlineBubbles(runs, 0, 0).ToList();

        if (mine.Any(m => bubbles.Any(m.Intersects)))
        {
            return false;
        }

        theirs.AddRange(bubbles.Select(b => b.Grow(_margin)));
        mine.AddRange(InlineBubbles(unit.Runs, dx, dy).Select(b => b.Grow(_margin)));

        foreach (var (_, points, _, _) in unit.Runs)
        {
            for (var s = 1; s < points.Count; s++)
            {
                var (a, b) = (points[s - 1].Offset(dx, dy), points[s].Offset(dx, dy));

                if (theirs.Any(o => Sheet.Passes(o, a, b)))
                {
                    return false;
                }
            }
        }

        foreach (var (_, points, _, _) in runs)
        {
            if (points.Count < 2 || mine.Any(o => o.ContainsInterior(points[0]) || o.ContainsInterior(points[^1])))
            {
                continue;
            }

            for (var s = 1; s < points.Count; s++)
            {
                if (mine.Any(o => Sheet.Passes(o, points[s - 1], points[s])))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The top of what a unit's instruments occupy, provisional units: its devices' bubbles (on their preferred free
    /// sides) and its own runs' sensor bubbles (<see cref="InlineBubbles"/>). Minus infinity when it carries none.
    /// </summary>
    private double BubbleTop(Unit unit)
    {
        var top = double.NegativeInfinity;

        foreach (var i in unit.Members)
        {
            foreach (var bubble in _sheet.FootprintOf(i, _sheet.Transform[i], _sheet.Centre[i]).Boxes.Skip(1))
            {
                top = Math.Max(top, bubble.Top);
            }
        }

        foreach (var bubble in InlineBubbles(unit.Runs, 0, 0))
        {
            top = Math.Max(top, bubble.Top);
        }

        return top;
    }

    /// <summary>
    /// Where the sensors on the inline points of runs a form has drawn will stand, before C15 chooses their sides
    /// (<c>D-151</c>): a bubble over a point where its run is level -- the side tried first -- and to the left of one
    /// where it is vertical, each one margin off the point. What placement keeps clear of, so the side C15 then tries
    /// first is free.
    /// </summary>
    /// <param name="runs">The runs, each from the member and port it leaves by, in provisional units.</param>
    /// <param name="dx">How far the runs are about to move right.</param>
    /// <param name="dy">How far the runs are about to move up.</param>
    private IEnumerable<Box> InlineBubbles(IEnumerable<RunDraft> runs, double dx, double dy)
    {
        var hats = _sheet.Hats();

        foreach (var (from, points, _, _) in runs)
        {
            if (_view.RunAt(from.Component, from.OutPort) is not { } run || run.Inline.Length == 0 || points.Count < 2)
            {
                continue;
            }

            var line = Sheet.Normalise(run.Start.Component == from.Component && run.Start.Port == from.OutPort ? points : [.. Enumerable.Reverse(points)]);

            if (line.Length < 2)
            {
                continue;
            }

            var pieces = Sheet.Pieces(line, run.Inline.Length);

            for (var t = 0; t < run.Inline.Length; t++)
            {
                if (!hats.TryGetValue(run.Inline[t].Element, out var on) || on.Count == 0)
                {
                    continue;
                }

                var at = pieces[t][^1].Offset(dx, dy);
                var level = Math.Abs(pieces[t][^2].Y - pieces[t][^1].Y) < Eps;
                var size = on.Max(static h => h.Size);
                var centre = level ? at.Towards(Direction.Up, _margin + (size / 2)) : at.Towards(Direction.Left, _margin + (size / 2));
                yield return Box.Around(centre, size, size);
            }
        }
    }

    /// <summary>
    /// The bottom rail's highest admissible height under the hanging branches: a margin under the lowest, and room for
    /// the node each returns to; a column's straight return as long as the bubbles on it need (<c>D-151</c>).
    /// </summary>
    private double Under(List<Hanger> hangers) =>
        hangers.Count == 0 ? double.MaxValue : hangers.Min(h =>
        {
            var half = Transform.Identity.Size(_view.Symbols[h.Bottom]).Height / 2;

            return h.Straight && _view.RunAt(h.OutFrom.Component, h.OutFrom.OutPort) is { } run
                ? h.Out.At.Y - _sheet.RunLength(run) - half
                : h.Out.At.Y - _margin - half - (_margin / 5);
        });

    /// <summary>The port a merge takes its parallel row by, while the row is being laid; null for any other member.</summary>
    private int? MergePortOf(int merge) => _parallelMerge.TryGetValue(merge, out var port) ? port : null;

    /// <summary>
    /// C14 for a branch that leaves a rail and rejoins it (a duty/standby pair, <c>C-130</c>): a row parallel to the
    /// spine a margin under its lowest box -- down from the split, rightwards along the row, the rise into the merge
    /// added by the rail -- laid on a canvas of its own, then lowered until it clears everything placed by the one test.
    /// Pumps side by side on branches between a common suction and a common discharge line, as HVAC schematics draw a
    /// pump set; the rows stacked under the rail is this project's mapping of that onto a level rail.
    /// </summary>
    /// <param name="j">The split, on the rail.</param>
    /// <param name="branch">The branch; plain, no loops.</param>
    /// <param name="members">Its boxed members in flow order.</param>
    /// <param name="spine">The rail's members from the split up to the merge, which the row stands under.</param>
    /// <returns>The row's last outlet, the member it leaves, and its runs; null when a member cannot be placed.</returns>
    private (PlacedAnchor End, Member Last, List<RunDraft> Runs)? Parallel(Member j, Branch branch, List<Member> members, List<int> spine)
    {
        _sheet.Side[(j.Component, branch.SplitPort)] = Direction.Down;
        var down = _sheet.AnchorOf(j.Component, branch.SplitPort);
        var bottom = spine.Min(i => _view.IsInline(i) ? _sheet.Centre[i].Y : _sheet.InnerOf(i).Y);
        var half = members.Count == 0 ? 0 : members.Max(m => Transform.Identity.Size(_view.Symbols[m.Component]).Height / 2);
        var yRow = Math.Min(bottom - _margin - half, down.At.Y - _margin);
        var cursor = Sheet.Anchor(new Point(down.At.X, yRow), Direction.Right, Direction.Right);
        var previous = new Member(j.Component, -1, branch.SplitPort);
        var pending = new List<Point> { down.At, cursor.At };
        var own = new List<RunDraft>();
        var placed = (bool[])_sheet.Placed.Clone();
        Array.Clear(_sheet.Placed);

        foreach (var m in members)
        {
            if (OnRail(cursor, m, m.InPort, m.OutPort) is not { } points)
            {
                placed.CopyTo(_sheet.Placed, 0);
                return null;
            }

            pending.AddRange(points.Skip(1));
            own.Add(new RunDraft(previous, pending, "C14", own.Count == 0 ? "the split's feed down to its parallel row" : "along the parallel row"));
            cursor = _sheet.AnchorOf(m.Component, m.OutPort);
            previous = m;
            pending = [cursor.At];
        }

        placed.CopyTo(_sheet.Placed, 0);
        var boxed = members.Select(static m => m.Component).ToList();
        var dy = 0.0;

        // Lowered, not slid: the split stands where the rail put it and the row hangs under it.
        for (var guard = 0; guard < 400 && !_sheet.ClearAt(boxed, 0, dy); guard++)
        {
            dy -= 0.1;
        }

        _sheet.Move(boxed, [], 0, dy);

        foreach (var i in boxed)
        {
            _sheet.Placed[i] = true;
        }

        // The feed's first point is the split's port, which does not move with the row.
        var shifted = own.Select((r, k) => r with { Points = [.. r.Points.Select((p, n) => k == 0 && n == 0 ? p : p.Offset(0, dy))] }).ToList();
        _rowBottom = Math.Min(_rowBottom, Math.Min(yRow + dy, boxed.Count == 0 ? double.MaxValue : boxed.Min(i => _sheet.InnerOf(i).Y)));
        _sheet.Note(_view.Name(j.Component), "C14", $"a branch rejoining the rail at {_view.Name(branch.Merge)} runs as a parallel row under it, {members.Count} member(s), at y {yRow + dy:0.##}");
        var end = members.Count == 0 ? Sheet.Anchor(new Point(down.At.X, yRow + dy), Direction.Right, Direction.Right) : _sheet.AnchorOf(previous.Component, previous.OutPort);
        return (end, previous, shifted);
    }

    /// <summary>Where the bottom rail must lie to keep a margin under the parallel rows (C14, <c>C-130</c>), its members' boxes included.</summary>
    private double RowFloor(List<Member> bottomMembers) =>
        _rowBottom == double.MaxValue
            ? double.MaxValue
            : _rowBottom - _margin - bottomMembers.Select(m => Transform.Identity.Size(_view.Symbols[m.Component])).Select(static s => Math.Max(s.Width, s.Height) / 2).DefaultIfEmpty(0).Max();

    // ---- closing a ring (C9, C10, C12) ----------------------------------------------------------------------------

    /// <summary>
    /// Closes a ring whose top rail is laid: the bottom rail rightwards from its start against the flow, the corner
    /// nodes (C10), the unit slid into the right side at the top rail's end, and the hanging branches' returns down
    /// into their merges. Appends the ring's remaining runs.
    /// </summary>
    private bool Close((PlacedAnchor End, List<Point> Pending, Member Previous) top, Unit unit, double drop, double yBottom, List<Member> bottomMembers, List<Point> bottomStart, Member? leftJunction, Member? rightJunction, List<Hanger> hangers, List<RunDraft> runs, (Member Member, Transform Transform)? leftTurner = null)
    {
        var (topEnd, topPending, topPrevious) = top;

        // The bottom rail, built left to right against the flow, each member placed by its outlet facing the left side.
        var cursor = Sheet.Anchor(bottomStart[^1], Direction.Right, Direction.Right);
        var pending = bottomStart;
        var bottomRuns = new List<RunDraft>();
        var first = bottomMembers.Count - 1;

        if (leftJunction is { } lj)
        {
            // The node nearest the left side takes the bottom-left corner: in from the rail, out up the left side; its free port is the block's outlet, facing out beside the inlet.
            _sheet.Place(lj.Component, Transform.Identity, bottomStart[^1], "C14", "the junction nearest the left side takes the bottom-left corner");
            _sheet.Side[(lj.Component, lj.OutPort)] = Direction.Up;
            _sheet.Side[(lj.Component, lj.InPort)] = Direction.Right;
            pending.RemoveAt(pending.Count - 1);
            pending.Add(_sheet.AnchorOf(lj.Component, lj.OutPort).At);
            bottomRuns.Add(new RunDraft(lj, pending, "C14", "the bottom-left junction up the left side"));
            cursor = _sheet.AnchorOf(lj.Component, lj.InPort);
            pending = [cursor.At];
            first--;
        }
        else if (leftTurner is { } lt)
        {
            // A member that turns the flow from leftward to upward takes the bottom-left corner (C9 mirrored): its inlet on the rail facing right, its outlet on the left side's line facing up.
            var dIn = _sheet.AnchorOffset(lt.Member.Component, lt.Member.InPort, lt.Transform)!.Value.Offset;
            var dOut = _sheet.AnchorOffset(lt.Member.Component, lt.Member.OutPort, lt.Transform)!.Value.Offset;
            var at = bottomStart[^1];
            _sheet.Place(lt.Member.Component, lt.Transform, new Point(at.X - dOut.X, at.Y - dIn.Y), "C9", "the left turner takes the bottom-left corner, mirrored (C-105)");
            pending.RemoveAt(pending.Count - 1);
            pending.Add(_sheet.AnchorOf(lt.Member.Component, lt.Member.OutPort).At);
            bottomRuns.Add(new RunDraft(lt.Member, pending, "C9", "the left turner up the left side"));
            cursor = _sheet.AnchorOf(lt.Member.Component, lt.Member.InPort);
            pending = [cursor.At];
        }

        // The first pass lays the bottom rail as if no column hung over it, so the floor a merge raises is where the rail
        // wants it -- not where a column hanging at its first, too-early place pushed the members before it.
        var columnMembers = _pass == 0 ? hangers.Where(static h => h.Straight).SelectMany(h => ColumnMembers(h)).ToList() : [];

        foreach (var c in columnMembers)
        {
            _sheet.Placed[c] = false;
        }

        for (var k = first; k >= 0; k--)
        {
            var m = bottomMembers[k];

            if (hangers.Find(h => h.Bottom == m.Component) is { } hanging)
            {
                // C14: the merge stands directly under the split -- as a loop's supply and return nodes align -- and a block's never nearer its outlet than a margin, so the return is one bend; the rail runs on to it.
                var half = Transform.Identity.Size(_view.Symbols[m.Component]).Width / 2;
                var under = hanging.Straight ? hanging.Out.At.X : Math.Min(_sheet.Centre[hanging.Top].X, hanging.Out.At.X - _margin);
                var length = _view.RunAt(m.Component, m.OutPort) is { } onward ? _sheet.RunLength(onward) : _margin;
                var x = under - half - length;

                if (x > cursor.At.X)
                {
                    cursor = Sheet.Anchor(new Point(x, cursor.At.Y), Direction.Right, Direction.Right);
                }
            }

            if (OnRail(cursor, m, m.OutPort, m.InPort) is not { } points)
            {
                return false;
            }

            pending.AddRange(points.Skip(1));
            bottomRuns.Add(new RunDraft(m, pending, "C2", "along the bottom rail"));
            cursor = _sheet.AnchorOf(m.Component, m.InPort);
            pending = [cursor.At];

            if (hangers.Find(h => h.Bottom == m.Component) is { } hanger)
            {
                // Where the rail could not put the merge under its split -- and, for a block, a margin short of its
                // outlet -- the next pass holds the split that much further right.
                var wanted = hanger.Straight ? hanger.Out.At.X : Math.Min(_sheet.Centre[hanger.Top].X, hanger.Out.At.X - _margin);
                var deficit = _sheet.Centre[m.Component].X - wanted;

                if (deficit > Eps)
                {
                    _hangFloor[hanger.Top] = Math.Max(_hangFloor.GetValueOrDefault(hanger.Top, double.NegativeInfinity), _sheet.Centre[hanger.Top].X + deficit);
                    _floorRaised = true;
                }
            }
        }

        foreach (var c in columnMembers)
        {
            _sheet.Placed[c] = true;
        }

        // The unit: as far right as the longer rail needs, its inlet level with the top rail's end (or a drop under it).
        var origin = Math.Max(topEnd.At.X, cursor.At.X);

        if (rightJunction is { } j0)
        {
            // The node moves under the unit's outlet (C10), so its rail box is not in the way: the unit needs only to put that outlet at or beyond the node's packed rail position.
            _sheet.Placed[j0.Component] = false;
            origin = Math.Max(topEnd.At.X, _sheet.Centre[j0.Component].X - (unit.Out.Along(_margin).X - unit.In.At.X) - _margin);
        }

        // An outlet facing left or down descends to the bottom rail one margin out -- to where the rail ends, which is
        // lower than it began where a standing member turned it down (C3, C-133); the descent must clear the boxes too,
        // the sensors on the pipes the form has drawn (D-151), and cross none of those pipes: a block hung between the
        // rails is passed, not cut through.
        var yRail = cursor.At.Y;
        var sensors = InlineBubbles(runs, 0, 0).Select(b => b.Grow(_margin)).ToList();
        var laid = runs.SelectMany(static r => r.Points.Zip(r.Points.Skip(1))).ToList();
        Func<double, double, bool>? clear = rightJunction is null && (unit.Out.Outward == Direction.Left || unit.Out.Outward == Direction.Down)
            ? (dx, dy) =>
            {
                var from = unit.Out.Along(_margin).Offset(dx, dy);
                var (low, high) = (new Point(from.X, yRail), from);
                return _sheet.Free(from.X, yRail, from.Y, unit.Members)
                    && !sensors.Any(o => Sheet.Passes(o, low, high))
                    && !laid.Any(s => Math.Abs(s.First.Y - s.Second.Y) < Eps && s.First.Y > low.Y + Eps && s.First.Y < high.Y - Eps
                        && Math.Min(s.First.X, s.Second.X) < from.X - Eps && Math.Max(s.First.X, s.Second.X) > from.X + Eps);
            }
            : null;
        var (uIn, uOut) = Slide(unit, origin, topEnd.At.Y - drop, runs, clear, topEnd.At.X, rightJunction is null ? cursor.At.X : null);

        if (rightJunction is { } j1)
        {
            _sheet.Placed[j1.Component] = true;
        }

        if (uIn.Outward == Direction.Up)
        {
            topPending.Add(new Point(uIn.At.X, topEnd.At.Y));
        }

        topPending.Add(uIn.At);
        runs.Add(new RunDraft(topPrevious, topPending, "C11", "the top rail into the unit"));
        var outOuter = uOut.Along(_margin);

        if (rightJunction is { } j)
        {
            // The node slides right along its rail until it clears the unit, under the outlet stub; the descent lands on it from above.
            var jx = outOuter.X;
            var jy = _sheet.Centre[j.Component].Y;

            while (_sheet.Clashes(j.Component, Transform.Identity, new Point(jx, jy)))
            {
                jx += 0.1;
            }

            _sheet.Centre[j.Component] = new Point(jx, jy);
            _sheet.Note(_view.Name(j.Component), "C14", $"the hanger's bottom junction slid right along its rail to ({jx:0.##}, {jy:0.##}), under the unit's outlet stub and clear of it");
            _sheet.Side[(j.Component, j.InPort)] = Direction.Up;
            var reaching = bottomRuns.FindIndex(r => r.From.Component == j.Component);
            bottomRuns[reaching].Points[^1] = _sheet.AnchorOf(j.Component, j.OutPort).At;
            pending = [_sheet.AnchorOf(j.Component, j.InPort).At, new Point(jx, uOut.At.Y), uOut.At];
        }
        else
        {
            pending.AddRange([new Point(outOuter.X, yRail), outOuter, uOut.At]);
        }

        bottomRuns.Add(new RunDraft(unit.OutFrom, pending, rightJunction is null ? "C11" : "C10", rightJunction is null ? "the unit's outlet down to the bottom rail" : "the unit's outlet down to the corner junction under it"));

        foreach (var draft in bottomRuns)
        {
            draft.Points.Reverse();
            runs.Add(draft);
        }

        // C14: each hanging branch returns from where it ends, level to above its merge and down into it.
        foreach (var h in hangers)
        {
            _sheet.Side[(h.Bottom, h.BottomPort)] = Direction.Up;
            var up = _sheet.AnchorOf(h.Bottom, h.BottomPort);
            runs.Add(new RunDraft(h.OutFrom, [h.Out.At, new Point(up.At.X, h.Out.At.Y), up.At], "C14", "the hanger's return, level then down into its merge"));
        }

        return true;
    }

    /// <summary>
    /// The nodes that take a ring's bottom corners (C10): on the right, the one next to the unit under an outlet that
    /// faces down or right -- unless a branch returns to it, which stands under its split instead; on the left, when
    /// the ring is a block whose outlet faces left, the one nearest the left side. One node takes one corner.
    /// </summary>
    private (Member? Left, Member? Right) Corners(List<Member> bottomMembers, Unit unit, bool leftFirst, List<Hanger>? hangers = null)
    {
        var left = leftFirst && bottomMembers.Count > 0 && _view.Wildcard(bottomMembers[^1].Component) ? bottomMembers[^1] : (Member?)null;
        var under = unit.Out.Outward == Direction.Down || unit.Out.Outward == Direction.Right;
        var right = under && bottomMembers.Count > (left is null ? 0 : 1) && _view.Wildcard(bottomMembers[0].Component) && hangers?.Exists(h => h.Bottom == bottomMembers[0].Component) != true
            ? bottomMembers[0]
            : (Member?)null;
        return (left, right);
    }

    /// <summary>One member as a ring's right side, placed with its inlet at the local origin: at the corner if it can turn the flow from leftward to downward (C9), else taking the flow from above, else entered and left on its left flank (<c>D-157</c>).</summary>
    private Unit? Single(Member m)
    {
        List<Transform> candidates;

        if (_view.Wildcard(m.Component))
        {
            candidates = [Transform.Identity];
        }
        else
        {
            bool Faces(Transform t, Direction inward) => _sheet.Outward(m.Component, m.InPort, t) == inward && _sheet.Outward(m.Component, m.OutPort, t) == Direction.Down;

            // Any arrangement the symbol offers may turn the corner, the default first (A9, D-112).
            var turning = _sheet.Admitted(m.Component).Where(t => Faces(t, Direction.Left)).ToList();
            candidates = turning.Count > 0 ? turning : _sheet.Admitted(m.Component).Where(t => Faces(t, Direction.Up)).ToList();

            if (candidates.Count == 0)
            {
                // A member entered and left on its left flank -- a tank taking its charging loop on its west side
                // (D-157): the top rail comes in level, the return leaves level from the outlet below.
                candidates = [.. _sheet.Admitted(m.Component).Where(t => _sheet.Outward(m.Component, m.InPort, t) == Direction.Left && _sheet.Outward(m.Component, m.OutPort, t) == Direction.Left)];
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var (_, h) = candidates[0].Size(_view.Symbols[m.Component]);
        var inOffset = _view.Wildcard(m.Component) ? new Point(0, h / 2) : _sheet.AnchorOffset(m.Component, m.InPort, candidates[0])!.Value.Offset;
        _sheet.Place(m.Component, candidates[0], new Point(-inOffset.X, -inOffset.Y), "C11", "a single member as the unit, its inlet at the origin, turned to face the top rail (A9)");

        if (_view.Wildcard(m.Component))
        {
            _sheet.Side[(m.Component, m.InPort)] = Direction.Up;
            _sheet.Side[(m.Component, m.OutPort)] = Direction.Down;
        }

        return new Unit([m.Component], _sheet.AnchorOf(m.Component, m.InPort), _sheet.AnchorOf(m.Component, m.OutPort), m, _sheet.InnerOf(m.Component).Y, []);
    }

    /// <summary>
    /// A loop laid out as its own clockwise ring at the local origin (C11): its consumer's unit on the right, the first
    /// member after it that turns the flow from upward to rightward at the top-left corner with the block's inlet
    /// facing out to the left, the members between them on the bottom rail and the rest on the top. The block's outlet
    /// -- its exit node's free port -- faces <paramref name="outlet"/>: left, beside the inlet, when the parent's return
    /// goes back that way; right when the parent continues on to the next member.
    /// </summary>
    /// <param name="loop">The loop.</param>
    /// <param name="entry">The member and port the outer path enters the block by.</param>
    /// <param name="exit">The member and port the outer path leaves the block by.</param>
    /// <param name="outlet">Which way the block's outlet faces.</param>
    /// <param name="deeper">How much lower than its own need the block's bottom rail lies, so that its outlet meets a taller neighbour's rail level (C12); zero for its own need.</param>
    /// <returns>The block as a unit, or null when no member can take the corner or the entry and exit do not face the ring.</returns>
    private Unit? Block(LoopStructure loop, Member entry, Member exit, Direction outlet, double deeper = 0)
    {
        if (InnerPath(loop) is not { } inner)
        {
            return null;
        }

        var path = inner.Members;
        var cornerAt = -1;
        var tc = Transform.Identity;

        for (var k = 1; k < path.Count && cornerAt < 0; k++)
        {
            var m = path[k];

            if (_view.Wildcard(m.Component))
            {
                continue;
            }

            var external = m.Component == entry.Component ? entry.InPort : -1;
            var turning = _sheet.Admitted(m.Component).Where(t => _sheet.Outward(m.Component, m.InPort, t) == Direction.Down && _sheet.Outward(m.Component, m.OutPort, t) == Direction.Right && (external < 0 || _sheet.Outward(m.Component, external, t) == Direction.Left)).ToList();

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
        var mark = _sheet.Groups.Count;

        // The block is laid out on its own (C11): nothing placed so far is an obstacle to it, and it comes back unplaced, to be slid in by its parent.
        var placed = (bool[])_sheet.Placed.Clone();
        Array.Clear(_sheet.Placed);

        Unit? Fail()
        {
            _sheet.Groups.RemoveRange(mark, _sheet.Groups.Count - mark);
            placed.CopyTo(_sheet.Placed, 0);
            return null;
        }

        var (innerEnd, unit) = UnitOf(inner, Direction.Left);

        if (unit is null || innerEnd >= cornerAt)
        {
            return Fail();
        }

        foreach (var i in unit.Members)
        {
            _sheet.Placed[i] = false;
        }

        var bottomMembers = path.GetRange(innerEnd + 1, cornerAt - innerEnd - 1);
        var items = path.GetRange(cornerAt + 1, path.Count - cornerAt - 1).Select(static m => new Item(m, null)).ToList();
        _sheet.Place(corner.Component, tc, new Point(0, 0), "C9", "the corner member at the origin, in the arrangement that turns the pipe");
        var cOut = _sheet.AnchorOf(corner.Component, corner.OutPort);
        var cIn = _sheet.AnchorOf(corner.Component, corner.InPort);
        var (left, right) = Corners(bottomMembers, unit, leftFirst: outlet == Direction.Left);
        var runs = new List<RunDraft>();
        var hangers = new List<Hanger>();

        if (Top(cOut, [cOut.At], corner, items, null, null, runs, hangers) is not { } top)
        {
            return Fail();
        }

        var natural = cIn.Along(_margin).Y - (left is { } l ? Transform.Identity.Size(_view.Symbols[l.Component]).Height / 2 : 0);
        var (drop, yBottom) = Bottom(unit, top.End.At.Y, natural, right?.Component ?? -1);

        if (deeper > Eps)
        {
            // C12: the bottom rail goes down by the caller's need, whatever set it; a unit hung from above keeps its outlet mid-side.
            yBottom -= deeper;
            drop += unit.In.Outward == Direction.Up ? deeper / 2 : 0;
        }

        if (!Close(top, unit, drop, yBottom, bottomMembers, [cIn.At, cIn.Along(_margin), new Point(cIn.At.X, yBottom)], left, right, hangers, runs))
        {
            return Fail();
        }

        if (_view.Wildcard(exit.Component))
        {
            // The outlet leaves by a side the node's other ports leave free, the parent's way first, then down.
            var used = new HashSet<Direction>();

            foreach (var p in _view.Connected(exit.Component))
            {
                if (p != exit.OutPort && _sheet.Side.TryGetValue((exit.Component, p), out var side))
                {
                    used.Add(side);
                }
            }

            Direction[] order = outlet == Direction.Left ? [Direction.Left, Direction.Down, Direction.Right] : [Direction.Right, Direction.Down, Direction.Left];
            _sheet.Side[(exit.Component, exit.OutPort)] = order.First(d => !used.Contains(d));
        }

        var members = path.Select(static m => m.Component).ToList();
        var entering = _sheet.AnchorOf(entry.Component, entry.InPort);
        var leaving = _sheet.AnchorOf(exit.Component, exit.OutPort);

        if (entering.Outward != Direction.Left || leaving.Outward == Direction.Up)
        {
            return Fail();
        }

        // The block is a group (A8), listed before the blocks it holds.
        _sheet.Groups.Insert(mark, (members, false));
        placed.CopyTo(_sheet.Placed, 0);

        return new Unit(members, entering, leaving, new Member(exit.Component, -1, exit.OutPort), members.Min(i => _sheet.InnerOf(i).Y), runs);
    }

    /// <summary>
    /// How far the unit's inlet drops below the rail it is fed from and where the bottom rail lies: level with an
    /// outlet that faces left; else low enough for the unit, for the stub out of it, and for a corner node under its
    /// outlet (C10). A unit entered from above with the other side the taller is centred on its side (C12).
    /// </summary>
    private (double Drop, double YBottom) Bottom(Unit unit, double yTop, double natural, int junction)
    {
        var drop = unit.In.Outward == Direction.Up ? _margin : 0;
        var half = junction < 0 ? 0 : Transform.Identity.Size(_view.Symbols[junction]).Height / 2;
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

    /// <summary>What the bubbles on the run into a unit's inlet ask of the level run to it (<c>D-151</c>); zero for an inlet facing away.</summary>
    private double InletReserve(Unit unit)
    {
        if (unit.In.Outward == Direction.Right)
        {
            return 0;
        }

        foreach (var i in unit.Members)
        {
            foreach (var p in _view.Connected(i))
            {
                if (_sheet.AnchorOffset(i, p, _sheet.Transform[i]) is { } anchor && _sheet.Centre[i].Offset(anchor.Offset.X, anchor.Offset.Y).ManhattanTo(unit.In.At) < Eps)
                {
                    return _view.RunAt(i, p) is { } run ? _sheet.RunLength(run) : 0;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Slides a unit from its provisional place into the layout: its inlet level with <paramref name="yIn"/>, one margin
    /// right of <paramref name="originX"/> and further right until every member clears what is placed (H2) -- jumping
    /// past each obstacle in whole tenths -- and, when <paramref name="clear"/> is given, by tenths until it holds for
    /// the offset. Its runs move with it.
    /// </summary>
    /// <returns>The unit's inlet and outlet where they landed.</returns>
    private (PlacedAnchor In, PlacedAnchor Out) Slide(Unit unit, double originX, double yIn, List<RunDraft> runs, Func<double, double, bool>? clear = null, double? feedFrom = null, double? returnTo = null)
    {
        // The runs into and out of the unit are laid long enough for any sensor's bubble on them (C15, D-151): the one in
        // counted from where it starts, the one out from where it returns to; the unit stands a margin clear of the
        // origin in any case.
        var dx = Math.Max(originX + _margin, (feedFrom ?? originX) + Math.Max(_margin, InletReserve(unit))) - Math.Min(unit.In.At.X, unit.Out.At.X);

        if (returnTo is { } back && _view.RunAt(unit.OutFrom.Component, unit.OutFrom.OutPort) is { } leaving)
        {
            dx = Math.Max(dx, back + Math.Max(_margin, _sheet.RunLength(leaving)) - unit.Out.At.X);
        }
        var dy = yIn - unit.In.At.Y;

        foreach (var i in unit.Members)
        {
            _sheet.Placed[i] = false;
        }

        while (true)
        {
            var shift = _sheet.Shift(unit.Members, dx, dy);

            if (shift > 0)
            {
                // The same lattice as stepping by tenths from the start, in one jump.
                dx += Math.Ceiling((shift * 10) - 1e-6) / 10;
                continue;
            }

            if ((clear is not null && !clear(dx, dy)) || !PipesClear(unit, dx, dy, runs))
            {
                dx += 0.1;
                continue;
            }

            break;
        }

        foreach (var i in unit.Members)
        {
            _sheet.Centre[i] = _sheet.Centre[i].Offset(dx, dy);
            _sheet.Placed[i] = true;
            _sheet.Note(_view.Name(i), "C11", $"slid in as a unit member by ({dx:0.##}, {dy:0.##}) to ({_sheet.Centre[i].X:0.##}, {_sheet.Centre[i].Y:0.##}), a margin right of its origin and past every obstacle (H2)");
        }

        foreach (var draft in unit.Runs)
        {
            runs.Add(draft with { Points = draft.Points.Select(p => p.Offset(dx, dy)).ToList() });
        }

        return (unit.In with { At = unit.In.At.Offset(dx, dy) }, unit.Out with { At = unit.Out.At.Offset(dx, dy) });
    }
}
