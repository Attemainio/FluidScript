using FluidScript.Core.Physics.Fluids;

namespace FluidScript.Core.Sizing;

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

    /// <summary>Gets the flow through a three-way valve's common port, when the component is one.</summary>
    /// <value>
    /// kg/s, unsigned, through <c>ab</c>: what the two switched legs split, or what they mix. <see langword="null"/>
    /// for anything that is not a three-way valve with its bypass connected, including a two-way valve,
    /// whose one flow is <see cref="MassFlow"/>. A mixing valve is sized on this flow to a drop band
    /// (<c>D-122</c>), while <see cref="MassFlow"/> stays the variable leg's, which the achieved authority
    /// is still reported against.
    /// </value>
    public double? CommonFlow { get; init; }

    /// <summary>The volume flow through the component, at a density.</summary>
    /// <param name="density">kg/m³, the fluid's at <see cref="State"/> or wherever the rule reads it.</param>
    /// <returns>m³/s, unsigned.</returns>
    public double VolumeFlow(double density) => Math.Abs(MassFlow) / density;

    /// <summary>The volume flow through the component in litres per second, the unit every Kv and pump table is read in.</summary>
    /// <param name="density">kg/m³.</param>
    /// <returns>l/s, unsigned.</returns>
    public double LitresPerSecond(double density) => LitresPerSecond(Math.Abs(MassFlow), density);

    /// <summary>A mass flow as litres per second at a density.</summary>
    /// <param name="massFlow">kg/s, the sign kept.</param>
    /// <param name="density">kg/m³.</param>
    /// <returns>l/s, with the sign of <paramref name="massFlow"/>.</returns>
    public static double LitresPerSecond(double massFlow, double density) => massFlow / density * 1000;
}
