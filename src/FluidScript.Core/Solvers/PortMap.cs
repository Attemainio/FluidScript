using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Solvers;

/// <summary>Where one port reads its state and its flow from in the solved system.</summary>
/// <param name="Node">
/// The node whose pressure and enthalpy this port carries, or <c>-1</c> for a port that reads none —
/// a node's own port, whose state belongs to what is attached to it, and an unconnected optional port.
/// </param>
/// <param name="Branch">The branch supplying this port's mass flow, or <c>-1</c> when nothing does.</param>
/// <param name="Sign">
/// <c>+1</c> when the branch's flow enters the component here, <c>-1</c> when it leaves, <c>0</c> for a
/// port no branch reaches. Multiplying the branch's unknown by it gives <c>22</c>'s convention: mass
/// flow positive <em>into</em> the component.
/// </param>
public readonly record struct PortBinding(int Node, int Branch, int Sign)
{
    /// <summary>Gets the binding of a port nothing is connected to.</summary>
    public static PortBinding Unconnected => new(-1, -1, 0);

    /// <summary>Gets whether a branch reaches this port at all.</summary>
    public bool CarriesFlow => Branch >= 0;
}

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

        foreach (var branch in graph.Branches)
        {
            Trace(graph, components, branch, bindings);
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
    private static void Trace(
        CircuitGraph graph,
        Dictionary<object, int> components,
        Branch branch,
        PortBinding[][] bindings)
    {
        var current = components[branch.From.Element];
        var exit = branch.From.Port;

        // The flow leaves the From end through this port, so it is negative into that element.
        bindings[current][exit] = bindings[current][exit] with { Branch = branch.Index, Sign = -1 };

        while (true)
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
