using System.Collections.Immutable;

using FluidScript.Core.Catalogs;
using FluidScript.Core.Components;
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

    // `C-62`. A two-way valve on a path between two stated pressures with no pump anywhere: the
    // driving pressure is fixed, so the valve takes what the rest of the path leaves and the catalogue
    // selection has to round *up*. `Offered` needs exactly two stated pressures and `Driven` needs no
    // free pump on a circuit through the valve, and this shape gives both.
    private const string BoundedValve = """
        fluidscript 1
        circuit district
        fluid water

        HE1 heat_exchanger power=30
        CV1 valve
        P1  pipe length=25 dn=25

        connections
        N1 - HE1 - N2 - CV1 - N3 - P1 - N4

        N1 supply t=20 p=300
        N4 return p=280
        """;

    [Fact]
    public async Task ATwoWayValveOnABoundedPathIsToldWhatTheBoundariesOfferAndRoundsUp()
    {
        // The branch `C-62` opened. `SizingContext.AvailableDrop` used to be filled only by the
        // three-way pass, so an ordinary `valve` was never told the circuit was pressure-bounded,
        // `ValveSizer`'s `bounded` arithmetic could not run for one, and it rounded down --- which at a
        // fixed differential puts the design flow out of reach at every position rather than making the
        // valve safer. `OuterLoop.Context` fills it now, from the same `Driven`/`Offered` pair the
        // three-way pass uses.
        var result = await Loop().RunAsync(
            GraphFixture.Bind(BoundedValve), Water.Instance, "district", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);

        var basis = result.Value.Bases["CV1.kv"];

        Assert.Contains("boundaries offer", basis, StringComparison.Ordinal);
        Assert.Contains("rounds up", basis, StringComparison.Ordinal);

        // And the pump-driven wording is *absent*, which is the half that would have passed before.
        Assert.DoesNotContain("free pump absorbs", basis, StringComparison.Ordinal);
    }

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
    public async Task TheDistributionHeaderReproducesTheVisionsFiguresEndToEnd()
    {
        // **`01`'s distribution header, reached by the pipeline rather than by hand.** Each coil's flow is
        // its duty over its own 20 K; each header draw is the same duty over the header's 30 K, because the
        // valve makes up the rest from the coil's own 30 C return; the source carries the two draws, which
        // is also 54 kW over its 30 K rise. `ReferenceNumbers.DistributionHeader` holds the figures and
        // `ReferenceNumberConsistencyTests` checks they agree with each other; this checks the solver
        // agrees with them.
        //
        // The positions are the part worth a line: strictly inside the travel. Until `S-58` a junction
        // delivered the plain average of its inlet enthalpies whatever the split, so the position column
        // moved nothing a constraint could feel and Newton ran both valves to a stop.
        var run = await RunAsync("m2-distribution-header.fluid");

        Assert.True(run.Solve.Converged, $"stopped at {run.Solve.Termination}.");
        Assert.True(run.Settled, $"sizes were still moving after {run.Passes} passes.");

        var layout = SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        double Flow(string branch) =>
            Math.Abs(solved[Index(layout, UnknownKind.BranchFlow, branch)]);

        Assert.Equal(ReferenceNumbers.DistributionHeader.AhuBranchFlow, Flow("TV_AHU.ab->NM_AHU"), 0.001);
        Assert.Equal(ReferenceNumbers.DistributionHeader.RadiatorBranchFlow, Flow("TV_RAD.ab->NM_RAD"), 0.001);
        Assert.Equal(ReferenceNumbers.DistributionHeader.AhuHeaderFlow, Flow("TV_AHU.a->N3"), 0.001);
        Assert.Equal(ReferenceNumbers.DistributionHeader.RadiatorHeaderFlow, Flow("TV_RAD.a->N3"), 0.001);
        Assert.Equal(ReferenceNumbers.DistributionHeader.SourceFlow, Flow("N3->N5"), 0.001);

        foreach (var valve in (string[])["TV_AHU", "TV_RAD"])
        {
            var position = solved[Index(layout, UnknownKind.Parameter, valve, $"{valve}.position")];

            Assert.InRange(position, 0.05, 0.95);
        }

        foreach (var pump in (string[])["PU_AHU", "PU_RAD"])
        {
            Assert.True(
                solved[Index(layout, UnknownKind.Parameter, pump, $"{pump}.head")] > 0,
                $"{pump} runs backwards.");
        }
    }

    private static int Index(SystemLayout layout, UnknownKind kind, string owner, string? name = null)
    {
        // By label rather than by position, so a change in how the layout orders its unknowns cannot make
        // this test read a different branch and still pass.
        for (var index = 0; index < layout.Unknowns.Length; index++)
        {
            var unknown = layout.Unknowns[index];

            if (unknown.Kind == kind
                && string.Equals(unknown.OwnerComponentId, owner, StringComparison.Ordinal)
                && (name is null || string.Equals(unknown.Name, name, StringComparison.Ordinal)))
            {
                return index;
            }
        }

        throw new Xunit.Sdk.XunitException($"No {kind} unknown owned by {owner}{(name is null ? "" : $" named {name}")}.");
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

    private const string OneUnstatedSourceAndOneLoad = """
        fluidscript 1
        circuit heating
        fluid water

        SOURCE heater in=30 out=80
        LOAD   load power=20 in=80 out=30
        PU1    pump
        P1     pipe length=10 dn=25

        connections
        N1 - PU1 - SOURCE - N2 - LOAD - P1 - N1
        """;

    private const string OneUnstatedSourceAndTwoLoads = """
        fluidscript 1
        circuit heating
        fluid water

        SOURCE heater in=30 out=80
        LOAD1  load power=20
        LOAD2  load power=20
        PU1    pump
        P1     pipe length=10 dn=25

        connections
        N1 - PU1 - SOURCE - N2 - LOAD1 - N3 - LOAD2 - P1 - N1
        """;

    private const string DistributedHeaderWithAutomaticSourceDuty = """
        fluidscript 1
        project static plant_01

        circuit heating
        fluid water

        PB      pipe length=4 dn=32
        SOURCE  heater out=80
        TV_MAIN three_way_valve kv=25

        connections
        N1 - SOURCE - TV_MAIN.a
        N1 - PB - TV_MAIN.b
        TV_MAIN.ab - N3

        N1 node p=250
        N3 node t=60

        circuit AHU

        HE_AHU load in=50 out=30 power=20 kW
        TV_AHU three_way_valve kv=25
        PU_AHU pump
        PA1 pipe length=12 dn=25
        PA2 pipe length=12 dn=25

        connections
        N3 - PA1 - TV_AHU.a
        NM_AHU - TV_AHU.b
        TV_AHU.ab - PU_AHU - HE_AHU - NM_AHU
        NM_AHU - PA2 - N1

        circuit radiators

        HE_RAD load in=50 out=30 power=20 kW
        TV_RAD three_way_valve kv=25
        PU_RAD pump
        PR1 pipe length=18 dn=25
        PR2 pipe length=18 dn=25

        connections
        N3 - PR1 - TV_RAD.a
        NM_RAD - TV_RAD.b
        TV_RAD.ab - PU_RAD - HE_RAD - NM_RAD
        NM_RAD - PR2 - N1
        """;

    [Fact]
    public async Task TheDistributedHeaderFindsItsFortyKilowattSourceDutyAndConverges()
    {
        // `S-55`'s acceptance test, written before its fix. The fixture has no source pump on purpose: each
        // distribution pump draws from the shared supply and discharges to the shared return, which is the
        // pressure difference that drives both legs of `TV_MAIN`. The driver analysis looks for a pump on
        // one cycle-local loop, finds none, and reports `FS2214` on a circuit the equations can solve.
        // Skipped while that symptom is present rather than deleted, so the day the analysis is repaired
        // this runs -- and `S-55` says in as many words that adding a source pump to pass it is wrong.
        var model = GraphFixture.Bind(DistributedHeaderWithAutomaticSourceDuty);
        var result = await Loop().RunAsync(
            model,
            Water.Instance,
            "automatic-distribution-header",
            TestContext.Current.CancellationToken);
        var report = FluidScript.Core.Diagnostics.SolveExplanation.Render(
            result,
            GraphFixture.Lower(DistributedHeaderWithAutomaticSourceDuty).Graph,
            "automatic-distribution-header");

        Assert.SkipWhen(
            report.Contains("FS2214", StringComparison.Ordinal),
            "S-55: the driver analysis still reports FS2214 on the pump-free mixing header.");

        Assert.True(result.IsSuccess, report);
        Assert.True(result.Value.Solve.Converged, report);
        Assert.Equal(40_000.0, result.Value.Sizes.For("SOURCE", "power"));
    }

    [Theory]
    [InlineData(OneUnstatedSourceAndOneLoad, 20_000.0)]
    [InlineData(OneUnstatedSourceAndTwoLoads, 40_000.0)]
    public void TheOnlyUnstatedDutyClosesTheCircuitEnergyBalance(string script, double expectedPower)
    {
        var prepared = Loop().Prepare(GraphFixture.Bind(script), Water.Instance, "automatic-source-duty");

        Assert.Equal(expectedPower, prepared.Sizes.For("SOURCE", "power"));
        Assert.Contains("closed-circuit energy balance", prepared.Bases["SOURCE.power"], StringComparison.Ordinal);
    }

    private const string TwoPumpedSourcesThreePumpedConsumers = """
        fluidscript 1
        project static plant_02

        circuit heating 100
        fluid water

        HS_A    heater power=40 kW out=70
        HS_B    heater out=70
        PU_A    pump
        PU_B    pump
        PS_A    pipe length=6 dn=32
        PS_B    pipe length=6 dn=32

        connections
        N1 - PU_A - HS_A - PS_A - N3
        N1 - PU_B - HS_B - PS_B - N3
        N3 - N4
        N4 - N7
        N8 - N6
        N6 - N5
        N5 - N1

        N1 node p=250

        circuit AHU 101

        HE_AHU  load in=50 out=30 power=24 kW
        TV_AHU  three_way_valve
        PU_AHU  pump
        PA1     pipe length=12 dn=25
        PA2     pipe length=12 dn=25

        connections
        N3 - PA1 - TV_AHU.a
        NM_AHU - TV_AHU.b
        TV_AHU.ab - PU_AHU - HE_AHU - NM_AHU
        NM_AHU - PA2 - N5

        circuit radiators 102

        HE_RAD  load in=50 out=30 power=30 kW
        TV_RAD  three_way_valve
        PU_RAD  pump
        PR1     pipe length=18 dn=25
        PR2     pipe length=18 dn=25

        connections
        N4 - PR1 - TV_RAD.a
        NM_RAD - TV_RAD.b
        TV_RAD.ab - PU_RAD - HE_RAD - NM_RAD
        NM_RAD - PR2 - N6

        circuit dhw 103

        HE_DHW  load out=40 power=16 kW
        PU_DHW  pump
        PD1     pipe length=10 dn=25
        PD2     pipe length=10 dn=25

        connections
        N7 - PD1 - PU_DHW - HE_DHW - PD2 - N8
        """;

    [Fact]
    public async Task TwoPumpedSourcesShareALoadTheirConsumersSetAndEveryPumpKeepsItsOwnLoop()
    {
        // The fourth plant, built to exercise what the three reference circuits do not: two sources in
        // parallel, one of them with its duty left to the closed-circuit balance, feeding two mixing
        // consumers and one direct one. Every number below is a hand calculation at cp 4.18 kJ/kg K.
        //
        // Loads 24 + 30 + 16 = 70 kW, so `HS_B` closes at 30 kW. The mixing consumers draw their duty over
        // the header's 40 K (70 to 30 C): 0.1435 and 0.1794 kg/s. The direct consumer draws 16 kW over
        // 30 K: 0.1275 kg/s. The return header is the mass-weighted mix, (0.3229 x 30 + 0.1275 x 40) /
        // 0.4504 = 32.8 C, and each source then carries its duty over 70 - 32.8 = 37.2 K: 0.2573 and
        // 0.1930 kg/s, summing to the 0.4504 the consumers draw.
        //
        // Two things this plant found. `S-59`: the closure ran after the first count, so `HS_B.out`
        // promoted the power the closure was about to size, `PU_B` got sized meanwhile, and on the next
        // pass the constraint reached for `PU_AHU` and shifted every promotion after it by one. `D-93`:
        // `PU_A` was sized to a loop through a consumer's own pump, took 46.8 kPa, and the header
        // differential that made over-drove the direct consumer until its pump was asked for a negative
        // head. Sized to the loop it alone drives, it takes its own branch's 19 kPa and the header sits at
        // no differential -- a low-loss header without the component.
        var model = GraphFixture.Bind(TwoPumpedSourcesThreePumpedConsumers);
        var result = await Loop().RunAsync(
            model, Water.Instance, "two-pumped-sources", TestContext.Current.CancellationToken);
        var report = FluidScript.Core.Diagnostics.SolveExplanation.Render(
            result, GraphFixture.Lower(TwoPumpedSourcesThreePumpedConsumers).Graph, "two-pumped-sources");

        Assert.True(result.IsSuccess, report);

        var run = result.Value;

        Assert.True(run.Solve.Converged, report);
        Assert.True(run.Settled, report);
        Assert.Equal(30_000.0, run.Sizes.For("HS_B", "power"));

        // Each constraint on the pump that drives the flow it pins -- the cascade `S-59` produced put
        // `HS_B.out` on `PU_AHU.head` and left `HE_DHW.out` with nothing.
        var promoted = CoreTopology.WellPosedness.Check(run.Graph).Counting.Promotions
            .ToDictionary(static p => $"{p.Constraint.Component}.{p.Constraint.Parameter}", static p => p.Label, StringComparer.Ordinal);

        Assert.Equal("PU_B.head", promoted["HS_B.out"]);
        Assert.Equal("PU_AHU.head", promoted["HE_AHU.out"]);
        Assert.Equal("PU_RAD.head", promoted["HE_RAD.out"]);
        Assert.Equal("PU_DHW.head", promoted["HE_DHW.out"]);

        var layout = SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        double Flow(string branch) =>
            Math.Abs(solved[Index(layout, UnknownKind.BranchFlow, branch)]);

        var sources = run.Graph.Branches
            .Where(static branch => branch.Path.Any(static element => element.Name is "HS_A" or "HS_B"))
            .ToDictionary(
                static branch => branch.Path.Single(static element => element.Name is "HS_A" or "HS_B").Name,
                branch => Math.Abs(solved[layout.BranchFlow(branch.Index)]),
                StringComparer.Ordinal);

        Assert.Equal(0.2573, sources["HS_A"], 0.001);
        Assert.Equal(0.1930, sources["HS_B"], 0.001);
        Assert.Equal(0.1435, Flow("TV_AHU.a->N3"), 0.001);
        Assert.Equal(0.1794, Flow("TV_RAD.a->N4"), 0.001);
        Assert.Equal(0.1275, Flow("N4->N6"), 0.001);

        // The direct consumer's pump develops its own branch's drop, which is the sign `D-93` exists for:
        // with the source pump sized to a loop through a consumer it was held at zero and 6 kPa short.
        Assert.InRange(solved[Index(layout, UnknownKind.Parameter, "PU_DHW", "PU_DHW.head")], 1.5, 3.0);
        Assert.InRange(run.Sizes.For("PU_A", "head")!.Value, 1.5, 2.5);
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

    [Fact]
    public async Task AComponentTheWaterRunsThroughBackwardsIsNamedAfterTheSolve()
    {
        // `L-47`. The simple loop with `LOAD` written the other way round: `N4 - LOAD - N3` puts its `in`
        // on `N4`, and `PU1` drives the water `N3` to `N4`, so it enters at `out`. The solve is unchanged --
        // a load takes its heat out of whichever node it discharges into (`D-69`) -- and that is exactly
        // why the reversal is worth a warning: nothing else in the answer would show it.
        const string reversed = """
            fluidscript 1
            circuit loop
            fluid water
            HE1  heat_exchanger power=30 in=20 out=50
            LOAD heat_exchanger power=-30 dp=0
            CV1  valve
            PU1  pump
            P1   pipe length=25
            connections
            N1 - PU1 - N2 - HE1 - N3
            N4 - LOAD - N3
            N4 - CV1 - N5 - P1 - N1
            """;

        var result = await Loop().RunAsync(
            GraphFixture.Bind(reversed), Water.Instance, "reversed-load", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged);

        var warning = Assert.Single(result.Value.Solve.Diagnostics, static d => d.Code == "FS3013");

        Assert.StartsWith("LOAD carries 0.239 kg/s from 'out' to 'in'", warning.Message, StringComparison.Ordinal);

        // And the loop as written runs everything forwards, so the same code is absent there.
        var forwards = await RunAsync("m2-simple-loop.fluid");

        Assert.DoesNotContain(forwards.Solve.Diagnostics, static d => d.Code == "FS3013");
    }
}
