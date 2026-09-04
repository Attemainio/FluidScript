using FluidScript.Core.Components;
using FluidScript.Core.Sizing;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
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
}
