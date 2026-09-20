using FluidScript.Core.Catalogs;
using FluidScript.Core.Fluids;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>
/// A stated flow is a constraint (P5.13b, <c>S-72</c>). Until it was, <c>HE1 flow=0.3</c> set the flow
/// its 20 kPa was measured at and <c>PU1 flow=0.3</c> its curve's duty point, and the simple loop solved
/// to 0.086 kg/s and then out of the fluid's range. A volume flow is the same constraint through the
/// density of the side's inlet node as the solve finds it: <c>V̇ = ṁ/ρ</c> written where it holds.
/// </summary>
[Trait("Category", "Unit")]
public sealed class StatedFlowTests
{
    private const string Loop = """
        fluidscript 1
        circuit simpleLoop
        fluid water
        HE1  heat_exchanger power=30 in.t=20 {he}
        LOAD heat_exchanger power=-30 dp=0
        CV1  valve
        PU1  pump {pu}
        connections
        N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5
        N5 - N1 length=25
        """;

    [Theory]
    [InlineData("flow=0.3", "", "HE1")]
    [InlineData("", "flow=0.3", "PU1")]
    public async Task AStatedMassFlowPinsTheBranchAndThePumpHeadAnswersIt(string he, string pu, string owner)
    {
        var script = Loop.Replace("{he}", he, StringComparison.Ordinal).Replace("{pu}", pu, StringComparison.Ordinal);
        var check = WellPosedness.Check(GraphFixture.Lower(script).Graph);

        Assert.Equal(0, check.Counting.Excess);
        Assert.Contains(check.Counting.Promotions, p =>
            p.Constraint.Kind == ConstraintKind.FixedFlow && p.Constraint.Component == owner
            && p.Constraint.Parameter == "flow" && p.Component == "PU1" && p.Parameter == "head");

        var run = await Solve(script);

        Assert.True(run.Solve.Converged);
        Assert.Equal(0.3, Flow(run), 6);
        // 30 kW at 0.3 kg/s of water from 20 °C is a rise of 23.9 K, not the 83 K the unpinned loop
        // walked into.
        Assert.InRange(Temperature(run, "N3"), 43.5, 44.5);
    }

    [Theory]
    [InlineData("", "vflow=0.3 l/s", "N1", 20.0)]
    [InlineData("vflow=0.3 l/s", "", "N2", 20.0)]
    public async Task AStatedVolumeFlowIsHeldAtTheInletNodesSolvedDensity(string he, string pu, string inlet, double expectedInlet)
    {
        var script = Loop.Replace("{he}", he, StringComparison.Ordinal).Replace("{pu}", pu, StringComparison.Ordinal);
        var run = await Solve(script);

        Assert.True(run.Solve.Converged);

        var (temperature, density) = State(run, inlet);

        Assert.Equal(expectedInlet, temperature, 1);
        Assert.Equal(0.0003 * density, Flow(run), 6);
        Assert.InRange(Flow(run), 0.2990, 0.2999);
    }

    [Fact]
    public async Task AVolumeFlowOnAHotSideIsLessMassThanTheSameOnAColdOne()
    {
        // The whole point: 0.3 l/s of 60 °C water (983 kg/m³) is 0.2950 kg/s, of 20 °C water 0.2995.
        // A conversion at bind time with one density gets one of them wrong by 1.5 %.
        var hot = await Solve(Loop.Replace("{he}", "vflow=0.3 l/s", StringComparison.Ordinal)
            .Replace("in.t=20", "in.t=60", StringComparison.Ordinal).Replace("{pu}", "", StringComparison.Ordinal));
        var cold = await Solve(Loop.Replace("{he}", "vflow=0.3 l/s", StringComparison.Ordinal).Replace("{pu}", "", StringComparison.Ordinal));

        Assert.True(hot.Solve.Converged);
        Assert.True(cold.Solve.Converged);
        Assert.InRange(Flow(hot), 0.2949, 0.2951);
        Assert.InRange(Flow(cold), 0.2994, 0.2996);
    }

    [Fact]
    public void APumpWithHeadAndFlowStatesItsCurveAndPinsNothing()
    {
        // Both numbers are the duty point the curve passes through; the circuit decides where on that
        // curve it runs. No row, no promotion.
        var script = Loop.Replace("{he}", "out.t=44", StringComparison.Ordinal).Replace("{pu}", "head=7 flow=0.3", StringComparison.Ordinal);
        var check = WellPosedness.Check(GraphFixture.Lower(script).Graph);

        Assert.DoesNotContain(check.Counting.Constraints, c => c.Component == "PU1");
    }

    private static async Task<OuterLoopResult> Solve(string script)
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var loop = new OuterLoop(new NewtonSolver(), new CatalogBoreLookup(resolved.Value), OuterLoop.Rules(resolved.Value.Catalog), 10);
        var result = await loop.RunAsync(GraphFixture.Bind(script), Water.Instance, "flow", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);

        return result.Value;
    }

    private static double Flow(OuterLoopResult run)
    {
        var layout = SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);
        return Math.Abs(run.Solve.Solution.Values[layout.BranchFlow(0)]);
    }

    private static (double Temperature, double Density) State(OuterLoopResult run, string node)
    {
        var layout = SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);
        var index = run.Graph.Nodes.ToList().FindIndex(n => n.Name == node);
        var state = run.Graph.Substance.FromPressureEnthalpy(
            Quantity.FromSi(run.Solve.Solution.Values[layout.NodePressure(index)], Dimension.Pressure),
            Quantity.FromSi(run.Solve.Solution.Values[layout.NodeEnthalpy(index)], Dimension.Enthalpy));

        Assert.True(state.IsSuccess);

        return (state.Value.Temperature.SiValue - 273.15, state.Value.Density.SiValue);
    }

    private static double Temperature(OuterLoopResult run, string node) => State(run, node).Temperature;
}
