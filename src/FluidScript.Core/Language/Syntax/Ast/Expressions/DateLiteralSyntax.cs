using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A language 2 date or clock time as a value: <c>start = 2026-01-15 06:00</c>, <c>at 06:30</c>.</summary>
/// <param name="Literal">The <see cref="TokenKind.DateLiteral"/> token.</param>
/// <remarks>The text is read as ISO 8601 by the binder (<c>D-60</c>); the parser only says where it is.</remarks>
public sealed record DateLiteralSyntax(Token Literal) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Literal];
}
