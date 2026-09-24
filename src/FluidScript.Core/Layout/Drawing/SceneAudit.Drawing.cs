using System.Collections.Immutable;

using FluidScript.Core.Language.Binding;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout.Drawing;

public static partial class SceneAudit
{
    /// <summary>A7: every pipe and every signal line is an orthogonal polyline -- each segment level or plumb.</summary>
    private static void Orthogonal(Scene scene, ImmutableArray<Finding>.Builder findings)
    {
        foreach (var route in scene.Routes)
        {
            for (var k = 1; k < route.Points.Length; k++)
            {
                var a = route.Points[k - 1];
                var b = route.Points[k];

                if (Math.Abs(a.X - b.X) > Eps && Math.Abs(a.Y - b.Y) > Eps)
                {
                    findings.Add(new Finding("diagonal", route.ConnectionId, route.Kind, $"segment {Text(a)} {Text(b)} is neither level nor plumb (28 A7)"));
                }
            }
        }
    }

    /// <summary>H6: no inline element sits on a corner -- the two pipes that meet at an inline point leave it in opposite directions.</summary>
    private static void Corners(Scene scene, List<Route> pipes, ImmutableArray<Finding>.Builder findings)
    {
        foreach (var point in scene.Placements.Where(static p => p.IsInline))
        {
            var at = point.Inner.Centre;
            var leaving = Leaving(pipes, [at]);

            if (leaving.Count == 2 && leaving[0].Side != leaving[1].Side.Opposite)
            {
                findings.Add(new Finding("inline-on-corner", point.ComponentId, $"{leaving[0].Pipe} {leaving[1].Pipe}", $"the pipes leave {Text(at)} {leaving[0].Side} and {leaving[1].Side}; an inline element sits on a straight run (28 H6)"));
            }
        }
    }

    /// <summary>H8: every connection is drawn as a pipe and every component is placed -- a pipe with cells (<c>C-124</c>) as its cells.</summary>
    private static void Drawn(Scene scene, SemanticModel model, List<Route> pipes, ImmutableArray<Finding>.Builder findings)
    {
        var routed = pipes.Select(static r => r.ConnectionId).ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < model.Connections.Length; i++)
        {
            var id = "c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (!routed.Contains(id))
            {
                var c = model.Connections[i];
                findings.Add(new Finding("undrawn", id, $"{c.From.Component} - {c.To.Component}", "the connection has no pipe (28 H8)"));
            }
        }

        var placed = scene.Placements.Select(static p => p.ComponentId).ToHashSet(StringComparer.Ordinal);

        foreach (var component in model.Components)
        {
            if (!placed.Contains(component.Name) && !placed.Any(p => p.StartsWith(component.Name + "#", StringComparison.Ordinal)))
            {
                findings.Add(new Finding("undrawn", component.Name, component.WrittenKind, "the component has no placement (28 H8)"));
            }
        }
    }

    /// <summary>A6: a junction -- a node with three or more pipes -- is a dot with one pipe per side.</summary>
    private static void Junctions(Scene scene, List<Route> pipes, ImmutableArray<Finding>.Builder findings)
    {
        foreach (var node in scene.Placements.Where(static p => !p.IsInline && KindOf(p) == "node"))
        {
            var leaving = Leaving(pipes, [.. node.Anchors.Values.Select(static a => a.At)]);

            if (leaving.Count < 3)
            {
                continue;
            }

            foreach (var side in leaving.GroupBy(static l => l.Side).Where(static g => g.Count() > 1))
            {
                findings.Add(new Finding("junction-side", node.ComponentId, string.Join(" ", side.Select(static l => l.Pipe)), $"{side.Count()} pipes leave the junction {side.Key}; a junction takes one pipe per side (28 A6)"));
            }
        }
    }

    /// <summary>H10 for a tank (<c>29</c> step 9): its connected charging ports stand left of its connected discharging ports.</summary>
    private static void Tanks(Scene scene, List<Route> pipes, ImmutableArray<Finding>.Builder findings)
    {
        foreach (var tank in scene.Placements.Where(static p => KindOf(p) == "tank"))
        {
            var used = tank.Anchors.Where(a => pipes.Any(r => r.Points.Length > 0 && (r.Points[0].ManhattanTo(a.Value.At) < Eps || r.Points[^1].ManhattanTo(a.Value.At) < Eps))).ToList();
            var charging = used.Where(static a => a.Key.StartsWith("in", StringComparison.Ordinal)).Select(static a => a.Value.At.X).ToList();
            var discharging = used.Where(static a => a.Key.StartsWith("out", StringComparison.Ordinal)).Select(static a => a.Value.At.X).ToList();

            if (charging.Count > 0 && discharging.Count > 0 && charging.Average() > discharging.Average() + Eps)
            {
                findings.Add(new Finding("charging-side-right", tank.ComponentId, "in", $"the charging ports sit at x {N(charging.Average())}, the discharging at {N(discharging.Average())}; heat flows left to right (28 H10)"));
            }
        }
    }

    /// <summary>A pipe that runs through another run's inline point: where the point carries a sensor, which pipe it measures is no longer legible.</summary>
    private static void Points(Scene scene, List<Route> pipes, ImmutableArray<Finding>.Builder findings)
    {
        foreach (var point in scene.Placements.Where(static p => p.IsInline))
        {
            var at = point.Inner.Centre;

            foreach (var pipe in pipes)
            {
                if (pipe.Points.Length < 2 || pipe.Points[0].ManhattanTo(at) < Eps || pipe.Points[^1].ManhattanTo(at) < Eps)
                {
                    continue;
                }

                for (var k = 1; k < pipe.Points.Length; k++)
                {
                    if (Segments.Collinear(pipe.Points[k - 1], at, pipe.Points[k], Eps) && Math.Abs(pipe.Points[k - 1].ManhattanTo(at) + at.ManhattanTo(pipe.Points[k]) - pipe.Points[k - 1].ManhattanTo(pipe.Points[k])) < Eps)
                    {
                        findings.Add(new Finding("pipe-through-point", pipe.ConnectionId, point.ComponentId, $"segment {Text(pipe.Points[k - 1])} {Text(pipe.Points[k])} runs through {Text(at)}"));
                        break;
                    }
                }
            }
        }
    }

    /// <summary>The pipes that end on one of <paramref name="points"/>, each with the direction it leaves that point by.</summary>
    private static List<(Direction Side, string Pipe)> Leaving(List<Route> pipes, Point[] points)
    {
        var leaving = new List<(Direction Side, string Pipe)>();

        foreach (var pipe in pipes.Where(static r => r.Points.Length >= 2))
        {
            foreach (var at in points)
            {
                if (pipe.Points[0].ManhattanTo(at) < Eps && Direction.Of(pipe.Points[1].Offset(-at.X, -at.Y)) is { } first)
                {
                    leaving.Add((first, pipe.ConnectionId));
                }

                if (pipe.Points[^1].ManhattanTo(at) < Eps && Direction.Of(pipe.Points[^2].Offset(-at.X, -at.Y)) is { } last)
                {
                    leaving.Add((last, pipe.ConnectionId));
                }
            }
        }

        return leaving;
    }

    /// <summary>The kind a placement's symbol draws: the symbol id's first part (<c>node</c>, <c>tank</c>).</summary>
    private static string KindOf(Placement p) => p.SymbolId.Split('.')[0];
}
