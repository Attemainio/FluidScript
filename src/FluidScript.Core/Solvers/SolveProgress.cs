namespace FluidScript.Core.Solvers;

/// <summary>How a solve is going, reported once per iteration.</summary>
/// <param name="Iteration">The iteration just completed, from one.</param>
/// <param name="ResidualNorm">The scaled infinity norm after it.</param>
/// <param name="StepLength">The line-search factor the step was taken at.</param>
public sealed record SolveProgress(int Iteration, double ResidualNorm, double StepLength);
