using System.Collections.Immutable;

using FluidScript.Core.Language.Binding;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Components.Observation;

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
