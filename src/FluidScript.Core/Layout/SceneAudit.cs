using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Binding;

namespace FluidScript.Core.Layout;

/// <summary>
/// Checks a solved scene against the clearance rules of <c>D-103</c> and <c>D-105</c>: margins may overlap one
/// another, but no inner boundary may be entered. A symbol's inner box stays out of every other symbol's outer
/// box; a pipe's centreline stays out of every outer box but those of the two symbols its run joins; two pipes
/// never run side by side closer than a margin, except the two runs of one symbol inside that symbol's own
/// clearance, where the port pitch decides. The findings are what the layout diagnostics print and what the
/// layout tests assert empty.
/// </summary>
public static class SceneAudit
{
    private const double Eps = 1e-6;

    /// <summary>One breach of the clearance rules (<c>28</c> §22).</summary>
    /// <param name="Kind">Hard (<c>28</c> B H1–H10): <c>inner-in-inner</c>, <c>clearance</c>, <c>pipe-in-inner</c>, <c>ends-off-port</c>, <c>stub-short</c>, <c>pipes-overlap</c>, <c>loop-counter-clockwise</c>, <c>losing-side-right</c>, <c>signal-in-inner</c>. Soft: <c>pipe-in-outer</c>, <c>pipe-beside-pipe</c>, <c>pipes-cross</c>, <c>signal-along-pipe</c>.</param>
    /// <param name="First">The element that enters: a component id or a connection id.</param>
    /// <param name="Second">The element entered.</param>
    /// <param name="Detail">Where, in world units.</param>
    public sealed record Finding(string Kind, string First, string Second, string Detail)
    {
        /// <summary>Gets whether the finding invalidates the layout, as opposed to counting against it.</summary>
        public bool Hard => Kind is "inner-in-inner" or "clearance" or "pipe-in-inner" or "ends-off-port" or "stub-short" or "pipes-overlap" or "loop-counter-clockwise" or "losing-side-right" or "signal-in-inner";

        /// <inheritdoc/>
        public override string ToString() => $"{Kind} {(Hard ? "hard" : "soft")} {First} {Second}: {Detail}";
    }

