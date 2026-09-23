using FluidScript.Core.Solvers;
using FluidScript.Core.Solvers.Steady;

namespace FluidScript.Api.Pipeline;

/// <summary>The production factory: a Newton solver on the default settings.</summary>
public sealed class NewtonSolverFactory : ISolverFactory
{
    /// <inheritdoc />
    public ISolver Create() => new NewtonSolver();
}
