using FluidScript.Core.Diagnostics.Explanations;
using FluidScript.Core.Layout;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Model;
using FluidScript.Core.Tests.Model;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Layout;

/// <summary>
/// The predicate sweep (<c>62</c>, <c>D-71</c>): every predicate against every fixture, on the scene and never on a picture.
/// The hard constraints of <c>28</c> B are <see cref="SceneAudit"/>'s and are asserted empty by the ladder and the sample
/// gates; what stands here is the rest of <c>62</c>'s table as the ladder engine (<c>D-107</c>) answers it -- determinism,
/// the port-for-port bijection, normalised routes, the transform class, spacing as presentation, congruent assemblies and
/// edit stability.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LayoutPredicateTests
{
    private const int Builds = 10;

    private static string Ladder => Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Ladder");

    public static TheoryData<string> Fixtures =>
    [
        "m2-cooling-loop", "m2-simple-loop", "m2-substation", "m4-storage-header", "m2-distribution-header", "m1-syntax-tour", "header-200",
        "step-04-valve", "step-07-ring-one-branch", "step-08e-header-mixed", "step-10-instruments", "step-11a-two-loops", "step-11c-tour-loops",
    ];

    private static string Source(string name) => name switch
    {
        "header-200" => ReferenceModels.DistributionHeader(ReferenceModels.TwoHundredComponentConsumers),
        _ when name.StartsWith("step-", StringComparison.Ordinal) => File.ReadAllText(Path.Combine(Ladder, name + ".fluid")),
        _ => ContractFixture.Sample(name + ".fluid"),
    };

    private static (Scene Scene, ModelContractInput Input) Solve(string source)
    {
        var input = ContractFixture.Compile(source);
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        return (LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model)), input);
    }

    private static string Report(string source)
    {
        var (scene, input) = Solve(source);
        return SceneText.Render(scene, input.Graph, input.Model);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void L1TheReportIsByteIdenticalAcrossBuilds(string name)
    {
        var source = Source(name);
        var first = Report(source);

        for (var build = 1; build < Builds; build++)
        {
            Assert.Equal(first, Report(source));
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void L2EveryConnectionIsDrawnPortForPort(string name)
    {
        // The one predicate to keep if only one survives: a route landing on the wrong port of the right component draws a plant nobody described.
        var (scene, input) = Solve(Source(name));
        var byId = scene.Placements.ToDictionary(static p => p.ComponentId, StringComparer.Ordinal);

        for (var i = 0; i < input.Model.Connections.Length; i++)
        {
            var connection = input.Model.Connections[i];
            var route = Assert.Single(scene.Routes, r => r.ConnectionId == $"c{i}");
            AssertOnPort(name, route, route.Points[0], byId[connection.From.Component], connection.From.Port);
            AssertOnPort(name, route, route.Points[^1], byId[connection.To.Component], connection.To.Port);
        }

        Assert.Equal(input.Model.Connections.Length, scene.Routes.Count(static r => r.Kind == "pipe"));
    }

    private static void AssertOnPort(string name, Route route, Point end, Placement placement, string port)
    {
        if (port.Length > 0)
        {
            Assert.True(placement.Anchors.TryGetValue(port, out var anchor), $"{name} {route.ConnectionId}: {placement.ComponentId} has no port {port}");
            Assert.True(anchor.At == end, $"{name} {route.ConnectionId}: ends at {end}, not on {placement.ComponentId}.{port} at {anchor.At}");
        }
        else
        {
            Assert.True(placement.Anchors.Values.Any(a => a.At == end), $"{name} {route.ConnectionId}: ends at {end}, on no port of {placement.ComponentId}");
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void L7EveryRouteIsNormalised(string name)
    {
        // Axis-aligned, no zero-length segment, no collinear or backtracking point (26): what the wire promises the renderer.
        var (scene, _) = Solve(Source(name));

        foreach (var route in scene.Routes)
        {
            for (var i = 1; i < route.Points.Length; i++)
            {
                var a = route.Points[i - 1];
                var b = route.Points[i];
                var vertical = Math.Abs(a.X - b.X) < 1e-9;
                var horizontal = Math.Abs(a.Y - b.Y) < 1e-9;
                Assert.True(vertical != horizontal, $"{name} {route.ConnectionId}: segment {a} {b} is {(vertical ? "zero-length" : "not axis-aligned")}");

                if (i >= 2)
                {
                    var before = route.Points[i - 2];
                    var collinear = (Math.Abs(before.X - a.X) < 1e-9 && vertical) || (Math.Abs(before.Y - a.Y) < 1e-9 && horizontal);
                    Assert.False(collinear, $"{name} {route.ConnectionId}: {a} lies on the line {before} {b}; the polyline is not normalised");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void L10EveryTransformIsOneItsClassAdmits(string name)
    {
        // 28 A4, D-108: a standing kind is mirrored, never turned; an upright kind only mirrored; the rest turn freely.
        var (scene, _) = Solve(Source(name));

        foreach (var placement in scene.Placements)
        {
            var symbol = SymbolCatalog.All.FirstOrDefault(s => s.Id == placement.SymbolId);
            var admitted = symbol?.TransformClass switch
            {
                "standing" => placement.Rotation is 0 or 180,
                "upright" => placement.Rotation == 0,
                _ => true,
            };

            Assert.True(admitted, $"{name}: {placement.ComponentId} ({symbol?.TransformClass}) is turned {placement.Rotation}");
        }
    }

    [Fact]
    public async Task L14SpacingChangesPlacementsAndNothingCoreComputes()
    {
        // D-37: spacing is presentation. Two values move the boxes and leave the solved circuit byte for byte.
        var source = ContractFixture.Sample("m2-cooling-loop.fluid");
        Assert.DoesNotContain("spacing", source, StringComparison.Ordinal);
        var wide = source.Replace("fluidscript 1", "fluidscript 1\nspacing 1", StringComparison.Ordinal);
        Assert.NotEqual(source, wide);

        var (narrowScene, _) = Solve(source);
        var (wideScene, _) = Solve(wide);
        Assert.Equal(0.5, narrowScene.Margin);
        Assert.Equal(1.0, wideScene.Margin);
        Assert.NotEqual(narrowScene.Placements.Select(static p => p.Inner), wideScene.Placements.Select(static p => p.Inner));

        var narrowRun = await ContractFixture.SolveAsync(source);
        var wideRun = await ContractFixture.SolveAsync(wide);
        Assert.Equal(SolveExplanation.Render(narrowRun.Run!, "loop"), SolveExplanation.Render(wideRun.Run!, "loop"));
    }

    [Fact]
    public void L18EqualBranchesAreDrawnCongruent()
    {
        // 28 B priority 5, D-100 item 3 as the ring rules answer it: every branch of the header is the same block, so every
        // branch has the same width, the same height and its members at the same places relative to its own origin.
        var (scene, _) = Solve(Source("header-200"));
        var branches = scene.Groups.Where(static g => g.Kind == "loop").ToList();
        Assert.Equal(ReferenceModels.TwoHundredComponentConsumers, branches.Count);
        var byId = scene.Placements.ToDictionary(static p => p.ComponentId, StringComparer.Ordinal);

        List<(double X, double Y, double W, double H)> Shape(LayoutGroup g) =>
            [.. g.Members.Select(m => byId[m].Inner).Select(b => (Math.Round(b.X - g.Bounds.X, 6), Math.Round(b.Y - g.Bounds.Y, 6), b.Width, b.Height))];

        var first = Shape(branches[0]);

        foreach (var branch in branches.Skip(1))
        {
            Assert.Equal((branches[0].Bounds.Width, branches[0].Bounds.Height), (branch.Bounds.Width, branch.Bounds.Height));
            Assert.Equal(first, Shape(branch));
        }
    }

    [Fact]
    public void C15AnInstrumentStandsOnItsHostOneMarginOutAndItsRunMakesRoom()
    {
        // D-151, which retires L19's "adding an instrument moves no process symbol": a sensor and the node it reads, a
        // controller and the device it drives, are one footprint. In 11c TE5 reads NR2, an inline point on the level
        // rail from TV5 to PU5, and stands above it; PID5 drives TV5 and stands on its stem, up. Each is joined to its
        // host by a line one margin long, and no process box comes within a margin of either bubble -- the rail was
        // lengthened to hold TE5, where one clearance had laid its bubble over both TV5 and PU5.
        var (scene, input) = Solve(Source("step-11c-tour-loops"));
        var margin = LayoutSolver.MarginOf(input.Model);
        Placement Of(string id) => Assert.Single(scene.Placements, p => p.ComponentId == id);
        Route Line(string id) => Assert.Single(scene.Routes, r => r.ConnectionId == id);

        var (node, sensor) = (Of("NR2").Inner.Centre, Of("TE5").Inner);
        Assert.Equal(node.X, sensor.Centre.X, 9);
        Assert.Equal(node.Y + margin, sensor.Y, 9);
        Assert.Equal(margin, Line("TE5:measures").Length, 9);

        var (valve, controller) = (Of("TV5").Inner, Of("PID5").Inner);
        Assert.Equal(valve.Centre.X, controller.Centre.X, 9);
        Assert.Equal(valve.Top + margin, controller.Y, 9);
        Assert.Equal(margin, Line("PID5:actuates").Length, 9);

        foreach (var bubble in new[] { sensor, controller })
        {
            foreach (var process in scene.Placements.Where(p => !p.IsInline && p.ComponentId is not ("TE5" or "PID5")))
            {
                Assert.False(process.Inner.Intersects(bubble.Grow(margin)), $"{process.ComponentId} {process.Inner} is within a margin of {bubble}");
            }
        }
    }

    [Fact]
    public void C15AThreeWayValvesControllerStandsOnItsOnlyFreeSide()
    {
        // The user's example: a three-way valve whose connections leave left, right and down has one place for its
        // controller -- up, where its stem is. m4-demand-step's 3WV takes HE1 from the left, P1 to the right and the
        // recirculation from below.
        var (scene, input) = Solve(ContractFixture.Sample("m4-demand-step.fluid"));
        var margin = LayoutSolver.MarginOf(input.Model);
        var valve = Assert.Single(scene.Placements, static p => p.ComponentId == "3WV").Inner;
        var controller = Assert.Single(scene.Placements, static p => p.ComponentId == "TC1").Inner;

        Assert.Equal(valve.Centre.X, controller.Centre.X, 9);
        Assert.Equal(valve.Top + margin, controller.Y, 9);
    }

    [Fact]
    public void C15ASignalCrossesTheDrawingByTheFewestBendsThenTheShortestWay()
    {
        // D-152, the user's sketch: TC1 on 3WV's stem reads NS__TE under NS, and its line drops through the loop,
        // crossing the supply and the return, rather than round the drawing's right side (7.8 long, also two bends).
        // It never runs along a pipe and crosses each a quarter margin or more from the pipe's ends.
        var (scene, input) = Solve(ContractFixture.Sample("m4-demand-step.fluid"));
        var margin = LayoutSolver.MarginOf(input.Model);
        var line = Assert.Single(scene.Routes, static r => r.ConnectionId == "TC1:measures");
        var controller = Assert.Single(scene.Placements, static p => p.ComponentId == "TC1").Inner;

        Assert.Equal(4, line.Points.Length);
        Assert.Equal(5.5, line.Length, 9);
        Assert.Contains(line.Points, p => Math.Abs(p.X - controller.X) < 1e-9 && Math.Abs(p.Y - controller.Centre.Y) < 1e-9);

        Assert.DoesNotContain(SceneAudit.Findings(scene, input.Model), static f => f.First == "TC1:measures");

        foreach (var pipe in scene.Routes.Where(static r => r.Kind == "pipe"))
        {
            for (var s = 1; s < line.Points.Length; s++)
            {
                for (var t = 1; t < pipe.Points.Length; t++)
                {
                    if (Crossing(line.Points[s - 1], line.Points[s], pipe.Points[t - 1], pipe.Points[t]) is { } at)
                    {
                        Assert.True(Math.Min(at.ManhattanTo(pipe.Points[t - 1]), at.ManhattanTo(pipe.Points[t])) >= (margin / 4) - 1e-9, $"{pipe.ConnectionId} crossed at {at}, near its end");
                    }
                }
            }
        }
    }

    /// <summary>Where a vertical and a level segment cross strictly inside both, or nothing.</summary>
    private static Point? Crossing(Point a, Point b, Point c, Point d)
    {
        if (Math.Abs(a.X - b.X) > 1e-9)
        {
            (a, b, c, d) = (c, d, a, b);
        }

        if (Math.Abs(a.X - b.X) > 1e-9 || Math.Abs(c.Y - d.Y) > 1e-9)
        {
            return null;
        }

        var inside = a.X > Math.Min(c.X, d.X) + 1e-9 && a.X < Math.Max(c.X, d.X) - 1e-9 && c.Y > Math.Min(a.Y, b.Y) + 1e-9 && c.Y < Math.Max(a.Y, b.Y) - 1e-9;
        return inside ? new Point(a.X, c.Y) : null;
    }

    [Fact]
    public void L19AddingAComponentToOneBranchMovesOnlyThatBranchAndWhatItPushes()
    {
        var source = ContractFixture.Sample("m2-distribution-header.fluid");
        var edited = source
            .Replace("PU_AHU  pump", "PU_AHU  pump\nCV_AHU  valve kv=6.3", StringComparison.Ordinal)
            .Replace("TV_AHU.ab - PU_AHU - HE_AHU - NM_AHU", "TV_AHU.ab - PU_AHU - CV_AHU - HE_AHU - NM_AHU", StringComparison.Ordinal);
        Assert.NotEqual(source, edited);

        var (before, _) = Solve(source);
        var (after, _) = Solve(edited);
        Assert.Contains(after.Placements, static p => p.ComponentId == "CV_AHU");

        // The source and everything before the edited branch stands still.
        AssertSame(before, after, "HS1");

        // The branch after it is pushed along the rail as one piece: the same shape, translated.
        AssertCongruent(before, after, ["TV_RAD", "PU_RAD", "HE_RAD", "NM_RAD"]);
    }

    [Fact]
    public void L19AnEditInOneCircuitLeavesEveryOtherCircuitIdentical()
    {
        // C17: circuits are laid out on their own canvases and stacked, so an edit in the second leaves the first byte for byte.
        var source = Source("step-11a-two-loops");
        var edited = source
            .Replace("PU_C  pump", "PU_C  pump\nCV_C  valve kv=6.3", StringComparison.Ordinal)
            .Replace("TV_C - P_C", "TV_C - CV_C - P_C", StringComparison.Ordinal);
        Assert.NotEqual(source, edited);

        var (before, _) = Solve(source);
        var (after, _) = Solve(edited);
        Assert.Contains(after.Placements, static p => p.ComponentId == "CV_C");

        foreach (var id in new[] { "PU_H", "HS_H", "LOAD", "CV_H" })
        {
            AssertSame(before, after, id);
        }
    }

    private static void AssertSame(Scene before, Scene after, string id)
    {
        var a = Assert.Single(before.Placements, p => p.ComponentId == id);
        var b = Assert.Single(after.Placements, p => p.ComponentId == id);
        Assert.True(a.Inner == b.Inner && a.Rotation == b.Rotation && a.Mirrored == b.Mirrored, $"{id} moved from {a.Inner} to {b.Inner}");
    }

    private static void AssertCongruent(Scene before, Scene after, string[] ids)
    {
        var a = ids.Select(id => before.Placements.Single(p => p.ComponentId == id).Inner).ToList();
        var b = ids.Select(id => after.Placements.Single(p => p.ComponentId == id).Inner).ToList();
        var dx = b[0].X - a[0].X;
        var dy = b[0].Y - a[0].Y;

        for (var k = 0; k < ids.Length; k++)
        {
            Assert.True(a[k].Offset(dx, dy) == b[k], $"{ids[k]}: {a[k]} moved to {b[k]}, not the branch's translation ({dx}, {dy})");
        }
    }
}
