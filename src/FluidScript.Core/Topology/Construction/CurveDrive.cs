using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Topology.Construction;

/// <summary>A component parameter that follows a curve of time during a run (<c>D-149</c>).</summary>
/// <param name="Component">The component whose parameter follows the clock.</param>
/// <param name="Parameter">The parameter's key, as the registry resolves it: <c>power</c>, <c>position</c>.</param>
/// <param name="Expression">What the script wrote for it, read again at every evaluation time.</param>
/// <param name="Expected">The parameter's dimension, so a curve's bare <c>y</c> lands in its canonical unit (<c>D-14</c>).</param>
/// <param name="WrittenKind">The kind as the script spelled it, whose role word carries a duty's sign.</param>
/// <remarks>
/// The run applies it as it applies a <see cref="ScheduledChange"/>: through <c>EquationSystem.Schedule</c>
/// at every evaluation time, so <c>FS3105</c> and <c>FS3109</c> refuse the same targets for both.
/// </remarks>
public sealed record CurveDrive(
    string Component,
    string Parameter,
    ExpressionSyntax Expression,
    Dimension? Expected,
    string WrittenKind);
