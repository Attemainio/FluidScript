using FluidScript.Core.Catalogs.Pipes;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Passes;
using FluidScript.Core.Solvers.Steady;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Tests.Solvers.Seeding;

/// <summary>
/// <c>S-53</c>'s acceptance: the header seed once copied a consumer's whole circulation onto both
/// legs of its mixing valve, so the coils were sized at twice their flow and the source at the sum
/// of the doubled coils. The fixes landed one at a time under <c>S-58</c>, <c>S-63</c> and
/// <c>S-68</c>; this pins the figures the entry asked for, on the entry's own script.
/// </summary>
[Trait("Category", "Unit")]
public sealed class HeaderSeedTests
{
    /// <summary>The no-bypass header: the source heats the return straight into the supply, two pumped consumers mix 80 °C down to 50 °C.</summary>
    private const string NoBypassHeader = """
        fluidscript 1
        project static plant_01

        circuit heating 100
        fluid water

        HS1     heat_exchanger power=54 kW out.t=80

        connections
        N1 - HS1 - N3
        N3 - N4
        N6 - N5
        N5 - N1

        N1 node p=250

        circuit AHU 101

        HE_AHU  load in.t=50 out.t=30 power=24 kW
        TV_AHU  three_way_valve
        PU_AHU  pump

        connections
        N3 - TV_AHU.a length=12 dn=25
        NM_AHU - TV_AHU.b
        TV_AHU.ab - PU_AHU - HE_AHU - NM_AHU
        NM_AHU - N5 length=12 dn=25

        circuit radiators 102

        HE_RAD  load in.t=50 out.t=30 power=30 kW
        TV_RAD  three_way_valve
        PU_RAD  pump

        connections
        N4 - TV_RAD.a length=18 dn=25
        NM_RAD - TV_RAD.b
        TV_RAD.ab - PU_RAD - HE_RAD - NM_RAD
        NM_RAD - N6 length=18 dn=25
        """;

    [Fact]
    public async Task TheNoBypassHeaderSizesItsCoilsAndItsSourceOnTheirOwnDuties()
    {
        // Water at 20 dK carries 24 kW at 0.2871 kg/s and 30 kW at 0.3589 kg/s; at 50 dK (80 → 30) the
        // source's 54 kW is 0.2581 kg/s. Each consumer draws from the header the share its mix needs --
        // (50 − 30)/(80 − 30) = 0.4 of its circulation -- and recirculates the rest.
        var result = await Loop().RunAsync(
            GraphFixture.Bind(NoBypassHeader), Water.Instance, "s53", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);

        var run = result.Value;
        var report = FluidScript.Core.Diagnostics.Explanations.SolveExplanation.Render(result, run.Graph, "s53");

        Assert.True(run.Solve.Converged, report);
        Assert.True(run.Settled, report);
        Assert.Equal(0.2871, run.Sizes.For("HE_AHU", "flow")!.Value, 3);
        Assert.Equal(0.3589, run.Sizes.For("HE_RAD", "flow")!.Value, 3);
        Assert.Equal(0.2581, run.Sizes.For("HS1", "flow")!.Value, 3);

        var layout = SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        Assert.Equal(0.2871, Through(run.Graph, layout, solved, "HE_AHU"), 3);
        Assert.Equal(0.3589, Through(run.Graph, layout, solved, "HE_RAD"), 3);
        Assert.Equal(0.2581, Through(run.Graph, layout, solved, "HS1"), 3);

        // A three-way valve partitions one circulation: |a| + |b| = |ab|, and neither leg is the whole.
        foreach (var valve in new[] { "TV_AHU", "TV_RAD" })
        {
            var a = Leg(run.Graph, layout, solved, valve, "a");
            var b = Leg(run.Graph, layout, solved, valve, "b");
            var ab = Leg(run.Graph, layout, solved, valve, "ab");

            Assert.Equal(ab, a + b, 4);
            Assert.InRange(a / ab, 0.3, 0.5);
            Assert.InRange(b / ab, 0.5, 0.7);
        }
    }

