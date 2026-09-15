using System.Collections.Immutable;
using System.Reflection;

using FluidScript.Core.Binding;
using FluidScript.Core.Catalogs;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Fluids;
using FluidScript.Core.Layout;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Layout;

/// <summary>
/// <c>25</c>'s worked examples, reproduced. Every assertion here is a value the plan document states;
/// where the derivation had to choose, the test says which choice and why.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LayoutHintsTests
{
    // ---- the cooling loop -----------------------------------------------------------------------

    [Fact]
    public void TheCoolingLoopIsOrderedDepthFirstFromItsDatum()
    {
        var (hints, diagnostics) = Unsolved(GraphFixture.CoolingLoop);

        Assert.Equal(
            ["N1", "N2", "PU1", "PU1__HE1", "HE1", "HE1__3WV", "3WV", "3WV__P1", "P1", "N3"],
            hints.Order);
        Assert.Contains(diagnostics, static d => d.Code == "FS2401");
    }

    [Fact]
    public void RankCountsHopsFromTheLoopAndLoopMembersHaveNone()
    {
        var (hints, _) = Unsolved(GraphFixture.CoolingLoop);

        Assert.Equal(1, hints.Rank["N1"]);
        Assert.Equal(1, hints.Rank["3WV__P1"]);
        Assert.Equal(2, hints.Rank["P1"]);
        Assert.Equal(3, hints.Rank["N3"]);
        Assert.DoesNotContain("PU1", hints.Rank.Keys);
        Assert.DoesNotContain("3WV", hints.Rank.Keys);
    }

    [Fact]
    public void TheLoopIsOneClosedWalkAndTheFourInferredComponentsAreNamed()
    {
        var (hints, _) = Unsolved(GraphFixture.CoolingLoop);

        var loop = Assert.Single(hints.Loops);
        Assert.Equal(["N2", "PU1", "PU1__HE1", "HE1", "HE1__3WV", "3WV"], loop);
        Assert.Equal([LoopOrientation.Clockwise], hints.LoopOrientations);
        Assert.Equal(["3WV__P1", "HE1__3WV", "N2", "PU1__HE1"], hints.Inferred.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ASolvedLoopHasAnOrientationAndEveryConnectionADirection()
    {
        var (hints, _) = await SolvedAsync(GraphFixture.CoolingLoop, "cooling-loop");

        Assert.Equal([LoopOrientation.Clockwise], hints.LoopOrientations);
        // Seven written lines, three of them split by I2 around an inferred node: ten adjacencies.
        Assert.Equal(10, hints.Flow.Count);
        Assert.All(hints.Flow.Values, static direction => Assert.Equal(FlowDirection.Forward, direction));
    }

    [Fact]
    public void TheCoolingLoopIsOneNeutralStage()
    {
        var (hints, _) = Unsolved(GraphFixture.CoolingLoop);

        var stage = Assert.Single(hints.ThermalStages);
        Assert.Equal(0, stage.Rank);
        Assert.Equal(ThermalStageRole.Neutral, stage.Role);
        Assert.Equal(10, stage.Components.Length);
    }

    [Fact]
    public void PortSidesFollowRoleAndTheThreeWayValveSeparatesItsOutlets()
    {
        var (hints, _) = Unsolved(GraphFixture.CoolingLoop);

        Assert.Equal(PortSide.West, hints.PortSides["3WV.ab"]);
        Assert.Equal(PortSide.North, hints.PortSides["3WV.a"]);
        Assert.Equal(PortSide.South, hints.PortSides["3WV.b"]);
        Assert.Equal(PortSide.West, hints.PortSides["PU1.in"]);
        Assert.Equal(PortSide.East, hints.PortSides["PU1.out"]);
        Assert.Equal(PortSide.North, hints.PortSides["HE1.in2"]);
        Assert.Equal(PortSide.South, hints.PortSides["HE1.out2"]);
        Assert.DoesNotContain("N1.1", hints.PortSides.Keys);
    }

    [Fact]
    public void PermutingIndependentStatementsLeavesTheOrderUnchanged()
    {
        var permuted = GraphFixture.CoolingLoop
            .Replace("PU1 pump head=6 flow=0.24\nHE1 heat_exchanger power=30\n3WV three_way_valve kv=6.3\nP1 pipe length=10 dn=25", "P1 pipe length=10 dn=25\n3WV three_way_valve kv=6.3\nHE1 heat_exchanger power=30\nPU1 pump head=6 flow=0.24");

        Assert.NotEqual(GraphFixture.CoolingLoop, permuted);
        Assert.Equal(Unsolved(GraphFixture.CoolingLoop).Hints.Order, Unsolved(permuted).Hints.Order);
    }

    // ---- the storage header ----------------------------------------------------------------------

    [Fact]
    public void TheStorageHeaderHasThreeStagesAroundTheTank()
    {
        var (hints, _) = Unsolved(Sample("m4-storage-header.fluid"));

        Assert.Equal(
            [(0, ThermalStageRole.Source, "S1 S2"), (1, ThermalStageRole.Storage, "T1"), (2, ThermalStageRole.Consumer, "RAD_NETWORK AHU_NETWORK")],
            hints.ThermalStages.Select(static s => (s.Rank, s.Role, string.Join(' ', s.Components))));
        Assert.Equal(PortSide.West, hints.PortSides["T1.in1"]);
        Assert.Equal(PortSide.West, hints.PortSides["T1.in2"]);
        Assert.Equal(PortSide.East, hints.PortSides["T1.out1"]);
        Assert.Equal(PortSide.East, hints.PortSides["T1.out2"]);
    }

    // ---- the distribution header -------------------------------------------------------------------

    [Fact]
    public void TheDistributionHeaderIsOneGroupOfTwoSubcircuits()
    {
        var (hints, _) = Unsolved(Sample("m2-distribution-header.fluid"));

        var group = Assert.Single(hints.DistributionGroups);
        Assert.Equal("heating", group.ParentCircuit);
        Assert.Equal(["AHU", "radiators"], group.Members);

        var ahu = hints.Circuits.Single(static c => c.Name == "AHU");
        Assert.Equal(("heating", "N3", "N5", "ahu"), (ahu.ParentCircuit, ahu.SupplyAnchorId, ahu.ReturnAnchorId, ahu.Role?.CanonicalName));
        var radiators = hints.Circuits.Single(static c => c.Name == "radiators");
        Assert.Equal(("heating", "N4", "N6", "radiator"), (radiators.ParentCircuit, radiators.SupplyAnchorId, radiators.ReturnAnchorId, radiators.Role?.CanonicalName));
        // `heating` is a registered role with a Neutral stage: resolved, and no bias either way.
        Assert.Equal(new CircuitRoleHint("heating", ThermalStageRole.Neutral), hints.Circuits.Single(static c => c.Name == "heating").Role);
        Assert.Null(hints.Circuits.Single(static c => c.Name == "heating").ParentCircuit);

        Assert.Equal("AHU", hints.CircuitOf["PU_AHU"]);
        Assert.Equal("radiators", hints.CircuitOf["PU_RAD"]);
        Assert.Equal("heating", hints.CircuitOf["N3"]);
    }

    [Fact]
    public void BothSubcircuitsShareAConsumerRank()
    {
        var (hints, _) = Unsolved(Sample("m2-distribution-header.fluid"));

        var consumers = hints.ThermalStages.Where(static s => s.Role == ThermalStageRole.Consumer).ToArray();
        var consumer = Assert.Single(consumers);

        Assert.Contains("PU_AHU", consumer.Components);
        Assert.Contains("PU_RAD", consumer.Components);
        Assert.Equal(consumer.Rank, hints.ThermalStages.Single(static s => s.Role == ThermalStageRole.Neutral).Rank);
    }

    [Fact]
    public void EquivalentBranchesHaveEqualShapesUntilOneGainsAValve()
    {
        var source = Sample("m2-distribution-header.fluid");
        var (hints, _) = Unsolved(source);

        // `load` is a spelling of `heat_exchanger`; the shape carries the kind, not the spelling.
        Assert.Equal(["pipe", "three_way_valve", "pump", "heat_exchanger", "pipe"], hints.BranchShapes["AHU"]);
        Assert.Equal(hints.BranchShapes["AHU"], hints.BranchShapes["radiators"]);

        var renamed = source
            .Replace("HE_RAD", "COIL_R").Replace("TV_RAD", "MIX_R").Replace("PU_RAD", "PMP_R")
            .Replace("PR1", "PIPE_R1").Replace("PR2", "PIPE_R2").Replace("NM_RAD", "NODE_R");
        var (renamedHints, _) = Unsolved(renamed);
        Assert.Equal(renamedHints.BranchShapes["AHU"], renamedHints.BranchShapes["radiators"]);

        var withValve = source
            .Replace("PR2     pipe length=18 dn=25", "PR2     pipe length=18 dn=25\nBV_RAD  valve kv=10")
            .Replace("NM_RAD - PR2 - N6", "NM_RAD - PR2 - BV_RAD - N6");
        var (valved, _) = Unsolved(withValve);
        Assert.NotEqual(valved.BranchShapes["AHU"], valved.BranchShapes["radiators"]);
        Assert.Equal(["pipe", "three_way_valve", "pump", "heat_exchanger", "pipe", "valve"], valved.BranchShapes["radiators"]);
    }

    // ---- the substation ------------------------------------------------------------------------------

    [Fact]
    public void TheSubstationPlacesItsSourceSideBeforeTheExchangerAndTheHeatingSideAfter()
    {
        var (hints, _) = Unsolved(TwoCircuitSubstation(districtFirst: true));

        var stageOf = hints.ThermalStages
            .SelectMany(static s => s.Components.Select(c => (Component: c, s.Rank, s.Role)))
            .ToDictionary(static e => e.Component, static e => (e.Rank, e.Role), StringComparer.Ordinal);

        Assert.Equal(ThermalStageRole.Conversion, stageOf["HX1"].Role);
        Assert.Equal(ThermalStageRole.Source, stageOf["NPS"].Role);
        Assert.Equal(ThermalStageRole.Consumer, stageOf["LOAD"].Role);
        Assert.True(stageOf["NPS"].Rank < stageOf["HX1"].Rank);
        Assert.True(stageOf["HX1"].Rank < stageOf["LOAD"].Rank);
        Assert.Equal(stageOf["NPS"].Rank, stageOf["PCV"].Rank);
    }

    [Fact]
    public void TheExchangerBelongsToItsLosingSideWhicheverBlockComesFirst()
    {
        var (first, _) = Unsolved(TwoCircuitSubstation(districtFirst: true));
        var (second, _) = Unsolved(TwoCircuitSubstation(districtFirst: false));

        Assert.Equal("district", first.CircuitOf["HX1"]);
        Assert.Equal("district", second.CircuitOf["HX1"]);
        Assert.Equal(
            first.ThermalStages.Select(static s => (s.Rank, s.Role, string.Join(' ', s.Components.Order(StringComparer.Ordinal)))),
            second.ThermalStages.Select(static s => (s.Rank, s.Role, string.Join(' ', s.Components.Order(StringComparer.Ordinal)))));
    }

    [Fact]
    public void ARoleContradictedByItsDutyIsPlacedByTheDutyAndSaysSo()
    {
        // A circuit named `radiators` whose only exchanger gives heat to the water is a source.
        var (hints, diagnostics) = Unsolved("""
            fluidscript 1
            circuit radiators
            fluid water

            HS1 heat_exchanger power=54 kW out=60
            PU1 pump
            P1  pipe length=10 dn=25

            connections
            N1 - HS1 - PU1 - P1 - N1

            N1 node p=250
            """);

        var stage = Assert.Single(hints.ThermalStages, static s => s.Components.Contains("HS1"));
        Assert.Equal(ThermalStageRole.Source, stage.Role);
        Assert.Contains(diagnostics, static d => d.Code == "FS2403");
    }

    // ---- non-flow elements --------------------------------------------------------------------------

    [Fact]
    public void AControllerIsAnchoredToItsActuatorAndReadsThroughItsSensor()
    {
        var (hints, _) = Unsolved(GraphFixture.CoolingLoop.Replace(
            "N3 return p=280",
            """
            N3 return p=280
            TE1 t_sensor at N2
            TC1 pid kp=2
            control actuate=3WV.position measure=TE1.t by=TC1 setpoint=20
            """));

        var sensor = Assert.Single(hints.NonFlowElements, static e => e.ComponentId == "TE1");
        var controller = Assert.Single(hints.NonFlowElements, static e => e.ComponentId == "TC1");

        Assert.Equal(("N2", "N2", null), (sensor.PlacementAnchorId, sensor.MeasurementTargetId, sensor.ActuationTargetId));
        Assert.Equal(("3WV", "N2", "3WV"), (controller.PlacementAnchorId, controller.MeasurementTargetId, controller.ActuationTargetId));
        // One tab order: N2 is Order[1], the sensor follows it at 2, so 3WV (Order[6]) sits at 7 and
        // its controller at 8.
        Assert.Equal(2, sensor.NavigationOrder);
        Assert.Equal(hints.Order.IndexOf("3WV") + 2, controller.NavigationOrder);
    }

    // ---- invariants ----------------------------------------------------------------------------------

    [Fact]
    public void TheHintsCarryNoGeometryTagSpacingOrMode()
    {
        // Whole words of the PascalCase name: "Stages" is not a tag and "Index" is not an x.
        var forbidden = new[] { "Width", "Height", "X", "Y", "Position", "Pixel", "Length", "Tag", "Spacing", "Mode", "Colour", "Color" };
        var types = new[]
        {
            typeof(LayoutHints), typeof(ThermalStage), typeof(CircuitHint), typeof(CircuitRoleHint),
            typeof(DistributionGroup), typeof(ComponentGroupHint), typeof(NonFlowElementHint),
        };

        foreach (var type in types)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var words = System.Text.RegularExpressions.Regex.Split(property.Name, "(?<=[a-z])(?=[A-Z])");
                Assert.DoesNotContain(words, word => forbidden.Contains(word, StringComparer.Ordinal));
                Assert.NotEqual(typeof(double), property.PropertyType);
                Assert.NotEqual(typeof(float), property.PropertyType);
            }
        }
    }

    [Fact]
    public void EveryNonLoopComponentInEverySampleHasARank()
    {
        foreach (var sample in Directory.EnumerateFiles(RepositoryLayout.Samples, "*.fluid").Order(StringComparer.Ordinal))
        {
            var lowered = GraphFixture.Lower(File.ReadAllText(sample));
            var (hints, _) = LayoutHintsDerivation.Derive(lowered.Graph, GraphFixture.Bind(File.ReadAllText(sample)), null);
            var members = hints.Loops.SelectMany(static l => l).ToHashSet(StringComparer.Ordinal);

            foreach (var component in lowered.Graph.Components)
            {
                Assert.Equal(!members.Contains(component.Name), hints.Rank.ContainsKey(component.Name));
            }

            Assert.Equal(lowered.Graph.Components.Length, hints.Order.Length);
            Assert.Equal(lowered.Graph.Components.Length, hints.ThermalStages.Sum(static s => s.Components.Length));
        }
    }

    [Fact]
    public void AnExpandedPipeIsOneGroupUnderItsDeclaredName()
    {
        var source = GraphFixture.CoolingLoop.Replace("P1 pipe length=10 dn=25", "P1 pipe length=10 dn=25 nodes=4");
        Assert.NotEqual(GraphFixture.CoolingLoop, source);
        var (hints, _) = Unsolved(source);

        var group = Assert.Single(hints.Groups);
        Assert.Equal("P1", group.ParentComponentId);
        Assert.Equal(9, group.Children.Length);
        Assert.All(group.Children, child => Assert.Equal("cooling", hints.CircuitOf[child]));
    }

    [Fact]
    public void AGroupPastTenMembersIsReportedAsCollapsing()
    {
        // nodes=4 makes nine members and says nothing; nodes=6 makes thirteen and says FS2402.
        var (_, quiet) = Unsolved(GraphFixture.CoolingLoop.Replace("P1 pipe length=10 dn=25", "P1 pipe length=10 dn=25 nodes=4"));
        var (_, loud) = Unsolved(GraphFixture.CoolingLoop.Replace("P1 pipe length=10 dn=25", "P1 pipe length=10 dn=25 nodes=6"));

        Assert.DoesNotContain(quiet, static d => d.Code == "FS2402");
        var collapsed = Assert.Single(loud, static d => d.Code == "FS2402");
        Assert.Contains("'P1' has 13 members", collapsed.Message, StringComparison.Ordinal);
    }

    // ---- fixtures ---------------------------------------------------------------------------------

    private static string Sample(string name) => File.ReadAllText(Path.Combine(RepositoryLayout.Samples, name));

    private static (LayoutHints Hints, ImmutableArray<Diagnostic> Diagnostics) Unsolved(string source) =>
        LayoutHintsDerivation.Derive(GraphFixture.Lower(source).Graph, GraphFixture.Bind(source), null);

    private static async Task<(LayoutHints Hints, ImmutableArray<Diagnostic> Diagnostics)> SolvedAsync(string source, string name)
    {
        var resolved = PipeCatalogs.Resolve(pin: null);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var loop = new OuterLoop(new NewtonSolver(), new CatalogBoreLookup(resolved.Value.Catalog), OuterLoop.Rules(resolved.Value.Catalog), 10);
        var model = GraphFixture.Bind(source);
        var run = await loop.RunAsync(model, Water.Instance, name, TestContext.Current.CancellationToken);
        Assert.True(run.IsSuccess, run.Error?.Message);

        var graph = run.Value.Graph;
        var layout = SystemLayout.Build(graph, WellPosedness.Check(graph).Counting);
        var values = run.Value.Solve.Solution.Values;
        var flows = ImmutableArray.CreateRange(Enumerable.Range(0, graph.Branches.Length).Select(i => values[layout.BranchFlow(i)]));

        return LayoutHintsDerivation.Derive(graph, model, flows);
    }

    private static string TwoCircuitSubstation(bool districtFirst)
    {
        const string district = """
            circuit district
            fluid water

            NPS supply t=85 p=600
            NPR return p=350
            PCV valve
            PP  pipe length=12 dn=25
            HX1 heat_exchanger power=150 in=40 out=60 in2=85 out2=45 u=3300

            connections
            NPS - PCV - PP - HX1.in2
            HX1.out2 - NPR
            """;

        const string heating = """
            circuit heating
            fluid water

            SP   pump
            SS   pipe length=30 dn=32
            SR   pipe length=30 dn=32
            LOAD heat_exchanger power=-150 dt=20

            connections
            HX1.out - SS - NSUP
            NSUP - LOAD - NRET
            NRET - SR - SP - HX1.in
            """;

        return "fluidscript 1\n" + (districtFirst ? district + "\n\n" + heating : heating + "\n\n" + district) + "\n";
    }
}
