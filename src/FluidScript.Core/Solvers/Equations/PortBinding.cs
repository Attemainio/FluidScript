namespace FluidScript.Core.Solvers.Equations;

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
