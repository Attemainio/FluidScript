using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>A span between two values.</summary>
/// <param name="From">The lower end.</param>
/// <param name="DotDot">The <c>..</c>.</param>
/// <param name="To">The upper end.</param>
public sealed record RangeSyntax(
    ExpressionSyntax From,
    Token DotDot,
    ExpressionSyntax To) : RangeOrPointSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [.. From.Tokens, DotDot, .. To.Tokens];
}
