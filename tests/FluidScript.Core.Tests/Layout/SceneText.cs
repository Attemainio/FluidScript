using System.Globalization;
using System.Text;

using FluidScript.Core.Binding;
using FluidScript.Core.Language;
using FluidScript.Core.Layout;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Tests.Layout;

/// <summary>
/// A scene as text (<c>28</c> §31), for diagnosing a layout without the SVG: every symbol with its group,
/// rotation, inner and outer boxes and each port at both boundaries with its flow vector; every connection
/// with its points, length, bends and crossings and the envelope its clearance occupies; every group; the
/// validation totals; and the interference the audit finds. <c>y</c> grows upward.
/// </summary>
internal static class SceneText
{
    public static string Render(Scene scene, CircuitGraph graph, SemanticModel model)
    {
        var text = new StringBuilder();
        var m = scene.Margin;
        text.Append("margin ").Append(N(m)).Append('\n');
        text.Append("extent ").Append(Text(scene.Extent)).Append("\n\nCOMPONENTS\n");

        foreach (var p in scene.Placements)
        {
            var flow = graph.Components.FirstOrDefault(c => c.Name == p.ComponentId);
            var kind = flow?.Kind ?? p.SymbolId[..p.SymbolId.IndexOf('.', StringComparison.Ordinal)];
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

        text.Append("\nVALIDATION\n");
        text.Append(" hard ").Append(findings.Count(f => f.Hard))
            .Append(" (inner-in-inner ").Append(findings.Count(f => f.Kind == "inner-in-inner"))
            .Append(", clearance ").Append(findings.Count(f => f.Kind == "clearance"))
            .Append(", pipe-in-inner ").Append(findings.Count(f => f.Kind == "pipe-in-inner"))
            .Append(", ends-off-port ").Append(findings.Count(f => f.Kind == "ends-off-port"))
            .Append(", stub-short ").Append(findings.Count(f => f.Kind == "stub-short"))
            .Append(", pipes-overlap ").Append(findings.Count(f => f.Kind == "pipes-overlap"))
            .Append(", loop-counter-clockwise ").Append(findings.Count(f => f.Kind == "loop-counter-clockwise"))
            .Append(", losing-side-right ").Append(findings.Count(f => f.Kind == "losing-side-right")).Append(")\n");
        text.Append(" soft ").Append(findings.Count(f => !f.Hard))
            .Append(" (pipe-in-outer ").Append(findings.Count(f => f.Kind == "pipe-in-outer"))
            .Append(", pipe-beside-pipe ").Append(findings.Count(f => f.Kind == "pipe-beside-pipe"))
            .Append(", pipes-cross ").Append(findings.Count(f => f.Kind == "pipes-cross")).Append(")\n");
        text.Append(" bends ").Append(totalBends).Append(" length ").Append(N(totalLength)).Append('\n');

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

    private static string N(double value) => Math.Round(value, 6).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Text(Point p) => $"({N(p.X)}, {N(p.Y)})";

    private static string Text(Box b) => $"[({N(b.X)}, {N(b.Y)}), ({N(b.Right)}, {N(b.Top)})]";

    private static string Text(IEnumerable<Point> points) => "[" + string.Join(", ", points.Select(Text)) + "]";
}

