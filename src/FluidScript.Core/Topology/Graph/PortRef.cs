namespace FluidScript.Core.Topology.Graph;

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
