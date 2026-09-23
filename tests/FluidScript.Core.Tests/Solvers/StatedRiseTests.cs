using FluidScript.Core.Catalogs.Pipes;
using FluidScript.Core.Components;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Passes;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Solvers.Steady;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology.Counting;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>
/// <c>C-109</c>: a pump's stated <c>dp</c> is the rise the equation holds, and a valve's stated
/// <c>dp</c> is the drop its Kv is chosen at. Before this both bound and neither was read.
/// </summary>
[Trait("Category", "Unit")]
public sealed class StatedRiseTests
{
    private const string Loop =
        """
        fluidscript 1
        circuit loop
        fluid water
        HE1  heat_exchanger power=30 in.t=20 out.t=50
        LOAD heat_exchanger power=-30 dp=0
        CV1  valve
        PU1  pump
        P1   pipe length=25
        connections
        N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5 - P1 - N1
        """;

    [Fact]
    public async Task AStatedRiseIsHeldExactlyAndTheHeadFollowsFromTheSolvedDensity()
    {
        // 30 kPa across the pump whatever the density; the valve is what the flow constraint moves.
        var run = await Solve(Loop.Replace("PU1  pump", "PU1  pump dp=30", StringComparison.Ordinal), "rise");
        var (inlet, outlet) = PumpPorts(run, "PU1");

        var report = run.ToString();

        Assert.Equal(30_000, outlet.Pressure - inlet.Pressure, 3);
        Assert.Contains(Layout(run).Unknowns, static u => u.Kind == UnknownKind.Parameter && u.OwnerComponentId == "CV1");
        Assert.Equal(1, run.Passes);
        Assert.Contains("rise stated, 30.00 kPa", report, StringComparison.Ordinal);
        Assert.Contains($"head {30_000 / (inlet.Density * 9.80665),7:0.00} m", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARiseWrittenFromTheDesignDropIsStatedFromTheSeedAndHeld()
    {
        // 14's example on the parameter it was meant for: 1.2 x the exchanger's 20 kPa, 24 kPa, from
        // the seed, held by the equation, and the valve opens to Kv 7.0 to pass the duty flow.
        var run = await Solve(Loop.Replace("PU1  pump", "PU1  pump dp=1.2*HE1.dp", StringComparison.Ordinal), "deferred-rise");
        var (inlet, outlet) = PumpPorts(run, "PU1");
        var kv = Layout(run).Unknowns.Single(static u => u.Kind == UnknownKind.Parameter && u.OwnerComponentId == "CV1");

        Assert.Equal(24_000, outlet.Pressure - inlet.Pressure, 3);
        Assert.Contains("from `1.2*HE1.dp` at pass 0", run.Bases["PU1.dp"], StringComparison.Ordinal);
        Assert.Equal(7.02, run.Solve.Solution.Values[kv.Index], 1);
    }

    [Fact]
    public async Task AValvesStatedDropChoosesTheNextLargerKvAndReportsWhatItAsked()
    {
        // 0.2392 kg/s at 30 kPa asks Kv 1.57; the next R5 row is 1.6, which drops 29 kPa there.
        var run = await Solve(Loop.Replace("CV1  valve", "CV1  valve dp=30", StringComparison.Ordinal), "valve-drop");
        var valve = Assert.IsType<Valve>(run.Graph.Components.Single(static c => c.Name == "CV1"));

        Assert.Equal(1.6, valve.SizedParameters["kv"].SiValue, 6);
        Assert.Contains("the stated 30 kPa at 0.24 l/s asks Kv 1.57; the next larger row drops 29 kPa", run.Bases["CV1.kv"], StringComparison.Ordinal);
        Assert.Contains("achieved at the stated drop", run.Bases["CV1.authority"], StringComparison.Ordinal);
    }

    private static SystemLayout Layout(OuterLoopResult run) =>
        SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);

    private static (SolvedPort Inlet, SolvedPort Outlet) PumpPorts(OuterLoopResult run, string name)
    {
        var index = run.Graph.Components.ToList().FindIndex(c => c.Name == name);
        var ports = SolvedStates.Ports(run.Graph, Layout(run), run.Solve.Solution)[index];
        return (ports[0]!.Value, ports[1]!.Value);
    }

    private static async Task<OuterLoopResult> Solve(string script, string name)
    {
        var resolved = PipeCatalogs.Resolve(pin: null);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var loop = new OuterLoop(new NewtonSolver(), new CatalogBoreLookup(resolved.Value), OuterLoop.Rules(resolved.Value.Catalog), 10);
        var result = await loop.RunAsync(GraphFixture.Bind(script), Water.Instance, name, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged);
        Assert.True(result.Value.Settled);

        return result.Value;
    }
}
