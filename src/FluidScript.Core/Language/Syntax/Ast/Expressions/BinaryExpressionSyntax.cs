using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>An arithmetic operator applied to two operands.</summary>
/// <param name="Left">The left operand.</param>
/// <param name="OperatorToken">The operator as written.</param>
/// <param name="Right">The right operand.</param>
public sealed record BinaryExpressionSyntax(
    ExpressionSyntax Left,
    Token OperatorToken,
    ExpressionSyntax Right) : ExpressionSyntax
{
    /// <summary>Gets which operator this is.</summary>
    public BinaryOperator Operator => OperatorToken.Kind switch
    {
        TokenKind.Star => BinaryOperator.Multiply,
        TokenKind.Slash => BinaryOperator.Divide,
        TokenKind.Plus => BinaryOperator.Add,
        _ => BinaryOperator.Subtract,
    };

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [.. Left.Tokens, OperatorToken, .. Right.Tokens];
}
