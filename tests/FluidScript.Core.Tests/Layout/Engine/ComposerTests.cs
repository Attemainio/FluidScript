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
    [InlineData("zones.fluid")]
    [InlineData("zones-instruments.fluid")]
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
        var (scene, findings) = Solve("header-instruments.fluid");

        Assert.Empty(findings);
        Assert.Equal(X(scene, "N3"), X(scene, "N5"), 6);
    }
}
