namespace FluidScript.Core.Solvers;

/// <summary>One Newton iteration, as the report replays it (<c>S-71</c>).</summary>
/// <param name="Iteration">The iteration, from one.</param>
/// <param name="ResidualNorm">The scaled infinity norm <em>before</em> the step -- the residual this iteration set out to remove.</param>
/// <param name="StepLength">The line-search factor the step was taken at; 1 is a full Newton step.</param>
/// <param name="WorstEquation">The equation with the largest scaled residual before the step, as <c>owner: name</c>.</param>
/// <param name="LargestMove">The unknown the step moved most, in scaled units, as <c>name</c>.</param>
/// <param name="LargestMoveScaled">How far it moved, in that unknown's scale (1 is its own scale).</param>
/// <remarks>
/// A failed solve's diagnosis is its trajectory: where the residual stopped falling, whether the line
/// search was halving against a wall, which equation dominated at each step and which unknown moved.
/// The final norm and the iteration count, which is all a result used to carry, said none of that.
/// </remarks>
public sealed record IterationRecord(
    int Iteration,
    double ResidualNorm,
    double StepLength,
    string WorstEquation,
    string LargestMove,
    double LargestMoveScaled);
