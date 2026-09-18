using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using FluidScript.Core.Binding;
using FluidScript.Core.Language;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Layout;

/// <summary>
/// The layout report (<c>28</c> A10, <c>D-100</c>, <c>D-108</c>): a scene as text, so that a layout is read and checked
/// without its picture. Every symbol with its group, transform, inner and outer boxes and each port at both boundaries
/// with its flow vector; every connection with its points, length, bends, crossings, hops and the envelope its clearance
/// occupies; every group; a character raster of the arrangement; the audit's totals and metrics; and every finding.
/// <c>y</c> grows upward; the raster's first row is the top.
/// </summary>
public static class SceneText
{
    private const int CellsPerUnit = 4;

    private const int MostCells = 400_000;

    /// <summary>Renders the report.</summary>
    /// <param name="scene">The solved scene.</param>
    /// <param name="graph">The lowered graph the scene was solved from, for each component's kind and port roles.</param>
    /// <param name="model">The bound model, for the connections' ends and the audit.</param>
    /// <returns>The report, one text.</returns>
    public static string Render(Scene scene, CircuitGraph graph, SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(model);

        var text = new StringBuilder();
        var m = scene.Margin;
        text.Append("margin ").Append(N(m)).Append('\n');
        text.Append("extent ").Append(Text(scene.Extent)).Append("\n\nCOMPONENTS\n");

        foreach (var p in scene.Placements)
        {
            var flow = graph.Components.FirstOrDefault(c => c.Name == p.ComponentId);
            var kind = flow?.Kind ?? KindOf(p);
            text.Append(p.ComponentId).Append(' ').Append(kind);

            if (p.IsInline)
            {
                text.Append(" inline\n at ").Append(Text(p.Inner.Centre)).Append('\n');
            }
            else
            {
                text.Append('\n')
                    .Append(" group ").Append(p.Group ?? "-").Append('\n')
                    .Append(" rotation ").Append(p.Rotation).Append(p.Mirrored ? " mirrored" : string.Empty).Append(p.Arrangement == "default" ? string.Empty : " " + p.Arrangement).Append('\n')
                    .Append(" inner ").Append(Text(p.Inner)).Append('\n')
                    .Append(" outer ").Append(Text(p.Outer)).Append('\n');
            }

            foreach (var (name, anchor) in p.Anchors)
            {
                var role = flow?.Ports.FirstOrDefault(port => port.Name == name)?.Role switch
                {
                    PortRole.Inlet => "inlet",
                    PortRole.Outlet => "outlet",
                    _ => "port",
                };

                text.Append(' ').Append(role).Append(' ').Append(name).Append('\n');
                text.Append("  inner ").Append(Text(anchor.At)).Append('\n');

                if (!p.IsInline)
                {
                    text.Append("  outer ").Append(Text(anchor.Along(m))).Append('\n');
                }

                text.Append("  flow ").Append(anchor.Flow.ToString()).Append('\n');
            }

            text.Append('\n');
        }

        text.Append("CONNECTIONS\n");
        var findings = SceneAudit.Findings(scene, model);
        var totalBends = 0;
        var totalLength = 0.0;
        var totalManhattan = 0.0;

        foreach (var r in scene.Routes)
        {
            var name = r.ConnectionId.StartsWith('c') && int.TryParse(r.ConnectionId[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index < model.Connections.Length
                ? $"{model.Connections[index].From.Component}.{model.Connections[index].From.Port} -> {model.Connections[index].To.Component}.{model.Connections[index].To.Port} {r.ConnectionId}"
                : $"{r.ConnectionId} {r.Kind}";
            var bends = r.Bends.Count();
            var crossings = findings.Count(f => f.Kind == "pipes-cross" && f.First == r.ConnectionId);
            text.Append(name).Append(r.Kind == "pipe" ? " " + r.Layer : string.Empty).Append('\n');
            text.Append(" points ").Append(Text(r.Points)).Append('\n');
            text.Append(" length ").Append(N(r.Length)).Append(" bends ").Append(bends).Append(" crossings ").Append(crossings).Append('\n');

            if (r.Kind == "pipe")
            {
                text.Append(" envelope ").Append(Text(SceneAudit.Band(r.Points, m))).Append('\n');
                totalBends += bends;
                totalLength += r.Length;
                totalManhattan += r.Points.Length >= 2 ? r.Points[0].ManhattanTo(r.Points[^1]) : 0;
            }

            if (r.Hops.Length > 0)
            {
                text.Append(" hops ").Append(Text(r.Hops)).Append('\n');
            }
        }

        text.Append("\nGROUPS\n");

        if (scene.Groups.Length == 0)
        {
            text.Append(" none\n");
        }

        foreach (var g in scene.Groups)
        {
            text.Append(g.Id).Append(' ').Append(g.Kind).Append(g.Orientation.Length > 0 ? " " + g.Orientation : string.Empty).Append('\n');
            text.Append(" members [").Append(string.Join(", ", g.Members)).Append("]\n");
            text.Append(" bounds ").Append(Text(g.Bounds)).Append(" width ").Append(N(g.Bounds.Width)).Append(" height ").Append(N(g.Bounds.Height)).Append('\n');
        }

        text.Append("\nRASTER (").Append(CellsPerUnit).Append(" cells per unit; . outer box, # inner box, o node, - | pipe, : signal, * bend, + crossing, < > ^ v flow at a port)\n");
        Raster(text, scene, graph);

        text.Append("\nVALIDATION\n");
        text.Append(" hard ").Append(findings.Count(static f => f.Hard))
            .Append(" (inner-in-inner ").Append(Count(findings, "inner-in-inner"))
            .Append(", clearance ").Append(Count(findings, "clearance"))
            .Append(", pipe-in-inner ").Append(Count(findings, "pipe-in-inner"))
            .Append(", ends-off-port ").Append(Count(findings, "ends-off-port"))
            .Append(", stub-short ").Append(Count(findings, "stub-short"))
            .Append(", pipes-overlap ").Append(Count(findings, "pipes-overlap"))
            .Append(", loop-counter-clockwise ").Append(Count(findings, "loop-counter-clockwise"))
            .Append(", losing-side-right ").Append(Count(findings, "losing-side-right"))
            .Append(", signal-in-inner ").Append(Count(findings, "signal-in-inner")).Append(")\n");
        text.Append(" soft ").Append(findings.Count(static f => !f.Hard))
            .Append(" (pipe-in-outer ").Append(Count(findings, "pipe-in-outer"))
            .Append(", pipe-beside-pipe ").Append(Count(findings, "pipe-beside-pipe"))
            .Append(", pipes-cross ").Append(Count(findings, "pipes-cross"))
            .Append(", signal-along-pipe ").Append(Count(findings, "signal-along-pipe")).Append(")\n");
        text.Append(" bends ").Append(totalBends).Append(" length ").Append(N(totalLength)).Append('\n');

        // The metrics 62 trends rather than gates: how much longer the pipes run than their ends are apart, how much of the drawing is symbol, and its shape.
        var symbolArea = scene.Placements.Where(static p => !p.IsInline).Sum(static p => p.Inner.Width * p.Inner.Height);
        var extentArea = scene.Extent.Width * scene.Extent.Height;
        text.Append(" length-ratio ").Append(N(totalManhattan > 0 ? totalLength / totalManhattan : 1))
            .Append(" area-utilisation ").Append(N(extentArea > 0 ? symbolArea / extentArea : 0))
            .Append(" aspect ").Append(N(scene.Extent.Height > 0 ? scene.Extent.Width / scene.Extent.Height : 0)).Append('\n');

        text.Append("\nINTERFERENCE\n");

        if (findings.Length == 0)
        {
            text.Append(" none\n");
        }

        foreach (var finding in findings)
        {
            text.Append(' ').Append(finding).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>The arrangement as characters (<c>28</c> A10): the extent at four cells per unit, first row at the top.</summary>
    private static void Raster(StringBuilder text, Scene scene, CircuitGraph graph)
    {
        var extent = scene.Extent;
        var columns = (int)Math.Ceiling(extent.Width * CellsPerUnit) + 1;
        var rows = (int)Math.Ceiling(extent.Height * CellsPerUnit) + 1;

        if (extent.Width <= 0 || extent.Height <= 0 || (long)columns * rows > MostCells)
        {
            text.Append(" none\n");
            return;
        }

        var grid = new char[rows][];

        for (var r = 0; r < rows; r++)
        {
            grid[r] = new string(' ', columns).ToCharArray();
        }

        int Col(double x) => Math.Clamp((int)Math.Round((x - extent.X) * CellsPerUnit), 0, columns - 1);
        int Row(double y) => Math.Clamp(rows - 1 - (int)Math.Round((y - extent.Y) * CellsPerUnit), 0, rows - 1);

        void Fill(Box box, char c, bool onlyBlank)
        {
            for (var r = Row(box.Top); r <= Row(box.Y); r++)
            {
                for (var col = Col(box.X); col <= Col(box.Right); col++)
                {
                    if (!onlyBlank || grid[r][col] == ' ')
                    {
                        grid[r][col] = c;
                    }
                }
            }
        }

        var current = -1;
        var owner = new int[rows][];

        for (var r = 0; r < rows; r++)
        {
            owner[r] = new int[columns];
            Array.Fill(owner[r], -1);
        }

        void Mark(int r, int col, char c)
        {
            var existing = grid[r][col];

            if (existing is '#' or 'o' or '+' or '*')
            {
                return;
            }

            if ((existing is '-' or '|' or ':') && existing != c)
            {
                // Two directions in one cell: the route's own bend, or two routes crossing.
                grid[r][col] = owner[r][col] == current ? '*' : '+';
                return;
            }

            grid[r][col] = c;
            owner[r][col] = current;
        }

        var boxed = scene.Placements.Where(static p => !p.IsInline).ToList();

        foreach (var p in boxed)
        {
            Fill(p.Outer, '.', onlyBlank: true);
        }

        foreach (var p in boxed)
        {
            var node = (graph.Components.FirstOrDefault(c => c.Name == p.ComponentId)?.Kind ?? KindOf(p)) == "node";
            Fill(p.Inner, node ? 'o' : '#', onlyBlank: false);
        }

        foreach (var route in scene.Routes)
        {
            current++;
            var (h, v) = route.Kind == "pipe" ? ('-', '|') : (':', ':');

            for (var k = 1; k < route.Points.Length; k++)
            {
                var a = route.Points[k - 1];
                var b = route.Points[k];

                if (Math.Abs(a.Y - b.Y) < 1e-9)
                {
                    var r = Row(a.Y);

                    for (var col = Col(Math.Min(a.X, b.X)); col <= Col(Math.Max(a.X, b.X)); col++)
                    {
                        Mark(r, col, h);
                    }
                }
                else
                {
                    var col = Col(a.X);

                    for (var r = Row(Math.Max(a.Y, b.Y)); r <= Row(Math.Min(a.Y, b.Y)); r++)
                    {
                        Mark(r, col, v);
                    }
                }
            }
        }

        foreach (var route in scene.Routes)
        {
            foreach (var hop in route.Hops)
            {
                grid[Row(hop.Y)][Col(hop.X)] = '+';
            }
        }

        foreach (var p in scene.Placements)
        {
            if (p.IsInline)
            {
                grid[Row(p.Inner.Centre.Y)][Col(p.Inner.Centre.X)] = 'o';
                continue;
            }

            foreach (var anchor in p.Anchors.Values)
            {
                var c = anchor.Flow == Direction.Right ? '>' : anchor.Flow == Direction.Left ? '<' : anchor.Flow == Direction.Up ? '^' : 'v';
                grid[Row(anchor.At.Y)][Col(anchor.At.X)] = c;
            }
        }

        foreach (var p in boxed)
        {
            // The name inside the box where it fits, else just above it where nothing but margin lies; otherwise the box stands unnamed.
            var name = p.ComponentId;
            var inside = (Row(p.Inner.Centre.Y), Col(p.Inner.X) + 1);
            var above = (Row(p.Inner.Top) - 1, Col(p.Inner.X));

            if (inside.Item2 + name.Length <= Col(p.Inner.Right))
            {
                Write(grid, inside.Item1, inside.Item2, name, columns);
            }
            else if (above.Item1 >= 0 && Enumerable.Range(above.Item2, name.Length).All(col => col < columns && grid[above.Item1][col] is ' ' or '.'))
            {
                Write(grid, above.Item1, above.Item2, name, columns);
            }
        }

        foreach (var row in grid)
        {
            text.Append(' ').Append(new string(row).TrimEnd()).Append('\n');
        }
    }

    private static void Write(char[][] grid, int row, int column, string name, int columns)
    {
        for (var k = 0; k < name.Length && column + k < columns; k++)
        {
            grid[row][column + k] = name[k];
        }
    }

    private static string KindOf(Placement p) => p.SymbolId[..p.SymbolId.IndexOf('.', StringComparison.Ordinal)];

    private static int Count(ImmutableArray<SceneAudit.Finding> findings, string kind) => findings.Count(f => f.Kind == kind);

    private static string N(double value) => Math.Round(value, 6).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Text(Point p) => $"({N(p.X)}, {N(p.Y)})";

    private static string Text(Box b) => $"[({N(b.X)}, {N(b.Y)}), ({N(b.Right)}, {N(b.Top)})]";

    private static string Text(IEnumerable<Point> points) => "[" + string.Join(", ", points.Select(Text)) + "]";
}
