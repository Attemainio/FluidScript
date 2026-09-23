using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Primitives;

namespace FluidScript.Core.Sizing;

/// <summary>Fills the parameters a user left out (<c>D-02</c>).</summary>
/// <remarks>
/// <para>
/// <strong>A sizer never sees a parameter the user stated, and never sees a promoted one.</strong>
/// The first is <c>24</c>'s invariant 1. The second is the division of labour the counting table
/// already draws: a parameter omitted with <c>ParameterOmissionBehavior.Size</c> is normally sized
/// here, but where a stated constraint needs somewhere to go, <c>WellPosedness</c> promotes one such
/// parameter to an unknown and the solver determines it instead. <c>CountingTable.Promotions</c> names
/// exactly which, so the loop skips them rather than each rule having to know.
/// </para>
/// <para>
/// Sizing is not optimization. It applies deterministic engineering rules to reach a defensible design
/// in one pass and does not search; <c>35</c> owns the other thing, runs on request, and takes seconds.
/// </para>
/// </remarks>
public interface ISizer
{
    /// <summary>Gets the parameter names this rule can choose, as a script would spell them.</summary>
    ImmutableArray<string> Parameters { get; }

    /// <summary>Gets a value per parameter that merely lets a graph exist.</summary>
    /// <value>
    /// Canonical parameter name to a value, or empty when this rule's parameters do not block lowering.
    /// </value>
    /// <remarks>
    /// <strong>Not a design, and never solved against.</strong> A pipe has no component at all without a
    /// bore, so the outer loop's bootstrap lowering needs something in the slot before there is a graph
    /// to estimate flows on. Flow estimates come from stated duties and stated flows rather than from
    /// resistances, so nothing they produce depends on what is here, and the first real pass replaces it.
    /// A rule whose parameters never block lowering — a pump's head, a valve's Kv, both of which have a
    /// constructor fallback — returns nothing and stays promotable, which is the correct outcome.
    /// </remarks>
    ImmutableDictionary<string, Quantity> Provisional => [];

    /// <summary>Tells whether this rule applies to a component at all.</summary>
    /// <param name="component">The component.</param>
    /// <returns><see langword="true"/> when <see cref="Size"/> would have something to say.</returns>
    bool CanSize(IFlowComponent component);

    /// <summary>Chooses values for whatever <see cref="Parameters"/> this component left open.</summary>
    /// <param name="component">The component to size.</param>
    /// <param name="context">The flow and state to size against.</param>
    /// <returns>
    /// The values with their bases, or why the estimate was not enough to decide — which is ordinary on
    /// a first pass and is resolved by the loop iterating, not by guessing.
    /// </returns>
    Result<SizingResult> Size(IFlowComponent component, in SizingContext context);
}
