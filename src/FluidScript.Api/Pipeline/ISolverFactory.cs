using FluidScript.Core.Solvers;

namespace FluidScript.Api.Pipeline;

/// <summary>Makes the solver one request runs; one per solve, since a solver holds per-solve state (<c>41</c>).</summary>
/// <remarks>
/// An interface rather than a registration of <see cref="ISolver"/> itself so that a test can put a
/// counting or delaying solver under the host and watch cancellation reach it.
/// </remarks>
public interface ISolverFactory
{
    /// <summary>Creates a fresh solver.</summary>
    /// <returns>The solver.</returns>
    ISolver Create();
}
