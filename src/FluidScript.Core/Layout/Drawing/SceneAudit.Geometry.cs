using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Language.Binding;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout.Drawing;

public static partial class SceneAudit
{
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
                if (line.Count >= 2 && Segments.Collinear(line[^2], line[^1], p, Eps))
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
