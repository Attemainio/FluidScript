using FluidScript.Core.Layout;
using FluidScript.Core.Tests.Model;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Layout;

/// <summary>The layout solver's invariants (<c>D-103</c>), on every sample and the 200-component model.</summary>
[Trait("Category", "Unit")]
public sealed class LayoutSolverTests
{
    public static TheoryData<string> Samples =>
    [
        "m2-cooling-loop", "m2-simple-loop", "m2-substation", "m4-storage-header", "m2-distribution-header", "m1-syntax-tour", "header-200",
    ];

    private static string Source(string name) =>
        name == "header-200" ? ReferenceModels.DistributionHeader(ReferenceModels.TwoHundredComponentConsumers) : ContractFixture.Sample(name + ".fluid");

    private static Scene Solve(string name)
    {
        var input = ContractFixture.Compile(Source(name));
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        return LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void EveryComponentIsPlacedAndNoInnerBoxEntersAnotherOuterBox(string name)
    {
        var input = ContractFixture.Compile(Source(name));
        var scene = Solve(name);
        var placements = scene.Placements;

        // The diagnostic is written before anything is asserted: it is what a failure is read from (28 §31).
        Directory.CreateDirectory(Path.Combine(RepositoryLayout.Diagnostics, "layout"));
        File.WriteAllText(Path.Combine(RepositoryLayout.Diagnostics, "layout", name + ".svg"), SceneSvg.Render(scene));
        File.WriteAllText(Path.Combine(RepositoryLayout.Diagnostics, "layout", name + ".txt"), SceneText.Render(scene, input.Graph, input.Model));

        foreach (var component in input.Graph.Components)
        {
            Assert.Contains(placements, p => p.ComponentId == component.Name);
        }

        // An inline element has no box of its own and takes part in no clearance (D-105).
        for (var i = 0; i < placements.Length; i++)
        {
            for (var j = 0; j < placements.Length; j++)
            {
                if (i != j && !placements[i].IsInline && !placements[j].IsInline)
                {
                    Assert.False(
                        placements[i].Inner.Intersects(placements[j].Outer),
                        $"{name}: {placements[i].ComponentId}'s inner box {placements[i].Inner} enters {placements[j].ComponentId}'s outer box {placements[j].Outer}.");
                }
            }
        }
    }

    [Theory(Skip = "Until the layout ladder (29) reaches the samples: the engine is being rebuilt one rule at a time.")]
    [MemberData(nameof(Samples))]
    public void EveryConnectionIsRoutedFromAnchorToAnchorLeavingPerpendicularly(string name)
    {
        var input = ContractFixture.Compile(Source(name));
        var scene = Solve(name);
        var pipes = scene.Routes.Where(static r => r.Kind == "pipe").ToList();

        Assert.Equal(input.Model.Connections.Length, pipes.Count);

        foreach (var route in pipes)
        {
            Assert.True(route.Points.Length >= 2);

            for (var i = 1; i < route.Points.Length; i++)
            {
                var a = route.Points[i - 1];
                var b = route.Points[i];
                Assert.True(Math.Abs(a.X - b.X) < 1e-9 || Math.Abs(a.Y - b.Y) < 1e-9, $"{name} {route.ConnectionId}: segment {a}→{b} is not orthogonal.");
            }

            // The stubs: a whole margin along the anchor's direction at both ends that have a box -- inner boundary
            // straight to outer boundary, no turn between -- and every route starts and ends on an anchor of its own.
            var first = scene.Placements.FirstOrDefault(p => p.Anchors.Values.Any(a => a.At == route.Points[0]));
            var last = scene.Placements.FirstOrDefault(p => p.Anchors.Values.Any(a => a.At == route.Points[^1]));
            Assert.True(first is not null, $"{name} {route.ConnectionId}: starts at {route.Points[0]}, which is no anchor.");
            Assert.True(last is not null, $"{name} {route.ConnectionId}: ends at {route.Points[^1]}, which is no anchor.");
            Assert.True(first.IsInline || route.Points[0].ManhattanTo(route.Points[1]) >= scene.Margin - 1e-9, $"{name} {route.ConnectionId}: first stub too short.");
            Assert.True(last.IsInline || route.Points[^1].ManhattanTo(route.Points[^2]) >= scene.Margin - 1e-9, $"{name} {route.ConnectionId}: last stub too short.");
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void TheSceneIsDeterministicAndIgnoresValues(string name)
    {
        var first = Solve(name);
        var second = Solve(name);

        Assert.Equal(first.Placements.Select(static p => (p.ComponentId, p.Inner, p.Rotation, p.Mirrored, p.Arrangement)), second.Placements.Select(static p => (p.ComponentId, p.Inner, p.Rotation, p.Mirrored, p.Arrangement)));
        Assert.Equal(first.Routes.Select(static r => r.Points), second.Routes.Select(static r => r.Points));
    }

    [Fact]
    public void EditingAValueMovesNothing()
    {
        var source = ContractFixture.Sample("m2-distribution-header.fluid");
        var edited = source.Replace("power=24 kW", "power=25 kW", StringComparison.Ordinal);
        Assert.NotEqual(source, edited);

        Scene Of(string s)
        {
            var input = ContractFixture.Compile(s);
            var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
            return LayoutSolver.Solve(input.Graph, input.Model, hints);
        }

        var a = Of(source);
        var b = Of(edited);
        Assert.Equal(a.Placements.Select(static p => (p.ComponentId, p.Inner)), b.Placements.Select(static p => (p.ComponentId, p.Inner)));
    }

    [Fact(Skip = "Until the layout ladder (29) reaches the loop samples.")]
    public void TheRingKeepsItsCornersBare()
    {
        // D-44: on the loop samples no route bends inside any component's inner box.
        foreach (var name in new[] { "m2-cooling-loop", "m2-simple-loop", "m2-substation" })
        {
            var scene = Solve(name);

            foreach (var route in scene.Routes.Where(static r => r.Kind == "pipe"))
            {
                    foreach (var bend in route.Bends)
                    {
                        var inside = scene.Placements.FirstOrDefault(p => p.Inner.ContainsInterior(bend));
                        Assert.True(inside is null, $"{name} {route.ConnectionId}: bend {bend} is inside {inside?.ComponentId} {inside?.Inner}; route {string.Join(" ", route.Points)}");
                    }
            }
        }
    }

    [Theory(Skip = "Until the layout ladder (29) reaches the samples: the engine is being rebuilt one rule at a time.")]
    [MemberData(nameof(Samples))]
    public void TheAuditFindsNoInterference(string name)
    {
        // D-105 as restated: margins may overlap, inner boundaries may not be entered -- by a symbol or by a pipe,
        // and two pipes never run side by side closer than a margin. SceneAudit is the one implementation; the
        // diagnostics text prints the same findings.
        var input = ContractFixture.Compile(Source(name));
        var scene = Solve(name);
        var findings = SceneAudit.Findings(scene, input.Model);
        Assert.True(findings.Length == 0, $"{name}:\n{string.Join("\n", findings)}");
    }
}
