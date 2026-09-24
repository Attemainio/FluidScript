using System.Collections.Immutable;

using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Engine.Structures;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout.Engine;

/// <summary>
/// Composes each fragment from its plan (<c>28</c> E3): the form its plan names lays the body, the chain rules grow
/// everything else from what is placed, open ends are aligned, instruments take their sides, and the fragment is
/// stacked under the one before (C17).
/// </summary>
internal sealed partial class Composer
{
    private const double Eps = 1e-9;

    private readonly Sheet _sheet;
    private readonly CircuitView _view;
    private readonly double _margin;

    /// <summary>Creates a composer over a sheet.</summary>
    /// <param name="sheet">The sheet to compose on.</param>
    public Composer(Sheet sheet)
    {
        _sheet = sheet;
        _view = sheet.View;
        _margin = sheet.Margin;
        OnLoop = new bool[_view.Count];
    }

    /// <summary>Gets which elements a ring or block has placed on its loop (C6, C8).</summary>
    public bool[] OnLoop { get; }

    /// <summary>Gets the centre of the loop being laid, which a junction's free port faces away from (C8).</summary>
    public Point LoopCentre { get; private set; }

    /// <summary>C17: every fragment on a canvas of its own, then moved under the one before it, left edges aligned.</summary>
    /// <param name="plans">Each fragment's plan with its members, in script order.</param>
    public void Compose(IReadOnlyList<(FragmentPlan Plan, ImmutableArray<int> Members)> plans)
    {
        var left = double.NaN;
        var floor = double.NaN;

        for (var f = 0; f < plans.Count; f++)
        {
            var (plan, members) = plans[f];
            var fragmentRuns = _view.Runs.Where(r => members.Contains(r.Start.Component)).Select(static r => r.Index).ToList();

            using (_sheet.Canvas())
            {
                var drawn = Form(plan, members, $"fragment {f + 1}");
                GrowWithRings(plan, fragmentRuns, $"fragment {f + 1}", drawn);
                Stranded(members);
                AlignOpenEnds(members);
                _sheet.ChooseHats(_view.Ordered(members.Where(c => _sheet.Placed[c])));
            }

            var box = Extent(members, fragmentRuns);

            if (double.IsNaN(left))
            {
                left = box.X;
                floor = box.Y;
                continue;
            }

            var dx = left - box.X;
            var dy = floor - _margin - box.Top;
            _sheet.Move(members, fragmentRuns, dx, dy);
            floor = box.Y + dy;
        }
    }

    /// <summary>
    /// What no rule placed on a fragment's canvas -- a member the chain rules could not reach -- stands one margin under
    /// everything placed, in its drawn default, so it is still drawn and its runs are still routed with their stubs.
    /// </summary>
    private void Stranded(ImmutableArray<int> members)
    {
        foreach (var c in _view.Ordered(members.Where(c => !_view.IsInline(c) && !_sheet.Placed[c])))
        {
            var bottom = members.Where(i => _sheet.Placed[i] && !_view.IsInline(i)).Select(i => _sheet.InnerOf(i).Y).DefaultIfEmpty(0).Min();
            var (_, h) = Transform.Identity.Size(_view.Symbols[c]);
            _sheet.Place(c, Transform.Identity, new Point(0, bottom - _margin - (h / 2)), "E3", "reached by no rule; stood under its fragment");
            Grow(_view.Runs.Where(r => members.Contains(r.Start.Component)).Select(static r => r.Index).ToList());
        }
    }

    /// <summary>The box a fragment occupies: its elements' outer boxes, its runs' points and its instruments' bubbles.</summary>
    private Box Extent(ImmutableArray<int> members, List<int> runs)
    {
        var boxes = new List<Box>();

        foreach (var c in members.Where(c => _sheet.Stood[c]))
        {
            boxes.Add(_sheet.InnerOf(c).Grow(_view.IsInline(c) ? 0 : _margin));
        }

        foreach (var r in runs)
        {
            if (_sheet.RunPoints(r) is { } points)
            {
                boxes.AddRange(points.Select(static p => new Box(p.X, p.Y, 0, 0)));
            }
        }

        boxes.AddRange(_sheet.HatBoxes(members).Select(b => b.Grow(_margin)));
        return boxes.Aggregate(static (a, b) => a.Union(b));
    }

    // ---- the chain rules: C3, C4, C5, C6 --------------------------------------------------------------------------

