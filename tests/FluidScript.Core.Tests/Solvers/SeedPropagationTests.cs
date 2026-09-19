using FluidScript.Core.Catalogs;
using FluidScript.Core.Fluids;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>Where the seed places each node's temperature, and why it matters (<c>S-30b</c>).</summary>
public sealed class SeedPropagationTests
{
    private static StateVector Seed(string sample, out SystemLayout layout)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample));
        var graph = GraphFixture.Lower(source).Graph;
        var posedness = WellPosedness.Check(graph);

        layout = SystemLayout.Build(graph, posedness.Counting);

        return SolutionSeed.Build(graph, layout);
    }

    private static double Celsius(StateVector state, SystemLayout layout, string node)
    {
        for (var index = 0; index < layout.Unknowns.Length; index++)
        {
            if (string.Equals(layout.Unknowns[index].Name, $"{node}.h", StringComparison.Ordinal))
            {
                return (state.Values[index] / 4187) - 0.15;
            }
        }

        Assert.Fail($"no enthalpy unknown for {node}.");

        return 0;
    }

    [Fact]
    public void ARatedInletLandsOnTheNodeAtTheInletPortAndNotTheOneAfterIt()
    {
        // The version this replaced read the neighbours out of `branch.Path`, whose order is the order the
        // walk crossed the branch rather than the component's own orientation. On this circuit that laid
        // `HE1`'s rated 20/50 on backwards -- 46 C before it and 18 C after -- so the seed said the
        // exchanger cooled the water. The port map has no such ambiguity: a parameter is named after the
        // port it describes.
        var seed = Seed("m2-cooling-loop.fluid", out var layout);

        Assert.True(
            Celsius(seed, layout, "PU1__HE1") < Celsius(seed, layout, "HE1__3WV"),
            "the node at HE1's inlet must be seeded colder than the node at its outlet.");
    }

    [Theory]
    [InlineData("m2-simple-loop.fluid")]
    [InlineData("m2-cooling-loop.fluid")]
    [InlineData("m2-distribution-header.fluid")]
    [InlineData("m2-substation.fluid")]
    public void NoNodeIsSeededOutsideTheTemperaturesTheCircuitWorksBetween(string sample)
    {
        // `S-30`'s original shape: one global level and a monotone walk down from it put every secondary
        // node at 2 to 6 C on a loop running 20 to 50, and the walk's own magnitude later carried nodes to
        // 0.2 C and 115 C on other samples. Nothing may sit outside the span these scripts work between,
        // with room for the stagger that keeps adjacent nodes distinct.
        var seed = Seed(sample, out var layout);

        for (var index = 0; index < layout.Unknowns.Length; index++)
        {
            if (layout.Unknowns[index].Name.EndsWith(".h", StringComparison.Ordinal))
            {
                Assert.InRange((seed.Values[index] / 4187) - 0.15, 0, 100);
            }
        }
    }

    [Fact]
    public async Task TheCoolingLoopReachesItsOwnFiguresNowThatItStartsNearThem()
    {
        // `01`: the mixing node sits at 20 C between a 6 C primary and a 50 C return. Before the seed was
        // fixed this circuit terminated `Singular` at eleven iterations with the temperatures nowhere near
        // it. It does not converge yet -- `3WV`'s Kv law is still out, which is `S-30`'s remainder -- but
        // the energy side is solved, and that is what the seed was preventing.
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-cooling-loop.fluid"));
        var run = await new OuterLoop(
            new NewtonSolver(),
            new CatalogBoreLookup(resolved.Value),
            OuterLoop.Rules(resolved.Value.Catalog),
            10).RunAsync(
                GraphFixture.Bind(source), Water.Instance, "cooling",
                TestContext.Current.CancellationToken);

        Assert.True(run.IsSuccess, run.Error?.Message);

        var posedness = WellPosedness.Check(run.Value.Graph);
        var layout = SystemLayout.Build(run.Value.Graph, posedness.Counting);
        var solved = run.Value.Solve.Solution;

        Assert.Equal(20.0, Celsius(solved, layout, "N2"), 0.5);
        Assert.Equal(50.0, Celsius(solved, layout, "HE1__3WV"), 0.5);
        Assert.Equal(6.0, Celsius(solved, layout, "N1"), 0.5);

        // Not `Singular` any more, which is the whole point: the deficiency was the starting point.
        Assert.NotEqual(SolveTermination.Singular, run.Value.Solve.Termination);
    }

    [Theory]
    [InlineData("3WV - BV1\nBV1 - N2\n3WV - P1", "BV1 valve kv=1.6")]
    [InlineData("3WV - B1\nB1 - N2\n3WV - P1", "B1 pipe length=2 dn=25")]
    [InlineData("3WV - N2\n3WV - BV1\nBV1 - P1", "BV1 valve kv=1.6")]
    [InlineData("3WV - N2\n3WV - P1", "")]
    public async Task AComponentInAValvesLegDoesNotCollapseItsPortsOntoOnePressure(
        string legs, string declaration)
    {
        // `S-35`. `Steps` restarted its walk at each branch, so the first interior node of every branch
        // took step 1 and every leg of a junction seeded to the same pressure. A three-way valve then saw
        // no difference across itself at all, its Kv law's derivative with respect to `position` went to
        // zero with the square root, and the factorisation died at iteration zero.
        //
        // The cooling loop escaped it only because its bypass leg is *empty*: that side landed on `N2`,
        // a branch end, rather than on an interior node. Putting any real component there -- which every
        // installation of this arrangement has (`C-63`) -- was enough to make a well-posed circuit
        // singular, so the four legs below are one topology's worth of that, with and without.
        var source = $"""
            fluidscript 1
            circuit coolingLoop
            fluid water

            HE1 heat_exchanger power=30 in=20 out=50
            3WV three_way_valve
            PU1 pump
            P1  pipe length=25 dn=25
            {declaration}

            connections
            N1 - N2
            N2 - PU1
            PU1 - HE1
            HE1 - 3WV
            {legs}
            P1 - N3

            N1 inlet t=6 p=300
            N3 outlet p=280
            """;

        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var run = await new OuterLoop(
            new NewtonSolver(),
            new CatalogBoreLookup(resolved.Value),
            OuterLoop.Rules(resolved.Value.Catalog),
            10).RunAsync(
                GraphFixture.Bind(source), Water.Instance, "legs",
                TestContext.Current.CancellationToken);

        Assert.True(run.IsSuccess, run.Error?.Message);

        // Every one of the four reaches the same circuit, which is the point: what is in a leg changes
        // the hydraulics, and must not change whether the solver can start.
        Assert.NotEqual(SolveTermination.Singular, run.Value.Solve.Termination);
        Assert.NotEqual(SolveTermination.NonFinite, run.Value.Solve.Termination);

        var posedness = WellPosedness.Check(run.Value.Graph);
        var layout = SystemLayout.Build(run.Value.Graph, posedness.Counting);
        var solved = run.Value.Solve.Solution;

        Assert.Equal(20.0, Celsius(solved, layout, "N2"), 0.5);
        Assert.Equal(50.0, Celsius(solved, layout, "HE1__3WV"), 0.5);
    }

    [Theory]
    [InlineData("m2-cooling-loop.fluid")]
    [InlineData("m2-simple-loop.fluid")]
    [InlineData("m2-distribution-header.fluid")]
    [InlineData("m4-storage-header.fluid")]
    public void NoNodeSeedsOutsideTheRangeItsFluidIsDefinedOver(string sample)
    {
        // `S-35`, the second half. The band walked one-sidedly *down* from each level, so `Band` steps is
        // 8 K below it -- fine at 60 C and below freezing at 6 C, where the seed ends in a failed property
        // call rather than a bad guess. Chilled water at 6 C is not an exotic circuit, and the cooling
        // loop is one, so the band is centred on its level instead of hanging off it.
        var seed = Seed(sample, out var layout);

        for (var index = 0; index < layout.Unknowns.Length; index++)
        {
            if (!layout.Unknowns[index].Name.EndsWith(".h", StringComparison.Ordinal))
            {
                continue;
            }

            var celsius = (seed.Values[index] / 4187) - 0.15;

            Assert.True(
                celsius is > 0 and < 150,
                $"{layout.Unknowns[index].Name} seeds at {celsius:F2} C, outside liquid water.");
        }
    }
}
