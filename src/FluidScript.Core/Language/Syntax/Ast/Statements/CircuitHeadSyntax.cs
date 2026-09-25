using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>A language 2 <c>circuit "Title":</c> line, the head of a circuit block.</summary>
/// <param name="Keyword">The word <c>circuit</c>, lexed as an identifier and recognised by position.</param>
/// <param name="Title">The quoted title, never a reference; <see langword="null"/> when the line states none.</param>
public sealed record CircuitHeadSyntax(Token Keyword, Token? Title) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => Title is null ? [Keyword] : [Keyword, Title];
}