    /// <summary>Every breach in a scene; empty when the layout keeps its invariants.</summary>
    /// <param name="scene">The solved scene.</param>
    /// <param name="model">The semantic model the scene was solved from, for the connections' ends.</param>
    /// <returns>The findings, in scene order, hard and soft alike.</returns>
    public static ImmutableArray<Finding> Findings(Scene scene, SemanticModel model)
    {
        var findings = ImmutableArray.CreateBuilder<Finding>();
        var boxes = scene.Placements.Where(static p => !p.IsInline).ToList();
        var inline = scene.Placements.Where(static p => p.IsInline).Select(static p => p.ComponentId).ToHashSet(StringComparer.Ordinal);
        var pipes = scene.Routes.Where(static r => r.Kind == "pipe").ToList();
        var owners = pipes.Select(r => Owners(r, model, inline)).ToList();
        // A sensor stands one margin off the pipe it observes (28 §29), so its clearance touches that
        // pipe by rule; the brushing test below excuses it, and only it.
        var instruments = scene.Routes
            .Where(static r => r.Kind == "signal")
            .Select(static r => r.ConnectionId.Split(':')[0])
            .ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = 0; j < boxes.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                if (i < j && boxes[i].Inner.Intersects(boxes[j].Inner))
                {
                    findings.Add(new Finding("inner-in-inner", boxes[i].ComponentId, boxes[j].ComponentId, $"inner {Text(boxes[i].Inner)} intersects inner {Text(boxes[j].Inner)}"));
                }
                else if (boxes[i].Inner.Intersects(boxes[j].Outer))
                {
                    findings.Add(new Finding("clearance", boxes[i].ComponentId, boxes[j].ComponentId, $"inner {Text(boxes[i].Inner)} enters outer {Text(boxes[j].Outer)}"));
                }
            }
        }

        for (var r = 0; r < pipes.Count; r++)
        {
            for (var k = 1; k < pipes[r].Points.Length; k++)
            {
                var a = pipes[r].Points[k - 1];
                var b = pipes[r].Points[k];

                foreach (var box in boxes)
                {
                    // A pipe is excused from the clearance of the two boxes its run joins, never from their bodies: the stub leaves the edge outward and enters nothing, so a pipe inside its own component is a pipe through a box (C-95).
                    if (Enters(box.Inner, a, b))
                    {
                        findings.Add(new Finding("pipe-in-inner", pipes[r].ConnectionId, box.ComponentId, $"segment {Text(a)} {Text(b)} enters inner {Text(box.Inner)}"));
                    }
                    else if (!owners[r].Contains(box.ComponentId)
                        && (Enters(box.Outer, a, b) || (!instruments.Contains(box.ComponentId) && Brushes(box.Outer, a, b)))
                        && !SiblingRun(box, r))
                    {
                        findings.Add(new Finding("pipe-in-outer", pipes[r].ConnectionId, box.ComponentId, $"segment {Text(a)} {Text(b)} enters outer {Text(box.Outer)}"));
                    }
                }
            }
        }

        // A terminal node is a point and its clearance a convention (C-96): where its one pipe and
        // another are two runs of one symbol -- the tank's two supplies, at the symbol's port pitch --
        // the other run passing inside the node's clearance is the same allowance the beside test
        // makes below, and not a finding. A node with a body's worth of connections keeps its clearance.
        bool SiblingRun(Placement box, int r)
        {
            if (box.Anchors.Count != 1)
            {
                return false;
            }

            for (var s = 0; s < pipes.Count; s++)
            {
                if (s != r && owners[s].Contains(box.ComponentId) && owners[s].Any(o => o != box.ComponentId && owners[r].Contains(o)))
                {
                    return true;
                }
            }

            return false;
        }

        for (var r = 0; r < pipes.Count; r++)
        {
            for (var s = r + 1; s < pipes.Count; s++)
            {
                for (var i = 1; i < pipes[r].Points.Length; i++)
                {
                    for (var j = 1; j < pipes[s].Points.Length; j++)
                    {
                        var a = pipes[r].Points[i - 1];
                        var b = pipes[r].Points[i];
                        var c = pipes[s].Points[j - 1];
                        var d = pipes[s].Points[j];

                        if (Crosses(a, b, c, d) is { } crossing)
                        {
                            findings.Add(new Finding("pipes-cross", pipes[s].ConnectionId, pipes[r].ConnectionId, $"segment {Text(c)} {Text(d)} crosses {Text(a)} {Text(b)} at {Text(crossing)}"));
                            continue;
                        }

                        if (Beside(a, b, c, d, scene.Margin) is not { } stretch)
                        {
                            continue;
                        }

                        // Two runs of one symbol leave its neighbouring ports at the symbol's port pitch, which may
                        // be less than a margin: within a margin of that symbol's clearance they are allowed side by side.
                        var shared = boxes.Any(p => owners[r].Contains(p.ComponentId) && owners[s].Contains(p.ComponentId) && Within(p.Outer.Grow(scene.Margin), stretch));

                        if (!shared)
                        {
                            findings.Add(new Finding("pipe-beside-pipe", pipes[r].ConnectionId, pipes[s].ConnectionId, $"segment {Text(a)} {Text(b)} runs along {Text(c)} {Text(d)} over {Text(stretch)}"));
                        }
                    }
                }
            }
        }

        Ends(scene, model, findings);
        Overlaps(pipes, findings);
        Loops(scene, model, findings);
        Flanks(scene, model, findings);
        Signals(scene, pipes, findings);
        return findings.ToImmutable();
    }

    /// <summary>H4 and H5: every pipe starts and ends on an anchor of the component its connection names, and leaves each anchor that has a box along the anchor's outward direction for a whole margin before it turns.</summary>
    private static void Ends(Scene scene, SemanticModel model, ImmutableArray<Finding>.Builder findings)
    {
        var byId = scene.Placements.ToDictionary(static p => p.ComponentId, StringComparer.Ordinal);
        var pipes = scene.Routes.Where(static r => r.Kind == "pipe").ToList();
        var inlinePoints = scene.Placements.Where(static p => p.IsInline).Select(static p => p.Inner.Centre).ToList();

        foreach (var route in pipes)
        {
            if (Connection(route, model) is not { } connection || route.Points.Length < 2)
            {
                continue;
            }

            End(route, connection.From.Component, route.Points[0], route.Points[1], byId, pipes, inlinePoints, scene.Margin, findings);
            End(route, connection.To.Component, route.Points[^1], route.Points[^2], byId, pipes, inlinePoints, scene.Margin, findings);
        }
    }

    private static void End(Route route, string component, Point end, Point next, Dictionary<string, Placement> byId, List<Route> pipes, List<Point> inlinePoints, double margin, ImmutableArray<Finding>.Builder findings)
    {
        if (!byId.TryGetValue(component, out var placement))
        {
            return;
        }

        var ports = placement.Anchors.Values.Where(a => a.At.ManhattanTo(end) < Eps).ToList();

        if (ports.Count == 0)
        {
            findings.Add(new Finding("ends-off-port", route.ConnectionId, component, $"end {Text(end)} is on no port of {component}"));
            return;
        }

        if (placement.IsInline)
        {
            return;
        }

        var along = Direction.Of(next.Offset(-end.X, -end.Y));
        var length = along is { } d ? Straight(pipes, inlinePoints, end, d) : 0;

        if (ports.All(p => along != p.Outward) || length < margin - Eps)
        {
            findings.Add(new Finding("stub-short", route.ConnectionId, component, $"leaves {Text(end)} for {Text(next)}, {N(length)} along {along}, not a whole margin along {ports[0].Outward}"));
        }
    }

    /// <summary>How far a pipe runs straight from a point, through inline points where a run is cut (A5), until it bends or reaches a box.</summary>
    private static double Straight(List<Route> pipes, List<Point> inlinePoints, Point from, Direction along)
    {
        var total = 0.0;
        var at = from;

        for (var guard = 0; guard < 64; guard++)
        {
            Point? reached = null;

            foreach (var pipe in pipes)
            {
                for (var k = 1; k < pipe.Points.Length && reached is null; k++)
                {
                    var p = pipe.Points[k - 1];
                    var q = pipe.Points[k];

                    if (p.ManhattanTo(at) < Eps && Direction.Of(q.Offset(-p.X, -p.Y)) == along)
                    {
                        reached = q;
                    }
                    else if (q.ManhattanTo(at) < Eps && Direction.Of(p.Offset(-q.X, -q.Y)) == along)
                    {
                        reached = p;
                    }
                }

                if (reached is not null)
                {
                    break;
                }
            }

            if (reached is not { } end)
            {
                return total;
            }

            total += at.ManhattanTo(end);
            at = end;

            if (!inlinePoints.Any(ip => ip.ManhattanTo(at) < Eps))
            {
                return total;
            }
        }

        return total;
    }

    /// <summary>H7: no two pipes share a stretch of line -- two collinear segments overlapping over more than a point.</summary>
    private static void Overlaps(List<Route> pipes, ImmutableArray<Finding>.Builder findings)
    {
        for (var r = 0; r < pipes.Count; r++)
        {
            for (var s = r + 1; s < pipes.Count; s++)
            {
                for (var i = 1; i < pipes[r].Points.Length; i++)
                {
                    for (var j = 1; j < pipes[s].Points.Length; j++)
                    {
                        var a = pipes[r].Points[i - 1];
                        var b = pipes[r].Points[i];
                        var c = pipes[s].Points[j - 1];
                        var d = pipes[s].Points[j];

                        if (Shared(a, b, c, d) is { } length)
                        {
                            findings.Add(new Finding("pipes-overlap", pipes[s].ConnectionId, pipes[r].ConnectionId, $"segment {Text(c)} {Text(d)} shares {N(length)} of {Text(a)} {Text(b)}"));
                        }
                    }
                }
            }
        }
    }

    /// <summary>The length two collinear segments share beyond a point, or nothing.</summary>
    private static double? Shared(Point a, Point b, Point c, Point d)
    {
        var vertical = Math.Abs(a.X - b.X) < Eps;

        if (vertical != Math.Abs(c.X - d.X) < Eps)
        {
            return null;
        }

        var (line, otherLine) = vertical ? (a.X, c.X) : (a.Y, c.Y);

        if (Math.Abs(line - otherLine) > Eps)
        {
            return null;
        }

        var (a0, a1, c0, c1) = vertical ? (a.Y, b.Y, c.Y, d.Y) : (a.X, b.X, c.X, d.X);
        var low = Math.Max(Math.Min(a0, a1), Math.Min(c0, c1));
        var high = Math.Min(Math.Max(a0, a1), Math.Max(c0, c1));
        return high - low > Eps ? high - low : null;
    }

    /// <summary>A signal line is held like any other line (<c>C-95</c>): it enters no inner box but the two its ends touch (hard), and it runs along no pipe (soft); it crosses pipes freely, hopping them (C16).</summary>
    private static void Signals(Scene scene, List<Route> pipes, ImmutableArray<Finding>.Builder findings)
    {
        var boxes = scene.Placements.Where(static p => !p.IsInline).ToList();

        foreach (var signal in scene.Routes.Where(static r => r.Kind == "signal"))
        {
            if (signal.Points.Length < 2)
            {
                continue;
            }

            var ends = new[] { signal.Points[0], signal.Points[^1] };
            var own = boxes.Where(p => ends.Any(e => p.Inner.Grow(Eps).ContainsInterior(e))).Select(static p => p.ComponentId).ToHashSet(StringComparer.Ordinal);

            for (var k = 1; k < signal.Points.Length; k++)
            {
                var a = signal.Points[k - 1];
                var b = signal.Points[k];

                foreach (var box in boxes)
                {
                    if (!own.Contains(box.ComponentId) && Enters(box.Inner, a, b))
                    {
                        findings.Add(new Finding("signal-in-inner", signal.ConnectionId, box.ComponentId, $"segment {Text(a)} {Text(b)} enters inner {Text(box.Inner)}"));
                    }
                }

                foreach (var pipe in pipes)
                {
                    for (var j = 1; j < pipe.Points.Length; j++)
                    {
                        var c = pipe.Points[j - 1];
                        var d = pipe.Points[j];

                        if (Shared(a, b, c, d) is { } length)
                        {
                            findings.Add(new Finding("signal-along-pipe", signal.ConnectionId, pipe.ConnectionId, $"segment {Text(a)} {Text(b)} runs {N(length)} along {Text(c)} {Text(d)}"));
                        }
                    }
                }
            }
        }
    }

    /// <summary>H9: every simple directed cycle of the flow-oriented graph -- each connection directed by the flow vector at its first end -- walked through its members' centres encloses negative signed area (y up), which is clockwise.</summary>
    private static void Loops(Scene scene, SemanticModel model, ImmutableArray<Finding>.Builder findings)
    {
        var ids = scene.Placements.Select(static p => p.ComponentId).Order(StringComparer.Ordinal).ToList();
        var index = ids.Select((id, i) => (id, i)).ToDictionary(static e => e.id, static e => e.i, StringComparer.Ordinal);
        var centre = scene.Placements.ToDictionary(static p => p.ComponentId, static p => p.Inner.Centre, StringComparer.Ordinal);
        var adjacency = ids.Select(static _ => new List<int>()).ToList();

        foreach (var route in scene.Routes)
        {
            if (Connection(route, model) is not { } connection || route.Points.Length < 1 || !index.TryGetValue(connection.From.Component, out var from) || !index.TryGetValue(connection.To.Component, out var to))
            {
                continue;
            }

            // The connection's sense is the flow at its first end along the route's first segment: at an inline node both anchors' flows run along the run, so the segment decides where the anchor's outward direction would not (C-95).
            var start = scene.Placements.First(p => p.ComponentId == connection.From.Component).Anchors.Values.FirstOrDefault(a => a.At.ManhattanTo(route.Points[0]) < Eps);
            var along = route.Points.Length >= 2 ? Direction.Of(route.Points[1].Offset(-route.Points[0].X, -route.Points[0].Y)) : null;
            var leaving = along is { } d ? start.Flow == d : start.Flow == start.Outward;
            adjacency[leaving ? from : to].Add(leaving ? to : from);
        }

        var cycles = new List<List<int>>();
        var path = new List<int>();
        var onPath = new bool[ids.Count];

        for (var start = 0; start < ids.Count && cycles.Count < 1000; start++)
        {
            path.Add(start);
            onPath[start] = true;
            Cycles(start, start, adjacency, path, onPath, cycles);
            onPath[start] = false;
            path.RemoveAt(path.Count - 1);
        }

        foreach (var cycle in cycles)
        {
            var area = 0.0;

            for (var k = 0; k < cycle.Count; k++)
            {
                var p = centre[ids[cycle[k]]];
                var q = centre[ids[cycle[(k + 1) % cycle.Count]]];
                area += (p.X * q.Y) - (q.X * p.Y);
            }

            if (area / 2 > Eps)
            {
                findings.Add(new Finding("loop-counter-clockwise", ids[cycle[0]], string.Join(" > ", cycle.Select(k => ids[k])), $"signed area {N(area / 2)}; a flow loop runs clockwise (28 H9)"));
            }
        }
    }

    private static void Cycles(int at, int start, List<List<int>> adjacency, List<int> path, bool[] onPath, List<List<int>> cycles)
    {
        foreach (var next in adjacency[at])
        {
            if (next == start)
            {
                cycles.Add([.. path]);
            }
            else if (next > start && !onPath[next] && cycles.Count < 1000)
            {
                path.Add(next);
                onPath[next] = true;
                Cycles(next, start, adjacency, path, onPath, cycles);
                onPath[next] = false;
                path.RemoveAt(path.Count - 1);
            }
        }
    }

    /// <summary>H10: a two-sided exchanger's losing side is its left flank. A positive stated duty enters side 1, so side 2 loses; a role word fixes the sign as lowering applies it (<c>D-91</c>). Measured only where both sides are connected and a duty is stated.</summary>
    private static void Flanks(Scene scene, SemanticModel model, ImmutableArray<Finding>.Builder findings)
    {
        foreach (var placement in scene.Placements)
        {
            if (!placement.Anchors.TryGetValue("in", out var in1) || !placement.Anchors.TryGetValue("out", out var out1) || !placement.Anchors.TryGetValue("in2", out var in2) || !placement.Anchors.TryGetValue("out2", out var out2))
            {
                continue;
            }

            var component = model.Components.FirstOrDefault(c => c.Name == placement.ComponentId);
            var wired = model.Connections.Any(c => (c.From.Component == placement.ComponentId && c.From.Port is "in2" or "out2") || (c.To.Component == placement.ComponentId && c.To.Port is "in2" or "out2"));

            if (component is null || !wired || !component.Parameters.TryGetValue("power", out var duty) || duty.Value is not { } power || Math.Abs(power.SiValue) < Eps)
            {
                continue;
            }

            var signed = FluidScript.Core.Language.NameResolution.Normalize(component.WrittenKind) switch
            {
                "load" or "cooler" or "radiator" or "chiller" => -Math.Abs(power.SiValue),
                "heater" or "boiler" => Math.Abs(power.SiValue),
                _ => power.SiValue,
            };

            var side1 = (in1.At.X + out1.At.X) / 2;
            var side2 = (in2.At.X + out2.At.X) / 2;
            var (losing, gaining) = signed > 0 ? (side2, side1) : (side1, side2);

            if (losing > gaining + Eps)
            {
                findings.Add(new Finding("losing-side-right", placement.ComponentId, signed > 0 ? "side 2" : "side 1", $"the losing side sits at x {N(losing)}, the gaining at {N(gaining)}; heat flows left to right (28 H10)"));
            }
        }
    }

    /// <summary>The model connection a pipe route draws, or null for a signal or an unnumbered route.</summary>
    private static ConnectionSymbol? Connection(Route route, SemanticModel model) =>
        route.Kind == "pipe" && route.ConnectionId.StartsWith('c') && int.TryParse(route.ConnectionId[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index < model.Connections.Length
            ? model.Connections[index]
            : null;

    /// <summary>Where two perpendicular segments cross, strictly inside both; <see langword="null"/> otherwise.</summary>
    private static Point? Crosses(Point a, Point b, Point c, Point d)
    {
        var abVertical = Math.Abs(a.X - b.X) < Eps;
        var cdVertical = Math.Abs(c.X - d.X) < Eps;

        if (abVertical == cdVertical)
        {
            return null;
        }

        var (v0, v1, h0, h1) = abVertical ? (a, b, c, d) : (c, d, a, b);
        var x = v0.X;
        var y = h0.Y;
        var inside = x > Math.Min(h0.X, h1.X) + Eps && x < Math.Max(h0.X, h1.X) - Eps && y > Math.Min(v0.Y, v1.Y) + Eps && y < Math.Max(v0.Y, v1.Y) - Eps;
        return inside ? new Point(x, y) : null;
    }

    /// <summary>
    /// The closed polygon a pipe's clearance occupies: its centreline offset by <paramref name="margin"/> to both
    /// sides, mitred at the bends and flat at the two ends, which carry no margin. Collinear points are merged first.
    /// </summary>
    /// <param name="points">The pipe's centreline, orthogonal segments.</param>
    /// <param name="margin">The clearance to each side, world units.</param>
    /// <returns>The polygon, one side forward then the other side back; empty for fewer than two distinct points.</returns>
    public static ImmutableArray<Point> Band(ImmutableArray<Point> points, double margin)
    {
        var line = new List<Point>();
        foreach (var p in points)
        {
            if (line.Count == 0 || line[^1].ManhattanTo(p) > Eps)
            {
                if (line.Count >= 2 && Collinear(line[^2], line[^1], p))
                {
                    line[^1] = p;
                }
                else
                {
                    line.Add(p);
                }
            }
        }

        if (line.Count < 2)
        {
            return [];
        }

        var normals = new List<Point>();
        for (var k = 1; k < line.Count; k++)
        {
            var dx = line[k].X - line[k - 1].X;
            var dy = line[k].Y - line[k - 1].Y;
            var length = Math.Abs(dx) + Math.Abs(dy);
            normals.Add(new Point(-dy / length, dx / length));
        }

        var left = new List<Point>();
        var right = new List<Point>();
        for (var k = 0; k < line.Count; k++)
        {
            var n = k == 0 ? normals[0]
                : k == line.Count - 1 ? normals[^1]
                : Math.Abs(normals[k - 1].X - normals[k].X) < Eps && Math.Abs(normals[k - 1].Y - normals[k].Y) < Eps
                    ? normals[k]
                    : new Point(normals[k - 1].X + normals[k].X, normals[k - 1].Y + normals[k].Y);
            left.Add(line[k].Offset(n.X * margin, n.Y * margin));
            right.Add(line[k].Offset(-n.X * margin, -n.Y * margin));
        }

        right.Reverse();
        return [.. left, .. right];
    }

    /// <summary>The two symbols a connection's run joins: through the inline elements at either end to the first box.</summary>
    /// <param name="route">The pipe route.</param>
    /// <param name="model">The semantic model, for the connection's ends.</param>
    /// <param name="inline">The ids drawn inline, as points on their run.</param>
    /// <returns>The component ids at both ends of the run; the connection's own two when it cannot be read.</returns>
    public static HashSet<string> Owners(Route route, SemanticModel model, IReadOnlySet<string> inline)
    {
        var own = new HashSet<string>(StringComparer.Ordinal);

        if (!route.ConnectionId.StartsWith('c') || !int.TryParse(route.ConnectionId[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index >= model.Connections.Length)
        {
            return own;
        }

        var connection = model.Connections[index];

        foreach (var end in new[] { connection.From.Component, connection.To.Component })
        {
            var at = end;
            var visited = new HashSet<string>(StringComparer.Ordinal) { connection.From.Component, connection.To.Component };

            while (inline.Contains(at))
            {
                var next = model.Connections
                    .Where(c => c.From.Component == at || c.To.Component == at)
                    .Select(c => c.From.Component == at ? c.To.Component : c.From.Component)
                    .FirstOrDefault(other => visited.Add(other));

                if (next is null)
                {
                    break;
                }

                at = next;
            }

            own.Add(at);
        }

        return own;
    }

    private static bool Collinear(Point a, Point b, Point c) =>
        (Math.Abs(a.X - b.X) < Eps && Math.Abs(b.X - c.X) < Eps) || (Math.Abs(a.Y - b.Y) < Eps && Math.Abs(b.Y - c.Y) < Eps);

    private static bool Enters(Box outer, Point a, Point b)
    {
        var vertical = Math.Abs(a.X - b.X) < Eps;
        return vertical
            ? a.X > outer.X + Eps && a.X < outer.Right - Eps && Math.Max(Math.Min(a.Y, b.Y), outer.Y) < Math.Min(Math.Max(a.Y, b.Y), outer.Top) - Eps
            : a.Y > outer.Y + Eps && a.Y < outer.Top - Eps && Math.Max(Math.Min(a.X, b.X), outer.X) < Math.Min(Math.Max(a.X, b.X), outer.Right) - Eps;
    }

    /// <summary>
    /// Whether a segment runs along an outer box's edge for any length: on the boundary, not inside it.
    /// The clearance rule is <c>≥ m</c> between inner boxes, and a pipe exactly a margin from a box it
    /// does not serve satisfies it and draws as a line brushing the box's clearance (C-87). Strict
    /// <see cref="Enters"/> called that clean; a pipe past a box it does not serve wants <c>&gt; m</c>.
    /// </summary>
    private static bool Brushes(Box outer, Point a, Point b)
    {
        var vertical = Math.Abs(a.X - b.X) < Eps;

        return vertical
            ? (Math.Abs(a.X - outer.X) < Eps || Math.Abs(a.X - outer.Right) < Eps)
                && Math.Max(Math.Min(a.Y, b.Y), outer.Y) < Math.Min(Math.Max(a.Y, b.Y), outer.Top) - Eps
            : (Math.Abs(a.Y - outer.Y) < Eps || Math.Abs(a.Y - outer.Top) < Eps)
                && Math.Max(Math.Min(a.X, b.X), outer.X) < Math.Min(Math.Max(a.X, b.X), outer.Right) - Eps;
    }

    private static bool Within(Box box, Box stretch) =>
        stretch.X >= box.X - Eps && stretch.Right <= box.Right + Eps && stretch.Y >= box.Y - Eps && stretch.Top <= box.Top + Eps;

    /// <summary>The stretch over which two parallel segments run side by side closer than the margin, or nothing.</summary>
    private static Box? Beside(Point a, Point b, Point c, Point d, double margin)
    {
        var vertical = Math.Abs(a.X - b.X) < Eps;

        if (vertical != (Math.Abs(c.X - d.X) < Eps))
        {
            return null;
        }

        var apart = vertical ? Math.Abs(a.X - c.X) : Math.Abs(a.Y - c.Y);
        var (f1, t1) = vertical ? (Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)) : (Math.Min(a.X, b.X), Math.Max(a.X, b.X));
        var (f2, t2) = vertical ? (Math.Min(c.Y, d.Y), Math.Max(c.Y, d.Y)) : (Math.Min(c.X, d.X), Math.Max(c.X, d.X));
        var from = Math.Max(f1, f2);
        var to = Math.Min(t1, t2);

        if (apart >= margin - Eps || from >= to - Eps)
        {
            return null;
        }

        return vertical
            ? new Box(Math.Min(a.X, c.X), from, apart, to - from)
            : new Box(from, Math.Min(a.Y, c.Y), to - from, apart);
    }

    private static string Text(Point p) => $"({N(p.X)}, {N(p.Y)})";

    private static string Text(Box b) => $"[({N(b.X)}, {N(b.Y)}), ({N(b.Right)}, {N(b.Top)})]";

    private static string N(double v) => Math.Round(v, 6).ToString("0.###", CultureInfo.InvariantCulture);
}
