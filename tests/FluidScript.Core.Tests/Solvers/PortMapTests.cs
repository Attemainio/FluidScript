using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>Which branch's flow and which node's state each port sees.</summary>
/// <remarks>
/// The last structural fact between the two layouts and a residual. <c>Branch.Path</c> says which
/// components share a flow and not which way each faces, and a pass-through walked from the other end
/// is entered at its outlet — so every assertion here is about orientation rather than membership.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PortMapTests
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
    public void EveryConnectedPortIsReachedByExactlyOneBranch(string sample)
    {
        var lowered = GraphFixture.Lower(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample)));

        Assert.SkipWhen(
            !lowered.Unresolved.IsEmpty,
            $"{sample} drops {string.Join(", ", lowered.Unresolved)}, so some ports have no peer.");

        var graph = lowered.Graph;
        var map = PortMap.Build(graph);

        for (var element = 0; element < graph.Components.Length; element++)
        {
            for (var port = 0; port < graph.Components[element].Ports.Length; port++)
            {
                if (!graph.Adjacency.Peer(element, port).Exists)
                {
                    continue;
                }

                var binding = map[element, port];

                Assert.True(
                    binding.CarriesFlow,
                    $"{graph.Components[element].Name} port {port} has a peer and no branch, so its "
                    + "flow would be read as zero.");

                Assert.True(binding.Sign is 1 or -1);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void APassThroughIsEnteredAtOnePortAndLeftAtTheOther(string sample)
    {
        // The orientation the branch decomposition throws away. Both ports of a two-port flow group
        // carry the same branch, and they must carry opposite signs -- a component whose ports were
        // both positive would have mass entering from both ends.
        var graph = Lower(sample);
        var map = PortMap.Build(graph);

        for (var element = 0; element < graph.Components.Length; element++)
        {
            var component = graph.Components[element];
            var groups = component.FlowGroups;

            for (var port = 0; port < groups.Length; port++)
            {
                for (var other = port + 1; other < groups.Length; other++)
                {
                    if (groups[other] != groups[port])
                    {
                        continue;
                    }

                    var left = map[element, port];
                    var right = map[element, other];

                    if (!left.CarriesFlow || !right.CarriesFlow || left.Branch != right.Branch)
                    {
                        continue;
                    }

                    Assert.Equal(0, left.Sign + right.Sign);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void AFlowComponentAlwaysReadsItsStateFromANode(string sample)
    {
        // Rule I2 puts a node between two components and I3 terminates what is left, so a connected
        // port of anything but a node touches a node. It is asserted rather than assumed because a
        // port that did not would read a state that does not exist, silently, at every iterate.
        var lowered = GraphFixture.Lower(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample)));

        Assert.SkipWhen(
            !lowered.Unresolved.IsEmpty,
            $"{sample} drops {string.Join(", ", lowered.Unresolved)}, so some ports have no peer.");

        var graph = lowered.Graph;
        var map = PortMap.Build(graph);

        for (var element = 0; element < graph.Components.Length; element++)
        {
            if (graph.Components[element] is CircuitNode)
            {
                continue;
            }

            for (var port = 0; port < graph.Components[element].Ports.Length; port++)
            {
                if (graph.Adjacency.Peer(element, port).Exists)
                {
                    Assert.InRange(map[element, port].Node, 0, graph.Nodes.Length - 1);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ABranchLeavesItsFromEndAndEntersItsToEnd(string sample)
    {
        var lowered = GraphFixture.Lower(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample)));

        Assert.SkipWhen(
            !lowered.Unresolved.IsEmpty,
            $"{sample} drops {string.Join(", ", lowered.Unresolved)}, so a walk runs out of peers "
            + "mid-branch and never reaches the end it was headed for.");

        var graph = lowered.Graph;
        var map = PortMap.Build(graph);
        var indices = Indices(graph);

        foreach (var branch in graph.Branches)
        {
            var from = map[indices[branch.From.Element], branch.From.Port];
            var to = map[indices[branch.To.Element], branch.To.Port];

            Assert.Equal(branch.Index, from.Branch);
            Assert.Equal(-1, from.Sign);
            Assert.Equal(branch.Index, to.Branch);
            Assert.Equal(1, to.Sign);
        }
    }

    [Fact]
    public void TheCoolingLoopsPumpIsEnteredFromTheNodeThatFeedsIt()
    {
        // 23's own branch table: N2 -> PU1 -> PU1__HE1 -> HE1 -> HE1__3WV -> 3WV. Which end that
        // branch is walked from is Decompose's business and not a fact about the pump, so the sign is
        // asserted only against its own other port. What *is* fixed is which node each port reads,
        // and that is what lets `PU1.Ports[0].Pressure` mean the suction pressure whichever way the
        // walk ran.
        var graph = Lower("m2-cooling-loop.fluid");
        var map = PortMap.Build(graph);
        var indices = Indices(graph);
        var pump = graph.Components.Single(static component => component.Name == "PU1");
        var element = indices[pump];

        var inlet = map[element, 0];
        var outlet = map[element, 1];

        Assert.Equal(0, inlet.Sign + outlet.Sign);
        Assert.Equal(inlet.Branch, outlet.Branch);
        Assert.Equal("N2", graph.Nodes[inlet.Node].Name);
        Assert.Equal("PU1__HE1", graph.Nodes[outlet.Node].Name);
    }

    [Fact]
    public void AThreeWayValvesPortsEachBelongToADifferentBranch()
    {
        // A junction element is a vertex of the branch graph, so no branch crosses it: each of its
        // ports ends one. That is D-63's rule made visible -- three ports in one flow group, and three
        // separate flow unknowns rather than one.
        var graph = Lower("m2-cooling-loop.fluid");
        var map = PortMap.Build(graph);
        var indices = Indices(graph);
        var valve = graph.Components.Single(static component => component.Name == "3WV");
        var element = indices[valve];

        var branches = Enumerable.Range(0, valve.Ports.Length)
            .Select(port => map[element, port].Branch)
            .ToArray();

        Assert.Equal(3, branches.Length);
        Assert.Equal(3, branches.Distinct().Count());
        Assert.DoesNotContain(-1, branches);
    }

    private static Dictionary<IFlowComponent, int> Indices(CircuitGraph graph)
    {
        var indices = new Dictionary<IFlowComponent, int>();

        for (var index = 0; index < graph.Components.Length; index++)
        {
            indices[graph.Components[index]] = index;
        }

        return indices;
    }

    private static CircuitGraph Lower(string sample) =>
        sample.EndsWith(".fluid", StringComparison.Ordinal)
            ? GraphFixture.Lower(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample))).Graph
            : GraphFixture.Lower(sample).Graph;
}
