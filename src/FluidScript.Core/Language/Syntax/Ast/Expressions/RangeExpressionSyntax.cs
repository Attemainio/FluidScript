using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A language 2 range as a value: <c>output = 10..100 %</c>, <c>scale = 20..90 C</c>.</summary>
/// <param name="From">The lower end as written.</param>
/// <param name="DotDot">The <c>..</c>.</param>
/// <param name="To">The upper end as written.</param>
/// <remarks>
/// A unit written on the upper end only applies to both ends (<c>19</c> §Values): <c>30..40 min</c> is thirty
/// minutes to forty. Language 1 keeps ranges out of expressions (<see cref="Statements.RangeSyntax"/>), which
/// is why this is a separate node rather than that one.
/// </remarks>
public sealed record RangeExpressionSyntax(ExpressionSyntax From, Token DotDot, ExpressionSyntax To) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [.. From.Tokens, DotDot, .. To.Tokens];
}
