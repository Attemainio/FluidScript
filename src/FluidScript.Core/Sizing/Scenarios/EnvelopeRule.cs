namespace FluidScript.Core.Sizing.Scenarios;

/// <summary>How one parameter's value is taken across every scenario (<c>D-143</c>, <c>24</c>'s envelope table).</summary>
public enum EnvelopeRule
{
    /// <summary>The largest value any scenario asked for.</summary>
    /// <remarks>
    /// The rule for every size that is a capacity: a bore, a conductance, an area, a plate count, a
    /// head, a Kv, a hold-up. A component built to the largest demand meets the smaller ones, which is
    /// what makes the envelope a merge rather than a compromise.
    /// </remarks>
    Largest = 1,

    /// <summary>Not merged: the value is an outcome of the sizes rather than one of them.</summary>
    /// <remarks>
    /// <para>
    /// A valve's achieved <c>authority</c> is <c>Δp_valve / (Δp_valve + Δp_rest)</c>, two drops at one
    /// flow — so the flow cancels to first order and authority is a property of the built geometry,
    /// not of the operating point. What the merge changes is that geometry, twice: the valve takes a
    /// Kv that is not the one a case's sizer reported against, and the rest of the branch takes pipes
    /// that are not that case's either.
    /// </para>
    /// <para>
    /// So every case's reported figure is stale the moment the envelope is taken, and a maximum, a
    /// minimum or a governing case's value would each report one the built plant does not have. Left
    /// out here, and read off the frozen re-solve instead: <c>ScenarioSizing.Controllability</c> takes
    /// the lowest reading across the cases of the one plant (<c>C-121</c>).
    /// </para>
    /// <para>
    /// For the case that chose the Kv, its own figure is a floor — the merged Kv is that case's, and
    /// every other merged size only lowers the rest of the branch's resistance. For the other cases it
    /// is not: a stated value that differs by case (<c>dp=[5, 60]</c>) gives each its own branch, and
    /// measured on that plant the governing case read 0.76 while the other read 0.23.
    /// </para>
    /// </remarks>
    Solved,
}
