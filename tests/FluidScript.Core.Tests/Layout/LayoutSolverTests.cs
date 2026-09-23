using System.Collections.Immutable;

using FluidScript.Core.Layout;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Tests.Layout.Drawing;
using FluidScript.Core.Tests.Model;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Layout;

/// <summary>The layout solver's invariants (<c>D-103</c>), on every sample and the 200-component model.</summary>
[Trait("Category", "Unit")]
public sealed class LayoutSolverTests
{
    public static TheoryData<string> Samples =>
    [
        "m2-cooling-loop", "m2-simple-loop", "m2-substation", "m4-storage-header", "m2-distribution-header", "m1-syntax-tour", "m4-demand-step", "header-200",
    ];

    /// <summary>The samples the layout ladder (29) has reached, whose routing and audit gates are live: the simple loop (step 4), the substation (step 5), the cooling loop (step 6), the distribution header (step 8), the storage header (step 9). The rest join as their steps land.</summary>
    public static TheoryData<string> Reached =>
    [
        "m2-simple-loop", "m2-substation", "m2-cooling-loop", "m2-distribution-header", "m4-storage-header", "m1-syntax-tour",
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

    [Theory]
    [MemberData(nameof(Reached))]
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

            // Every route starts and ends on an anchor of its own. The stub -- a whole margin straight out of every
            // port that has a box (28 H5) -- is the audit's, measured on the run through inline points, and
            // TheAuditFindsNoInterference asserts it; a per-link check here would call a run an inline node cuts short.
            var first = scene.Placements.FirstOrDefault(p => p.Anchors.Values.Any(a => a.At == route.Points[0]));
            var last = scene.Placements.FirstOrDefault(p => p.Anchors.Values.Any(a => a.At == route.Points[^1]));
            Assert.True(first is not null, $"{name} {route.ConnectionId}: starts at {route.Points[0]}, which is no anchor.");
            Assert.True(last is not null, $"{name} {route.ConnectionId}: ends at {route.Points[^1]}, which is no anchor.");
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

    [Fact]
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

    [Theory]
    [MemberData(nameof(Reached))]
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

    [Fact]
    public void APipeWithNodesIsDrawnAsTheChainOfItsCells()
    {
        // C-124. `PB pipe ... nodes=4` lowers to five sub-pipes and four internal nodes, and the script's
        // connections still name `PB`. Both written connections to it are drawn -- at the cell that meets
        // the other end -- and the eight links between the cells the script never wrote get routes of
        // their own, so the recirculation branch is one run from 3WV to N2 and the cells sit inline on it.
        // Before, both were dropped: the branch was not drawn and the cells stacked below the diagram.
        var input = ContractFixture.Compile(Source("m4-demand-step"));
        var scene = Solve("m4-demand-step");
        var pipes = scene.Routes.Where(static r => r.Kind == "pipe").Select(static r => r.ConnectionId).ToList();

        Assert.Equal(input.Model.Connections.Length + 8, pipes.Count);
        Assert.Equal(["PB#c1", "PB#c2", "PB#c3", "PB#c4", "PB#c5", "PB#c6", "PB#c7", "PB#c8"], pipes.Where(static id => id.StartsWith("PB#", StringComparison.Ordinal)));
        Assert.All(
            scene.Placements.Where(static p => p.ComponentId.StartsWith("PB#", StringComparison.Ordinal)),
            static cell => Assert.True(cell.IsInline, $"{cell.ComponentId} is not inline"));

        // N2 is where the primary supply, the recirculation and the pump's pipe meet: a junction, boxed.
        Assert.False(Assert.Single(scene.Placements, static p => p.ComponentId == "N2").IsInline);
    }

    [Fact]
    public void APipeWithNodesCarriesTheSolvedDirectionOnEveryLink()
    {
        // The hints key each route's direction by its id: the written connections to `PB` read it at their
        // end cell, and each link between cells at its own pair. All branches forward at 1 kg/s: nothing
        // on the recirculation may be left without a direction.
        var input = ContractFixture.Compile(Source("m4-demand-step"));
        var flows = Enumerable.Repeat(1.0, input.Graph.Branches.Length).ToImmutableArray();
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, flows);

        var toPb = Enumerable.Range(0, input.Model.Connections.Length)
            .Where(i => input.Model.Connections[i].From.Component == "PB" || input.Model.Connections[i].To.Component == "PB")
            .Select(static i => $"c{i}")
            .ToList();

        Assert.Equal(2, toPb.Count);
        Assert.All(toPb.Concat(Enumerable.Range(1, 8).Select(static k => $"PB#c{k}")), id => Assert.NotEqual(FlowDirection.None, hints.Flow[id]));
    }
}
