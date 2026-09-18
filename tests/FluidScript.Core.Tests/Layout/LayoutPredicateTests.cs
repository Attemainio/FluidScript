using FluidScript.Core.Diagnostics;
using FluidScript.Core.Layout;
using FluidScript.Core.Model;
using FluidScript.Core.Solvers;
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
    public void L19AddingAnInstrumentMovesNoProcessSymbol()
    {
        // Step 10 is step 4 with a sensor, a controller and the node the sensor observes written out: every process box stands where it stood.
        var (before, _) = Solve(Source("step-04-valve"));
        var (after, _) = Solve(Source("step-10-instruments"));

        foreach (var placement in before.Placements.Where(static p => !p.IsInline))
        {
            var moved = Assert.Single(after.Placements, p => p.ComponentId == placement.ComponentId);
            Assert.True(placement.Inner == moved.Inner, $"{placement.ComponentId} moved from {placement.Inner} to {moved.Inner}");
        }
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
