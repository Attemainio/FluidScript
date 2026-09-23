using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Language.Binding;

namespace FluidScript.Core.Topology.Construction;

/// <summary>Builds the component that carries a symbol's equations.</summary>
/// <remarks>
/// A seam rather than a static method, because what a component is built from changes as the
/// pipeline grows: today it is the script's stated parameters, and once <c>24</c>'s outer loop exists
/// it is those plus whatever sizing most recently chose. Lowering re-runs per outer iteration and asks
/// again; a factory holding the current values is what makes that a parameter rather than a rewrite.
/// </remarks>
public interface IComponentFactory
{
    /// <summary>Builds the flow component for one bound symbol.</summary>
    /// <param name="symbol">The bound component, with its parameters evaluated to SI.</param>
    /// <param name="wiring">How the script connected it, for the kinds whose shape depends on it.</param>
    /// <returns>
    /// The component, or <see langword="null"/> when it cannot be built from what is known yet — a
    /// pipe whose bore no catalogue has resolved, most often. Null is a normal result and never an
    /// exception: a script under editing is malformed most of the time.
    /// </returns>
    IFlowComponent? Create(ComponentSymbol symbol, PortWiring wiring);

    /// <summary>Gets the labels, <c>component.parameter</c>, of sized values that are bootstrap provisionals.</summary>
    /// <value>
    /// Empty when every sized value the factory holds was a rule's choice. A provisional lets a component
    /// build and decides nothing (<c>D-96</c>); the graph carries the set so that well-posedness can treat
    /// those parameters as free.
    /// </value>
    ImmutableHashSet<string> Provisional => [];
}
