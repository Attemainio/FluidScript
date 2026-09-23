using FluidScript.Core.Primitives;
using FluidScript.Core.Solvers.Equations;

namespace FluidScript.Core.Solvers;

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
