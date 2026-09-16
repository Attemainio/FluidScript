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
    /// <param name="Kind"><c>inner-in-inner</c>, <c>clearance</c> and <c>pipe-in-inner</c> are hard; <c>pipe-in-outer</c>, <c>pipe-beside-pipe</c> and <c>pipes-cross</c> are soft.</param>
    /// <param name="First">The element that enters: a component id or a connection id.</param>
    /// <param name="Second">The element entered.</param>
    /// <param name="Detail">Where, in world units.</param>
    public sealed record Finding(string Kind, string First, string Second, string Detail)
    {
        /// <summary>Gets whether the finding invalidates the layout, as opposed to counting against it.</summary>
        public bool Hard => Kind is "inner-in-inner" or "clearance" or "pipe-in-inner";

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
                    if (owners[r].Contains(box.ComponentId))
                    {
                        continue;
                    }

                    if (Enters(box.Inner, a, b))
                    {
                        findings.Add(new Finding("pipe-in-inner", pipes[r].ConnectionId, box.ComponentId, $"segment {Text(a)} {Text(b)} enters inner {Text(box.Inner)}"));
                    }
                    else if (Enters(box.Outer, a, b))
                    {
                        findings.Add(new Finding("pipe-in-outer", pipes[r].ConnectionId, box.ComponentId, $"segment {Text(a)} {Text(b)} enters outer {Text(box.Outer)}"));
                    }
                }
            }
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

        return findings.ToImmutable();
    }

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

    /// <summary>Whether a point lies inside a closed polygon, by ray casting; a point on the boundary may fall either way.</summary>
    /// <param name="point">The point.</param>
    /// <param name="polygon">The polygon's vertices in order, without repeating the first.</param>
    /// <returns><see langword="true"/> when inside.</returns>
    public static bool Inside(Point point, IReadOnlyList<Point> polygon)
    {
        var inside = false;
        var j = polygon.Count - 1;

        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[j];

            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < ((b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }

            j = i;
        }

        return inside;
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