    /// <summary>
    /// C4, C5 and A5: from every placed port the run is followed to the next boxed element, which is placed along the
    /// port's axis -- a node by C4, a component by C5 or C3, after C6's lead where the port is a loop member's flank --
    /// and the run is laid with its inline points cut into it. Runs are taken in the order of their first link, until
    /// nothing more can be placed.
    /// </summary>
    private void Grow(List<int> runs)
    {
        var order = runs.OrderBy(r => _view.Runs[r].Links.Min(static l => l.Link)).ToList();

        for (var changed = true; changed;)
        {
            changed = false;

            foreach (var r in order)
            {
                var run = _view.Runs[r];
                var startPlaced = _sheet.Placed[run.Start.Component];
                var endPlaced = _sheet.Placed[run.End.Component];

                if (startPlaced == endPlaced || _sheet.RunPoints(r) is not null)
                {
                    continue;
                }

                var (from, far) = startPlaced ? (run.Start, run.End) : (run.End, run.Start);

                if (_view.Wildcard(from.Component) && !_sheet.Side.ContainsKey((from.Component, from.Port)))
                {
                    _sheet.Side[(from.Component, from.Port)] = FreeSide(from.Component);
                }

                var anchor = _sheet.AnchorOf(from.Component, from.Port);
                var (start, lead) = Lead(from.Component, anchor);
                var length = _sheet.RunLength(run);
                var mark = _sheet.Trace.Count;
                ImmutableArray<Point>? points;
                List<Point> line;

                // A run carrying a sensor's point is laid long enough that the point's bubble, on the side C15 tries
                // first, clears what is placed -- boxes and the bubbles placed devices will carry (E3's room, D-161).
                for (var extra = 0.0; ; extra += 0.1)
                {
                    points = _view.Wildcard(far.Component) ? PlaceNode(start, far.Component, far.Port, length + extra) : PlaceFrom(start, far.Component, far.Port, length + extra);
                    line = points is { } placedPoints ? [.. lead, .. placedPoints] : [];

                    if (points is null || run.Inline.Length == 0 || extra > 6 - Eps || SensorRoom(new RunDraft(new Member(from.Component, -1, from.Port), line, string.Empty, string.Empty), far.Component))
                    {
                        break;
                    }

                    _sheet.Trace.RemoveRange(mark, _sheet.Trace.Count - mark);
                    _sheet.Placed[far.Component] = false;
                }

                if (points is null)
                {
                    continue;
                }

                // The run takes the rule that placed the member it grew to (C3, C4, C5), as the trace just named it.
                var grown = _view.Name(far.Component);
                var rule = _sheet.Trace.LastOrDefault(n => n.Subject == grown)?.Rule ?? "C1";
                var reason = $"grown from {_view.Name(from.Component)} to {grown}";

                if (startPlaced)
                {
                    _sheet.Lay(run, line, rule, reason);
                }
                else
                {
                    _sheet.LayBackwards(run, line, rule, reason);
                }

                changed = true;
            }
        }
    }

