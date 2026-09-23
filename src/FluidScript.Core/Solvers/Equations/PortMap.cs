using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Solvers.Equations;

/// <summary>Which branch's flow and which node's state each port sees.</summary>
/// <remarks>
/// <para>
/// <strong>The branch decomposition says which components share a flow and not which way each one
/// faces.</strong> <see cref="Branch.Path"/> records the order a walk crossed the elements; a two-port
/// pass-through walked from the other end is entered at its outlet, and no residual can be evaluated
/// without knowing which. This replays that walk over <see cref="CircuitGraph.Adjacency"/> and records
/// the sign each port takes, which is the last structural fact standing between the layouts and a
/// residual (<c>S-10</c>).
/// </para>
/// <para>
/// <strong>A branch's flow is positive from its <c>From</c> end to its <c>To</c> end.</strong> That is
/// an orientation, not a claim: a solved negative flow is reverse flow and is a real answer.
/// </para>
/// </remarks>
public sealed class PortMap
{
    private readonly ImmutableArray<ImmutableArray<PortBinding>> _bindings;

    private PortMap(ImmutableArray<ImmutableArray<PortBinding>> bindings) => _bindings = bindings;

    /// <summary>Gets the binding of one port.</summary>
    /// <param name="component">The component's index in the graph.</param>
    /// <param name="port">The port's index in the component's own order.</param>
    /// <returns>Its binding, or <see cref="PortBinding.Unconnected"/> when the indices name no port.</returns>
    public PortBinding this[int component, int port] =>
        (uint)component < (uint)_bindings.Length && (uint)port < (uint)_bindings[component].Length
            ? _bindings[component][port]
            : PortBinding.Unconnected;

    /// <summary>Gets how many ports a component has, as the map recorded them.</summary>
    /// <param name="component">The component's index in the graph.</param>
    /// <returns>The port count, or zero when the index names no component.</returns>
    public int PortCount(int component) =>
        (uint)component < (uint)_bindings.Length ? _bindings[component].Length : 0;

    /// <summary>Binds every port of every component to its branch and its node.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <returns>The map.</returns>
    /// <remarks>
    /// The walk is <c>Lowering.Decompose</c>'s, run again from the published adjacency rather than from
    /// lowering's private tables — which is the point of publishing it. A port the walk never reaches is
    /// left unconnected rather than guessed at: an optional port with nothing on it carries no flow, and
    /// inventing a branch for it would put a column of zeros in the Jacobian.
    /// </remarks>
    public static PortMap Build(CircuitGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var nodes = new Dictionary<object, int>(graph.Nodes.Length, ReferenceEqualityComparer.Instance);

        for (var index = 0; index < graph.Nodes.Length; index++)
        {
            nodes[graph.Nodes[index].Component] = index;
        }

        var components = new Dictionary<object, int>(graph.Components.Length, ReferenceEqualityComparer.Instance);

        for (var index = 0; index < graph.Components.Length; index++)
        {
            components[graph.Components[index]] = index;
        }

        var bindings = new PortBinding[graph.Components.Length][];

        for (var index = 0; index < graph.Components.Length; index++)
        {
            bindings[index] = new PortBinding[graph.Components[index].Ports.Length];
            Array.Fill(bindings[index], PortBinding.Unconnected);
        }

        // A walk leaves each element by a fresh port, so no branch is longer than the graph has ports.
        var ports = bindings.Sum(static row => row.Length);

        foreach (var branch in graph.Branches)
        {
            Trace(graph, components, branch, bindings, ports);
        }

        // The state a port reads is the node it touches, and every non-node port touches one: rule I2
        // puts a node between two components and I3 terminates what is left. A port whose peer is not a
        // node keeps -1, which is a fact the assembler can report rather than a silent zero.
        for (var index = 0; index < graph.Components.Length; index++)
        {
            if (graph.Components[index] is CircuitNode)
            {
                continue;
            }

            for (var port = 0; port < bindings[index].Length; port++)
            {
                var peer = graph.Adjacency.Peer(index, port);

                if (peer.Exists && nodes.TryGetValue(graph.Components[peer.Component], out var node))
                {
                    bindings[index][port] = bindings[index][port] with { Node = node };
                }
            }
        }

        return new PortMap([.. bindings.Select(static row => row.ToImmutableArray())]);
    }

    /// <summary>Walks one branch end to end, recording the sign every port it crosses takes.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="components">Each component's index, by reference.</param>
    /// <param name="branch">The branch to walk.</param>
    /// <param name="bindings">The table being filled.</param>
    /// <param name="steps">The most steps a branch can take: the graph's port count.</param>
    /// <remarks>
    /// A walk that outlives <paramref name="steps"/> is adjacency looping back on itself -- lowering's
    /// invariant broken, not a branch. It stops, leaving the rest unconnected for the assembler to
    /// report; walking on would hold a request worker forever, which is worse than a wrong answer.
    /// </remarks>
    private static void Trace(
        CircuitGraph graph,
        Dictionary<object, int> components,
        Branch branch,
        PortBinding[][] bindings,
        int steps)
    {
        var current = components[branch.From.Element];
        var exit = branch.From.Port;

        // The flow leaves the From end through this port, so it is negative into that element.
        bindings[current][exit] = bindings[current][exit] with { Branch = branch.Index, Sign = -1 };

        while (steps-- > 0)
        {
            var peer = graph.Adjacency.Peer(current, exit);

            if (!peer.Exists)
            {
                return;
            }

            var entry = peer.Port;
            current = peer.Component;

            bindings[current][entry] = bindings[current][entry] with { Branch = branch.Index, Sign = 1 };

            if (ReferenceEquals(graph.Components[current], branch.To.Element) && entry == branch.To.Port)
            {
                return;
            }

            var groups = graph.Components[current].FlowGroups;
            var partner = -1;

            for (var candidate = 0; candidate < groups.Length; candidate++)
            {
                if (candidate != entry && groups[candidate] == groups[entry])
                {
                    partner = candidate;
                    break;
                }
            }

            if (partner < 0)
            {
                return;
            }

            bindings[current][partner] = bindings[current][partner] with { Branch = branch.Index, Sign = -1 };
            exit = partner;
        }
    }
}
