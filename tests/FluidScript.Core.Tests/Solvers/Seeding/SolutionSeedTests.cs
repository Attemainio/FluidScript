using FluidScript.Core.Components;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Sizing.Flows;
using FluidScript.Core.Solvers;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Solvers.Seeding;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers.Seeding;

/// <summary>
/// The properties the starting iterate has to have, held against the whole sample corpus.
/// </summary>
/// <remarks>
/// <para>
/// <strong>These are not "the seed is close to the answer" tests, and they should not become them.</strong>
/// A seed is allowed to be wrong about magnitudes — that is what Newton is for. What it is not allowed
/// to be is <em>structurally</em> wrong: a zero flow makes a momentum relation's derivative vanish, and
/// a node with no outflow makes its own enthalpy column vanish. Both are singular Jacobians at the
/// starting point, both report <c>FS3002</c>, and neither has anything to do with the circuit
/// (<c>S-21</c>).
/// </para>
/// <para>
/// The mass-balance check is the one that subsumes the other two: a field satisfying every node's
/// balance has an outflow wherever it has an inflow, and cannot be identically zero unless nothing
/// drives the circuit.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SolutionSeedTests
{
    public static TheoryData<string> Samples()
    {
        var data = new TheoryData<string>();

        foreach (var path in Directory.GetFiles(RepositoryLayout.Samples, "*.fluid").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void EveryNodeMassBalanceCloses(string sample)
    {
        var graph = Lower(sample);
        var layout = SystemLayout.Build(graph, WellPosedness.Check(graph).Counting);
        var seed = SolutionSeed.Build(graph, layout);
        var ports = PortMap.Build(graph);

        var scale = Math.Max(
            Tolerances.FlowScaleFloor,
            Enumerable.Range(0, graph.Branches.Length)
                .Select(branch => Math.Abs(seed.Values[layout.BranchFlow(branch)]))
                .DefaultIfEmpty(0)
                .Max());

        for (var element = 0; element < graph.Components.Length; element++)
        {
            if (graph.Components[element] is not NodeComponent { CarriesMassBalance: true } node)
            {
                continue;
            }

            var balance = Flux(graph, layout, seed, node);

            foreach (var stream in Streams(layout, ports, seed, element, node))
            {
                balance += stream;
            }

            Assert.True(
                Math.Abs(balance) <= 1e-9 * scale,
                $"{node.Name} is seeded {balance:G4} kg/s out of balance, so the field is not "
                + "divergence-free and the seed's whole claim is false.");
        }
    }

    /// <remarks>
    /// <strong>A dead leg is exempt, and that is not the claim weakening.</strong> A terminal node with
    /// no boundary role admits no external flux (<c>D-64</c>), so its branch's mass balance forces the
    /// flow to exactly zero — that is the answer, and a seed putting it anywhere else would be seeding
    /// a value the first Newton step has to undo. (Its enthalpy is closed against the live end since
    /// <c>S-23</c>; the flow was never the question.)
    /// </remarks>
    [Theory]
    [MemberData(nameof(Samples))]
    public void EveryDrivenBranchIsSeededAwayFromRest(string sample)
    {
        var graph = Lower(sample);
        var layout = SystemLayout.Build(graph, WellPosedness.Check(graph).Counting);
        var seed = SolutionSeed.Build(graph, layout);

        foreach (var branch in graph.Branches)
        {
            if (DeadLeg(layout, branch.From) || DeadLeg(layout, branch.To))
            {
                continue;
            }

            Assert.True(
                Math.Abs(seed.Values[layout.BranchFlow(branch.Index)]) > Tolerances.FlowZero,
                $"branch {branch.Index} ({branch.From.Label} -> {branch.To.Label}) is seeded at rest, "
                + "and R*m|m| has a zero derivative there whatever the circuit is (S-21).");
        }
    }

    /// <remarks>
    /// A node nothing reaches is skipped rather than asserted on, because a stagnant node's enthalpy is
    /// undetermined by the steady equations themselves and no seed can rescue it (<c>S-23</c>).
    /// </remarks>
    [Theory]
    [MemberData(nameof(Samples))]
    public void EveryNodeCarryingFlowHasSomethingLeavingIt(string sample)
    {
        var graph = Lower(sample);
        var layout = SystemLayout.Build(graph, WellPosedness.Check(graph).Counting);
        var seed = SolutionSeed.Build(graph, layout);
        var ports = PortMap.Build(graph);

        for (var element = 0; element < graph.Components.Length; element++)
        {
            if (graph.Components[element] is not NodeComponent node)
            {
                continue;
            }

            var streams = Streams(layout, ports, seed, element, node)
                .Append(Flux(graph, layout, seed, node))
                .ToArray();

            if (!streams.Any(static stream => stream > Tolerances.FlowZero))
            {
                continue;
            }

            Assert.True(
                streams.Any(static stream => stream < -Tolerances.FlowZero),
                $"nothing leaves {node.Name} at the seed, so its own enthalpy enters no upwind term "
                + "and its column is zero -- the second half of S-21.");
        }
    }

    private const string MachineHoldingItsLeavingTemperature = """
        fluidscript 1

        circuit heating
        fluid water

        HPC  heater out.t=45 dp=30
        HL   load power=120 out.t=40 dp=20
        TVH  three_way_valve position=1
        PUH  pump
        PR   pipe length=5 dn=65

        connections
        NB_in - NM
        NM - PUH - HPC - TVH.ab
        TVH.a - HL - PR - NM
        TVH.b - NB_out

        NB_in  inlet t=10 p=200
        NB_out outlet p=200
        """;

    [Fact]
    public void ALoadFedByAMachineHoldingItsLeavingTemperatureIsRatedFromIt()
    {
        // `S-83`. The machine states its 45 C and not its duty, which is how leaving-water control is
        // written; the load states 120 kW and its 40 C return. The load's inlet is the machine's outlet
        // through the diverting valve, so its stream is 120 kW over 45/40, 5.736 kg/s at the estimate's
        // reference pressure. Without the walk
        // the load had no inlet to rate against, every branch started at the nominal 0.1 kg/s, and the
        // first Newton step drove the machine's duty negative.
        var graph = GraphFixture.Lower(MachineHoldingItsLeavingTemperature).Graph;
        var estimates = BranchFlows.Estimate(graph);

        var load = estimates[graph.Branches.Single(branch => branch.Path.Any(part => part.Name == "HL")).Index];

        Assert.Equal(FlowBasis.Duty, load.Basis);
        Assert.Equal("HL", load.Source);
        Assert.Equal(5.736, load.Magnitude, 3);
    }

    [Fact]
    public void ASourceReadsItsReturnOnlyFromLoadsItsWaterReaches()
    {
        // `S-83`'s second half. BLR's inlet meets a junction, so the upstream walk stops and the common
        // return of the opposing loads decides: R1 and R2 both return at 50 C. The chilled loop in the
        // same file returns at 12 C; read too, the returns disagreed and BLR was left at the nominal flow.
        var graph = GraphFixture.Lower("""
            fluidscript 1

            circuit heating
            fluid water

            BLR  heater power=40 out.t=70
            R1   load power=20 out.t=50
            R2   load power=20 out.t=50
            PU1  pump

            connections
            N2 - PU1 - BLR - N1
            N1 - R1 - N2
            N1 - R2 - N2
            N2 p=200

            circuit cooling
            fluid water

            CH   chiller power=30 out.t=7
            CL   load power=30 out.t=12
            PU2  pump

            connections
            N4 - PU2 - CH - N3
            N3 - CL - N4
            N4 p=200
            """).Graph;
        var estimates = BranchFlows.Estimate(graph);

        var source = estimates[graph.Branches.Single(branch => branch.Path.Any(part => part.Name == "BLR")).Index];

        Assert.Equal(FlowBasis.Duty, source.Basis);
        Assert.Equal("BLR", source.Source);

        // 40 kW over 50 -> 70 C.
        Assert.Equal(0.4775, source.Magnitude, 3);
    }

    [Fact]
    public void AStatedDutyFixesItsBranchFlowDirectly()
    {
        // 24's step 1: 30 kW between 20 and 50 C. The document's own worked example puts this at
        // 0.2392 kg/s, computed from water's enthalpy table rather than from a constant cp.
        var graph = Lower("m2-simple-loop.fluid");
        var estimates = BranchFlows.Estimate(graph);

        var duty = Assert.Single(estimates, estimate => estimate.Basis is FlowBasis.Duty);

        Assert.Equal("HE1", duty.Source);
        Assert.Equal(0.2392, duty.Magnitude, 3);
    }

    /// <summary>A load stating only its duty is rated at its sources' design temperatures, and not when two sources disagree (<c>S-85</c>).</summary>
    [Fact]
    public void ALoadWithOnlyADutyIsRatedAtItsSourcesDesignTemperatures()
    {
        // Three zones on one 70/40 plant: 20 kW over 70 -> 40 C is 0.1594 kg/s, the split the zones settle at. Before,
        // a zone had no rating, took the plant's whole 0.478 kg/s from the copy rule, and zone 1 seeded backwards.
        const string Zones = """
            fluidscript 1
            circuit zones
            fluid water

            PU1 pump
            HE1 heat_exchanger power=60 in.t=40 out.t=70
            HE2 heat_exchanger power=0.001 in.t=40 out.t=70
            LD1 heat_exchanger power=-20
            LD2 heat_exchanger power=-20
            LD3 heat_exchanger power=-20

            connections
            PU1 - HE1 - HE2 - NA1
            NA1 - LD1 - NB1
            NA1 - NA2
            NA2 - LD2 - NB2
            NA2 - LD3 - NB2
            NB2 - NB1
            NB1 - PU1
            """;

        var graph = GraphFixture.Lower(Zones).Graph;
        var zone = BranchFlows.Estimate(graph)[graph.Branches.Single(branch => branch.Path.Any(part => part.Name == "LD1")).Index];

        Assert.Equal(FlowBasis.Duty, zone.Basis);
        Assert.Equal("LD1", zone.Source);
        Assert.Equal(0.1594, zone.Magnitude, 3);

        // A second source at 80/60 leaves no one design difference, and none is invented.
        var disagreeing = GraphFixture.Lower(Zones.Replace("HE2 heat_exchanger power=0.001 in.t=40 out.t=70", "HE2 heat_exchanger power=0.001 in.t=60 out.t=80", StringComparison.Ordinal)).Graph;
        var unrated = BranchFlows.Estimate(disagreeing)[disagreeing.Branches.Single(branch => branch.Path.Any(part => part.Name == "LD1")).Index];

        Assert.NotEqual(FlowBasis.Duty, unrated.Basis);

        // An open circuit takes heat in through its boundaries, so its exchangers are not its loads' design reference:
        // the heat pump's evaporator, rated at its cooling coil's 7/12 while the bores' 10 C water joined, sent a
        // converging solve to its valve's stop.
        var open = GraphFixture.Lower(Zones.Replace("NB1 - PU1", "NB1 - PU1\nNB_in - NB1\nNA1 - NB_out\nNB_in inlet t=40 p=200\nNB_out outlet p=200", StringComparison.Ordinal)).Graph;
        var borrowed = BranchFlows.Estimate(open)[open.Branches.Single(branch => branch.Path.Any(part => part.Name == "LD1")).Index];

        Assert.NotEqual(FlowBasis.Duty, borrowed.Basis);
    }
    /// <summary>The legs a valve partitions from a rated coil carry the coil's own basis one rank down, so the forest keeps them ahead of anything merely propagated (<c>S-68</c>).</summary>
    [Fact]
    public void AThreeWayValvePartitionsTheCommonDutyFlowAcrossItsInletLegs()
    {
        var graph = GraphFixture.Lower(
            """
            fluidscript 1
            circuit heating
            fluid water

            SOURCE heater in.t=30 out.t=80 power=24 kW
            LOAD   load in.t=50 out.t=30 power=24 kW
            TV     three_way_valve kv=25
            PU     pump
            P1     pipe length=10 dn=25

            connections
            N1 - SOURCE - TV.a
            N2 - TV.b
            TV.ab - PU - LOAD - N2
            N2 - P1 - N1

            N1 node p=250
            """).Graph;
        var valve = Assert.Single(graph.Components.OfType<ThreeWayValveComponent>());
        var estimates = BranchFlows.Estimate(graph);

        var legs = graph.Branches
            .Where(branch => ReferenceEquals(branch.From.Element, valve)
                || ReferenceEquals(branch.To.Element, valve))
            .Select(branch => (
                Name: ValveLegs.PortName(branch, valve),
                Estimate: estimates[branch.Index]))
            .ToArray();

        BranchFlow Leg(string name) => Assert.Single(
            legs,
            leg => string.Equals(leg.Name, name, StringComparison.Ordinal)).Estimate;

        var common = Leg("ab");
        var first = Leg("a");
        var second = Leg("b");

        Assert.Equal(0.2871, common.Magnitude, 3);
        Assert.Equal(common.Magnitude, first.Magnitude + second.Magnitude, 9);
        Assert.True(first.Magnitude < common.Magnitude);
        Assert.True(second.Magnitude < common.Magnitude);
        Assert.Equal(FlowBasis.Duty, first.Basis); // the source sits in the `a` leg's path
        Assert.Equal(FlowBasis.Partitioned, second.Basis);
    }

    /// <summary>The ladder's series header: the radiators' block first, the AHU's block cooling what is left (<c>S-63</c>).</summary>
    private const string SeriesHeader = """
        fluidscript 1
        project static plant_01

        circuit heating 100
        fluid water

        HS1     heat_exchanger power=30 kW out.t=60

        connections
        N1 - HS1 - N3
        N5 - N1

        N1 node p=250

        circuit radiators 102

        HE_RAD  load in.t=50 out.t=40 power=20 kW
        TV_RAD  three_way_valve
        PU_RAD  pump

        connections
        N3 - TV_RAD.a length=18 dn=25
        NM_RAD - TV_RAD.b
        TV_RAD.ab - PU_RAD - HE_RAD - NM_RAD
        NM_RAD - N4 length=18 dn=25

        circuit AHU 101

        HE_AHU  load in.t=35 out.t=30
        TV_AHU  three_way_valve
        PU_AHU  pump

        connections
        N4 - TV_AHU.a length=12 dn=25
        NM_AHU - TV_AHU.b
        TV_AHU.ab - PU_AHU - HE_AHU - NM_AHU
        NM_AHU - N5 length=12 dn=25
        """;

    [Fact]
    public void ASecondBlockInSeriesReadsItsFeedFromTheFirstBlocksReturn()
    {
        // `S-63`. The AHU's valve is fed by the radiators' 40 C return, not by the boiler's 60 C
        // supply: mixing 40 and 30 to 35 is half and half, so the stream through its `a` port is half
        // its 0.4785 kg/s circulation. Reading the graph's hottest source there made it a sixth,
        // 0.0797 kg/s, and the seeded field was three times wrong on the ring's whole stream.
        var graph = GraphFixture.Lower(SeriesHeader).Graph;
        var valve = Assert.Single(graph.Components.OfType<ThreeWayValveComponent>(), v => v.Name == "TV_AHU");
        var estimates = BranchFlows.Estimate(graph);

        var legs = graph.Branches
            .Where(branch => ReferenceEquals(branch.From.Element, valve) || ReferenceEquals(branch.To.Element, valve))
            .Select(branch => (Name: ValveLegs.PortName(branch, valve), Estimate: estimates[branch.Index]))
            .ToArray();

        BranchFlow Leg(string name) => Assert.Single(legs, leg => string.Equals(leg.Name, name, StringComparison.Ordinal)).Estimate;

        Assert.Equal(0.4785, Leg("ab").Magnitude, 3);
        Assert.Equal(0.2393, Leg("a").Magnitude, 3);
        Assert.Equal(0.2393, Leg("b").Magnitude, 3);
    }

    [Fact]
    public void AStatedPressureOnAnInlineNodeAnchorsThePressureWalk()
    {
        // `S-63`'s second half. `N1` has two connections, so it is inline (`D-114`) and sits inside a
        // branch's path rather than at an end; the walk read only the ends, found no stated pressure,
        // started at the 100 kPa scale, and met the 250 kPa datum half way round -- a 150 kPa closure
        // error that landed on the AHU valve's leg. Anchored, every node of the seed sits near the datum.
        var graph = GraphFixture.Lower(SeriesHeader).Graph;
        var layout = SystemLayout.Build(graph, WellPosedness.Check(graph).Counting);
        var seed = SolutionSeed.Build(graph, layout);

        for (var node = 0; node < graph.Nodes.Length; node++)
        {
            Assert.InRange(seed.Values[layout.NodePressure(node)], 200_000, 300_000);
        }
    }

    [Fact]
    public void ARatedCoilAmongUnratedBlocksKeepsItsDutyAsAChord()
    {
        // `S-68`. The ladder's 8c: four injection blocks in series on one ring, only the radiators rated.
        // The forest used to keep the three unrated coils' 0.1 kg/s nominals as chords and let the rated
        // coil fall out of continuity at 0.0197 kg/s against its 0.478 duty, which starved the ring and
        // put `N1` below freezing. Ranked by basis, the rated coil and the ring are the chords and the
        // nominal coils take what continuity leaves.
        //
        // Since `S-69`'s seed rules the unrated coils are not left to continuity either: the closed field
        // hands each block's feed leg the ring's 0.3587, and the stated `in`/`out` -- 38/36 fed at 40,
        // 34.5/33 fed at 36, 31.5/30 fed at 33, each half way -- partition the coil at twice the ring.
        var source = File.ReadAllText(
            Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Ladder", "step-08c-header-series-four.fluid"));
        var graph = GraphFixture.Lower(source).Graph;
        var layout = SystemLayout.Build(graph, WellPosedness.Check(graph).Counting);
        var seed = SolutionSeed.Build(graph, layout);

        double Flow(string from, string to) => Math.Abs(seed.Values[layout.BranchFlow(Assert.Single(
            graph.Branches,
            branch => branch.From.Label == from && branch.To.Label == to).Index)]);

        Assert.Equal(0.4784, Flow("TV_RAD.ab", "NM_RAD"), 3);
        Assert.Equal(0.3587, Flow("TV_RAD.a", "NM_DHW"), 3);

        foreach (var block in new[] { "AHU", "FLR", "DHW" })
        {
            Assert.Equal(0.3587, Flow($"TV_{block}.a", block == "AHU" ? "NM_RAD" : block == "FLR" ? "NM_AHU" : "NM_FLR"), 3);
            Assert.Equal(0.7174, Flow($"TV_{block}.ab", $"NM_{block}"), 3);
        }

        for (var node = 0; node < graph.Nodes.Length; node++)
        {
            Assert.InRange(seed.Values[layout.NodeEnthalpy(node)], 80_000, 280_000);
        }
    }

    [Fact]
    public void ABranchNothingDeterminesFallsToTheNominalAndSaysSo()
    {
        var graph = GraphFixture.Lower(
            """
            fluidscript 1
            circuit bare
            fluid water

            P1 pipe length=10 dn=25
            V1 valve

            connections
            P1 - V1
            """).Graph;

        foreach (var estimate in BranchFlows.Estimate(graph))
        {
            Assert.Equal(FlowBasis.Nominal, estimate.Basis);
            Assert.Equal(BranchFlows.Nominal, estimate.Magnitude);
            Assert.Equal(string.Empty, estimate.Source);
        }
    }

    /// <summary>The signed flow of every branch meeting one node, in its own sign convention.</summary>
    private static IEnumerable<double> Streams(
        SystemLayout layout, PortMap ports, StateVector seed, int element, NodeComponent node)
    {
        for (var port = 0; port < node.Ports.Length; port++)
        {
            var binding = ports[element, port];

            if (binding.CarriesFlow)
            {
                yield return binding.Sign * seed.Values[layout.BranchFlow(binding.Branch)];
            }
        }
    }

    /// <summary>The external flux at one node, whether the seed chose it or the script stated it.</summary>
    private static double Flux(CircuitGraph graph, SystemLayout layout, StateVector seed, NodeComponent node)
    {
        var index = layout.FluxNodes.IndexOf(
            graph.Nodes.First(candidate => ReferenceEquals(candidate.Component, node)));

        if (index >= 0)
        {
            return seed.Values[layout.ExternalFluxOffset + index];
        }

        return HydraulicPartition.Stated(node, HydraulicPartition.Flow) is { } stated
            ? node.Boundary is BoundaryRole.Outlet ? -stated : stated
            : 0;
    }

    /// <summary>Whether a branch end is a terminal nothing enters or leaves the model at.</summary>
    private static bool DeadLeg(SystemLayout layout, BranchEnd end) =>
        end.Element is NodeComponent { Ports.Length: 1 } node
        && HydraulicPartition.Stated(node, HydraulicPartition.Flow) is null
        && !layout.FluxNodes.Any(flux => ReferenceEquals(flux.Component, node));

    private static CircuitGraph Lower(string sample) =>
        GraphFixture.Lower(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample))).Graph;

    [Fact]
    public void APromotedValveKvIsSeededFromTheDropItHasToSpend()
    {
        // `C-75`'s follow-on. The bootstrap gave the substation's `PCV.kv` the provisional Kv 630 -- a valve
        // that drops nothing -- and Newton had to walk it down three orders of magnitude to the 2.1 m3/h
        // that spends 600 - 350 kPa at 0.895 kg/s. The seed now reads the branch's stated end pressures
        // and offers the valve half the difference, so the column starts within a factor of two of the
        // answer rather than a factor of three hundred.
        var graph = Lower("m2-substation.fluid");
        var layout = SystemLayout.Build(graph, WellPosedness.Check(graph).Counting);
        var seed = SolutionSeed.Build(graph, layout);

        var kv = Enumerable.Range(layout.PromotionOffset, layout.Count - layout.PromotionOffset)
            .Single(index => layout.Unknowns[index].Name == "PCV.kv");

        Assert.InRange(seed.Values[kv], 1.0, 5.0);
    }

    [Fact]
    public void ACoupledExchangerSeedsBothSidesTemperaturesFromTheirOwnDesignPoints()
    {
        // `S-32`'s other half. The primary's 85 C enters at `NPS` and must leave near 45 C, not at 85 C
        // minus the *secondary's* 20 K; the secondary's supply must sit near 60 C and its return near 40 C.
        // A seed with either side's level wrong by tens of kelvin is a seed whose first property read is
        // outside the domain, which is how the substation stayed NonFinite for so long.
        var graph = Lower("m2-substation.fluid");
        var layout = SystemLayout.Build(graph, WellPosedness.Check(graph).Counting);
        var seed = SolutionSeed.Build(graph, layout);

        double Celsius(string node)
        {
            var index = Enumerable.Range(0, layout.Count)
                .Single(i => layout.Unknowns[i].Kind == UnknownKind.NodeEnthalpy && layout.Unknowns[i].OwnerComponentId == node);

            return seed.Values[index] / FluidScript.Core.Physics.Fluids.Substances.ConstantPropertyWater.SpecificHeatValue;
        }

        Assert.InRange(Celsius("NPR"), 35, 55);
        Assert.InRange(Celsius("NSUP"), 50, 65);
        Assert.InRange(Celsius("NRET"), 35, 50);
        Assert.True(Celsius("NSUP") - Celsius("NRET") > 8, "the secondary's supply and return are seeded on top of each other");
    }

    [Fact]
    public void APromotedPositionStartsAtMidTravelRatherThanOnEitherBound()
    {
        // `S-26` then `S-50`, and they are one argument applied twice. Promoted columns first fell to
        // **zero**, which the seed's own remarks called "honest, and the thing the outer loop replaces
        // when promotion becomes live" -- and zero is not neutral, it is a *bound*, so the column's
        // forward difference was dead before the first iteration. Reading the component's own value fixed
        // that and walked into the mirror image: `ThreeWayValve.Position` defaults to **1**, which is the
        // other bound.
        //
        // It is the worse of the two for a three-way valve. On the equal-percentage characteristic every
        // promotable position uses, phi(1) = 1 with slope 3.91 while the complementary leg sits at
        // phi(0) = 0.02 with slope 0.078 -- fifty times flatter -- so the column carries almost no signal
        // about the bypass and Newton pushes the position further into its stop. Measured on the
        // injection header, both consumer valves pinned at 1 (`FS3008`) while the answer they wanted was
        // (50-30)/(60-30) = 0.667.
        //
        // Mid-travel is symmetric: both legs at 0.141, both slopes 0.553. It is also where a valve sized
        // for authority 0.5 is meant to sit.
        var graph = Lower("m2-distribution-header.fluid");
        var counting = WellPosedness.Check(graph).Counting;
        var layout = SystemLayout.Build(graph, counting);
        var seed = SolutionSeed.Build(graph, layout);

        var positions = Enumerable.Range(layout.PromotionOffset, layout.Count - layout.PromotionOffset)
            .Where(index => layout.Unknowns[index].Name.EndsWith(".position", StringComparison.Ordinal))
            .Select(index => seed.Values[index])
            .ToArray();

        Assert.NotEmpty(positions);
        Assert.All(positions, position => Assert.Equal(0.5, position));
    }

    [Fact]
    public void APromotedBarePumpStartsWithProvisionalHead()
    {
        // A bare pump's head is solved from the complete circuit. Starting that promoted variable at zero
        // also makes the pressure seed a loop with no driver, so the valve bypass sees the wrong pressure
        // direction before Newton gets a useful derivative. Two-point-two metres is only a seed; it is not a sizing
        // result or a constraint on the solved head.
        var graph = Lower("m2-distribution-header.fluid");
        var counting = WellPosedness.Check(graph).Counting;
        var layout = SystemLayout.Build(graph, counting);
        var seed = SolutionSeed.Build(graph, layout);

        var heads = Enumerable.Range(layout.PromotionOffset, layout.Count - layout.PromotionOffset)
            .Where(index => layout.Unknowns[index].Name.EndsWith(".head", StringComparison.Ordinal))
            .Select(index => seed.Values[index])
            .ToArray();

        Assert.NotEmpty(heads);
        Assert.All(heads, head => Assert.Equal(2.2, head));
    }

    /// <remarks>
    /// <para>
    /// <strong>A branch estimate is a magnitude, and the branch it is laid on has an orientation nothing
    /// physical chose</strong> (<c>S-51</c>). <c>From</c> and <c>To</c> record the direction the lowering
    /// walk happened to cross the branch; the seed gave every chord its estimate as positive along that,
    /// so whether the field described the plant running forwards or backwards was an artefact of where
    /// the walk started.
    /// </para>
    /// <para>
    /// A rated exchanger is where that becomes checkable without restating the fix. <c>in</c> and
    /// <c>out</c> name ports, not path positions, so an exchanger that states both says which way its
    /// water goes; the seed must agree. Measured on <c>m2-cooling-loop</c>, whose supply branch is lowered
    /// <c>3WV</c> to <c>N2</c> while the water runs <c>N2</c>, <c>PU1</c>, <c>HE1</c>, <c>3WV</c>: a chord
    /// there at +0.239 kg/s seeds <c>PU1</c> pushing backwards through <c>HE1</c>, which is the same
    /// inversion <c>ARatedInletLandsOnTheNodeAtTheInletPortAndNotTheOneAfterIt</c> caught in the
    /// enthalpies, one iterate earlier and in the flows instead.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Samples))]
    public void TheSeedRunsARatedExchangerFromItsInletTowardsItsOutlet(string sample)
    {
        var graph = Lower(sample);
        var layout = SystemLayout.Build(graph, WellPosedness.Check(graph).Counting);
        var seed = SolutionSeed.Build(graph, layout);
        var reversed = new List<string>();

        foreach (var branch in graph.Branches)
        {
            var flow = seed.Values[layout.BranchFlow(branch.Index)];

            // A branch at a standstill has no direction to be wrong about, and whether it is allowed to
            // stand still at all is EveryDrivenBranchIsSeededAwayFromRest's question rather than this
            // one's. m1-syntax-tour has one: it is a grammar exercise and several of its circuits are
            // deliberately incomplete as plant.
            if (Math.Abs(flow) <= Tolerances.FlowZero)
            {
                continue;
            }

            for (var step = 0; step < branch.Path.Length; step++)
            {
                if (Rated(graph, branch.Path[step]) is not { } rated)
                {
                    continue;
                }

                var entering = Where(graph, branch, step, rated.Inlet);
                var leaving = Where(graph, branch, step, rated.Outlet);

                if (entering == leaving || (entering < leaving ? flow > 0 : flow < 0))
                {
                    continue;
                }

                reversed.Add(
                    $"{branch.Path[step].Name} states in and out, so the seed must carry water from the "
                    + $"port called in to the one called out; branch {branch.Index} runs {flow:G4} kg/s "
                    + "the other way, which is the exchanger cooling when it heats.");
            }
        }

        // The same convention as the ladder's gates (62): a sample whose first line says `# does not
        // seed: S-nn` is expected to seed a rated exchanger backwards until that defect closes, so the
        // marker has to come off the moment it does.
        var marker = File.ReadLines(Path.Combine(RepositoryLayout.Samples, sample)).FirstOrDefault() ?? string.Empty;

        if (marker.StartsWith("# does not seed: S-", StringComparison.Ordinal))
        {
            Assert.True(reversed.Count > 0, $"{sample} is marked '{marker}' but every rated exchanger seeds forwards; the marker is stale.");
            return;
        }

        Assert.True(reversed.Count == 0, string.Join(Environment.NewLine, reversed));
    }

    /// <summary>A rated exchanger's inlet and outlet port indices, or <see langword="null"/>.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="element">The candidate component.</param>
    /// <returns>The two port indices, when the component states a temperature at both.</returns>
    private static (int Inlet, int Outlet)? Rated(CircuitGraph graph, IFlowComponent element)
    {
        if (!graph.Components.Contains(element))
        {
            return null;
        }

        var inlet = -1;
        var outlet = -1;

        for (var port = 0; port < element.Ports.Length; port++)
        {
            if (!element.StatedParameters.TryGetValue(element.Ports[port].Name, out var stated)
                || stated.Dimension != Dimension.Temperature)
            {
                continue;
            }

            if (element.Ports[port].Role is PortRole.Inlet && inlet < 0)
            {
                inlet = port;
            }
            else if (element.Ports[port].Role is PortRole.Outlet && outlet < 0)
            {
                outlet = port;
            }
        }

        return inlet >= 0 && outlet >= 0 ? (inlet, outlet) : null;
    }

    /// <summary>Where along a branch the neighbour on one of a component's ports sits.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="branch">The branch the component lies on.</param>
    /// <param name="step">The component's own position in <see cref="Branch.Path"/>.</param>
    /// <param name="port">The port to follow.</param>
    /// <returns>A path position, -1 for the <c>From</c> end and the path length for the <c>To</c> end.</returns>
    private static int Where(CircuitGraph graph, Branch branch, int step, int port)
    {
        var element = graph.Components.IndexOf(branch.Path[step]);
        var peer = element < 0 ? PortRef.None : graph.Adjacency.Peer(element, port);

        if (!peer.Exists)
        {
            return step;
        }

        var neighbour = graph.Components[peer.Component];

        if (step > 0 && ReferenceEquals(neighbour, branch.Path[step - 1]))
        {
            return step - 1;
        }

        if (step + 1 < branch.Path.Length && ReferenceEquals(neighbour, branch.Path[step + 1]))
        {
            return step + 1;
        }

        if (ReferenceEquals(neighbour, branch.From.Element))
        {
            return -1;
        }

        return ReferenceEquals(neighbour, branch.To.Element) ? branch.Path.Length : step;
    }

    /// <remarks>
    /// <para>
    /// <strong>Being a vertex's parent means being solved for whatever closes its balance, and zero is a
    /// legal answer to that</strong> (<c>S-51</c>). The forest picks parents by walking the graph, which
    /// knows nothing about which branches are allowed to stand still, so a branch that must move water
    /// can be handed the leftover and land on nothing.
    /// </para>
    /// <para>
    /// The arrangement below is where it was measured: a hydraulically separated header, the standard
    /// answer to a boiler that wants constant flow feeding consumers that do not. The primary loop
    /// through <c>HS1</c> and the decoupler <c>PDC</c> both run <c>N7</c> to <c>N8</c>, the walk reached
    /// <c>N8</c> by the primary, and the primary closed at <strong>-0</strong> -- a 54 kW source moving
    /// no water, every node along it enthalpy-undetermined, and the system <c>Singular</c> at iteration
    /// zero with rank 57 of 59.
    /// </para>
    /// <para>
    /// <strong>The decoupler is the branch that should carry the leftover</strong>, and this is not a
    /// coincidence of the walk: carrying the difference between primary and secondary flow is the whole
    /// reason one is fitted, and near zero is its ordinary operating answer. The seed does not know that
    /// as a rule, and does not need to -- it re-spans, barring whatever stalled, and keeps the bar only
    /// while the count of stalled branches falls.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoBranchStandsStillWhereAnotherSpanningForestWouldHaveMovedIt()
    {
        var source = """
            fluidscript 1
            circuit heating 100
            fluid water

            HS1     heat_exchanger power=54 out.t=80
            PU_SRC  pump
            TV_MAIN three_way_valve
            PU_MAIN pump
            PP1     pipe length=5 dn=40
            PP2     pipe length=5 dn=40
            PDC     pipe length=1 dn=50
            PS1     pipe length=6 dn=32
            PB      pipe length=4 dn=32

            connections
            N1 - HS1 - N2 - PU_SRC - PP1 - N7
            N7 - PDC - N8
            N8 - PP2 - N1
            N7 - PS1 - TV_MAIN.a
            TV_MAIN.b - PB - N5
            TV_MAIN.ab - PU_MAIN - N3
            N3 - N4
            N6 - N5
            N5 - N8

            N1 node p=250
            N3 node t=60

            circuit AHU 101

            HE_AHU  heat_exchanger in.t=50 out.t=30 power=-24 kW
            TV_AHU  three_way_valve
            PU_AHU  pump
            PA1     pipe length=12 dn=25
            PA2     pipe length=12 dn=25

            connections
            NM_AHU - PU_AHU - HE_AHU - TV_AHU
            TV_AHU.b - NM_AHU
            N3 - PA1 - NM_AHU
            TV_AHU.a - PA2 - N5

            circuit radiators 102

            HE_RAD  heat_exchanger in.t=50 out.t=30 power=-30 kW
            TV_RAD  three_way_valve
            PU_RAD  pump
            PR1     pipe length=18 dn=25
            PR2     pipe length=18 dn=25

            connections
            NM_RAD - PU_RAD - HE_RAD - TV_RAD
            TV_RAD.b - NM_RAD
            N4 - PR1 - NM_RAD
            TV_RAD.a - PR2 - N6
            """;


        var graph = GraphFixture.Lower(source).Graph;
        var layout = SystemLayout.Build(graph, WellPosedness.Check(graph).Counting);
        var seed = SolutionSeed.Build(graph, layout);

        foreach (var branch in graph.Branches)
        {
            Assert.True(
                Math.Abs(seed.Values[layout.BranchFlow(branch.Index)]) > Tolerances.FlowZero,
                $"branch {branch.Index} ({branch.From.Label} -> {branch.To.Label}) is seeded at rest on a "
                + "closed circuit whose every branch carries water, so the forest chose the wrong parent "
                + "rather than the circuit choosing to stop (S-51).");
        }
    }
}

