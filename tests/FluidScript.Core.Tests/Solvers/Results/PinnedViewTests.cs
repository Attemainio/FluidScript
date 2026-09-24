using FluidScript.Core.Catalogs.Pipes;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Passes;
using FluidScript.Core.Solvers.Steady;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers.Results;

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
    public async Task TheDemandStepLoopHasItsPipeCellsAndTheExchangersHoldUpAsDifferentialStates()
    {
        // Partition by volume, not by kind: the four cells of `PB` carry 0.740 l each (2 m of the
        // 21.7 mm bore), and `HE1`'s stated 0.5 dm³ lands on the node it discharges into (`C-114`).
        // The mixing node and the terminals carry none and stay algebraic, as in the steady solve.
        var run = await DesignAsync("m4-demand-step.fluid");
        var layout = SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);
        var cell = Math.PI * 0.0217 * 0.0217 / 4 * 2;

        Assert.Equal(SolveMode.Transient, run.Graph.Mode);
        Assert.Equal(
            ["HE1__3WV.h", "PB#n1.h", "PB#n2.h", "PB#n3.h", "PB#n4.h"],
            layout.Differential.Select(static s => s.Name).ToArray());
        Assert.Equal(0.0005, layout.Differential[0].Volume, 1e-12);
        Assert.All(layout.Differential.Skip(1), state => Assert.Equal(cell, state.Volume, 1e-9));
        Assert.All(layout.Differential, state => Assert.True(state.Column >= layout.NodeEnthalpyOffset));

        // The hold-up costs no column and no row: it is a volume on a node that already existed, which
        // is why no counting table moved when exchangers gained one (`D-146`).
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

    /// <summary>
    /// The interface flows between layers (<c>33</c> §Stratified tank, <c>S-80</c>): the cumulative port flow below each
    /// interface, carrying the layer below's water up when it is positive and the layer above's down when it is negative.
    /// </summary>
    /// <remarks>
    /// The storage header matches every layer's inflow with an outflow, so every interface there carries zero and neither
    /// branch ran. Here one stream crosses a three-layer tank at 30/40/50 C, 0.1 kg/s of 60 C water: entering the bottom
    /// by <c>in</c> it pushes each layer's water into the one above; entering the top by the <c>out</c> port -- the
    /// nominal outlet, run backwards -- it pushes each layer's water into the one below. Every rate is one product.
    /// </remarks>
    [Theory]
    [InlineData("S1 - T1.in\nT1.out - LD", new[] { 60.0, 30.0, 30.0, 40.0, 40.0, 50.0 })]
    [InlineData("S1 - T1.out\nT1.in - LD", new[] { 40.0, 30.0, 50.0, 40.0, 60.0, 50.0 })]
    public async Task EachInterfaceCarriesTheLayerItsFlowLeavesIntoTheNext(string connections, double[] pairs)
    {
        var source = $"""
            fluidscript 1
            circuit tank
            fluid dynamic water

            S1 inlet t=60 flow=0.1
            T1 tank volume=300 layers=3 layer[1].t=30 layer[2].t=40 layer[3].t=50 in.level=10% out.level=90%
            LD outlet flow=0.1

            connections
            {connections}
            """;
        var result = await Loop().RunAsync(GraphFixture.Bind(source), Water.Instance, "tank", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged, result.Value.Solve.Termination.ToString());

        var run = result.Value;
        var system = EquationSystem.Build(run.Graph, WellPosedness.Check(run.Graph), run.Solve.Solution);
        var pressure = run.Solve.Solution.Values[system.Unknowns.NodePressure(0)];

        double Enthalpy(double celsius)
        {
            var state = Water.Instance.FromPressureTemperature(
                Quantity.FromSi(pressure, Dimension.Pressure), Quantity.FromSi(celsius + 273.15, Dimension.Temperature));

            Assert.True(state.TryGetValue(out var fluid));

            return fluid.Enthalpy.SiValue;
        }

        system.Pin([Enthalpy(30), Enthalpy(40), Enthalpy(50)]);

        var pinned = await new NewtonSolver().SolveAsync(system, run.Solve.Solution, null, TestContext.Current.CancellationToken);

        Assert.True(pinned.Converged, pinned.Termination.ToString());

        var balances = new double[3];
        var inflows = new double[3];

        Assert.True(system.TryEvaluateRates(pinned.Solution.Values.AsSpan(), balances, inflows, out _, out _));

        // The water arriving at a port is the boundary's node's, which the solve holds at 60 C at its own pressure.
        var arriving = pinned.Solution.Values[system.Unknowns.NodeEnthalpy(run.Graph.Nodes.ToList().FindIndex(static node => node.Name == "S1"))];

        double H(double celsius) => celsius == 60 ? arriving : Enthalpy(celsius);

        for (var layer = 0; layer < 3; layer++)
        {
            Assert.Equal(0.1 * (H(pairs[2 * layer]) - H(pairs[(2 * layer) + 1])), balances[layer], 0.5);
            Assert.Equal(0.1, inflows[layer], 1e-9);
        }
    }
}
