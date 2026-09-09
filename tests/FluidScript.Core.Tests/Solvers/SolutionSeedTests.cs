using FluidScript.Core.Components;
using FluidScript.Core.Language;
using FluidScript.Core.Sizing;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

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
            if (graph.Components[element] is not CircuitNode { CarriesMassBalance: true } node)
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
    /// flow to exactly zero — that is the answer, reported as <c>FS4010</c>, and a seed putting it
    /// anywhere else would be seeding a value the first Newton step has to undo.
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
            if (graph.Components[element] is not CircuitNode node)
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
        SystemLayout layout, PortMap ports, StateVector seed, int element, CircuitNode node)
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
    private static double Flux(CircuitGraph graph, SystemLayout layout, StateVector seed, CircuitNode node)
    {
        var index = layout.FluxNodes.IndexOf(
            graph.Nodes.First(candidate => ReferenceEquals(candidate.Component, node)));

        if (index >= 0)
        {
            return seed.Values[layout.ExternalFluxOffset + index];
        }

        return HydraulicPartition.Stated(node, HydraulicPartition.Flow) is { } stated
            ? node.Boundary is BoundaryRole.Return ? -stated : stated
            : 0;
    }

    /// <summary>Whether a branch end is a terminal nothing enters or leaves the model at.</summary>
    private static bool DeadLeg(SystemLayout layout, BranchEnd end) =>
        end.Element is CircuitNode { Ports.Length: 1 } node
        && HydraulicPartition.Stated(node, HydraulicPartition.Flow) is null
        && !layout.FluxNodes.Any(flux => ReferenceEquals(flux.Component, node));

    private static CircuitGraph Lower(string sample) =>
        GraphFixture.Lower(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample))).Graph;

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
    public void APromotedParameterBoundedOnOneSideKeepsTheValueItsComponentHolds()
    {
        // The other half of `S-50`, and the reason it is conditional rather than "seed every promotion at
        // the middle". Only a parameter bounded on *both* sides has a middle to fall back to; one bounded
        // on a single side has no non-arbitrary interior point, so its own value stands. `head` is the
        // case: `Pump.Resolvable` bounds it below and not above, and this rule must leave it exactly
        // where `S-26` put it.
        //
        // **It leaves it at zero, and that is a finding rather than an assertion of correctness.** An
        // unsized pump holds head 0, so a promoted head still starts on the bound `S-26` warned about --
        // "a pump seeded at zero head is a loop with no driver". `S-50` does not reach it, because the
        // midpoint trick needs two bounds and there is only one. Recorded here so the day it is fixed
        // shows up as this test failing rather than as nothing at all.
        var graph = Lower("m2-distribution-header.fluid");
        var counting = WellPosedness.Check(graph).Counting;
        var layout = SystemLayout.Build(graph, counting);
        var seed = SolutionSeed.Build(graph, layout);

        var heads = Enumerable.Range(layout.PromotionOffset, layout.Count - layout.PromotionOffset)
            .Where(index => layout.Unknowns[index].Name.EndsWith(".head", StringComparison.Ordinal))
            .Select(index => (layout.Unknowns[index].OwnerComponentId, Seed: seed.Values[index]))
            .ToArray();

        Assert.NotEmpty(heads);

        foreach (var (owner, value) in heads)
        {
            var pump = graph.Components.Single(
                element => string.Equals(element.Name, owner, StringComparison.Ordinal));

            var held = pump.Resolvable
                .Single(parameter => string.Equals(parameter.Name, "head", StringComparison.Ordinal));

            Assert.Equal(held.Value, value);
        }
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

                if (entering == leaving)
                {
                    continue;
                }

                Assert.True(
                    entering < leaving ? flow > 0 : flow < 0,
                    $"{branch.Path[step].Name} states in and out, so the seed must carry water from the "
                    + $"port called in to the one called out; branch {branch.Index} runs {flow:G4} kg/s "
                    + "the other way, which is the exchanger cooling when it heats.");
            }
        }
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

            HS1     heat_exchanger power=54 out=80
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

            HE_AHU  heat_exchanger in=50 out=30 power=-24 kW
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

            HE_RAD  heat_exchanger in=50 out=30 power=-30 kW
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

