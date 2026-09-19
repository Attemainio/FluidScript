using System.Collections.Immutable;

using FluidScript.Core.Catalogs;
using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Sizing;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Fixtures;
using FluidScript.Core.Units;

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
            new CatalogBoreLookup(resolved.Value),
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

        // A-4: the run's iterations are the sum over its passes, the last pass's count being one term of it.
        Assert.True(run.Passes > 1, "the sizing loop is expected to take more than one pass here");
        Assert.True(run.Iterations > run.Solve.Iterations, $"{run.Iterations} over {run.Passes} passes against {run.Solve.Iterations} on the last");

        // `24`'s worked example, reached rather than transcribed: DN25 after DN15 and DN20 miss the
        // 150 Pa/m target at the flow HE1's duty fixes.
        Assert.Equal(25.0, run.Sizes.For("N5__N1", "dn")!.Value);
        Assert.Contains("DN25", run.Bases["N5__N1.dn"], StringComparison.Ordinal);
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

        // C-74, L-21: a cap the sizes have not settled under is said as FS2301, naming what moved,
        // and the last pass's own findings still reach the run rather than being lost with the pass.
        var capped = await RunAsync("m2-simple-loop.fluid", maxPasses: 1);

        Assert.False(capped.Settled);
        var notSettled = Assert.Single(capped.Solve.Diagnostics, static d => d.Code == "FS2301");
        Assert.Contains("Sizes did not settle for ", notSettled.Message, StringComparison.Ordinal);
        Assert.Contains(".", notSettled.Message.Split(" for ")[1], StringComparison.Ordinal);
        Assert.DoesNotContain(run.Solve.Diagnostics, static d => d.Code == "FS2301");
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

        N1 inlet t=20 p=300
        N4 outlet p=280
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
        // this sample reaches the Newton iteration.
        //
        // What is asserted here is the shape: the table's prediction and the assembly agree, so the run
        // gets past the guard rather than being refused by it. That it also converges, and to 01's
        // figures, is `P4.1`'s claim and `RatedExchangerSolveTests` holds it.
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
    public async Task TwoPumpsInSeriesConvergeAndTheSecondSuctionIsNamedAsUnderVacuum()
    {
        // `S-29`, closed by `D-121`. The ring's datum is PU1's suction at 0 gauge (`D-98`), and PU2
        // discharges into it, so PU2's own suction sits its 3 m head below the datum: 29.4 kPa under
        // atmospheric, 72 kPa absolute. That is liquid water, and the solve reaches it now that the
        // property floor is the triple point rather than the atmosphere; until then the true solution
        // lay outside the table and the line search halved against it to NonFinite. What the relative
        // figures cannot say -- that filled at 0 gauge at N1 the plant would draw a vacuum at N6 --
        // FS2221 says, with the fill pressure practice would state: 29.4 + 50 rounded up to tens.
        var result = await Loop().RunAsync(
            GraphFixture.Bind(SeriesPumps), Water.Instance, "series", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged, result.Value.Solve.Termination.ToString());

        var run = result.Value;
        var layout = SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        Assert.Equal(0.0, solved[Index(layout, UnknownKind.NodePressure, "N1")], 1.0);
        Assert.Equal(-998 * 9.80665 * 3, solved[Index(layout, UnknownKind.NodePressure, "N6")], 100.0);

        var vacuum = Assert.Single(run.Solve.Diagnostics, static d => d.Code == "FS2221");

        Assert.Equal(FluidScript.Core.Diagnostics.DiagnosticSeverity.Warning, vacuum.Severity);
        Assert.Equal("N6", vacuum.ComponentName);
        Assert.Equal(
            "'N6' is 29 kPa below atmospheric pressure. State a pressure on 'N1' of at least 80 kPa.",
            vacuum.Message);
    }

    [Fact]
    public async Task ALoopWhoseEveryNodeSitsAboveItsDatumIsNotToldToFillIt()
    {
        // The other half of FS2221: one pump, so the datum at its suction is the loop's low point and
        // nothing is under vacuum. The corpus's closed samples are all of this shape and none gained
        // the warning.
        var result = await Loop().RunAsync(
            GraphFixture.Bind(SimpleLoop), Water.Instance, "simple", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged);
        Assert.DoesNotContain(result.Value.Solve.Diagnostics, static d => d.Code == "FS2221");
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

        Assert.Equal(25.0, run.Sizes.For("N5__N1", "dn"));
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

    [Fact]
    public async Task TheCoolingLoopMixesTo20DegreesAndRecirculatesTheFiguresFlow()
    {
        // **`01`'s cooling loop, reached by the pipeline.** 0.2392 kg/s round the secondary, 0.1630 drawn
        // from the 6 C primary, and 0.0763 kg/s of 50 C return recirculated through the valve's `a` leg to
        // make the 20 C the exchanger states at its inlet (`23`'s branch table names the legs). The mixing
        // node `N2` is where the three meet, so its temperature is the whole of the criterion: 20 C, between
        // the 6 C supply and the 50 C return.
        var run = await RunAsync("m2-cooling-loop.fluid");

        Assert.True(run.Solve.Converged, $"stopped at {run.Solve.Termination}.");
        Assert.True(run.Settled, $"sizes were still moving after {run.Passes} passes.");

        var layout = SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        double Flow(string branch) =>
            Math.Abs(solved[Index(layout, UnknownKind.BranchFlow, branch)]);

        Assert.Equal(ReferenceNumbers.CoolingLoop.RecirculationFlow, Flow("3WV.a->N2"), 0.001);
        Assert.Equal(ReferenceNumbers.CoolingLoop.PrimaryFlow, Flow("N1->N2"), 0.001);
        Assert.Equal(ReferenceNumbers.CoolingLoop.SecondaryFlow, Flow("3WV.ab->N2"), 0.001);

        double Celsius(string node) =>
            Water.Instance.FromPressureEnthalpy(
                    Quantity.FromSi(solved[Index(layout, UnknownKind.NodePressure, node)], Dimension.Pressure),
                    Quantity.FromSi(solved[Index(layout, UnknownKind.NodeEnthalpy, node)], Dimension.Enthalpy))
                .Value.Temperature.SiValue - 273.15;

        Assert.Equal(20.0, Celsius("N2"), 0.05);
        Assert.Equal(6.0, Celsius("N1"), 0.05);
        Assert.Equal(50.0, Celsius("N3"), 0.1);
    }

    [Fact]
    public async Task AMixingValveDeliversTheInflowWeightedEnthalpyAndNotTheAverage()
    {
        // **`S-58`'s rule, checked by hand on the solved header.** A junction element's outlet carries
        // Σ ṁᵢhᵢ / Σ ṁᵢ over the ports that flow in -- what a mixing tee does. Until `S-58` the weight was
        // a 0-to-1 step, so any two inflows above a gram per second were averaged, and 0.167 kg/s of
        // 80 C with 0.063 kg/s of 30 C delivered 55 C where the mix is 66 C. On the header `TV_AHU`
        // blends 0.1914 kg/s of 60 C header water with 0.0957 kg/s of its own 30 C return: the
        // mass-weighted mix is 50 C and the plain average 45 C, so the two rules are 5 K apart here.
        var run = await RunAsync("m2-distribution-header.fluid");

        Assert.True(run.Solve.Converged, $"stopped at {run.Solve.Termination}.");

        var layout = SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        double Flow(string branch) => Math.Abs(solved[Index(layout, UnknownKind.BranchFlow, branch)]);
        double Enthalpy(string node) => solved[Index(layout, UnknownKind.NodeEnthalpy, node)];

        var hot = Flow("TV_AHU.a->N3");
        var cold = Flow("TV_AHU.b->NM_AHU");
        var mixed = (hot * Enthalpy("N3__TV_AHU__out") + cold * Enthalpy("NM_AHU")) / (hot + cold);
        var average = (Enthalpy("N3__TV_AHU__out") + Enthalpy("NM_AHU")) / 2;

        Assert.Equal(hot + cold, Flow("TV_AHU.ab->NM_AHU"), 1e-6);
        Assert.Equal(mixed, Enthalpy("TV_AHU__PU_AHU"), 1.0);
        Assert.True(Math.Abs(average - mixed) > 15_000, "the two rules must be told apart by this circuit");
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

        var owners = string.Join(", ", layout.Unknowns.Where(u => u.Kind == kind).Select(static u => u.OwnerComponentId).Distinct(StringComparer.Ordinal));

        throw new Xunit.Sdk.XunitException(
            $"No {kind} unknown owned by {owner}{(name is null ? "" : $" named {name}")}; the layout has {owners}.");
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

    private const string BivalentPair = """
        fluidscript 1
        design tout=-26
        curve heating tout
        -26  50
         20   0

        circuit heating
        fluid water

        HP1  heater power=heating sized_at tout=-5
        BL1  heater
        LOAD load power=heating in=70 out=40
        PU1  pump
        P1   pipe length=20 dn=32

        connections
        N1 - PU1 - HP1 - N2 - BL1 - N3 - LOAD - P1 - N1
        """;

    [Fact]
    public async Task ABivalentPairSplitsTheDesignDayAtTheHeatPumpsOwnPoint()
    {
        // `D-94`: the heat pump reads the heating curve at its own −5, not the file's −26, so its
        // stated power is its capacity at the bivalent point: 50 × 25 / 46 = 27.174 kW. The load reads
        // the same curve at −26 and asks 50 kW; the boiler wrote nothing, so the closed-circuit balance
        // gives it the difference. Hand figures: 0.3987 kg/s round the loop from 50 kW over 70→40 K,
        // the heat pump lifting 40 → 56.3 °C and the boiler 56.3 → 70 °C.
        var result = await Loop().RunAsync(
            GraphFixture.Bind(BivalentPair), Water.Instance, "bivalent", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged);

        var run = result.Value;

        Assert.Equal(22_826, run.Sizes.For("BL1", "power")!.Value, 1.0);
        Assert.Contains("22.83 kW", run.Bases["BL1.power"], StringComparison.Ordinal);
        Assert.Equal(
            "27.174 kW at tout=-5, 0.54 of the 50 kW the design day asks", run.Bases["HP1.power"]);
        Assert.Null(run.Sizes.For("HP1", "power"));

        var layout = SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        Assert.Equal(0.3984, Math.Abs(solved[layout.BranchFlow(0)]), 0.001);

        // Between the two heaters the water sits at 56.3 °C: 40 + 27.174 / (0.3984 × 4.18), which is
        // 235.8 kJ/kg of enthalpy above the 0 °C liquid datum.
        Assert.Equal(235_844, solved[Index(layout, UnknownKind.NodeEnthalpy, "N2")], 500.0);
    }

    private const string RoofLoop = """
        fluidscript 1
        circuit heating
        fluid water

        HE1  heat_exchanger power=30 in=20 out=50
        LOAD heat_exchanger power=-30 dp=0 elevation=32
        CV1  valve
        PU1  pump
        P1   pipe length=32
        P2   pipe length=32

        connections
        N1 - PU1 - N2 - HE1 - N3 - P1 - N4 - LOAD - N5 - CV1 - N6 - P2 - N1

        N1 node p=450
        """;

    [Fact]
    public async Task ALoadOnTheRoofCostsThePumpNothingAndTheWaterThreeHundredJoulesOnTheWayUp()
    {
        // `D-70`: the load states 32 m and nothing else does, so P1 rises 32 m and P2 falls 32 m. Round
        // the loop the two hydrostatic terms cancel and the pump sees friction alone -- the same 5.3 m
        // the flat loop needs. Between the bottom and the top of the riser the pressure falls by
        // rho*g*32 = 313 kPa on top of friction, and the enthalpy by g*32 = 314 J/kg with no change of
        // temperature: the loss is pv, not heat (`D-69`).
        var result = await Loop().RunAsync(
            GraphFixture.Bind(RoofLoop), Water.Instance, "roof", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged);

        var run = result.Value;
        var layout = SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        Assert.InRange(run.Sizes.For("PU1", "head") ?? solved[layout.PromotionOffset], 5.0, 5.6);

        var bottom = solved[Index(layout, UnknownKind.NodePressure, "N3")];
        var top = solved[Index(layout, UnknownKind.NodePressure, "N4")];

        // 988 kg/m3 at the riser's 50 C: 310.1 kPa of static head plus 32 m at 89.9 Pa/m of friction.
        Assert.Equal((988 * 9.80665 * 32) + (32 * 89.9), bottom - top, 1_500.0);
        Assert.Equal(
            9.80665 * 32,
            solved[Index(layout, UnknownKind.NodeEnthalpy, "N3")] - solved[Index(layout, UnknownKind.NodeEnthalpy, "N4")],
            1.0);
    }

    [Fact]
    public async Task ABareLinkDownFromTheRoofCarriesTheStaticHeadAndSizingCountsIt()
    {
        // The return from the roof is N5 - N6, a bare connection: D-25's ideal link with D-70's
        // hydrostatic term. Sizing has to count the same 313 kPa the assembler writes, or the pump is
        // sized to the riser alone: measured at 45.8 m before `BranchResistance.Along` walked links.
        var script = RoofLoop.Replace(
            "N1 - PU1 - N2 - HE1 - N3 - P1 - N4 - LOAD - N5 - CV1 - N6 - P2 - N1",
            "N1 - PU1 - N2 - HE1 - N3 - P1 - N4 - LOAD - N5\nN5 - N6\nN6 - CV1 - N7 - P2 - N1",
            StringComparison.Ordinal);

        var result = await Loop().RunAsync(
            GraphFixture.Bind(script), Water.Instance, "link", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged);

        var run = result.Value;
        var layout = SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        Assert.InRange(solved[layout.PromotionOffset], 5.0, 5.6);
        Assert.Equal(
            998 * 9.80665 * 32,
            solved[Index(layout, UnknownKind.NodePressure, "N6")] - solved[Index(layout, UnknownKind.NodePressure, "N5")],
            1_000.0);
        Assert.Equal(
            9.80665 * 32,
            solved[Index(layout, UnknownKind.NodeEnthalpy, "N6")] - solved[Index(layout, UnknownKind.NodeEnthalpy, "N5")],
            1.0);
    }

    [Fact]
    public async Task AnOpenRiserSpendsTheStaticHeadFirstAndFrictionWarmsTheWater()
    {
        // 300 kPa in at the bottom, 150 kPa out 10 m up, one DN25 pipe between. 98 kPa of the 150
        // available goes to lifting the water, 52 kPa to friction, which DN25 passes at 2.0 kg/s. The
        // enthalpy falls by g*10 = 98 J/kg; the pv term fell by 150 J/kg, so u rose by the 52 J/kg
        // dissipated -- +0.0125 K, the sign the Joule-Thomson coefficient of liquid water requires.
        var result = await Loop().RunAsync(
            GraphFixture.Bind("""
                fluidscript 1
                circuit heating
                fluid water

                P1   pipe length=10 dn=25

                connections
                N1 - P1 - N2

                N1 inlet t=20 p=300
                N2 outlet p=150 elevation=10
                """),
            Water.Instance,
            "open-riser",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged);

        var run = result.Value;
        var layout = SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        Assert.Equal(2.0, Math.Abs(solved[layout.BranchFlow(0)]), 0.05);
        Assert.Equal(
            -9.80665 * 10,
            solved[Index(layout, UnknownKind.NodeEnthalpy, "N2")] - solved[Index(layout, UnknownKind.NodeEnthalpy, "N1")],
            0.5);
    }

    // ---- M2a exit criteria that had no test (05) ----------------------------------------------------

    private const string SimpleLoop = """
        fluidscript 1
        circuit simpleLoop
        fluid water

        HE1  heat_exchanger power=30 in=20 out=50
        LOAD heat_exchanger power=-30 dp=0
        CV1  valve
        PU1  pump
        P1   pipe length=25

        connections
        N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5 - P1 - N1
        """;

    private static async Task<OuterLoopResult> Solve(string script, string name)
    {
        var result = await Loop().RunAsync(
            GraphFixture.Bind(script), Water.Instance, name, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged);

        return result.Value;
    }

    [Fact]
    public async Task APipeWithFourInternalNodesShowsAMonotonicProfileAlongItsLength()
    {
        // `05`: `nodes=4` is four thermal nodes and five hydraulic sub-pipes. In a steady solve with no
        // heat loss the enthalpy is one number end to end -- friction is isenthalpic -- and the pressure
        // falls in five equal steps. The temperature still moves: at constant h a falling p converts pv
        // into u, so it *rises* along the run, by dP/(rho*cp) = 33.7 kPa / (998 * 4184) = 8 mK. That is
        // the sign the Joule-Thomson coefficient of liquid water gives, and the one a model carrying h
        // constant across a falling pressure would get wrong (`D-70`'s remark on friction).
        var run = await Solve(SimpleLoop.Replace("P1   pipe length=25", "P1   pipe length=25 nodes=4", StringComparison.Ordinal), "cells");
        var layout = SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        string[] along = ["N5", "P1#n1", "P1#n2", "P1#n3", "P1#n4", "N1"];
        var pressures = along.Select(node => solved[Index(layout, UnknownKind.NodePressure, node)]).ToArray();
        var enthalpies = along.Select(node => solved[Index(layout, UnknownKind.NodeEnthalpy, node)]).ToArray();

        for (var i = 1; i < along.Length; i++)
        {
            Assert.True(pressures[i] < pressures[i - 1], $"{along[i]} is not below {along[i - 1]}");
            Assert.Equal(pressures[0] - pressures[1], pressures[i - 1] - pressures[i], 1.0);
            Assert.Equal(enthalpies[0], enthalpies[i], 1e-6);
        }

        double Temperature(int i) => Water.Instance
            .FromPressureEnthalpy(
                Quantity.FromSi(pressures[i], Dimension.Pressure), Quantity.FromSi(enthalpies[i], Dimension.Enthalpy))
            .Value.Temperature.SiValue;

        var temperatures = Enumerable.Range(0, along.Length).Select(Temperature).ToArray();

        for (var i = 1; i < along.Length; i++)
        {
            Assert.True(temperatures[i] >= temperatures[i - 1], $"the water cooled between {along[i - 1]} and {along[i]}");
        }

        Assert.Equal((pressures[0] - pressures[^1]) / (998 * 4184), temperatures[^1] - temperatures[0], 0.002);
    }

    [Fact]
    public async Task AStatedMinorLossAddsExactlyKTimesTheVelocityHeadAndAnOmittedOneAddsNothing()
    {
        // `05`: `minor_loss` contributes the stated K and nothing invents one. The two loops differ in
        // the one term K * v^2 / 2g: 0.2392 kg/s through DN25's 27.3 mm bore is 0.41 m/s, so K=5 is
        // 5 * 0.41^2 / 19.6 = 0.043 m on a 5.26 m head. The sizing basis names the absence.
        var plain = await Solve(SimpleLoop, "plain");
        var fitted = await Solve(SimpleLoop.Replace("P1   pipe length=25", "P1   pipe length=25 minor_loss=5", StringComparison.Ordinal), "fitted");

        static double Head(OuterLoopResult run)
        {
            var layout = SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);
            return run.Solve.Solution.Values[layout.PromotionOffset];
        }

        var velocity = 0.2392 / (998 * Math.PI * 0.0273 * 0.0273 / 4);

        Assert.Equal(5 * velocity * velocity / (2 * 9.80665), Head(fitted) - Head(plain), 0.002);

        var pipe = Assert.IsType<Pipe>(plain.Graph.Components.Single(static c => c.Name == "P1"));
        Assert.Equal(0, pipe.MinorLoss);
        Assert.True(pipe.DefaultParameters.ContainsKey("minor_loss"), "an omitted K is a visible default, not a sized value");
    }

    [Fact]
    public async Task APumpWithAKnownFlowAndNoResistanceSizesToZeroHeadAndSaysWhy()
    {
        // `05`: every connection ideal and both exchangers at dp=0, so the duty fixes 0.239 kg/s and
        // nothing resists it. The criterion imagined the *sizer* reaching zero and saying FS2312; what
        // happens is one step earlier: the duty's flow constraint promotes the head, the solver drives
        // it to its floor of 0, and FS3008 says so and names the resistance. The sizer never runs for a
        // promoted parameter, so `PumpSizer`'s own "no modelled resistance" note (`C-57`) is for the
        // un-promoted case only, and FS2312 itself is one of thirteen FS23xx codes `24` specifies that
        // nothing registers (`C-74`).
        var run = await Solve("""
            fluidscript 1
            circuit loop
            fluid water

            HE1  heat_exchanger power=30 in=20 out=50 dp=0
            LOAD heat_exchanger power=-30 dp=0
            PU1  pump

            connections
            N1 - PU1 - N2 - HE1 - N3 - LOAD - N1
            """, "no-resistance");

        var layout = SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);

        Assert.Equal(0, run.Solve.Solution.Values[layout.PromotionOffset], 1e-6);
        var floor = Assert.Single(run.Solve.Diagnostics, static d => d.Code == "FS3008");

        Assert.StartsWith("PU1.head was held at 0", floor.Message, StringComparison.Ordinal);
        Assert.Contains("the resistance", floor.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStatedPumpHeadIsSpentByTheBalancingValveAndNothingSizesTheValve()
    {
        // M2a, `R-02`, after `C-75`. The ring needs 5.28 m; the pump states 15. The stated head is a
        // constraint, so the duty's flow constraint falls to `CV1.kv` (`23`'s balancing-valve rule, dead
        // until `D-96`), and the valve closes on the surplus: 15 m is 146.8 kPa at 20 C, the exchanger
        // takes 20 kPa and DN25 at 0.2392 kg/s takes 2.5 kPa over 25 m, so the valve drops about 124 kPa
        // and by the Kv law 0.861 m3/h over sqrt(1.24 bar) is Kv 0.77 -- solved, not chosen. No rule
        // reports a Kv or an authority for it, and `FS3008` is silent: the iterate passed through Kv 0
        // on its way down from the bootstrap's 630, and a bound it passed through is not one it sits on
        // (`S-61`).
        var run = await Solve(SimpleLoop.Replace("PU1  pump", "PU1  pump head=15", StringComparison.Ordinal), "head15");

        var layout = SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        Assert.Equal("CV1.kv", Assert.Single(layout.Unknowns.Where(static u => u.Kind == UnknownKind.Parameter)).Name);
        Assert.Equal(0.773, solved[layout.PromotionOffset], 0.005);

        var pump = Assert.IsType<Pump>(run.Graph.Components.Single(static c => c.Name == "PU1"));
        Assert.Equal(15, pump.StatedParameters["head"].SiValue, 1e-9);

        Assert.DoesNotContain("CV1.kv", run.Bases.Keys);
        Assert.DoesNotContain("CV1.authority", run.Bases.Keys);
        Assert.True(run.Sizes.IsProvisional("CV1", "kv"), "the bootstrap Kv stays a placeholder, never a choice");
        Assert.DoesNotContain(run.Solve.Diagnostics, static d => d.Code == "FS3008");
    }

    // ---- a warm start that fails is discarded, and the loop says so (S-20, FS3012) -----------------

    [Fact]
    public async Task AWarmStartThatDoesNotConvergeIsDiscardedForTheSeedAndReported()
    {
        // 32's retry-from-seed rule, implemented where the two seeds both exist -- the outer loop, not
        // the solver. A solver that rejects whatever it is first handed stands in for a warm start in
        // the wrong basin; the loop throws it away, reruns from the sizing seed, and the run carries
        // FS3012 so the log can show the retry happened. Without a warm start the same solver's
        // refusal is the answer, and no FS3012 is invented.
        var resolved = PipeCatalogs.Resolve(pin: null);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);
        var model = GraphFixture.Bind(SimpleLoop);

        var cold = await Loop().RunAsync(model, Water.Instance, "warm", TestContext.Current.CancellationToken);
        Assert.True(cold.IsSuccess, cold.Error?.Message);
        var warmStart = new WarmStart(cold.Value.Solve.Solution, cold.Value.TopologyHash);

        var loop = new OuterLoop(new RejectsFirstCall(), new CatalogBoreLookup(resolved.Value), OuterLoop.Rules(resolved.Value.Catalog), 10);
        var warm = await loop.RunAsync(model, Water.Instance, warmStart, "warm", TestContext.Current.CancellationToken);

        Assert.True(warm.IsSuccess, warm.Error?.Message);
        Assert.True(warm.Value.Solve.Converged);
        Assert.Single(warm.Value.Solve.Diagnostics, static d => d.Code == "FS3012");
        Assert.Equal(cold.Value.Passes + 1, warm.Value.Passes); // the discarded pass ran, and is counted (A-4)

        var noWarm = await new OuterLoop(new RejectsFirstCall(), new CatalogBoreLookup(resolved.Value), OuterLoop.Rules(resolved.Value.Catalog), 10)
            .RunAsync(model, Water.Instance, "cold", TestContext.Current.CancellationToken);

        Assert.True(noWarm.IsSuccess, noWarm.Error?.Message);
        Assert.False(noWarm.Value.Solve.Converged);
        Assert.DoesNotContain(noWarm.Value.Solve.Diagnostics, static d => d.Code == "FS3012");
    }

    /// <summary>Refuses the first system it is handed as diverging, and is Newton for every one after.</summary>
    private sealed class RejectsFirstCall : ISolver
    {
        private readonly NewtonSolver inner = new();
        private bool first = true;

        public string Name => inner.Name;

        public Result<Unit> CanSolve(EquationSystem system) => inner.CanSolve(system);

        public Task<SolveResult> SolveAsync(EquationSystem system, StateVector initialGuess, IProgress<SolveProgress>? progress, CancellationToken cancellationToken)
        {
            if (!first)
            {
                return inner.SolveAsync(system, initialGuess, progress, cancellationToken);
            }

            first = false;
            return Task.FromResult(new SolveResult
            {
                Converged = false,
                Solution = initialGuess,
                Iterations = 1,
                ResidualNorm = double.PositiveInfinity,
                Termination = SolveTermination.Diverging,
                WorstResiduals = [],
                Diagnostics = [],
            });
        }
    }
}