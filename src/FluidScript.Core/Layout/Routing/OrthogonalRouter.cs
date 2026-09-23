using System.Collections.Immutable;

using FluidScript.Core.Layout.Drawing;

namespace FluidScript.Core.Layout.Routing;

/// <summary>
/// Routes one connection at a time through the boxes already placed and the pipes already drawn, on a
/// Hanan grid of their outer edges (<c>53</c>'s routing objective, <c>D-105</c>'s clearance).
/// </summary>
/// <remarks>
/// <para>
/// Every element keeps the same clearance: a route's centreline stays a margin from every inner box
/// and from every pipe drawn before it, and may cross a pipe only at right angles, which is counted
/// and reported as a hop. The candidate coordinates are the edges of the outer boxes and of the pipe
/// bands inside a window round the two ends, so the cheapest legal channel -- the one exactly a margin
/// from the boxes -- is always among the candidates. Cost is Manhattan length plus a bend penalty
/// plus a crossing penalty, searched by Dijkstra over (point, heading) so bends are counted.
/// </para>
/// <para>
/// The stub -- a margin straight out of the port, inner boundary to outer boundary -- is inside the owner's
/// own outer box, which is the one box a route may run inside; a route turns only from the outer boundary on.
/// A route that finds no legal path widens its window twice and
/// then takes the straight join, drawn beneath whatever it crosses (<c>53</c>'s error case).
/// </para>
/// <para>
/// A signal (<paramref name="signal"/>, C16, <c>D-152</c>) keeps none of that clearance: only a box's inner outline stops
/// it, and a margin is a small cost per unit of length run inside it. It never runs along a pipe, and crosses one only a
/// quarter margin or more from the pipe's ends -- a port, a junction, an inline point, a bend. Bends cost most, then
/// length; crossings and time in a margin only break ties, so a line takes the short way through the drawing rather than
/// round it. Its stub is half a margin, and the grid adds the inner edges and the midpoints between its lines.
/// </para>
/// </remarks>
/// <param name="margin">The clearance, world units.</param>
/// <param name="signal">Whether the routes are signal lines, which cross the drawing rather than keep its clearance.</param>
internal sealed class OrthogonalRouter(double margin, bool signal = false)
{
    private const double Eps = 1e-9;
    private readonly double _bendCost = signal ? 2.0 : 1.0;
    private readonly double _crossingCost = signal ? 0.25 : 5.0;
    private const double MarginCost = 0.25;
    private readonly double _stub = signal ? margin / 2 : margin;

    private readonly List<(Box Inner, Box Outer, int Owner)> _boxes = [];
    private readonly List<Pipe> _pipes = [];

    /// <summary>A drawn segment and its band; a port's stub carries its owner and port so its own route may use it.</summary>
    private readonly record struct Pipe(bool Vertical, double Line, double From, double To, int Route, int Owner = -1, int Port = -1, int OwnerB = -1);

    private readonly Dictionary<int, Box> _outerOf = [];

    /// <summary>
    /// Reserves a port's stub before its route is drawn: the pipe straight out of the port to its lane, which
    /// every other route keeps a margin from and may only cross, so an earlier route cannot wrap round a
    /// port and leave its own route nowhere to start.
    /// </summary>
    /// <param name="anchor">The port's anchor.</param>
    /// <param name="length">How far out the stub reaches: to the lane the compaction reserved for it.</param>
    /// <param name="owner">The component the port belongs to.</param>
    /// <param name="port">The port's index on that component.</param>
    public void AddStub(PlacedAnchor anchor, double length, int owner, int port)
    {
        var end = anchor.Along(length);
        var vertical = Math.Abs(anchor.Direction.X) < 0.5;
        _pipes.Add(vertical
            ? new Pipe(true, anchor.At.X, Math.Min(anchor.At.Y, end.Y), Math.Max(anchor.At.Y, end.Y), -1, owner, port)
            : new Pipe(false, anchor.At.Y, Math.Min(anchor.At.X, end.X), Math.Max(anchor.At.X, end.X), -1, owner, port));
    }

