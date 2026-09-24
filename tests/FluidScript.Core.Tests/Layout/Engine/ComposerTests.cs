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
        // 28 A10: the trace names the rule behind every placement, and since D-155 every laid run too, under its
        // connections' ids. A run a rule laid skew is refused and left to the router (A7), so no drawn pipe is
        // diagonal -- on the ladder, the pending scripts and the stress plant alike.
        var tests = Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout");
        var files = Directory.GetFiles(Path.Combine(tests, "Ladder"), "step-*.fluid")
            .Concat(Directory.GetFiles(Pending, "*.fluid"))
            .Concat(Directory.GetFiles(Path.Combine(tests, "Stress"), "*.fluid"))
            .Order(StringComparer.Ordinal);

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
            // A refusal names the rule that laid the run skew (C-133 had two such runs until D-156's bands removed the last).
            Assert.All(scene.Provenance.Where(static n => n.Reason.Contains("not orthogonal", StringComparison.Ordinal)), n => Assert.Matches("^[CE][0-9]+$", n.Rule));
        }
    }

    [Fact]
    public void ADutyStandbyPairRunsAsARowOverTheRailSquareWithTheSpine()
    {
        // C14 for a branch that rejoins its rail (C-130), as D-158 settles it: the stress plant's pump pair splits at NP_S
        // and rejoins at NP_M, both on the ring's top rail. PU_S1's branch, declared first, stands a margin over the duty
        // pair, each member square with its counterpart, the row dropping into NP_M -- out of the ring, in script order.
        var input = ContractFixture.Compile(File.ReadAllText(Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Stress", "plant-distribution.fluid")));
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        var scene = LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model), LayoutEngineKind.Composed);
        Box Inner(string id) => scene.Placements.Single(p => p.ComponentId == id).Inner;
        var findings = SceneAudit.Findings(scene, input.Model);

        Assert.True(Inner("PU_S1").Y >= Inner("PU_S2").Top + scene.Margin - 1e-9, "the standby pump stands a margin over the duty pump");
        Assert.Equal(Inner("PU_S2").Centre.X, Inner("PU_S1").Centre.X, 6);
        Assert.Equal(Inner("CV_S2").Centre.X, Inner("CV_S1").Centre.X, 6);
        Assert.True(Inner("NP_M").X >= Inner("CV_S1").Right + scene.Margin - 1e-9, "the merge stands right of the row");
        Assert.DoesNotContain(findings, f => f.Hard && (f.First is "NP_S" or "NP_M" or "PU_S1" or "CV_S1" || f.Second is "NP_S" or "NP_M" or "PU_S1" or "CV_S1"));
        Assert.Contains(scene.Provenance, n => n.Rule == "C14" && n.Subject == "NP_S" && n.Reason.Contains("parallel row over it", StringComparison.Ordinal) && n.Reason.Contains("square", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("step-12-zones.fluid", "CV1", "CV3", "LD1", "LD3", "NR1", "NR3")]
    [InlineData("step-12-zones-controls.fluid", "CV1", "CV3", "LD1", "LD3", "NR1", "NR3")]
    public void AHeadersSpineOnTheRightSideStandsAsAColumnLikeItsSiblings(string file, string valve, string spineValve, string load, string spineLoad, string point, string spinePoint)
    {
        // D-158: zone 3 is the header's spine and the ring's right side. It stands as zone 1 and zone 2 hang -- its valve
        // over its load, both level with theirs, its return's point on its drop -- so the three zones read the same.
        var (scene, findings) = Solve(file);
        Box Inner(string id) => scene.Placements.Single(p => p.ComponentId == id).Inner;

        Assert.Empty(findings);
        Assert.Equal(Inner(load).Centre.X - Inner(valve).Centre.X, Inner(spineLoad).Centre.X - Inner(spineValve).Centre.X, 6);
        Assert.Equal(Inner(valve).Centre.Y, Inner(spineValve).Centre.Y, 6);
        Assert.Equal(Inner(load).Centre.Y, Inner(spineLoad).Centre.Y, 6);
        // The sibling's return ends on its merge's dot, the spine's on the rail's corner: their points differ by half the dot.
        Assert.True(Math.Abs(Inner(point).Centre.Y - Inner(spinePoint).Centre.Y) <= 0.05 + 1e-9, "the return's point stands on the drop, level with its siblings'");
        Assert.Equal(X(scene, spineValve), Inner(spinePoint).Centre.X, 6);
        Assert.Contains(scene.Provenance, n => n.Subject == spineValve && n.Reason.Contains("right side's column", StringComparison.Ordinal));
    }

    [Fact]
    public void ASensorStandsOnItsControllersSideOfThePipe()
    {
        // D-158: TE_R1 reads the return RAD1's valve CV_R1 holds. TC_R1 stands on the valve's actuator side, right of the
        // column, so TE_R1 takes the right side of NR1 too and the signal between them crosses no pipe; likewise for the
        // pump pair's controller over the row and its pressure sensor over the rail.
        var input = ContractFixture.Compile(File.ReadAllText(Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Stress", "plant-distribution-controls.fluid")));
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        var scene = LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model), LayoutEngineKind.Composed);
        Box Inner(string id) => scene.Placements.Single(p => p.ComponentId == id).Inner;

        foreach (var (sensor, point, controller, valve) in new[] { ("TE_R1", "NR1", "TC_R1", "CV_R1"), ("TE_R2", "NR2", "TC_R2", "CV_R2"), ("TE_R3", "NR3", "TC_R3", "CV_R3") })
        {
            Assert.True(Inner(sensor).X > Inner(point).Centre.X && Inner(controller).X > Inner(valve).Centre.X, $"{sensor} and {controller} stand right of their column");
        }

        Assert.True(Inner("PC_S1").Y > Inner("PU_S1").Top && Inner("PE_A2").Y > Inner("NA2").Top, "the pump pair's controller and its sensor stand over their pipes");
    }

    [Fact]
    public void HeadersInSeriesAreStackedBands()
    {
        // D-156 (C-131): the radiator header's return feeds the injection header. The radiator header is a band of its
        // own -- RAD3 on its right side, its columns returning to NRB1/NRB2 under their splits -- and its return steps
        // down on the left to the injection header's rail, whose blocks hang to the ring's bottom rail. Hard 0.
        var input = ContractFixture.Compile(File.ReadAllText(Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Stress", "plant-distribution.fluid")));
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        var scene = LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model), LayoutEngineKind.Composed);
        Point At(string id) => scene.Placements.Single(p => p.ComponentId == id).Inner.Centre;

        Assert.Empty(SceneAudit.Findings(scene, input.Model).Where(static f => f.Hard).Select(static f => f.ToString()));
        Assert.Equal(At("NA1").X, At("NRB1").X, 6);
        Assert.Equal(At("NA2").X, At("NRB2").X, 6);
        Assert.True(At("NC1").Y < At("NRB1").Y && At("NC1").X <= At("NRB1").X + 1e-6, "the injection header's rail runs under the radiator header's return, from the step at its left");
        Assert.True(At("ND1").Y < At("NM_AHU").Y, "the injection blocks return to the ring's bottom rail");
        Assert.Contains(scene.Provenance, n => n.Subject == "NA1" && n.Reason.Contains("band of its own, RAD3 on its right side", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLastBandsRightSideStandsPastTheBlocksHungFromItsRail()
    {
        // Piece B with its controls: the floor block's sensor point widens its level pipe, leaving a gap between its pump
        // and its load wide enough for the DHW load. The right side's descent crosses no pipe the form has laid and its box
        // keeps clear of the sensor bubbles still to come, so HE_DHW stands past the whole block. Hard 0.
        var input = ContractFixture.Compile(File.ReadAllText(Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Stress", "plant-distribution-controls.fluid")));
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        var scene = LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model), LayoutEngineKind.Composed);
        Box Inner(string id) => scene.Placements.Single(p => p.ComponentId == id).Inner;

        Assert.Empty(SceneAudit.Findings(scene, input.Model).Where(static f => f.Hard).Select(static f => f.ToString()));
        Assert.True(Inner("HE_DHW").X > Inner("HE_FLR").Right, "the DHW load stands right of the floor block");
    }

    [Fact]
    public void BoilersInParallelRiseAsColumnsWithThePumpInTheRiser()
    {
        // D-159 (C-129): B1 and B2 are siblings between NB_S and NB_M, the tank the one consumer. B1's branch is the
        // ring's left side and B2's rises beside it, each a column -- pump, boiler, valve, the pump standing in the
        // riser -- level with each other, NB_S straight under NB_M; the tank takes the boiler loop on its west flank.
        var input = ContractFixture.Compile(File.ReadAllText(Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Stress", "plant-production.fluid")));
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        var scene = LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model), LayoutEngineKind.Composed);
        Placement At(string id) => scene.Placements.Single(p => p.ComponentId == id);

        Assert.Empty(SceneAudit.Findings(scene, input.Model).Where(static f => f.Hard).Select(static f => f.ToString()));

        foreach (var (one, two) in new[] { ("PU_B1", "PU_B2"), ("B1", "B2"), ("CV_B1", "CV_B2") })
        {
            Assert.Equal(At(one).Inner.Centre.Y, At(two).Inner.Centre.Y, 6);
        }

        Assert.True(At("PU_B1").Inner.Centre.Y < At("B1").Inner.Centre.Y && At("B1").Inner.Centre.Y < At("CV_B1").Inner.Centre.Y, "the branch rises: pump, boiler, valve");
        Assert.True(At("PU_B2").Rotation is 90 or 270, "the pump stands in the riser");
        Assert.Equal(At("NB_M").Inner.Centre.X, At("NB_S").Inner.Centre.X, 6);
        Assert.Contains(scene.Provenance, static n => n.Subject == "NB_M" && n.Reason.Contains("rises as a column", StringComparison.Ordinal));
    }

    [Fact]
    public void ATankSharedByTwoLoopsTakesALoopPerFlank()
    {
        // D-157 (C-129): the boiler loop enters T1 by in and leaves by out[2]; the secondary leaves by out and returns by
        // in[2]. The boiler loop's two ports stand on T1's west flank and the secondary's on its east, so neither loop
        // crosses the tank; the secondary is an attached ring laid by C2 with T1 as its fixed head, its load right of it.
        var (scene, findings) = Solve("step-13a-buffer-tank.fluid");
        var tank = scene.Placements.Single(static p => p.ComponentId == "T1");

        Assert.Empty(findings);
        Assert.Equal(tank.Inner.X, tank.Anchors["in1"].At.X, 6);
        Assert.Equal(tank.Inner.X, tank.Anchors["out2"].At.X, 6);
        Assert.Equal(tank.Inner.Right, tank.Anchors["out1"].At.X, 6);
        Assert.Equal(tank.Inner.Right, tank.Anchors["in2"].At.X, 6);
        Assert.True(X(scene, "B1") < tank.Inner.X && X(scene, "LOAD") > tank.Inner.Right, "the boiler stands west of the tank and the load east");
        Assert.Contains(scene.Provenance, static n => n.Reason.Contains("attached ring at T1", StringComparison.Ordinal));
    }
}
