using System.Collections.Immutable;

using FluidScript.Core.Catalogs;
using FluidScript.Core.Fluids;
using FluidScript.Core.Sizing;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Fixtures;

using CoreTopology = FluidScript.Core.Topology;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>
/// The single outer fixed-point loop from <c>plan/30-solver/31-solver-architecture.md</c>, run against
/// the simple loop with nothing stating its pipe's diameter.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The corpus is the acceptance criterion here.</strong> <c>m2-simple-loop.fluid</c> carried
/// <c>dn=25</c> with a comment saying to remove it when <c>P3.7</c> landed, because lowering cannot
/// build a pipe without a bore and nothing chose one. It is gone, and these tests are what it was
/// waiting for: the loop sizes it to the DN25 that <c>24</c>'s worked example computes by hand, and
/// the circuit still converges to the flow <c>22</c>'s energy balance fixes.
/// </para>
/// <para>
/// Two properties matter more than the number. Sizing is applied by <em>lowering again</em> rather
/// than by mutating a component, so every pass is a pure function of its own graph; and a promoted
/// parameter is never also sized, so no question gets two answers.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class OuterLoopTests
{
    private static OuterLoop Loop(int maxPasses = 10)
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        return new OuterLoop(
            new NewtonSolver(),
            new CatalogBoreLookup(resolved.Value.Catalog),
            OuterLoop.Rules(resolved.Value.Catalog),
            maxPasses);
    }

    private static async Task<OuterLoopResult> RunAsync(string sample, int maxPasses = 10)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample));
        var result = await Loop(maxPasses)
            .RunAsync(GraphFixture.Bind(source), Water.Instance, sample, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);

        return result.Value;
    }

    [Fact]
    public async Task TheSimpleLoopSolvesWithNothingStatingItsPipeDiameter()
    {
        var run = await RunAsync("m2-simple-loop.fluid");

        Assert.True(run.Solve.Converged, $"stopped at {run.Solve.Termination} after {run.Solve.Iterations}.");
        Assert.True(run.Settled, $"sizes were still moving after {run.Passes} passes.");

        // `24`'s worked example, reached rather than transcribed: DN25 after DN15 and DN20 miss the
        // 150 Pa/m target at the flow HE1's duty fixes.
        Assert.Equal(25.0, run.Sizes.For("P1", "dn")!.Value);
        Assert.Contains("DN25", run.Bases["P1.dn"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFlowIsTheOneTheDutyFixesWhateverTheLoopChoseForThePipe()
    {
        var run = await RunAsync("m2-simple-loop.fluid");
        var layout = SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);

        // 22's worked energy balance: 30 kW over the enthalpy rise from 20 to 50 C is 0.2392 kg/s. The
        // pipe's size cannot move it -- HE1's three stated parameters pin it -- which is exactly why the
        // simple loop is the right first case: the sizing is checkable independently of the solve.
        var flow = Math.Abs(run.Solve.Solution.Values[layout.BranchFlow(0)]);

        Assert.Equal(0.2392, flow, 0.002);
    }

    [Fact]
    public async Task ASizedParameterIsNeverAlsoPromoted()
    {
        var run = await RunAsync("m2-simple-loop.fluid");
        var promoted = CoreTopology.WellPosedness.Check(run.Graph).Counting.Promotions
            .Select(static promotion => promotion.Label)
            .ToImmutableHashSet(StringComparer.Ordinal);

        // The division of labour, asserted rather than assumed. `IsFree` read two of the three parameter
        // maps until this package, so a value could have been chosen by a rule and solved for as an
        // unknown at the same time, with the two answers disagreeing and nothing saying so.
        foreach (var (component, chosen) in run.Sizes.Values)
        {
            foreach (var parameter in chosen.Keys)
            {
                Assert.DoesNotContain($"{component}.{parameter}", promoted);
            }
        }

        // And the pump's head is still the promoted one, because HE1's duty over-specifies its own side
        // and something has to absorb it.
        Assert.Contains("PU1.head", promoted);
    }

    [Fact]
    public async Task SizingNeverOverridesAStatedValue()
    {
        // `24`'s invariant 1. The cooling loop states diameters throughout, so a loop that reached past
        // them would show up as an overlay entry for a parameter the script already fixed.
        var run = await RunAsync("m2-cooling-loop.fluid");

        foreach (var component in run.Graph.Components)
        {
            foreach (var parameter in run.Sizes.For(component.Name).Keys)
            {
                Assert.DoesNotContain(parameter, component.StatedParameters.Keys);
            }
        }
    }

    [Fact]
    public async Task TheLoopSettlesInFarFewerPassesThanItsCap()
    {
        // `24`: two passes for this circuit -- one to size, one to confirm -- because discreteness
        // stabilises the loop. Once DN25 is DN25, the only thing that could still move is a continuous
        // value, and it moves only if a flow does.
        var run = await RunAsync("m2-simple-loop.fluid");

        Assert.True(run.Settled);
        Assert.InRange(run.Passes, 1, 3);
    }

    /// <summary>The simple loop with a second pump in series, for <c>S-29</c>.</summary>
    /// <remarks>
    /// <c>PU2</c>'s head is <em>stated</em> on purpose, so nothing about sizing is in the picture: this
    /// is 14 unknowns for 14 equations with one free head and a flow the duty fixes, which is the shape
    /// the one-pump loop solves in five iterations.
    /// </remarks>
    private const string SeriesPumps = """
        fluidscript 1
        circuit series
        fluid water

        HE1  heat_exchanger power=30 in=20 out=50
        LOAD heat_exchanger power=-30
        CV1  valve
        PU1  pump
        PU2  pump head=3
        P1   pipe length=25 dn=25

        connections
        N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5 - P1 - N6 - PU2 - N1
        """;

    [Fact]
    public async Task AScriptWhoseComponentsAreNotConnectedIsToldSoRatherThanThrowing()
    {
        // `S-28`. `m1-syntax-reference` is 18 unknowns for 19 equations, and says in its own header that
        // it is not a solvable circuit. The counting table's verdict was never consulted before the
        // system was assembled, so `DenseLu.Factor` threw `ArgumentException` on a Jacobian that was not
        // square -- a pipeline stage throwing on user input, which the contract forbids outright.
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m1-syntax-reference.fluid"));
        var result = await Loop().RunAsync(
            GraphFixture.Bind(source), Water.Instance, "reference", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("not connected", result.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACountingTableThatDisagreesWithAssemblyIsReportedRatherThanFactored()
    {
        // `S-28` again, for the half `CanSolve` cannot see. The counting table is a prediction: it counts
        // 23 for 23 on the substation while assembly produces 22 rows, because `S-14b`'s coupled
        // exchanger declares one momentum equation fewer than the table credits it with. A prediction
        // that disagreed with the system used to reach `DenseLu` and throw; now it is a diagnostic.
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-substation.fluid"));
        var result = await Loop().RunAsync(
            GraphFixture.Bind(source), Water.Instance, "substation", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("22 equations for 23 unknowns", result.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APumpOnNoLoopIsSizedToZeroAndToldWhichThingIsMissing()
    {
        // `C-57`, end to end. `Circuit` returns null rather than zero for a component no cycle contains,
        // and that is the whole reason the rule can say "no closed circuit" instead of blaming a loss
        // nobody modelled. PU1 is declared and never connected here, which is the sample's stated point.
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m1-syntax-reference.fluid"));
        var prepared = Loop().Prepare(GraphFixture.Bind(source), Water.Instance, "reference");

        Assert.Equal(0.0, prepared.Sizes.For("PU1", "head"));
        Assert.Contains("no closed circuit", prepared.Bases["PU1.head"], StringComparison.Ordinal);
        Assert.Contains("no closed circuit", Assert.Single(prepared.Notes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoPumpsInSeriesDoNotConvergeYet()
    {
        // `S-29`, pinned so that fixing it shows up as a failing test rather than as nothing at all.
        // Two pumps in series is the one thing separating this from a circuit that converges in five
        // iterations at the same 14 unknowns, and the second head is stated, so sizing is not involved.
        var result = await Loop().RunAsync(
            GraphFixture.Bind(SeriesPumps), Water.Instance, "series", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.False(result.Value.Solve.Converged, "S-29 is fixed -- delete this test and its note.");
    }
}
