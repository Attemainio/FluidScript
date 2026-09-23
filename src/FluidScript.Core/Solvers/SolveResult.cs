using System.Collections.Immutable;
using FluidScript.Core.Solvers.Equations;

namespace FluidScript.Core.Solvers;

/// <summary>What a solve produced.</summary>
public sealed record SolveResult
{
    /// <summary>Gets whether every scaled residual reached the tolerance.</summary>
    public required bool Converged { get; init; }

    /// <summary>Gets the last iterate, converged or not.</summary>
    /// <value>
    /// <strong>The last iterate is returned even on failure, and that is deliberate.</strong> A circuit
    /// that got most of the way to a balance shows a user where it was heading; returning nothing shows
    /// them an empty canvas. What must never happen is presenting it as solved, which is what
    /// <see cref="Converged"/> is for.
    /// </value>
    public required StateVector Solution { get; init; }

    /// <summary>Gets how many Newton iterations were taken.</summary>
    public required int Iterations { get; init; }

    /// <summary>Gets the scaled infinity norm of the final residual.</summary>
    public required double ResidualNorm { get; init; }

    /// <summary>Gets why it stopped.</summary>
    public required SolveTermination Termination { get; init; }

    /// <summary>Gets the worst-offending equations, worst first.</summary>
    /// <value>
    /// Named by component and equation rather than by row index. The mapping from row to component
    /// exists only in <see cref="EquationLayout"/>, so a result that does not carry it leaves nobody
    /// downstream able to reconstruct it (<c>S-7</c>).
    /// </value>
    public required ImmutableArray<ResidualReport> WorstResiduals { get; init; }

    /// <summary>Gets everything worth telling the user, in a stable order.</summary>
    public required ImmutableArray<Diagnostics.Diagnostic> Diagnostics { get; init; }

    /// <summary>Gets the iterations, in order, one record per step taken (<c>S-71</c>).</summary>
    /// <value>Empty for a solver that does not record them. A solve that stopped before its first step has none.</value>
    public ImmutableArray<IterationRecord> History { get; init; } = [];
}
