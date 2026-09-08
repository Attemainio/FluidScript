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
        // `24` says two passes for this circuit -- one to size, one to confirm -- because discreteness
        // stabilises the loop. It is four, and the extra two are a chain the document did not have when
        // it wrote that: the exchanger's design flow comes from the solve, its drop then changes the
        // branch, the valve's Kv is sized against that branch, and the valve's drop changes the flow
        // again. Each link costs a pass.
        //
        // Four of a cap of ten, and `Settled` is the assertion that matters -- it says the sizes stopped
        // moving rather than that the cap ran out. Discreteness is still what ends it: DN25 stays DN25
        // and Kv 1.6 stays Kv 1.6 while the continuous values underneath them are still settling.
        var run = await RunAsync("m2-simple-loop.fluid");

        Assert.True(run.Settled, $"sizes were still moving after {run.Passes} passes.");
        Assert.InRange(run.Passes, 1, 5);
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
    public async Task TheSubstationAssemblesASquareSystemAndReachesTheSolver()
    {
        // **`S-14b`, closed.** Two branches cross `HX1`, so the counting table credited it with two
        // pressure relations while it declared one -- 23 counted, 22 assembled, and a circuit that could
        // not be handed to a solver at all. Side 2 has a momentum relation now, the two agree at 23, and
        // this sample reaches the Newton iteration for the first time.
        //
        // It does not converge yet, and that is a different defect from this one. What is asserted here
        // is the shape: the table's prediction and the assembly agree, so the run gets past the guard
        // rather than being refused by it.
        //
        // Note also what this removes: `S-28`'s second guard -- the one comparing `Rows` to `Columns` on
        // the built system -- no longer has a sample that trips it. It stays as a backstop, because the
        // reason it was written is that a counting table is a prediction and predictions can be wrong
        // again; but nothing in the corpus proves it fires, and that is worth knowing.
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-substation.fluid"));
        var result = await Loop().RunAsync(
            GraphFixture.Bind(source), Water.Instance, "substation", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);

        var counting = CoreTopology.WellPosedness.Check(result.Value.Graph).Counting;

        Assert.Equal(counting.Unknowns, counting.Equations);
        Assert.Equal(0, counting.Excess);
        Assert.True(result.Value.Solve.Iterations > 0, "the solver never ran.");
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
        // One note among others now: `C-60` adds its own for `3WV`, whose `kv` no rule can size.
        Assert.Contains(
            prepared.Notes,
            static note => note.Contains("no closed circuit", StringComparison.Ordinal));
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

    [Fact]
    public async Task TheSimpleLoopReproducesTheWorkedExampleEndToEnd()
    {
        // **`24`'s worked example, reached by the pipeline rather than by hand.** Every number in it is
        // now produced by a rule reading the solved field: DN25 at 99.8 Pa/m, Kv 1.6 dropping 29.0 kPa,
        // a 51.5 kPa ring and 5.26 m of head. The document says Kv 1.6, 29.32 kPa, 51.67 kPa and 5.28 m.
        //
        // The residual is entirely the density basis: `24` works its example at the loop's 35 C mean and
        // every rule here works at the component's own inlet. 0.4 % on the density, and it is the last
        // difference left between the document and the tool on this circuit -- which is what makes it
        // worth writing down rather than widening a tolerance until it disappears.
        var run = await RunAsync("m2-simple-loop.fluid");

        Assert.True(run.Solve.Converged, $"stopped at {run.Solve.Termination}.");
        Assert.True(run.Settled, $"sizes were still moving after {run.Passes} passes.");

        Assert.Equal(25.0, run.Sizes.For("P1", "dn"));
        Assert.Equal(1.6, run.Sizes.For("CV1", "kv"));
        Assert.Contains("Kv 1.6", run.Bases["CV1.kv"], StringComparison.Ordinal);
        Assert.Contains("achieved against a target", run.Bases["CV1.authority"], StringComparison.Ordinal);

        // Rounding down raises authority above the target, always.
        Assert.True(run.Sizes.For("CV1", "authority") > 0.5);

        // The head is promoted rather than sized, so it is the solver's answer to the same question the
        // pump rule would have answered -- and it lands on the document's number either way.
        var posedness = CoreTopology.WellPosedness.Check(run.Graph);
        var layout = SystemLayout.Build(run.Graph, posedness.Counting);
        var head = run.Solve.Solution.Values[layout.PromotionOffset];

        Assert.Equal("PU1.head", Assert.Single(posedness.Counting.Promotions).Label);
        Assert.Equal(5.28, head, 0.03);
    }

    [Fact]
    public async Task EveryExchangerResistsFlowAndSaysAtWhatFlowItWasMeasured()
    {
        // A `dp` is not a resistance until something says at what flow, so `ExchangerSizer` pairs the
        // decided 20 kPa with the flow the circuit runs at. `LOAD` states `dp=0` because it is the
        // device that closes the energy balance rather than a physical block, and nothing in the
        // language can tell those apart -- so the fiction has to say so (`C-59`).
        var run = await RunAsync("m2-simple-loop.fluid");

        Assert.Equal(0.2392, run.Sizes.For("HE1", "flow")!.Value, 0.002);
        Assert.Contains("20 kPa is measured at", run.Bases["HE1.flow"], StringComparison.Ordinal);
        Assert.Contains("0 kPa is measured at", run.Bases["LOAD.flow"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoComponentCarriesAValueNoParameterMapRecords()
    {
        // `C-58`. `ComponentFactory` built every valve with a literal `?? 1` that reached the component
        // and none of the three maps, so `IsFree` called `kv` free and promotable while lowering had
        // `C-58`. `ComponentFactory` built every valve with a literal `?? 1` that reached the component
        // and none of the three maps, so `IsFree` called `kv` free and promotable while lowering had
        // already chosen it. `D-02` allows sizing or a visible decided default and allows nothing else,
        // so every parameter a component resolves must now appear somewhere a reader can find it.
        var run = await RunAsync("m2-simple-loop.fluid");
        var valve = Assert.Single(run.Graph.Components.Where(static c => c.Kind == "valve"));

        Assert.Contains("kv", valve.SizedParameters.Keys);
        Assert.Equal(1.6, valve.SizedParameters["kv"].SiValue);
        Assert.DoesNotContain("kv", valve.StatedParameters.Keys);
    }
}
