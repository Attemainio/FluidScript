using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>An expression the user wrapped in parentheses.</summary>
/// <param name="OpenParen">The opening parenthesis.</param>
/// <param name="Inner">The expression inside.</param>
/// <param name="CloseParen">The closing parenthesis.</param>
/// <remarks>
/// Kept rather than re-derived from precedence when printing (<c>D-54</c>). <c>(a + b) * c</c> and
/// <c>a + b * c</c> differ, and a redundant grouping in an engineering formula is usually deliberate.
/// </remarks>
public sealed record ParenthesizedExpressionSyntax(
    Token OpenParen,
    ExpressionSyntax Inner,
    Token CloseParen) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [OpenParen, .. Inner.Tokens, CloseParen];
}
