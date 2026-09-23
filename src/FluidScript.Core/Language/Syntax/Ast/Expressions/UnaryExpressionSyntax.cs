using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A negation.</summary>
/// <param name="OperatorToken">The minus.</param>
/// <param name="Operand">What is negated.</param>
/// <remarks>
/// Unary minus is the only prefix operator. Negating an absolute temperature is an error, but that is
/// the evaluator's to say (<c>FS1302</c>) — the parser does not know what the operand denotes.
/// </remarks>
public sealed record UnaryExpressionSyntax(
    Token OperatorToken,
    ExpressionSyntax Operand) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [OperatorToken, .. Operand.Tokens];
}
