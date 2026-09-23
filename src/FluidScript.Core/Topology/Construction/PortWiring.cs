using System.Collections.Immutable;

namespace FluidScript.Core.Topology.Construction;

/// <summary>How the script connected one component.</summary>
/// <param name="Connections">How many connection endpoints name it.</param>
/// <param name="NamedPorts">The ports named explicitly, in the order the connections wrote them.</param>
/// <remarks>
/// <strong>A count alone is not enough, and the difference matters on exactly one kind today.</strong>
/// A three-way valve with two connections has one port open, and which one decides whether it is a
/// two-way valve or something stranger: leaving <c>c</c> open is the documented two-way arrangement,
/// while a script that qualifies <c>c</c> and leaves <c>b</c> open has connected the bypass and meant
/// something else. Reading only the degree would treat the two the same (<c>S-14a</c>).
/// </remarks>
public readonly record struct PortWiring(int Connections, ImmutableArray<string> NamedPorts)
{
    /// <summary>Gets the wiring of a component no connection names.</summary>
    public static PortWiring None { get; } = new(0, []);

    /// <summary>Tells whether a connection named this port explicitly.</summary>
    /// <param name="port">The port's name, as the kind declares it.</param>
    /// <returns><see langword="true"/> when some connection qualified it.</returns>
    public bool Names(string port) => NamedPorts.Contains(port);
}
