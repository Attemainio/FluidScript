using System.Globalization;
using System.Text;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Model;

namespace FluidScript.Core.Tests.Layout.Drawing;

/// <summary>Draws a scene as SVG the way the renderer will: strokes inside each inner box, routes as polylines.</summary>
/// <remarks>A test instrument (<c>D-100</c>): a session cannot see a canvas, so it looks at this.</remarks>
public static class SceneSvg
{
    private const double Scale = 60;

    /// <summary>The scene as SVG. World <c>y</c> grows upward (<c>28</c> §1); the flip to screen happens here, once, where units become pixels.</summary>
    /// <remarks>
    /// Everything the engine reasons on is visible: the margin area (outer box) as a yellow field, the symbol
    /// area (inner box) as a red field, every node -- junction or not -- as its box with its name, every port
    /// as a red dot with a small arrow along its flow vector, and every route as a blue line.
    /// </remarks>
    /// <param name="scene">The solved scene.</param>
    /// <param name="boxes">Whether to draw the inner and outer areas.</param>
    /// <returns>The SVG document.</returns>
    public static string Render(Scene scene, bool boxes = true)
    {
        var e = scene.Extent.Grow(scene.Margin);
        var svg = new StringBuilder();
        var width = e.Width * Scale;
        var height = e.Height * Scale;
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width:0}\" height=\"{height:0}\" viewBox=\"{e.X * Scale:0.##} {-e.Top * Scale:0.##} {width:0.##} {height:0.##}\" font-family=\"sans-serif\" font-size=\"11\">\n");
        svg.Append("<rect x=\"").Append(F(e.X * Scale)).Append("\" y=\"").Append(Y(e.Top)).Append("\" width=\"").Append(F(width)).Append("\" height=\"").Append(F(height)).Append("\" fill=\"white\"/>\n");

        // The groups (A8) under everything: a dashed frame outside the bounds, wider for a group that holds others, with the id at its top-left corner.
        foreach (var g in scene.Groups)
        {
            var holds = scene.Groups.Count(o => !ReferenceEquals(o, g) && o.Members.All(g.Members.Contains));
            var b = g.Bounds.Grow(scene.Margin / 4 * (1 + holds));
            svg.Append("<rect x=\"").Append(F(b.X * Scale)).Append("\" y=\"").Append(Y(b.Top)).Append("\" width=\"").Append(F(b.Width * Scale)).Append("\" height=\"").Append(F(b.Height * Scale))
                .Append("\" fill=\"#eef2ff\" fill-opacity=\"0.35\" stroke=\"#5b6abf\" stroke-width=\"1\" stroke-dasharray=\"6 3\"/>\n");
            svg.Append("<text x=\"").Append(F((b.X * Scale) + 4)).Append("\" y=\"").Append(F((-b.Top * Scale) + 12)).Append("\" fill=\"#5b6abf\" font-size=\"10\">").Append(g.Id).Append("</text>\n");
        }

        // The areas next, still under the symbols: margins, then symbols, so a symbol inside another's margin shows.
        if (boxes)
        {
            foreach (var placement in scene.Placements.Where(static p => !p.IsInline))
            {
                Area(svg, placement.Outer, "#fff3b0", "#e6c94c", "2 2");
            }

            foreach (var placement in scene.Placements.Where(static p => !p.IsInline))
            {
                Area(svg, placement.Inner, "#ffd6d6", "#e07070", null);
            }
        }

        // The routes from the back -- signals, then return pipes, then supply pipes (28 C16) -- each broken around the crossings it owns, so the route in front runs through.
        foreach (var route in scene.Routes.OrderBy(static r => r.Layer switch { "supply" => 2, "return" => 1, _ => 0 }))
        {
            var dash = route.Kind == "signal" ? " stroke-dasharray=\"4 3\"" : string.Empty;

            foreach (var piece in Pieces(route, scene.Margin / 4))
            {
                var points = string.Join(" ", piece.Select(p => $"{F(p.X * Scale)},{Y(p.Y)}"));
                svg.Append("<polyline points=\"").Append(points).Append("\" fill=\"none\" stroke=\"#1f5f8b\" stroke-width=\"2\"").Append(dash).Append("/>\n");
            }
        }

        foreach (var placement in scene.Placements)
        {
            var symbol = SymbolCatalog.All.First(s => s.Id == placement.SymbolId);
            var inner = placement.Inner;

            // An inline element (D-105) has no box; a hollow dot marks a node's point and a pipe keeps its label.
            if (placement.IsInline)
            {
                if (symbol.Id.StartsWith("node", StringComparison.Ordinal))
                {
                    svg.Append("<circle cx=\"").Append(F(inner.X * Scale)).Append("\" cy=\"").Append(Y(inner.Y)).Append("\" r=\"3\" fill=\"white\" stroke=\"#1f5f8b\"/>\n");
                }

                svg.Append("<text x=\"").Append(F(placement.LabelAt.X * Scale)).Append("\" y=\"").Append(Y(placement.LabelAt.Y)).Append("\" text-anchor=\"middle\" fill=\"#777\" font-style=\"italic\" font-size=\"9\">").Append(placement.ComponentId).Append("</text>\n");
                continue;
            }

            // Symbol space (y up) → screen: mirror, flip, rotate clockwise, scale to the inner box, translate to its centre.
            var c = inner.Centre;
            var sx = Scale;
            var transform = $"translate({F(c.X * Scale)} {Y(c.Y)}) rotate({placement.Rotation}) scale({(placement.Mirrored ? -sx : sx)} {-sx})";
            svg.Append("<g transform=\"").Append(transform).Append("\" stroke=\"#222\" stroke-width=\"").Append(F(1.5 / Scale)).Append("\" fill=\"none\" vector-effect=\"non-scaling-stroke\">\n");

            foreach (var p in symbol.Primitives)
            {
                var fill = p.Fill switch { "state" => "#dfe9f3", "stroke" => "#222", _ => "none" };
                var dash = p.Dashed == true ? $" stroke-dasharray=\"{F(3 / Scale)} {F(2 / Scale)}\"" : string.Empty;

                switch (p.Kind)
                {
                    case "rect":
                        svg.Append("<rect x=\"").Append(F(p.X!.Value)).Append("\" y=\"").Append(F(p.Y!.Value)).Append("\" width=\"").Append(F(p.Width!.Value)).Append("\" height=\"").Append(F(p.Height!.Value)).Append("\" fill=\"").Append(fill).Append('"').Append(dash).Append("/>\n");
                        break;
                    case "circle":
                        svg.Append("<circle cx=\"").Append(F(p.X!.Value)).Append("\" cy=\"").Append(F(p.Y!.Value)).Append("\" r=\"").Append(F(p.R!.Value)).Append("\" fill=\"").Append(fill).Append('"').Append(dash).Append("/>\n");
                        break;
                    case "line":
                        svg.Append("<line x1=\"").Append(F(p.From!.Value[0])).Append("\" y1=\"").Append(F(p.From.Value[1])).Append("\" x2=\"").Append(F(p.To!.Value[0])).Append("\" y2=\"").Append(F(p.To.Value[1])).Append("\"/>\n");
                        break;
                    default:
                        var pts = new StringBuilder();
                        for (var i = 0; i < p.Points!.Value.Length; i += 2)
                        {
                            pts.Append(F(p.Points.Value[i])).Append(',').Append(F(p.Points.Value[i + 1])).Append(' ');
                        }

                        svg.Append('<').Append(p.Kind).Append(" points=\"").Append(pts.ToString().Trim()).Append("\" fill=\"").Append(fill).Append("\"/>\n");
                        break;
                }
            }

            svg.Append("</g>\n");

            // Each port: a dot on the inner anchor and an arrow the length of the stub along the flow vector.
            foreach (var (_, anchor) in placement.Anchors)
            {
                var tip = anchor.At.Towards(anchor.Flow, scene.Margin * 0.6);
                var back = tip.Towards(anchor.Flow, -0.12);
                var left = back.Towards(anchor.Flow.TurnLeft, 0.07);
                var right = back.Towards(anchor.Flow.TurnRight, 0.07);
                svg.Append("<line x1=\"").Append(F(anchor.At.X * Scale)).Append("\" y1=\"").Append(Y(anchor.At.Y)).Append("\" x2=\"").Append(F(tip.X * Scale)).Append("\" y2=\"").Append(Y(tip.Y)).Append("\" stroke=\"#c0392b\" stroke-width=\"1\"/>\n");
                svg.Append("<polygon points=\"").Append(F(tip.X * Scale)).Append(',').Append(Y(tip.Y)).Append(' ').Append(F(left.X * Scale)).Append(',').Append(Y(left.Y)).Append(' ').Append(F(right.X * Scale)).Append(',').Append(Y(right.Y)).Append("\" fill=\"#c0392b\"/>\n");
                svg.Append("<circle cx=\"").Append(F(anchor.At.X * Scale)).Append("\" cy=\"").Append(Y(anchor.At.Y)).Append("\" r=\"2.5\" fill=\"#c0392b\"/>\n");
            }

            svg.Append("<text x=\"").Append(F(placement.LabelAt.X * Scale)).Append("\" y=\"").Append(Y(placement.LabelAt.Y)).Append("\" text-anchor=\"middle\" fill=\"#333\">").Append(placement.ComponentId).Append("</text>\n");
        }

        svg.Append("</svg>\n");
        return svg.ToString();
    }

    /// <summary>The route's polyline cut at its hops (28 C16): a gap of <paramref name="gap"/> on either side of each crossing shows this route passing behind the other.</summary>
    private static IEnumerable<List<Point>> Pieces(Route route, double gap)
    {
        var piece = new List<Point> { route.Points[0] };

        for (var i = 1; i < route.Points.Length; i++)
        {
            var a = route.Points[i - 1];
            var b = route.Points[i];
            var dx = Math.Sign(b.X - a.X);
            var dy = Math.Sign(b.Y - a.Y);
            var on = route.Hops
                .Where(h => Math.Abs(((h.X - a.X) * dy) - ((h.Y - a.Y) * dx)) < 1e-9 && ((h.X - a.X) * dx) + ((h.Y - a.Y) * dy) > 0 && ((b.X - h.X) * dx) + ((b.Y - h.Y) * dy) > 0)
                .OrderBy(h => ((h.X - a.X) * dx) + ((h.Y - a.Y) * dy));

            foreach (var h in on)
            {
                piece.Add(new Point(h.X - (dx * gap), h.Y - (dy * gap)));
                yield return piece;
                piece = [new Point(h.X + (dx * gap), h.Y + (dy * gap))];
            }

            piece.Add(b);
        }

        yield return piece;
    }

    private static void Area(StringBuilder svg, Box box, string fill, string stroke, string? dash)
    {
        svg.Append("<rect x=\"").Append(F(box.X * Scale)).Append("\" y=\"").Append(Y(box.Top)).Append("\" width=\"").Append(F(box.Width * Scale)).Append("\" height=\"").Append(F(box.Height * Scale))
            .Append("\" fill=\"").Append(fill).Append("\" fill-opacity=\"0.45\" stroke=\"").Append(stroke).Append('"');

        if (dash is not null)
        {
            svg.Append(" stroke-dasharray=\"").Append(dash).Append('"');
        }

        svg.Append("/>\n");
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>A world <c>y</c> as a screen coordinate: scaled and flipped.</summary>
    private static string Y(double y) => F(-y * Scale);
}

