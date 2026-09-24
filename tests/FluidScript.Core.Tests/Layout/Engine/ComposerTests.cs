using FluidScript.Core.Layout;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Tests.Model;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Layout.Engine;

/// <summary>
/// The composed engine's forms (<c>28</c> E3, P6.10 R4) where they go past the ladder engine: a header's plain branch
/// hangs as a column under its split, its merge straight under it (C14, <c>C-126</c>).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ComposerTests
{
    private static string Pending => Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Parity");

    private static (Scene Scene, List<string> Findings) Solve(string file)
    {
        var input = ContractFixture.Compile(File.ReadAllText(Path.Combine(Pending, file)));
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        var scene = LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model), LayoutEngineKind.Composed);
        return (scene, [.. SceneAudit.Findings(scene, input.Model).Select(static f => f.ToString())]);
    }

    private static double X(Scene scene, string id) => scene.Placements.Single(p => p.ComponentId == id).Inner.Centre.X;

    [Theory]
    [InlineData("step-12-zones.fluid")]
    [InlineData("step-12-zones-controls.fluid")]
    public void PlainZonesHangAsColumnsWithTheirMergesStraightUnderTheirSplits(string file)
    {
        // C-126: zones 1 and 2 are branches with no loop. Each hangs straight down from its split -- valve, load, the
        // return's node -- into its merge on the bottom rail directly under the split, so no return bends sideways
        // (C-127) and every sensor on a return has the pipe its bubble needs (C-128).
        var (scene, findings) = Solve(file);

        Assert.Empty(findings);
        Assert.Equal(X(scene, "NA1"), X(scene, "NB1"), 6);
        Assert.Equal(X(scene, "NA2"), X(scene, "NB2"), 6);
        Assert.Equal(X(scene, "NA1"), X(scene, "CV1"), 6);
        Assert.Equal(X(scene, "NA2"), X(scene, "CV2"), 6);
    }

    [Fact]
    public void AHangingBlockKeepsItsBubblesUnderTheRailAndTheRightSideClearsItsPipes()
    {
        // Step 8a with its controls: the AHU block hangs low enough that its valve's controller and the sensor on its
        // own supply clear the header by a margin (D-151); the radiators, the ring's right side, slide on until their
        // pipes clear the hanging block's boxes and its pipes theirs; and the merge N5 stands under the split N3 even
        // though the primary return's sensor asks for a longer run (C14, one more pass).
        var (scene, findings) = Solve("step-08a-header-parallel-controls.fluid");

        Assert.Empty(findings);
        Assert.Equal(X(scene, "N3"), X(scene, "N5"), 6);
    }

    [Fact]
    public void SeriesLoopsWithTheirControlsDrawClean()
    {
        // Step 8b with its controls: both blocks stand on the ring -- the radiators on the top rail, the AHU as its
        // right side -- so nothing hangs between the rails, and every bubble finds a free side.
        var (_, findings) = Solve("step-08b-header-series-controls.fluid");

        Assert.Empty(findings);
    }

    [Fact]
    public void EveryPipeNamesTheRuleThatLaidItAndNoneIsDiagonal()
    {
        // 28 A10: the trace names the rule behind every placement, and since P6.10's diagnostics package every laid run
        // too, under its connections' ids. A run a rule laid skew is refused and left to the router (A7), so no drawn
        // pipe is diagonal -- on the ladder, the pending scripts and the stress plant alike.
        var tests = Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout");
        var files = Directory.GetFiles(Path.Combine(tests, "Ladder"), "step-*.fluid")
            .Concat(Directory.GetFiles(Pending, "*.fluid"))
            .Concat(Directory.GetFiles(Path.Combine(tests, "Stress"), "*.fluid"))
            .Order(StringComparer.Ordinal);
        var refused = 0;

        foreach (var file in files)
        {
            var input = ContractFixture.Compile(File.ReadAllText(file));
            var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
            var scene = LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model), LayoutEngineKind.Composed);
            var named = scene.Provenance
                .Where(static n => n.Subject.EndsWith(']'))
                .SelectMany(static n => n.Subject[(n.Subject.LastIndexOf(" [", StringComparison.Ordinal) + 2)..^1].Split(", "))
                .ToHashSet(StringComparer.Ordinal);
            var name = Path.GetFileName(file);

            Assert.All(scene.Routes.Where(static r => r.Kind == "pipe"), r => Assert.True(named.Contains(r.ConnectionId), $"{name}: {r.ConnectionId} was laid by no named rule"));
            Assert.Empty(SceneAudit.Findings(scene, input.Model).Where(static f => f.Kind == "diagonal").Select(f => $"{name}: {f}"));
            refused += scene.Provenance.Count(static n => n.Reason.Contains("not orthogonal", StringComparison.Ordinal) && n.Reason.EndsWith("left to the router (A7)", StringComparison.Ordinal));
        }

        // The refusal's witness while C-130/C-131 stand: the stress plant's pieces lay two runs skew (a junction moved
        // after its feed was laid; a unit's descent to a rail that is not where the form thought).
        Assert.True(refused > 0, "no run was refused: the stress plant no longer lays one skew; find the refusal a new witness");
    }
}
