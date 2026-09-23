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
}
