using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Components.Observation;

/// <summary>A node's solved state, as an instrument attached to it sees it.</summary>
/// <remarks>
/// <para>
/// Everything an observer may read, and nothing else. It is deliberately narrower than tier 30's
/// <c>SolveContext</c>, which a flow component needs: a sensor has no ports, so port states and
/// branch flows are not its business, and building it from the two things that already exist —
/// <see cref="FluidState"/> and a mass flow — keeps this phase from fixing the solver's central data
/// shape a phase before the solver exists.
/// </para>
/// <para>
/// <strong>Nothing solves yet.</strong> Until <c>P3.6</c> this is constructed by hand, which is
/// exactly what makes an observer testable without a solver.
/// </para>
/// </remarks>
public readonly record struct NodeObservation
{
    /// <summary>Gets the fluid state at the node.</summary>
    /// <value>
    /// Its pressure is gauge, like every pressure the model carries (<c>D-26</c>), and its temperature
    /// is absolute.
    /// </value>
    public required FluidState State { get; init; }

    /// <summary>Gets the mass flow through the node.</summary>
    /// <value>
    /// kg/s, positive, defined as the <strong>sum of the flows entering the node</strong>. On a node
    /// with one inlet and one outlet that is the through-flow and the definition is invisible; at a
    /// tee it is the one reading that is well defined, since "the flow at this node" otherwise names
    /// two or three different numbers. See <c>C-14</c>: <c>22</c> §7 does not state this, and the
    /// alternative readings differ by a factor of two at a mixing junction.
    /// </value>
    public required Quantity MassFlow { get; init; }
}
