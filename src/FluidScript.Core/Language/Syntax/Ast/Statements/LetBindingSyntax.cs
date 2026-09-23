using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Binds a name to an expression.</summary>
/// <param name="Keyword">The <c>let</c> word.</param>
/// <param name="Name">The name being bound.</param>
/// <param name="EqualsToken">The <c>=</c>.</param>
/// <param name="Value">What it is bound to.</param>
public sealed record LetBindingSyntax(
    Token Keyword,
    IdentifierSyntax Name,
    Token EqualsToken,
    ExpressionSyntax Value) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        [Keyword, .. Name.Tokens, EqualsToken, .. Value.Tokens];
}
