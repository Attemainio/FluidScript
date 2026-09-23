namespace FluidScript.Core.Topology.Counting;

/// <summary>What a stated parameter asks the circuit to do, beyond supplying a coefficient.</summary>
/// <remarks>
/// The distinction decides what can absorb it. A mixed inlet temperature can only be met by moving a
/// mixing split; a fixed flow can only be met by moving whatever sets the flow. Collapsing the two
/// into "a constraint" lets a valve's <c>kv</c> be offered as the fix for a temperature it cannot
/// change, and the circuit is then reported well-posed when it has no solution.
/// </remarks>
public enum ConstraintKind
{
    /// <summary>A heat exchanger's stated inlet temperature, met by a mixing split.</summary>
    MixedInlet = 1,

    /// <summary>A stated duty or outlet that pins a branch's mass flow.</summary>
    FixedFlow,

    /// <summary>A temperature stated on a node that is not a boundary.</summary>
    NodeTemperature,

    /// <summary>
    /// A terminal temperature that pins the enthalpy level of a closed circuit rather than a flow
    /// (<c>D-90</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>It is the one constraint that promotes nothing, and it is the one that should.</strong> A
    /// closed circuit drops one energy balance as its level -- adding the same enthalpy to every node
    /// satisfies all of them -- so exactly one statement has to pay for that by contributing a row and
    /// claiming no unknown. The count already expected it and nothing arranged it: whichever constraint
    /// happened to run out of candidates paid, which is graph order deciding physics.
    /// </para>
    /// <para>
    /// <strong>A terminal pins a flow only when the other end of the same side is known.</strong>
    /// <c>power</c> with <c>in</c> and <c>out</c> gives m = Q/(h_out - h_in) and pins it; <c>power</c>
    /// with <c>out</c> alone is one equation in two unknowns and pins nothing -- but it does fix an
    /// absolute temperature, which is what the dropped level needs. In an <em>open</em> circuit the inlet
    /// arrives from a boundary and is known without being stated, so a lone <c>out</c> pins the flow
    /// there and this kind does not arise.
    /// </para>
    /// </remarks>
    EnthalpyLevel,
}
