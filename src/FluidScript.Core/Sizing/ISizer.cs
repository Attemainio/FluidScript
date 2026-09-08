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

    /// <summary>Gets the resistance the rest of this component's branch puts in its way.</summary>
    /// <value>
    /// Pa at <see cref="MassFlow"/>, positive against the flow, and <strong>excluding this component's
    /// own contribution</strong> — which is what a valve's authority is measured against.
    /// </value>
    public required double BranchDrop { get; init; }

    /// <summary>Gets the resistance around the whole circuit this component drives.</summary>
    /// <value>
    /// <para>
    /// Pa at the design flow, excluding this component's own contribution. Zero is a real answer rather
    /// than a missing one — a circuit of ideal links has no head to develop against.
    /// </para>
    /// <para>
    /// <see langword="null"/> is the different fact that <strong>the component is on no closed circuit
    /// at all</strong> — declared, possibly even on a branch, but on no cycle. Both reach the same head,
    /// and they are opposite defects: one is a loss nobody modelled, the other a connection nobody made.
    /// A rule that collapses them reports the wrong one, which is <c>C-57</c>.
    /// </para>
    /// </value>
    /// <remarks>
    /// <strong>A pump cannot be sized from a converged solve, and this is why it is handed the drop
    /// instead.</strong> At convergence the head and the flow are consistent by construction: the field
    /// balances whatever head the pump was given, so reading the rise back across it returns the value
    /// it started with and the loop reports settled at the wrong answer. The drop has to be evaluated
    /// at the flow the <em>duty</em> fixes, against the laws of everything else on the loop.
    /// </remarks>
    public required double? LoopDrop { get; init; }

    /// <summary>Gets the driving pressure the circuit offers, when the circuit fixes it.</summary>
    /// <value>
    /// <para>
    /// Pa, positive, and <strong>the whole of what is available to be spent</strong> — the difference
    /// between the two stated boundary pressures the variable circuit runs between. A rule spends what
    /// <see cref="BranchDrop"/> does not: the component's own drop is then <em>determined</em> rather
    /// than chosen, and an authority target has nothing left to choose with.
    /// </para>
    /// <para>
    /// <see langword="null"/> means the drop is a <strong>choice</strong>, because a pump on the path
    /// carries whatever head the circuit turns out to need. That is the ordinary case and the one the
    /// authority target was written for (<c>24</c>).
    /// </para>
    /// </value>
    /// <remarks>
    /// <strong>The two cases round opposite ways, and that is the point of carrying the distinction.</strong>
    /// Rounding a Kv <em>down</em> picks a smaller valve, which drops more — safe where a pump absorbs
    /// the difference, and wrong at a fixed differential, where a coefficient below the required one
    /// cannot pass the design flow at <em>any</em> position. So a bounded circuit rounds up and keeps the
    /// valve a little off its stop, which is the headroom a control valve is meant to have.
    /// </remarks>
    public double? AvailableDrop { get; init; }
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
