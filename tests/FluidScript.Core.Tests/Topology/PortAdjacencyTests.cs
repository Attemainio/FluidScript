using FluidScript.Core.Components;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Tests.Topology;

/// <summary>
/// The port-to-port table the graph now publishes, and the question it answers that nothing else can.
/// </summary>
/// <remarks>
/// <c>S-10</c>: lowering computed this to decompose the branches and then discarded it, so a graph
/// recorded the order a walk crossed its elements but not which port faced which. The solver cannot
/// assemble a <c>SolveContext</c> without it — a component's residual reads <c>Ports[i]</c> as the
/// state at the node that port touches.
/// </remarks>
public sealed class PortAdjacencyTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void EveryConnectedPortsPeerPointsBackAtIt()
    {
        // A one-way link is the shape of bug that makes a residual read one node's state and write
        // another's, and it would be invisible in every count.
        var graph = GraphFixture.Lower(GraphFixture.CoolingLoop).Graph;

        for (var element = 0; element < graph.Components.Length; element++)
        {
            for (var port = 0; port < graph.Components[element].Ports.Length; port++)
            {
                var peer = graph.Adjacency.Peer(element, port);

                if (!peer.Exists)
                {
                    continue;
                }

                Assert.Equal(
                    new PortRef(element, port),
                    graph.Adjacency.Peer(peer.Component, peer.Port));
            }
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheTableCoversEveryComponentAndEveryPort()
    {
        var graph = GraphFixture.Lower(GraphFixture.CoolingLoop).Graph;

        Assert.Equal(graph.Components.Length, graph.Adjacency.ComponentCount);

        for (var element = 0; element < graph.Components.Length; element++)
        {
            Assert.Equal(graph.Components[element].Ports.Length, graph.Adjacency.PortCount(element));
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnUnconnectedPortHasNoPeer()
    {
        // HE1 is a four-port component with only side 1 in this circuit, so its side-2 ports are
        // genuinely unattached. That is a normal graph, not a broken one -- rule I3 leaves optional
        // ports open -- so the table has to have a way of saying so that is not an exception.
        var graph = GraphFixture.Lower(GraphFixture.CoolingLoop).Graph;
        var exchanger = Index(graph, "HE1");

        Assert.Equal(4, graph.Adjacency.PortCount(exchanger));
        Assert.True(graph.Adjacency.Peer(exchanger, 0).Exists);
        Assert.True(graph.Adjacency.Peer(exchanger, 1).Exists);
        Assert.False(graph.Adjacency.Peer(exchanger, 2).Exists);
        Assert.False(graph.Adjacency.Peer(exchanger, 3).Exists);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnIndexNamingNoPortAnswersRatherThanThrowing()
    {
        // No pipeline stage throws on user input, and this table is read while assembling a graph
        // built from a script under editing.
        var graph = GraphFixture.Lower(GraphFixture.CoolingLoop).Graph;

        Assert.False(graph.Adjacency.Peer(-1, 0).Exists);
        Assert.False(graph.Adjacency.Peer(graph.Components.Length, 0).Exists);
        Assert.False(graph.Adjacency.Peer(0, 99).Exists);
        Assert.Equal(0, graph.Adjacency.PortCount(graph.Components.Length));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WalkingABranchByPortReproducesTheOrderTheDecompositionRecorded()
    {
        // The property the assembler depends on: from one end of a branch, the table plus the flow
        // groups reach every element of Path in the recorded order, and each one is entered at a
        // known port. Path alone gives the order and not the port, which is the whole of S-10.
        var graph = GraphFixture.Lower(GraphFixture.CoolingLoop).Graph;

        foreach (var branch in graph.Branches)
        {
            var current = new PortRef(Index(graph, branch.From.Element.Name), branch.From.Port);
            var crossed = new List<string>();

            while (true)
            {
                var entered = graph.Adjacency.Peer(current.Component, current.Port);
                Assert.True(entered.Exists, $"branch {branch.Index} leaves {current} for nothing.");

                var element = graph.Components[entered.Component];

                if (element.Name == branch.To.Element.Name && crossed.Count == branch.Path.Length)
                {
                    Assert.Equal(branch.To.Port, entered.Port);
                    break;
                }

                crossed.Add(element.Name);
                current = new PortRef(entered.Component, Partner(element, entered.Port));
            }

            Assert.Equal(branch.Path.Select(static part => part.Name), crossed);
        }
    }

    /// <summary>The other port of a port's flow group.</summary>
    /// <param name="element">The component being crossed.</param>
    /// <param name="port">The port the walk entered by.</param>
    /// <returns>The port it leaves by.</returns>
    private static int Partner(IFlowComponent element, int port)
    {
        for (var candidate = 0; candidate < element.FlowGroups.Length; candidate++)
        {
            if (candidate != port && element.FlowGroups[candidate] == element.FlowGroups[port])
            {
                return candidate;
            }
        }

        Assert.Fail($"{element.Name} port {port} has no partner, so a branch cannot cross it.");
        return -1;
    }

    /// <summary>Finds a component's index in the graph.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="name">The component's name.</param>
    /// <returns>Its position in <c>Components</c>, which is what the table is indexed by.</returns>
    private static int Index(CircuitGraph graph, string name)
    {
        for (var element = 0; element < graph.Components.Length; element++)
        {
            if (graph.Components[element].Name == name)
            {
                return element;
            }
        }

        Assert.Fail($"the graph has no component called {name}.");
        return -1;
    }
}
