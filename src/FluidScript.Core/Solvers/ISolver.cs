using System.Collections.Immutable;
using FluidScript.Core.Fluids;

namespace FluidScript.Core.Solvers;

/// <summary>Why a solve stopped.</summary>
public enum SolveTermination
{
    /// <summary>Every scaled residual is inside the tolerance.</summary>
    Converged = 1,

    /// <summary>The iteration cap was reached with the residual still above tolerance (<c>FS3001</c>).</summary>
    IterationCap,

    /// <summary>The Jacobian had no unique solution (<c>FS3002</c>).</summary>
    Singular,

    /// <summary>The residual grew away from a balance (<c>FS3003</c>).</summary>
    Diverging,

    /// <summary>Steps fell below tolerance while the residual stayed above it (<c>FS3004</c>).</summary>
    Stalled,

    /// <summary>The caller cancelled between iterations (<c>FS3006</c>).</summary>
    Cancelled,

    /// <summary>A residual or an iterate was not a finite number (<c>FS3007</c>).</summary>
    NonFinite,
}

/// <summary>One equation and how far from satisfied it is.</summary>
/// <param name="OwnerComponentId">The component that contributes it.</param>
/// <param name="EquationName">Its name, as the component declared it.</param>
/// <param name="Residual">How far off, in <paramref name="ResidualSiUnit"/>.</param>
/// <param name="ResidualSiUnit">The SI unit the residual is measured in.</param>
/// <param name="ScaledResidual">The same miss divided by the row's reference magnitude.</param>
/// <remarks>
/// <strong>Both numbers, because each answers a different question.</strong> The scaled one says which
/// row is worst — that is what a norm ranks by, and comparing a pascal to a kg/s any other way is
/// meaningless. The unscaled one is what a sentence can carry: "off by 4.2 kW" is actionable and
/// "off by 0.042" is not.
/// </remarks>
public sealed record ResidualReport(
    string OwnerComponentId,
    string EquationName,
    double Residual,
    string ResidualSiUnit,
    double ScaledResidual);

/// <summary>How a solve is going, reported once per iteration.</summary>
/// <param name="Iteration">The iteration just completed, from one.</param>
/// <param name="ResidualNorm">The scaled infinity norm after it.</param>
/// <param name="StepLength">The line-search factor the step was taken at.</param>
public sealed record SolveProgress(int Iteration, double ResidualNorm, double StepLength);

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
}

/// <summary>Solves a circuit.</summary>
/// <remarks>
/// Implementations differ in the question they answer, not in how they are invoked: Newton finds the
/// state where every residual is zero, a transient solver walks a trajectory, an optimizer searches a
/// parameter space with a full solve at each point.
/// </remarks>
public interface ISolver
{
    /// <summary>Gets the name a diagnostic refers to this solver by.</summary>
    string Name { get; }

    /// <summary>Tells whether this solver can handle a system at all.</summary>
    /// <param name="system">The assembled system.</param>
    /// <returns>
    /// A reason when it cannot. Checked before solving so a user gets a sentence rather than a
    /// divergence — a steady solver refuses a system carrying time derivatives, and an explicit
    /// transient one refuses a stiffness beyond its step limit.
    /// </returns>
    Result<Unit> CanSolve(EquationSystem system);

    /// <summary>Solves, reporting progress and honouring cancellation.</summary>
    /// <param name="system">The assembled system.</param>
    /// <param name="initialGuess">
    /// The starting iterate: from sizing on a first solve, from the previous solution on a re-solve,
    /// which is what makes editing feel instant.
    /// </param>
    /// <param name="progress">Per-iteration progress, or <see langword="null"/>. Never called after the method returns.</param>
    /// <param name="cancellationToken">Honoured between iterations, never inside a residual evaluation.</param>
    /// <returns>The result, which carries an iterate whether or not it converged.</returns>
    Task<SolveResult> SolveAsync(
        EquationSystem system,
        StateVector initialGuess,
        IProgress<SolveProgress>? progress,
        CancellationToken cancellationToken);
}
