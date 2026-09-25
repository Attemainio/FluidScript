using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>A language 2 <c>project "Title":</c> line, the head of the project block.</summary>
/// <param name="Keyword">The word <c>project</c>, lexed as an identifier and recognised by position.</param>
/// <param name="Title">The quoted title; <see langword="null"/> when the line states none.</param>
public sealed record ProjectHeadSyntax(Token Keyword, Token? Title) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => Title is null ? [Keyword] : [Keyword, Title];
}