    /// <summary>Frees a port's reserved stub: its own route is about to be drawn and is the one thing allowed there.</summary>
    /// <param name="owner">The component the port belongs to.</param>
    /// <param name="port">The port's index on that component.</param>
    public void ReleaseStub(int owner, int port) => _pipes.RemoveAll(p => p.Owner == owner && p.Port == port);

    /// <summary>The result of one search: the polyline from anchor to anchor and where it hops over earlier pipes.</summary>
    public sealed record Result(ImmutableArray<Point> Points, ImmutableArray<Point> Hops, PlacedAnchor From, PlacedAnchor To, bool Clean);

    /// <summary>Registers a placed component's inner box; its outer box is the clearance every other route keeps.</summary>
    public void AddBox(Box inner, int owner)
    {
        var outer = inner.Grow(margin);
        _boxes.Add((inner, outer, owner));
        _outerOf[owner] = outer;
    }

    /// <summary>Registers a drawn segment so later routes keep a margin from it and hop over it.</summary>
    /// <param name="a">One end.</param>
    /// <param name="b">The other end.</param>
    /// <param name="route">The run the segment belongs to.</param>
    /// <param name="ownerA">The component the run leaves; its other routes may run beside this one at port pitch within its clearance.</param>
    /// <param name="ownerB">The component the run arrives at.</param>
    public void AddPipe(Point a, Point b, int route, int ownerA, int ownerB)
    {
        if (a.ManhattanTo(b) < Eps)
        {
            return;
        }

        var vertical = Math.Abs(a.X - b.X) < Eps;
        _pipes.Add(vertical
            ? new Pipe(true, a.X, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y), route, ownerA, -1, ownerB)
            : new Pipe(false, a.Y, Math.Min(a.X, b.X), Math.Max(a.X, b.X), route, ownerA, -1, ownerB));
    }

    /// <summary>Routes between any of the start anchors and any of the end anchors, picking the cheapest pair.</summary>
    /// <param name="from">The anchors the route may leave from; one for a named port, up to four for a junction.</param>
    /// <param name="fromOwner">The component the route leaves; its outer box is not an obstacle.</param>
    /// <param name="to">The anchors the route may arrive at.</param>
    /// <param name="toOwner">The component the route arrives at.</param>
    /// <returns>The route, or <see langword="null"/> when there is no anchor pair at all.</returns>
    public Result? Route(IReadOnlyList<PlacedAnchor> from, int fromOwner, IReadOnlyList<PlacedAnchor> to, int toOwner)
    {
        if (from.Count == 0 || to.Count == 0)
        {
            return null;
        }

        // Two ports facing each other on one line are joined straight, whatever the stubs: the pipe between two
        // symbols a margin apart is both their stubs at once.
        foreach (var start in from)
        {
            foreach (var end in to)
            {
                if (Facing(start, end) && Clear(start.At, end.At, fromOwner, toOwner))
                {
                    return new Result([start.At, end.At], [.. HopsOn(_pipes, start.At, end.At)], start, end, true);
                }
            }
        }

        var starts = Usable(from.Select(anchor => (Anchor: anchor, Stub: anchor.Along(_stub))).ToList(), _pipes, fromOwner, toOwner);
        var ends = Usable(to.Select(anchor => (Anchor: anchor, Stub: anchor.Along(_stub))).ToList(), _pipes, fromOwner, toOwner);

        foreach (var window in new[] { 1.0, 3.0, 8.0, double.PositiveInfinity })
        {
            if (Search(starts, fromOwner, ends, toOwner, window) is { } found)
            {
                return found;
            }
        }


        // Nothing legal: the straight join, drawn beneath whatever it crosses.
        var a = from[0];
        var b = to[0];
        var p = a.Along(_stub);
        var q = b.Along(_stub);
        var elbow = new Point(q.X, p.Y);
        return new Result(Simplify([a.At, p, elbow, q, b.At]), [], a, b, false);
    }

    private Result? Search(List<(PlacedAnchor Anchor, Point Stub)> starts, int fromOwner, List<(PlacedAnchor Anchor, Point Stub)> ends, int toOwner, double window)
    {
        var ends_ = ends.Select(static e => e.Stub).Concat(starts.Select(static s => s.Stub)).ToList();
        var minX = ends_.Min(static p => p.X) - window;
        var maxX = ends_.Max(static p => p.X) + window;
        var minY = ends_.Min(static p => p.Y) - window;
        var maxY = ends_.Max(static p => p.Y) + window;

        var xs = new SortedSet<double>();
        var ys = new SortedSet<double>();

        foreach (var p in ends_)
        {
            xs.Add(Math.Round(p.X, 9));
            ys.Add(Math.Round(p.Y, 9));
        }

        var boxes = new List<(Box Inner, Box Outer, int Owner)>();
        foreach (var box in _boxes)
        {
            if (box.Outer.Right < minX || box.Outer.X > maxX || box.Outer.Top < minY || box.Outer.Y > maxY)
            {
                continue;
            }

            boxes.Add(box);
            AddCoordinate(xs, box.Outer.X, minX, maxX);
            AddCoordinate(xs, box.Outer.Right, minX, maxX);
            AddCoordinate(ys, box.Outer.Y, minY, maxY);
            AddCoordinate(ys, box.Outer.Top, minY, maxY);

            if (signal)
            {
                AddCoordinate(xs, box.Inner.X, minX, maxX);
                AddCoordinate(xs, box.Inner.Right, minX, maxX);
                AddCoordinate(ys, box.Inner.Y, minY, maxY);
                AddCoordinate(ys, box.Inner.Top, minY, maxY);
            }
        }

        var pipes = new List<Pipe>();
        foreach (var pipe in _pipes)
        {
            var inside = pipe.Vertical
                ? pipe.Line + margin >= minX && pipe.Line - margin <= maxX && pipe.To >= minY && pipe.From <= maxY
                : pipe.Line + margin >= minY && pipe.Line - margin <= maxY && pipe.To >= minX && pipe.From <= maxX;

            if (!inside)
            {
                continue;
            }

            pipes.Add(pipe);
            var axis = pipe.Vertical ? xs : ys;
            var (lo, hi) = pipe.Vertical ? (minX, maxX) : (minY, maxY);
            AddCoordinate(axis, pipe.Line - margin, lo, hi);
            AddCoordinate(axis, pipe.Line + margin, lo, hi);
        }

        if (signal)
        {
            // A signal may take the middle of any gap, where it keeps furthest from both sides.
            Midpoints(xs);
            Midpoints(ys);
        }

        var xv = xs.ToArray();
        var yv = ys.ToArray();
        var nx = xv.Length;
        var ny = yv.Length;

        // Per grid line: which unit steps are blocked, and how many earlier pipes each crosses. Each line first
        // collects the few boxes and pipes that reach it, then sweeps its steps against those alone.
        var blockedH = new bool[ny, Math.Max(nx - 1, 0)];
        var crossH = new double[ny, Math.Max(nx - 1, 0)];
        var blockedV = new bool[nx, Math.Max(ny - 1, 0)];
        var crossV = new double[nx, Math.Max(ny - 1, 0)];
        var intervals = new List<(double From, double To)>();
        var crossings = new List<double>();
        var soft = new List<(double From, double To)>();

        for (var j = 0; j < ny; j++)
        {
            var y = yv[j];
            Reaching(boxes, pipes, fromOwner, toOwner, false, y, intervals, crossings, soft);

            for (var i = 0; i + 1 < nx; i++)
            {
                var (blocked, count) = Step(intervals, crossings, xv[i], xv[i + 1]);
                blockedH[j, i] = blocked;
                crossH[j, i] = (count * _crossingCost) + (Inside(soft, xv[i], xv[i + 1]) * MarginCost);
            }
        }

        for (var i = 0; i < nx; i++)
        {
            var x = xv[i];
            Reaching(boxes, pipes, fromOwner, toOwner, true, x, intervals, crossings, soft);

            for (var j = 0; j + 1 < ny; j++)
            {
                var (blocked, count) = Step(intervals, crossings, yv[j], yv[j + 1]);
                blockedV[i, j] = blocked;
                crossV[i, j] = (count * _crossingCost) + (Inside(soft, yv[j], yv[j + 1]) * MarginCost);
            }
        }

        // Dijkstra over (x index, y index, heading); heading 0 east, 1 south, 2 west, 3 north.
        var states = nx * ny * 4;
        var best = new double[states];
        var previous = new int[states];
        Array.Fill(best, double.PositiveInfinity);
        Array.Fill(previous, -1);
        var queue = new PriorityQueue<int, double>();
        var startOf = new Dictionary<int, int>();
        var endOf = new Dictionary<int, int>();

        for (var s = 0; s < starts.Count; s++)
        {
            var node = NodeOf(xv, yv, starts[s].Stub);
            if (node < 0)
            {
                continue;
            }

            var state = (node * 4) + Heading(starts[s].Anchor.Direction);
            best[state] = 0;
            startOf[state] = s;
            queue.Enqueue(state, 0);
        }

        for (var e = 0; e < ends.Count; e++)
        {
            var node = NodeOf(xv, yv, ends[e].Stub);
            if (node >= 0)
            {
                endOf[node] = e;
            }
        }

        var goal = -1;
        var goalCost = double.PositiveInfinity;

        while (queue.TryDequeue(out var state, out var cost))
        {
            if (cost > best[state] + Eps || cost >= goalCost)
            {
                continue;
            }

            var node = state / 4;
            var heading = state % 4;
            var i = node % nx;
            var j = node / nx;

            if (endOf.TryGetValue(node, out var e))
            {
                // Arriving: the last segment runs into the port against its outward direction.
                var arrival = Heading(new Point(-ends[e].Anchor.Direction.X, -ends[e].Anchor.Direction.Y));

                if (heading == (arrival + 2) % 4)
                {
                    continue;
                }

                var total = cost + (arrival == heading ? 0 : _bendCost);

                if (total < goalCost)
                {
                    goalCost = total;
                    goal = state;
                }

                continue;
            }

            for (var h = 0; h < 4; h++)
            {
                var (ni, nj, step, crossing) = h switch
                {
                    0 when i + 1 < nx => (i + 1, j, xv[i + 1] - xv[i], blockedH[j, i] ? double.NaN : crossH[j, i]),
                    2 when i > 0 => (i - 1, j, xv[i] - xv[i - 1], blockedH[j, i - 1] ? double.NaN : crossH[j, i - 1]),
                    1 when j + 1 < ny => (i, j + 1, yv[j + 1] - yv[j], blockedV[i, j] ? double.NaN : crossV[i, j]),
                    3 when j > 0 => (i, j - 1, yv[j] - yv[j - 1], blockedV[i, j - 1] ? double.NaN : crossV[i, j - 1]),
                    _ => (-1, -1, 0.0, double.NaN),
                };

                // No doubling back: a pipe never reverses onto itself, and at the far end it cannot arrive along
                // its own stub from the wrong side.
                if (ni < 0 || double.IsNaN(crossing) || h == (heading + 2) % 4)
                {
                    continue;
                }

                var next = ((ni + (nj * nx)) * 4) + h;
                var candidate = cost + step + (h == heading ? 0 : _bendCost) + crossing;

                if (candidate < best[next] - Eps)
                {
                    best[next] = candidate;
                    previous[next] = state;
                    queue.Enqueue(next, candidate);
                }
            }
        }

        if (goal < 0)
        {

            return null;
        }

        // Walk back to the start, then assemble anchor, stub, grid points, stub, anchor.
        var chain = new List<int>();
        for (var state = goal; state >= 0; state = previous[state])
        {
            chain.Add(state);
        }

        chain.Reverse();
        var start = starts[startOf[chain[0]]];
        var end = ends[endOf[goal / 4]];
        var points = new List<Point> { start.Anchor.At };
        var hops = ImmutableArray.CreateBuilder<Point>();

        foreach (var state in chain)
        {
            var node = state / 4;
            points.Add(new Point(xv[node % nx], yv[node / nx]));
        }

        points.Add(end.Anchor.At);

        for (var k = 1; k < points.Count; k++)
        {
            foreach (var hop in HopsOn(pipes, points[k - 1], points[k]))
            {
                hops.Add(hop);
            }
        }




        return new Result(Simplify([.. points]), hops.ToImmutable(), start.Anchor, end.Anchor, true);
    }

    /// <summary>What reaches one grid line: the intervals along it a step may not enter (a box's interior, an owner's inner box, a parallel pipe's band) and the positions where a perpendicular pipe crosses it.</summary>
    /// <remarks>For a signal, <paramref name="soft"/> collects the margins it may enter at a cost, and a pipe blocks only where the line runs along it or would cross it within a quarter margin of its ends.</remarks>
    private void Reaching(List<(Box Inner, Box Outer, int Owner)> boxes, List<Pipe> pipes, int fromOwner, int toOwner, bool vertical, double line, List<(double From, double To)> intervals, List<double> crossings, List<(double From, double To)>? soft = null)
    {
        intervals.Clear();
        crossings.Clear();
        soft?.Clear();

        if (signal)
        {
            SignalReaching(boxes, pipes, fromOwner, toOwner, vertical, line, intervals, crossings, soft);
            return;
        }

        foreach (var (inner, outer, owner) in boxes)
        {
            var box = owner == fromOwner || owner == toOwner ? inner : outer;

            if (vertical ? line > box.X + Eps && line < box.Right - Eps : line > box.Y + Eps && line < box.Top - Eps)
            {
                intervals.Add(vertical ? (box.Y, box.Top) : (box.X, box.Right));
            }
        }

        foreach (var pipe in pipes)
        {
            if (pipe.Vertical == vertical)
            {
                if (Math.Abs(line - pipe.Line) < margin - Eps)
                {
                    if (OwnZone(pipe, fromOwner, toOwner) is { } zone)
                    {
                        // Beside the owners' own pipes only outside the zone where the port pitch rules.
                        if (pipe.From < zone.From - Eps)
                        {
                            intervals.Add((pipe.From, Math.Min(pipe.To, zone.From)));
                        }

                        if (pipe.To > zone.To + Eps)
                        {
                            intervals.Add((Math.Max(pipe.From, zone.To), pipe.To));
                        }
                    }
                    else
                    {
                        intervals.Add((pipe.From, pipe.To));
                    }
                }
            }
            else if (line > pipe.From + Eps && line < pipe.To - Eps)
            {
                crossings.Add(pipe.Line);
            }
        }

        crossings.Sort();
    }

    /// <summary><see cref="Reaching"/> for a signal: inner boxes block, margins cost, and a pipe blocks the line only along itself or within a quarter margin of its ends.</summary>
    private void SignalReaching(List<(Box Inner, Box Outer, int Owner)> boxes, List<Pipe> pipes, int fromOwner, int toOwner, bool vertical, double line, List<(double From, double To)> intervals, List<double> crossings, List<(double From, double To)>? soft)
    {
        var quarter = margin / 4;

        foreach (var (inner, outer, owner) in boxes)
        {
            if (vertical ? line > inner.X + Eps && line < inner.Right - Eps : line > inner.Y + Eps && line < inner.Top - Eps)
            {
                intervals.Add(vertical ? (inner.Y, inner.Top) : (inner.X, inner.Right));
            }

            // The margins of the two ends are the line's own way out and in.
            if (soft is not null && owner != fromOwner && owner != toOwner
                && (vertical ? line > outer.X + Eps && line < outer.Right - Eps : line > outer.Y + Eps && line < outer.Top - Eps))
            {
                soft.Add(vertical ? (outer.Y, outer.Top) : (outer.X, outer.Right));
            }
        }

        foreach (var pipe in pipes)
        {
            if (pipe.Vertical == vertical)
            {
                if (Math.Abs(line - pipe.Line) < Eps)
                {
                    intervals.Add((pipe.From, pipe.To));
                }
                else if (Math.Abs(line - pipe.Line) < margin - Eps)
                {
                    soft?.Add((pipe.From, pipe.To));
                }
            }
            else if (line > pipe.From - quarter - Eps && line < pipe.To + quarter + Eps)
            {
                // Clear of a pipe's ends by a quarter margin, the pipes of the two ends excepted where they meet the line's own end.
                var nearEnd = line < pipe.From + quarter + Eps || line > pipe.To - quarter - Eps;

                if (nearEnd && !Shares(pipe, fromOwner, toOwner))
                {
                    intervals.Add((pipe.Line - (quarter / 100), pipe.Line + (quarter / 100)));
                }
                else if (line > pipe.From + Eps && line < pipe.To - Eps)
                {
                    crossings.Add(pipe.Line);
                }
            }
        }

        crossings.Sort();
    }

    /// <summary>How much of a step lies inside the given intervals, overlaps counted once each.</summary>
    private static double Inside(List<(double From, double To)> intervals, double from, double to)
    {
        var length = 0.0;

        foreach (var (f, t) in intervals)
        {
            length += Math.Max(0, Math.Min(to, t) - Math.Max(from, f));
        }

        return length;
    }

    /// <summary>Adds the midpoint of every pair of neighbouring coordinates.</summary>
    private static void Midpoints(SortedSet<double> axis)
    {
        var values = axis.ToArray();

        for (var k = 1; k < values.Length; k++)
        {
            axis.Add(Math.Round((values[k - 1] + values[k]) / 2, 9));
        }
    }

    /// <summary>Whether a step is blocked, and otherwise how many pipes it crosses.</summary>
    private static (bool Blocked, double Crossings) Step(List<(double From, double To)> intervals, List<double> crossings, double from, double to)
    {
        foreach (var (f, t) in intervals)
        {
            if (Math.Max(from, f) < Math.Min(to, t) - Eps)
            {
                return (true, double.NaN);
            }
        }

        var count = 0;
        foreach (var x in crossings)
        {
            if (x > from + Eps && x < to - Eps)
            {
                count++;
            }
        }

        return (false, count);
    }

    /// <summary>Drops the anchors whose stub would lie along an earlier pipe, unless that leaves none.</summary>
    private List<(PlacedAnchor Anchor, Point Stub)> Usable(List<(PlacedAnchor Anchor, Point Stub)> anchors, List<Pipe> pipes, int fromOwner, int toOwner)
    {
        var clear = anchors.Where(a =>
        {
            var vertical = Math.Abs(a.Anchor.Direction.X) < 0.5;
            var line = vertical ? a.Anchor.At.X : a.Anchor.At.Y;
            var (from, to) = vertical
                ? (Math.Min(a.Anchor.At.Y, a.Stub.Y), Math.Max(a.Anchor.At.Y, a.Stub.Y))
                : (Math.Min(a.Anchor.At.X, a.Stub.X), Math.Max(a.Anchor.At.X, a.Stub.X));
            return signal ? !Collinear(pipes, vertical, line, from, to) : !ParallelBand(pipes, fromOwner, toOwner, vertical, line, from, to);
        }).ToList();

        return clear.Count > 0 ? clear : anchors;
    }

    private static void AddCoordinate(SortedSet<double> axis, double value, double lo, double hi)
    {
        if (value >= lo - Eps && value <= hi + Eps)
        {
            axis.Add(Math.Round(value, 9));
        }
    }

    private static int NodeOf(double[] xv, double[] yv, Point p)
    {
        var i = Array.BinarySearch(xv, Math.Round(p.X, 9));
        var j = Array.BinarySearch(yv, Math.Round(p.Y, 9));
        return i < 0 || j < 0 ? -1 : i + (j * xv.Length);
    }

    private static int Heading(Point direction) =>
        direction.X > 0.5 ? 0 : direction.Y > 0.5 ? 1 : direction.X < -0.5 ? 2 : 3;

    /// <summary>Whether a straight segment enters nothing it may not: no other box's outer box, no band of another pipe.</summary>
    private bool Clear(Point a, Point b, int fromOwner, int toOwner)
    {
        var vertical = Math.Abs(a.X - b.X) < Eps;
        var intervals = new List<(double From, double To)>();
        var crossings = new List<double>();
        Reaching(_boxes, _pipes, fromOwner, toOwner, vertical, vertical ? a.X : a.Y, intervals, crossings);
        var (from, to) = vertical ? (Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)) : (Math.Min(a.X, b.X), Math.Max(a.X, b.X));
        return !Step(intervals, crossings, from, to).Blocked;
    }

    /// <summary>Whether two anchors sit on one line and face each other along it.</summary>
    private static bool Facing(PlacedAnchor a, PlacedAnchor b)
    {
        var dx = b.At.X - a.At.X;
        var dy = b.At.Y - a.At.Y;

        if (Math.Abs(dx) < Eps && Math.Abs(dy) > Eps)
        {
            return Math.Abs(a.Direction.Y - Math.Sign(dy)) < Eps && Math.Abs(b.Direction.Y + Math.Sign(dy)) < Eps;
        }

        return Math.Abs(dy) < Eps && Math.Abs(dx) > Eps && Math.Abs(a.Direction.X - Math.Sign(dx)) < Eps && Math.Abs(b.Direction.X + Math.Sign(dx)) < Eps;
    }


    /// <summary>Whether a unit step runs along an earlier pipe inside its band; the owners' own pipes and stubs do not count, a port's pitch being the symbol's business.</summary>
    private bool ParallelBand(List<Pipe> pipes, int fromOwner, int toOwner, bool vertical, double line, double from, double to)
    {
        foreach (var pipe in pipes)
        {
            if (pipe.Vertical == vertical
                && Math.Abs(line - pipe.Line) < margin - Eps
                && !Shares(pipe, fromOwner, toOwner)
                && Math.Max(from, pipe.From) < Math.Min(to, pipe.To) - Eps)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a stretch runs along a drawn pipe.</summary>
    private static bool Collinear(List<Pipe> pipes, bool vertical, double line, double from, double to) =>
        pipes.Any(pipe => pipe.Vertical == vertical && Math.Abs(line - pipe.Line) < Eps && Math.Max(from, pipe.From) < Math.Min(to, pipe.To) - Eps);

    private static bool Shares(Pipe pipe, int fromOwner, int toOwner) =>
        (pipe.Owner >= 0 && (pipe.Owner == fromOwner || pipe.Owner == toOwner)) || (pipe.OwnerB >= 0 && (pipe.OwnerB == fromOwner || pipe.OwnerB == toOwner));

    /// <summary>
    /// The stretch along a pipe over which a route of the same symbol may run beside it closer than a margin: two
    /// ports of one symbol are at the symbol's port pitch, so their pipes leave side by side, and stay so until
    /// a margin past the symbol's clearance, where the lanes let them part.
    /// </summary>
    private (double From, double To)? OwnZone(Pipe pipe, int fromOwner, int toOwner)
    {
        foreach (var owner in new[] { pipe.Owner, pipe.OwnerB })
        {
            if (owner >= 0 && (owner == fromOwner || owner == toOwner) && _outerOf.TryGetValue(owner, out var outer))
            {
                return pipe.Vertical ? (outer.Y - margin, outer.Top + margin) : (outer.X - margin, outer.Right + margin);
            }
        }

        return null;
    }


    private static IEnumerable<Point> HopsOn(List<Pipe> pipes, Point a, Point b)
    {
        var vertical = Math.Abs(a.X - b.X) < Eps;
        var line = vertical ? a.X : a.Y;
        var from = vertical ? Math.Min(a.Y, b.Y) : Math.Min(a.X, b.X);
        var to = vertical ? Math.Max(a.Y, b.Y) : Math.Max(a.X, b.X);

        foreach (var pipe in pipes)
        {
            if (pipe.Vertical != vertical && pipe.Line > from + Eps && pipe.Line < to - Eps && line > pipe.From + Eps && line < pipe.To - Eps)
            {
                yield return vertical ? new Point(line, pipe.Line) : new Point(pipe.Line, line);
            }
        }
    }

    /// <summary>Drops repeated and collinear points so a bend is a bend.</summary>
    internal static ImmutableArray<Point> Simplify(Point[] points)
    {
        var result = new List<Point>();

        foreach (var point in points)
        {
            if (result.Count > 0 && result[^1].ManhattanTo(point) < Eps)
            {
                continue;
            }

            if (result.Count >= 2)
            {
                var a = result[^2];
                var b = result[^1];
                var sameLine = (Math.Abs(a.X - b.X) < Eps && Math.Abs(b.X - point.X) < Eps)
                    || (Math.Abs(a.Y - b.Y) < Eps && Math.Abs(b.Y - point.Y) < Eps);

                if (sameLine)
                {
                    result[^1] = point;
                    continue;
                }
            }

            result.Add(point);
        }

        return [.. result];
    }
}
