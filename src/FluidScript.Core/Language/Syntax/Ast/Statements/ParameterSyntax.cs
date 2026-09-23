using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>One <c>name=value</c> pair.</summary>
/// <param name="Name">The parameter name: <c>power</c>, or a port's quantity such as <c>in[2].t</c> (<c>D-120</c>).</param>
/// <param name="EqualsToken">The <c>=</c>.</param>
/// <param name="Value">
/// An expression, a reference, or a symbol — which of the three depends on the parameter's declared
/// kind, so the parser records an expression and the binder decides.
/// </param>
public sealed record ParameterSyntax(
    QualifiedNameSyntax Name,
    Token EqualsToken,
    ExpressionSyntax Value) : SyntaxNode
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [.. Name.Tokens, EqualsToken, .. Value.Tokens];
}
