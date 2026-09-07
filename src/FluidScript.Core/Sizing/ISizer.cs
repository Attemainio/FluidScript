using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Units;

namespace FluidScript.Core.Sizing;

/// <summary>One value sizing chose, and why it chose it.</summary>
/// <param name="Value">The value, carrying its own dimension so nothing downstream has to guess it.</param>
/// <param name="Basis">
/// What a user reads when they ask why. Never empty — <c>24</c>'s invariant 2, and the whole mitigation
/// for the risk that document opens with: a sized value always <em>looks</em> reasonable, so the number
/// alone is one a user has to trust and the basis is one they can argue with.
/// </param>
/// <param name="FromDefault">
/// <see langword="true"/> when the value came from the default catalogue rather than a computation.
/// Rendered differently, because a default is a placeholder and a computed size is a decision.
/// </param>
public readonly record struct SizedValue(Quantity Value, string Basis, bool FromDefault);

/// <summary>What one sizer decided about one component.</summary>
public sealed record SizingResult
{
    /// <summary>Gets the values chosen, by the parameter name a script would state.</summary>
    public required ImmutableDictionary<string, SizedValue> Values { get; init; }

    /// <summary>Gets what happened on the way, in the order it happened.</summary>
    /// <value>
    /// Sentences, not codes. <c>24</c>'s <c>FS2305</c>–<c>FS2307</c> are the eventual home for these and
    /// none of them is registered yet, so they are carried as text rather than emitted as diagnostics a
    /// user could not look up (<c>C-44</c>'s reasoning: a wrong estimate costs iterations, a wrong
    /// diagnostic is a sentence somebody acts on).
    /// </value>
    public required ImmutableArray<string> Notes { get; init; }
}

/// <summary>Everything a sizing rule is allowed to read.</summary>
/// <remarks>
/// <para>
/// Deliberately narrow. A rule sees the flow through its component and the fluid state there, and
/// nothing about the rest of the circuit — which is what keeps sizing deterministic (<c>24</c>'s
/// invariant 3) and what makes a rule testable without building a graph.
/// </para>
/// <para>
/// <strong>The flow is an estimate on the first pass and a solution afterwards</strong>, and no rule
/// may care which. That is the single outer loop's contract: sizing runs against whatever the last
/// solve produced, and the loop is what reconciles them (<c>31</c>).
/// </para>
/// </remarks>
public readonly record struct SizingContext
{
    /// <summary>Gets the fluid state at the component being sized.</summary>
    public required FluidState State { get; init; }

    /// <summary>Gets the mass flow through the component.</summary>
    /// <value>kg/s. Signed as the branch is; every rule here uses its magnitude.</value>
    public required double MassFlow { get; init; }
}

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