    /// <summary>The mixing-supply header (<c>S-55</c>): a source valve blends the 80 °C source with the return to the 60 °C the header states, and nothing but the consumer pumps moves anything.</summary>
    private const string MixingSupplyHeader = """
        fluidscript 1
        project static plant_01

        circuit heating 100
        fluid water

        HS1     heat_exchanger power=54 kW out.t=80
        TV_MAIN three_way_valve

        connections
        N1 - HS1 - TV_MAIN.a
        N1 - TV_MAIN.b
        TV_MAIN.ab - N3
        N3 node t=60
        N3 - N4
        N6 - N5
        N5 - N1

        N1 node p=250

        circuit AHU 101

        HE_AHU  load in.t=50 out.t=30 power=24 kW
        TV_AHU  three_way_valve
        PU_AHU  pump

        connections
        N3 - TV_AHU.a length=12 dn=25
        NM_AHU - TV_AHU.b
        TV_AHU.ab - PU_AHU - HE_AHU - NM_AHU
        NM_AHU - N5 length=12 dn=25

        circuit radiators 102

        HE_RAD  load in.t=50 out.t=30 power=30 kW
        TV_RAD  three_way_valve
        PU_RAD  pump

        connections
        N4 - TV_RAD.a length=18 dn=25
        NM_RAD - TV_RAD.b
        TV_RAD.ab - PU_RAD - HE_RAD - NM_RAD
        NM_RAD - N6 length=18 dn=25
        """;

    [Fact]
    public async Task ThePumpFreeMixingHeaderIsDrivenByItsConsumerPumpsAndSizesItsMainValve()
    {
        // S-55's acceptance, as the entry wrote it: no source pump -- adding one would change the plant,
        // not repair the analysis. The main valve's cycle (TV_MAIN.b → TV_MAIN.a → HS1 → N1) has no pump on
        // it and is driven by PU_AHU and PU_RAD through the block it shares with them; the driver check and
        // the valve's sizer both read the block now (HydraulicBlocks), so the graph is square and full
        // rank, FS2214 is silent, the main valve leaves the bootstrap Kv 630 for D-122's band, and each
        // consumer's head carries its own loss plus the shared source path at the combined operating point.
        var check = WellPosedness.Check(GraphFixture.Lower(MixingSupplyHeader).Graph);

        Assert.Equal(0, check.Counting.Excess);
        Assert.DoesNotContain(check.Diagnostics, static d => d.Code == "FS2214");

        var result = await Loop().RunAsync(
            GraphFixture.Bind(MixingSupplyHeader), Water.Instance, "s55", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);

        var run = result.Value;
        var report = FluidScript.Core.Diagnostics.Explanations.SolveExplanation.Render(result, run.Graph, "s55");

        Assert.True(run.Solve.Converged, report);
        Assert.True(run.Settled, report);
        Assert.DoesNotContain("FS2214", report, StringComparison.Ordinal);
        Assert.Equal(6.3, run.Sizes.For("TV_MAIN", "kv")!.Value, 1);
        Assert.Equal(0.2871, run.Sizes.For("HE_AHU", "flow")!.Value, 3);
        Assert.Equal(0.3589, run.Sizes.For("HE_RAD", "flow")!.Value, 3);

        var layout = SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        Assert.Equal(0.4307, Through(run.Graph, layout, solved, "HS1") + Leg(run.Graph, layout, solved, "TV_MAIN", "b"), 3);
        Assert.Equal(0.2581, Through(run.Graph, layout, solved, "HS1"), 3);
        Assert.InRange(solved[Promotion(layout, "PU_AHU", "head")], 1.0, 10.0);
        Assert.InRange(solved[Promotion(layout, "PU_RAD", "head")], 1.0, 10.0);
        Assert.True(solved[Promotion(layout, "PU_RAD", "head")] > solved[Promotion(layout, "PU_AHU", "head")], report);
    }

