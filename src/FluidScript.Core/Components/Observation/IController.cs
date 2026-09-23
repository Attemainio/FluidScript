using FluidScript.Core.Language.Binding;

namespace FluidScript.Core.Components.Observation;

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