    /// <summary>Whether the bubbles on a drafted run's sensor points keep a margin from every placed box and every bubble a placed element carries or will carry.</summary>
    private bool SensorRoom(RunDraft draft, int far)
    {
        var bubbles = InlineBubbles([draft], 0, 0).Select(b => b.Grow(_margin)).ToList();

        for (var i = 0; i < _view.Count && bubbles.Count > 0; i++)
        {
            if (!_sheet.Placed[i] || _view.IsInline(i))
            {
                continue;
            }

            if (_sheet.FootprintOf(i, _sheet.Transform[i], _sheet.Centre[i]).Boxes.Concat(_sheet.HatBoxes([i])).Any(o => bubbles.Any(o.Intersects)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>C4: a node on the placed port's axis, one run length out or as far as H2 needs.</summary>
    /// <returns>The pipe from the placed port to the node.</returns>
    private ImmutableArray<Point> PlaceNode(PlacedAnchor anchor, int j, int q, double length)
    {
        var d = anchor.Outward;
        var (w, h) = Transform.Identity.Size(_view.Symbols[j]);
        var half = d.Horizontal ? w / 2 : h / 2;
        var delta = new Point(d.X * half, d.Y * half);
        var at = _sheet.Clear(j, Transform.Identity, anchor.At, d, delta, length);
        _sheet.Place(j, Transform.Identity, at.Offset(delta.X, delta.Y), "C4", $"a node one clearance out on the placed port's axis, {Sheet.Name(d)}");
        _sheet.Side[(j, q)] = d.Opposite;
        return [anchor.At, at];
    }

    /// <summary>
    /// C5: a component placed off a placed port -- in the first admitted transform of its default arrangement whose port
    /// faces the pipe, straight along the axis, the one sending its outlet on to the right first (H10); else, for a
    /// kind that cannot face the pipe, below or beside a single turn at the port's outer anchor (C3); else in an
    /// alternative arrangement that faces the pipe.
    /// </summary>
    /// <param name="anchor">The placed port the pipe leaves.</param>
    /// <param name="j">The component.</param>
    /// <param name="q">Its port facing the pipe.</param>
    /// <param name="length">The pipe's least length, world units.</param>
    /// <param name="upright">Whether a level kind (a pump, C13) may stand in a vertical pipe -- a column's riser (<c>D-159</c>).</param>
    /// <returns>The pipe from the placed port to the component's port, or null when no admitted transform fits.</returns>
    private ImmutableArray<Point>? PlaceFrom(PlacedAnchor anchor, int j, int q, double length, bool upright = false)
    {
        var d = anchor.Outward;
        var admitted = _sheet.Admitted(j).ToList();
        var level = !upright && _view.Symbols[j].TransformClass == "level";
        var facing = admitted.Where(t => t.Arrangement == "default" && _sheet.AnchorOffset(j, q, t) is { } a && a.Outward == d.Opposite && (!level || t.Rotation is 0 or 180)).OrderBy(t => _sheet.Onward(j, q, t)).ToList();

        if (facing.Count == 0)
        {
            // C3: the pipe turns into a member that cannot face it -- down into a standing kind from a level pipe, rightwards into a level kind from a vertical one.
            var along = d == Direction.Left || d == Direction.Right ? Direction.Down : Direction.Right;
            var turned = admitted.Where(t => t.Arrangement == "default" && _sheet.AnchorOffset(j, q, t) is { } a && a.Outward == along.Opposite).ToList();

            if (turned.Count > 0)
            {
                var turn = _sheet.AnchorOffset(j, q, turned[0])!.Value.Offset;
                var corner = anchor.At.Towards(d, _margin);
                var inner = _sheet.Clear(j, turned[0], corner, along, new Point(-turn.X, -turn.Y), length);
                _sheet.Place(j, turned[0], inner.Offset(-turn.X, -turn.Y), "C3", $"the pipe turns {Sheet.Name(along)} into a member that cannot face it");
                return [anchor.At, corner, inner];
            }

            facing = admitted.Where(t => _sheet.AnchorOffset(j, q, t) is { } a && a.Outward == d.Opposite).OrderBy(t => _sheet.Onward(j, q, t)).ToList();
        }

        if (facing.Count == 0)
        {
            return null;
        }

        var offset = _sheet.AnchorOffset(j, q, facing[0])!.Value.Offset;
        var end = _sheet.Clear(j, facing[0], anchor.At, d, new Point(-offset.X, -offset.Y), length);
        _sheet.Place(j, facing[0], end.Offset(-offset.X, -offset.Y), "C5", $"facing the pipe arriving {Sheet.Name(d)}, straight along the axis, the arrangement sending its outlet on to the right first (H10)");
        return [anchor.At, end];
    }

    /// <summary>
    /// C6: what hangs from a loop member's flank port -- a standing exchanger's second side -- leaves along two straight
    /// margins and turns away from the loop on that flank's side (<c>C-86</c>). Off a chain's exchanger a port's
    /// continuation hangs straight.
    /// </summary>
    /// <returns>The anchor to place from, and the pipe up to it.</returns>
    private (PlacedAnchor Start, ImmutableArray<Point> Lead) Lead(int i, PlacedAnchor anchor)
    {
        if (!OnLoop[i] || _view.Graph.Components[i] is not HeatExchangerComponent || anchor.Outward.Horizontal)
        {
            return (anchor, []);
        }

        var flank = anchor.At.X < _sheet.Centre[i].X - Eps ? Direction.Left : Direction.Right;
        var corner = anchor.Along(2 * _margin);
        return (Sheet.Anchor(corner, flank, flank), [anchor.At]);
    }

    /// <summary>A node's side for a port no rule has placed: one no other port uses, the side facing away from the loop's centre first for a loop member -- vertical where the rail is level, level where it is vertical -- else the first free from right, up, left, down (C8).</summary>
    private Direction FreeSide(int i)
    {
        var used = new HashSet<Direction>();

        foreach (var p in _view.Connected(i))
        {
            if (_sheet.Side.TryGetValue((i, p), out var s))
            {
                used.Add(s);
            }
        }

        var preferred = new List<Direction>();

        if (OnLoop[i])
        {
            var away = _sheet.Centre[i].Offset(-LoopCentre.X, -LoopCentre.Y);
            var level = used.Contains(Direction.Left) || used.Contains(Direction.Right);
            var vertical = used.Contains(Direction.Up) || used.Contains(Direction.Down);
            var levelAway = away.X >= 0 ? Direction.Right : Direction.Left;
            var verticalAway = away.Y >= 0 ? Direction.Up : Direction.Down;
            preferred.AddRange(level && !vertical ? [verticalAway, levelAway] : [levelAway, verticalAway]);
        }

        preferred.AddRange([Direction.Right, Direction.Up, Direction.Left, Direction.Down]);
        return preferred.First(d => !used.Contains(d));
    }

    // ---- C7: open ends align ----------------------------------------------------------------------------------------

    /// <summary>
    /// C7: an open end -- a node with one connection -- hanging level off the fragment is moved out to the line of the
    /// farthest open end on the same side with the same root, when nothing placed lies in the way, and its run is laid
    /// again over the longer pipe; declared boundaries of one fragment share one root.
    /// </summary>
    private void AlignOpenEnds(ImmutableArray<int> members)
    {
        var ends = new List<(int Node, Run Run, Direction Approach, int Root)>();

        foreach (var b in members)
        {
            if (!_sheet.Placed[b] || _view.IsInline(b) || !_view.Wildcard(b) || _view.Degree(b) != 1)
            {
                continue;
            }

            var port = _view.Connected(b).First();

            if (_view.RunAt(b, port) is not { } run || _sheet.RunPoints(run.Index) is not { } line || line.Length < 2)
            {
                continue;
            }

            var atStart = run.Start.Component == b;
            var (end, before) = atStart ? (line[0], line[1]) : (line[^1], line[^2]);
            var host = atStart ? run.End : run.Start;

            if (Direction.Of(end.Offset(-before.X, -before.Y)) is { Horizontal: true } approach)
            {
                var root = _view.IsBoundary(b) ? -3 : Root(host);
                ends.Add((b, run, approach, root));
            }
        }

        for (var x = 0; x < ends.Count; x++)
        {
            for (var y = x + 1; y < ends.Count; y++)
            {
                var (a, b) = (ends[x], ends[y]);

                if (a.Root != b.Root || a.Approach != b.Approach)
                {
                    continue;
                }

                var d = a.Approach;
                var (near, far) = (_sheet.Centre[a.Node].X * d.X) < (_sheet.Centre[b.Node].X * d.X) ? (a, b) : (b, a);
                var target = new Point(_sheet.Centre[far.Node].X, _sheet.Centre[near.Node].Y);

                if (Math.Abs(target.X - _sheet.Centre[near.Node].X) < Eps || _sheet.Clashes(near.Node, Transform.Identity, target) || _sheet.Obstructed(_sheet.Centre[near.Node], target, near.Node))
                {
                    continue;
                }

                _sheet.Centre[near.Node] = target;
                _sheet.Note(_view.Name(near.Node), "C7", $"an open end aligned to ({target.X:0.##}, {target.Y:0.##}) with its counterpart");
                var line = _sheet.RunPoints(near.Run.Index)!.Value.ToList();
                var atStart = near.Run.Start.Component == near.Node;
                var tip = target.Towards(d.Opposite, _sheet.InnerOf(near.Node).Width / 2);

                if (atStart)
                {
                    line[0] = tip;
                }
                else
                {
                    line[^1] = tip;
                }

                _sheet.Lay(near.Run, line, "C7", "the open end's run, re-ended at its aligned place");
            }
        }
    }

    /// <summary>The element an open end's chain hangs from: its run followed through two-port elements to a loop member (every loop member is one root), a node, or the chain's far end.</summary>
    private int Root(RunEnd host)
    {
        var (m, q) = (host.Component, host.Port);

        for (var guard = 0; guard <= _view.Count && !OnLoop[m] && !_view.Wildcard(m); guard++)
        {
            var ports = _view.Connected(m).ToList();

            if (ports.Count != 2)
            {
                break;
            }

            var other = ports.First(p => p != q);

            if (_view.RunAt(m, other)?.From(m, other) is not { } next)
            {
                break;
            }

            (m, q) = (next.Component, next.Port);
        }

        return OnLoop[m] ? -2 : m;
    }
}
