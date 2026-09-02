using System.Collections.Immutable;

using FluidScript.Core.Units;

namespace FluidScript.Core.Components;

/// <summary>A named model participant, independent of whether it carries flow.</summary>
/// <remarks>
/// <para>
/// The root of <c>22</c>'s hierarchy. Two families sit under it: an <em>observer</em>, which reads
/// state and contributes nothing to the equation system, and a flow component, which has ports and
/// residuals. <strong>Only the observer half exists in this phase</strong> — <c>IFlowComponent</c>
/// arrives with the six kinds in <c>P3.3</c>, because its <c>DeclareUnknowns</c> and
/// <c>EvaluateResiduals</c> are stated against types tier 30 owns and no solver has yet fixed their
/// shape.
/// </para>
/// <para>
/// The order is deliberate (<c>08</c>): a seventh family added after six flow kinds exist is six
/// rewrites, and one addition before them is none.
/// </para>
/// <para>
/// <c>22</c>'s interface also names a <c>SymbolId</c> resolving to a declarative symbol definition
/// (<c>D-20</c>). It is absent here on that document's own terms: the symbol schema is a delivery gate
/// with the M3 renderer under <c>D-24</c>, not with M2 physics.
/// </para>
/// </remarks>
public interface IComponent
{
    /// <summary>Gets the user's identifier, or the generated name of an inferred component.</summary>
    string Name { get; }

    /// <summary>Gets the script keyword of this component's kind.</summary>
    /// <value>The canonical spelling, not the alias the user may have written.</value>
    string Kind { get; }

    /// <summary>Gets the canonical component-specific mode, or <see langword="null"/> for a kind with no modes.</summary>
    /// <remarks>A heat exchanger exposes duty, rated or coupled as inferred by <c>D-19</c>.</remarks>
    string? Mode { get; }

    /// <summary>Gets the parameters the user stated.</summary>
    /// <remarks>
    /// Absence means unresolved, never null (<c>D-02</c>). A missing parameter follows its registry
    /// omission policy — normally sizing, or a visible decided default.
    /// </remarks>
    ImmutableDictionary<string, Quantity> StatedParameters { get; }

    /// <summary>Gets the parameters resolved by sizing, keyed the same way.</summary>
    ImmutableDictionary<string, Quantity> SizedParameters { get; }

    /// <summary>Gets the explicit component defaults used because neither source nor sizing resolved them.</summary>
    /// <remarks>Always reported with a basis; includes the tank defaults fixed by <c>D-32</c>.</remarks>
    ImmutableDictionary<string, Quantity> DefaultParameters { get; }
}
