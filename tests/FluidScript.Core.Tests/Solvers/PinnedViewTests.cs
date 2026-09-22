using FluidScript.Core.Catalogs;
using FluidScript.Core.Fluids;
using FluidScript.Core.Sizing;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>The transient's assembly on the steady system (P6.0): the partition and the pinned view (<c>D-139</c>, <c>D-140</c>).</summary>
/// <remarks>
/// <para>
/// <strong>Nothing here integrates.</strong> These are the exits <c>08</c> gave P6.0: the partition
/// counts what <c>33</c> says it counts, and a design state at rest stays at rest when every
/// differential state is pinned at its design value and every promotion frozen at its — which is V9
/// with the integrator stubbed to zero, and the strongest check that the pinned view and the
/// equilibrium describe one circuit.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PinnedViewTests
{
    private static OuterLoop Loop()
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        return new OuterLoop(new NewtonSolver(), new CatalogBoreLookup(resolved.Value), OuterLoop.Rules(resolved.Value.Catalog), 10);
    }

    private static async Task<OuterLoopResult> DesignAsync(string sample)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Samples, sample), TestContext.Current.CancellationToken);
        var result = await Loop().RunAsync(GraphFixture.Bind(source), Water.Instance, sample, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged, result.Value.Solve.Termination.ToString());

        return result.Value;
    }

    [Fact]
    public async Task TheDemandStepLoopHasExactlyItsFourPipeCellsAsDifferentialStates()
    {
        // Partition by volume, not by kind: the four cells of `PB` carry 0.740 l each (2 m of the
        // 21.7 mm bore) and nothing else in the loop carries any. The exchanger's outlet, the mixing
        // node and the terminals are algebraic, exactly as in the steady solve.
        var run = await DesignAsync("m4-demand-step.fluid");
        var layout = SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);

        Assert.Equal(SolveMode.Transient, run.Graph.Mode);
        Assert.Equal(["PB#n1.h", "PB#n2.h", "PB#n3.h", "PB#n4.h"], layout.Differential.Select(static s => s.Name).ToArray());
        Assert.All(layout.Differential, state => Assert.Equal(Math.PI * 0.0217 * 0.0217 / 4 * 2, state.Volume, 1e-9));
        Assert.All(layout.Differential, state => Assert.True(state.Column >= layout.NodeEnthalpyOffset));
        Assert.Equal(layout.Count, run.Solve.Solution.Values.Length);
    }

    [Fact]
    public async Task TheStorageHeaderHasItsFiveLayersAsDifferentialStatesAndItsMixedEnthalpyIsNotOne()
    {
        // Five layers, bottom to top, 60 dm³ each, with no column of their own: the tank's single mixed
        // unknown stays allocated for the steady solve and is pinned to their mean in a run.
        var run = await DesignAsync("m4-storage-header.fluid");
        var layout = SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);

        Assert.Equal(
            ["T1.layer[1].h", "T1.layer[2].h", "T1.layer[3].h", "T1.layer[4].h", "T1.layer[5].h"],
            layout.Differential.Select(static s => s.Name).ToArray());
        Assert.All(layout.Differential, state => Assert.Equal(0.06, state.Volume, 1e-12));
        Assert.All(layout.Differential, state => Assert.Equal(-1, state.Column));
        Assert.Single(layout.Unknowns, static u => u.Name == "T1.h");
    }

    [Fact]
    public async Task ASteadyGraphHasNoDifferentialStates()
    {
        var run = await DesignAsync("m2-cooling-loop.fluid");
        var layout = SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);

        Assert.Empty(layout.Differential);
    }

    [Fact]
    public async Task ADesignStateAtRestStaysAtRestUnderThePinnedView()
    {
        // V9 with the integrator stubbed to zero. Pin every cell at its design enthalpy and freeze every
        // promotion -- PU1.head, 3WV.position -- at its design value: the released constraints
        // (HE1.out.t, the setpoint on N2) are replaced by the identities on what they chose, and the
        // design state is already the solution. Newton has nothing to do and says so.
        var run = await DesignAsync("m4-demand-step.fluid");
        var posedness = WellPosedness.Check(run.Graph);
        var system = EquationSystem.Build(run.Graph, posedness, run.Solve.Solution);
        var design = run.Solve.Solution.Values;

        var states = system.Unknowns.Differential.Select(state => design[state.Column]).ToArray();

        system.Pin(states);

        for (var promotion = 0; promotion < posedness.Counting.Promotions.Length; promotion++)
        {
            system.Freeze(promotion, design[system.Unknowns.PromotionOffset + promotion]);
        }

        Assert.True(system.Pinned);

        var rest = await new NewtonSolver().SolveAsync(system, run.Solve.Solution, null, TestContext.Current.CancellationToken);

        Assert.True(rest.Converged, rest.Termination.ToString());
        Assert.Equal(0, rest.Iterations);

        for (var column = 0; column < design.Length; column++)
        {
            Assert.Equal(design[column], rest.Solution.Values[column], Math.Max(1e-9, 1e-9 * Math.Abs(design[column])));
        }

        // And the pin is what holds: move one cell's pin by 5 K of enthalpy and the cell follows it
        // exactly -- the energy balance that would have argued is the integrator's now -- while the
        // flows move only through density and viscosity, a few parts in a million.
        var moved = (double[])states.Clone();
        moved[1] += 5 * 4180;
        system.Pin(moved);

        var pushed = await new NewtonSolver().SolveAsync(system, run.Solve.Solution, null, TestContext.Current.CancellationToken);
        var cell = system.Unknowns.Differential[1].Column;

        Assert.True(pushed.Converged, pushed.Termination.ToString());
        Assert.Equal(moved[1], pushed.Solution.Values[cell], 1e-3);

        for (var branch = 0; branch < run.Graph.Branches.Length; branch++)
        {
            Assert.Equal(design[system.Unknowns.BranchFlow(branch)], pushed.Solution.Values[system.Unknowns.BranchFlow(branch)], 1e-4);
        }

        system.Release();
        Assert.False(system.Pinned);
    }

    [Fact]
    public async Task ATankPortDeliversItsLayerWhenTheLayersArePinned()
    {
        // The storage header's stated profile, 25/30/40/50/60 C bottom to top, pinned: the radiator
        // network draws from `out` at 90 % (layer 5, 60 C) and the AHU network from `out[2]` at 30 %
        // (layer 2, 30 C). In the steady solve both drew the mixed tank; in the run each draws its layer.
        var run = await DesignAsync("m4-storage-header.fluid");
        var posedness = WellPosedness.Check(run.Graph);
        var system = EquationSystem.Build(run.Graph, posedness, run.Solve.Solution);
        var layout = system.Unknowns;

        double Enthalpy(double celsius)
        {
            var state = Water.Instance.FromPressureTemperature(
                Quantity.FromSi(run.Solve.Solution.Values[layout.NodePressure(0)], Dimension.Pressure),
                Quantity.FromSi(celsius + 273.15, Dimension.Temperature));

            Assert.True(state.TryGetValue(out var fluid));

            return fluid.Enthalpy.SiValue;
        }

        system.Pin([Enthalpy(25), Enthalpy(30), Enthalpy(40), Enthalpy(50), Enthalpy(60)]);

        var pinned = await new NewtonSolver().SolveAsync(system, run.Solve.Solution, null, TestContext.Current.CancellationToken);

        Assert.True(pinned.Converged, pinned.Termination.ToString());

        int Node(string name) => run.Graph.Nodes.ToList().FindIndex(node => node.Name == name);

        Assert.Equal(Enthalpy(60), pinned.Solution.Values[layout.NodeEnthalpy(Node("RAD_NETWORK"))], 1.0);
        Assert.Equal(Enthalpy(30), pinned.Solution.Values[layout.NodeEnthalpy(Node("AHU_NETWORK"))], 1.0);

        // The mixed unknown is the layers' mean, and the flows are what the boundaries state.
        var mixed = layout.Unknowns.Single(static u => u.Name == "T1.h");
        Assert.Equal((Enthalpy(25) + Enthalpy(30) + Enthalpy(40) + Enthalpy(50) + Enthalpy(60)) / 5, pinned.Solution.Values[mixed.Index], 1.0);
    }
}
