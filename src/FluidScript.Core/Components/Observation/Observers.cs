using System.Collections.Immutable;

using FluidScript.Core.Binding;
using FluidScript.Core.Fluids;
using FluidScript.Core.Language;
using FluidScript.Core.Units;

namespace FluidScript.Core.Components;

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

/// <summary>A model participant that reads state and contributes no equations.</summary>
/// <remarks>
/// An observer is outside the hydraulic graph entirely (<c>D-61</c>): no ports, no <c>DrivesFlow</c>,
/// no residuals. A pass-through instrument would carry two ports, gain an inserted node from rule I2,
/// and contribute equations that are all identities — a hundred of them would double the size of the
/// solve to compute nothing.
/// </remarks>
public interface IObserver : IComponent
{
    /// <summary>Gets the name of the node this instrument is placed on.</summary>
    /// <remarks>Written with the <c>at</c> clause. An observer that names none is <c>FS1533</c>.</remarks>
    string AttachedNode { get; }

    /// <summary>Gets the properties this observer reads.</summary>
    /// <value>
    /// Each names the <em>node</em>, not this instrument: <c>D-61</c>'s "<c>TE1.t</c> is <c>N2.t</c>"
    /// stated as data. A sensor reads exactly one.
    /// </value>
    ImmutableArray<PropertyReference> ObservedProperties { get; }

    /// <summary>Reads this instrument's measurement from a node's solved state.</summary>
    /// <param name="observation">The node's state and flow at the solution being reported.</param>
    /// <returns>The measured value, in the dimension the registry declares for the property.</returns>
    /// <remarks>
    /// A sensor holds no state of its own and is not a filter, a lag, or a source of error, so this is
    /// a projection rather than a computation. Instrument dynamics are post-v1 and would be parameters
    /// on the kind, not a different interface.
    /// </remarks>
    Quantity Read(in NodeObservation observation);
}

/// <summary>An observer that also drives one actuator during a transient.</summary>
/// <remarks>
/// The interface exists here so the family is complete; the control law that uses it is tier 30's
/// (<c>34-controllers</c>) and arrives with the solver.
/// </remarks>
public interface IController : IObserver
{
    /// <summary>Gets the parameter this controller moves.</summary>
    /// <value>
    /// Qualified as <c>component.parameter</c>. Where the kind names exactly one actuated parameter
    /// the script may leave it off, and the registry supplies it (<c>D-61</c>).
    /// </value>
    PropertyReference Actuator { get; }
}
