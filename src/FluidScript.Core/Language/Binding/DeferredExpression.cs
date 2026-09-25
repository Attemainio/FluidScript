using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

/// <summary>An expression held until sizing or solving supplies all of its inputs.</summary>
/// <param name="Expression">The expression, unevaluated.</param>
/// <param name="Target">What the expression sets.</param>
/// <param name="CurrentEstimate">
/// The value the last outer pass produced, or <see langword="null"/> when the first pass must obtain
/// the target from normal sizing before re-evaluating.
/// </param>
/// <param name="Dependencies">
/// Every <see cref="ValueId"/> the expression reads directly, including ones already static, so cycle
/// formatting and invalidation are deterministic.
/// </param>
public sealed record DeferredExpression(
    ExpressionSyntax Expression,
    ValueId Target,
    Quantity? CurrentEstimate,
    ImmutableHashSet<ValueId> Dependencies)
{
    /// <summary>Gets the text the expression was parsed from, so the outer loop can evaluate it and quote it (<c>L-59</c>).</summary>
    /// <value><see langword="null"/> only for a deferral built without one, which nothing in Core does.</value>
    public FluidScript.Core.Language.Syntax.Text.SourceText? Source { get; init; }

    /// <summary>Gets whether the expression was held only so a run can read its curve on the clock.</summary>
    /// <value>
    /// <see langword="true"/> when it reads a curve and the binder already evaluated it: a dynamic circuit's reader of a
    /// curve in language 1 (<c>D-58</c>), and every reader of a curve in language 2 (<c>19</c> §Runs). Its bound value
    /// is the design value, which the steady solve keeps; only the transient's clock reads it again (<c>D-149</c>).
    /// </value>
    public bool FollowsTheClock => CurrentEstimate is not null && Dependencies.Any(static id => id is ValueId.Curve);
}
