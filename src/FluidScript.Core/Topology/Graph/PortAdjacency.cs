using System.Collections.Immutable;

namespace FluidScript.Core.Topology;

/// <summary>One end of a connection: a component, and one of its ports.</summary>
/// <param name="Component">The component's index in <see cref="CircuitGraph.Components"/>.</param>
/// <param name="Port">The port's index in that component's <c>Ports</c>.</param>
/// <remarks>
/// An index rather than the component itself, because this is the one structure the solver indexes
/// per iterate: a reference would mean a dictionary lookup inside the loop that runs N+1 times per
/// Newton iteration.
/// </remarks>
public readonly record struct PortRef(int Component, int Port)
{
    /// <summary>Gets the reference meaning "nothing is attached here".</summary>
    /// <value>
    /// A port with no peer. Inference rule I3 leaves optional ports unconnected, and a component the
    /// factory could not build takes its connections with it, so this is a normal state and not an
    /// error.
    /// </value>
    public static PortRef None => new(-1, -1);

    /// <summary>Gets whether this reference names a port at all.</summary>
    public bool Exists => Component >= 0;
}

/// <summary>Which port each port is joined to.</summary>
/// <remarks>
/// <para>
/// <strong>Lowering computed this and threw it away, and the solver cannot be written without
/// it</strong> (<c>S-10</c>). <see cref="Branch.Path"/> gives an ordered walk with the interior nodes
/// interleaved, but not <em>which port</em> of one element faces the next — and a two-port
/// pass-through walked backwards is entered at its outlet, so the direction cannot be recovered from
/// the order. Assembling a <c>SolveContext</c> means filling each port's state from the node that
/// port touches and each port's flow from the branch that crosses it, and both need this table.
/// </para>
/// <para>
/// It is the relation the graph was always specified to carry — <c>23</c> calls the graph "the
/// solvable form of a model" — rather than something tier 30 added for its own convenience.
/// </para>
/// </remarks>
public sealed class PortAdjacency
{
    private readonly ImmutableArray<ImmutableArray<PortRef>> _peers;

    /// <summary>Initializes the table.</summary>
    /// <param name="peers">
    /// One row per component in <see cref="CircuitGraph.Components"/> order, one entry per port in
    /// that component's own port order, each holding the port it is joined to or
    /// <see cref="PortRef.None"/>.
    /// </param>
    public PortAdjacency(ImmutableArray<ImmutableArray<PortRef>> peers) => _peers = peers;

    /// <summary>Gets the table for a graph with nothing in it.</summary>
    public static PortAdjacency Empty { get; } = new([]);

    /// <summary>Gets how many components the table covers.</summary>
    public int ComponentCount => _peers.Length;

    /// <summary>Finds what a port is joined to.</summary>
    /// <param name="component">The component's index in <see cref="CircuitGraph.Components"/>.</param>
    /// <param name="port">The port's index in that component's <c>Ports</c>.</param>
    /// <returns>
    /// The port on the other side, or <see cref="PortRef.None"/> when the port is unconnected or the
    /// indices name no port. Out-of-range answers rather than throwing: a graph is assembled from a
    /// script under editing, and an index that does not exist is the same fact as a port with nothing
    /// on it.
    /// </returns>
    public PortRef Peer(int component, int port) =>
        (uint)component < (uint)_peers.Length && (uint)port < (uint)_peers[component].Length
            ? _peers[component][port]
            : PortRef.None;

    /// <summary>Gets how many ports a component has, as the table sees it.</summary>
    /// <param name="component">The component's index in <see cref="CircuitGraph.Components"/>.</param>
    /// <returns>The port count, or zero when the index names no component.</returns>
    public int PortCount(int component) =>
        (uint)component < (uint)_peers.Length ? _peers[component].Length : 0;
}