    [Fact]
    public void WithoutTheSourceLevelTheHeaderIsAThermalContinuumAndSaysSo()
    {
        // 60 °C at 0.4306 kg/s and 80 °C at 0.2581 kg/s are both 54 kW designs, so dropping `out.t=80`
        // leaves one degree free: 39 unknowns against 38 equations, refused with FS2211 naming the
        // temperature it needs (S-52's thermal-first advice), not a pressure.
        var check = WellPosedness.Check(GraphFixture.Lower(
            NoBypassHeader.Replace("power=54 kW out.t=80", "power=54 kW", StringComparison.Ordinal)).Graph);

        Assert.Equal(-1, check.Counting.Excess);

        var refused = Assert.Single(check.Diagnostics, static d => d.Code == "FS2211");
        Assert.StartsWith("This circuit is under-specified by 1. Add one of: a temperature on", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HE_AHU", "PU_AHU", "TV_AHU", "HE_RAD", 0.3589)]
    [InlineData("HE_RAD", "PU_RAD", "TV_RAD", "HE_AHU", 0.2871)]
    public async Task ASwitchedOffConsumerIsHeldStillByItsPumpAndSizesNothingFromItsSibling(
        string off, string pump, string valve, string on, double onFlow)
    {
        // S-56's acceptance on the entry's own script: `power=0` on one consumer. Its `in=50` asks
        // nothing of its split, its `out=30` pins the branch at zero flow, and the pump promoted to hold
        // it dead-heads at the head the other consumer's push amounts to (FS3015). The off coil is not
        // sized from anything -- least of all its sibling's 30 kW flow, which it used to inherit -- the
        // running coil keeps its own design flow, and the only flow on the stopped side is the mixing
        // valve's 2 % leakage crossing from the return header to the supply through the valve body,
        // which is a path the script wrote. The stopped branch's nodes sit at the return header's
        // temperature (the anchor rule), not at an artefact of the seed.
        // The source's duty is left to the balance, as the entry's script has it: with one coil off the
        // plant carries the other's load and nothing else.
        var script = NoBypassHeader
            .Replace("HS1     heat_exchanger power=54 kW out.t=80", "HS1     heat_exchanger out.t=60", StringComparison.Ordinal)
            .Replace($"{off}  load in.t=50 out.t=30 power=", $"{off}  load in.t=50 out.t=30 power=0 kW #", StringComparison.Ordinal);
        var check = WellPosedness.Check(GraphFixture.Lower(script).Graph);

        Assert.Equal(0, check.Counting.Excess);
        Assert.DoesNotContain(check.Counting.Constraints, c => c.Component == off && c.Kind == ConstraintKind.MixedInlet);
        Assert.Contains(check.Counting.Promotions, p => p.Component == pump && p.Parameter == "head" && p.Constraint.Component == off);
        Assert.DoesNotContain(check.Counting.Promotions, p => p.Component == valve);

        var result = await Loop().RunAsync(GraphFixture.Bind(script), Water.Instance, "s56", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);

        var run = result.Value;
        var report = FluidScript.Core.Diagnostics.Explanations.SolveExplanation.Render(result, run.Graph, "s56");

        Assert.True(run.Solve.Converged, report);
        Assert.True(run.Settled, report);
        Assert.Contains(run.Solve.Diagnostics, d => d.Code == "FS3015" && d.Message.Contains(pump, StringComparison.Ordinal));
        Assert.Null(run.Sizes.For(off, "flow"));
        Assert.Equal(onFlow, run.Sizes.For(on, "flow")!.Value, 3);

        var layout = SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        Assert.Equal(0, Through(run.Graph, layout, solved, off), 6);
        Assert.InRange(solved[Promotion(layout, pump, "head")], 1.5, 3.0);

        var leak = Leg(run.Graph, layout, solved, valve, "a");

        Assert.Equal(leak, Leg(run.Graph, layout, solved, valve, "b"), 6);
        Assert.InRange(leak, 0.001, 0.02);
    }

    [Fact]
    public async Task AMainPumpPushesForwardThroughTheStoppedCoilAndTheReportSaysWhatWouldCloseIt()
    {
        // The other sign of S-56: a main pump on the return puts the supply header above the return at
        // the stopped branch's ends, so holding it still takes a *negative* head -- a resistance no pump
        // is. The bound is lifted for that one column so the plant solves (−6.0 m measured), and FS3014
        // names the fix: close the branch. The leakage now crosses supply to return.
        var script = NoBypassHeader
            .Replace("HS1     heat_exchanger power=54 kW out.t=80", "HS1     heat_exchanger out.t=60\nPU_MAIN pump head=8", StringComparison.Ordinal)
            .Replace("HE_AHU  load in.t=50 out.t=30 power=", "HE_AHU  load in.t=50 out.t=30 power=0 kW #", StringComparison.Ordinal)
            .Replace("N5 - N1", "N5 - PU_MAIN - N1", StringComparison.Ordinal);

        var result = await Loop().RunAsync(GraphFixture.Bind(script), Water.Instance, "s56-main", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);

        var run = result.Value;
        var report = FluidScript.Core.Diagnostics.Explanations.SolveExplanation.Render(result, run.Graph, "s56-main");

        Assert.True(run.Solve.Converged, report);
        Assert.Contains(run.Solve.Diagnostics, static d => d.Code == "FS3014" && d.Message.Contains("PU_AHU.head", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Solve.Diagnostics, static d => d.Code == "FS3008");

        var layout = SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;

        Assert.Equal(0, Through(run.Graph, layout, solved, "HE_AHU"), 6);
        Assert.InRange(solved[Promotion(layout, "PU_AHU", "head")], -8.0, -4.0);
    }

    [Fact]
    public async Task ADeadLegTakesTheTemperatureOfTheNodeItHangsFromAndTheLoopStillSolves()
    {
        // S-23. A stub to a terminal node nothing else touches -- a script between two keystrokes --
        // carries zero flow by its own mass balance, and then the node's energy balance is ṁ·h with
        // ṁ = 0: a zero row on a zero column, and the whole loop went Singular at zero iterations
        // naming N9.h. The closure S-56 built for a pinned branch applies with the live end as anchor.
        const string script = """
            fluidscript 1
            circuit simpleLoop
            fluid water
            HE1  heat_exchanger power=30 in.t=20 out.t=50
            LOAD heat_exchanger power=-30 dp=0
            CV1  valve
            PU1  pump
            connections
            N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5
            N5 - N1 length=25
            N3 - N9 length=5 dn=25
            """;

        var result = await Loop().RunAsync(GraphFixture.Bind(script), Water.Instance, "s23", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);

        var run = result.Value;
        var report = FluidScript.Core.Diagnostics.Explanations.SolveExplanation.Render(result, run.Graph, "s23");

        Assert.True(run.Solve.Converged, report);
        Assert.DoesNotContain(run.Solve.Diagnostics, static d => d.Code is "FS3002" or "FS3009" or "FS3010");

        var layout = SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);
        var solved = run.Solve.Solution.Values;
        var n3 = Assert.Single(Enumerable.Range(0, run.Graph.Nodes.Length), i => run.Graph.Nodes[i].Name == "N3");
        var n9 = Assert.Single(Enumerable.Range(0, run.Graph.Nodes.Length), i => run.Graph.Nodes[i].Name == "N9");

        Assert.Equal(0, Through(run.Graph, layout, solved, "N3__N9"), 9);
        Assert.Equal(solved[layout.NodeEnthalpy(n3)], solved[layout.NodeEnthalpy(n9)], 3);
        Assert.Contains("rank         15: 0 unknown(s) nothing determines", report, StringComparison.Ordinal);
    }

    private static OuterLoop Loop()
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        return new OuterLoop(new NewtonSolver(), new CatalogBoreLookup(resolved.Value), OuterLoop.Rules(resolved.Value.Catalog), 10);
    }

    /// <summary>The index of a promoted parameter in the solved vector.</summary>
    private static int Promotion(SystemLayout layout, string owner, string parameter)
    {
        for (var index = 0; index < layout.Unknowns.Length; index++)
        {
            var unknown = layout.Unknowns[index];

            if (unknown.Kind == UnknownKind.Parameter
                && string.Equals(unknown.OwnerComponentId, owner, StringComparison.Ordinal)
                && unknown.Name.EndsWith(parameter, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new InvalidOperationException($"{owner}.{parameter} is not a promoted unknown.");
    }

    /// <summary>The solved flow magnitude through the branch that carries a component.</summary>
    private static double Through(CircuitGraph graph, SystemLayout layout, System.Collections.Immutable.ImmutableArray<double> solved, string component)
    {
        var branch = Assert.Single(graph.Branches, b => b.Path.Any(e => e.Name == component));
        return Math.Abs(solved[layout.BranchFlow(branch.Index)]);
    }

    /// <summary>The solved flow magnitude in the branch that ends on a valve's named port.</summary>
    private static double Leg(CircuitGraph graph, SystemLayout layout, System.Collections.Immutable.ImmutableArray<double> solved, string valve, string port)
    {
        var branch = Assert.Single(graph.Branches, b =>
            (b.From.Element.Name == valve && b.From.Element.Ports[b.From.Port].Name == port)
            || (b.To.Element.Name == valve && b.To.Element.Ports[b.To.Port].Name == port));
        return Math.Abs(solved[layout.BranchFlow(branch.Index)]);
    }
}
