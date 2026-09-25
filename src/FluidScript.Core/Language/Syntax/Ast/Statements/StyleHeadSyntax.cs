using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>A language 2 <c>style:</c> line inside a project or circuit block (<c>D-171</c>).</summary>
/// <param name="Keyword">The word <c>style</c>, an identifier recognised by position.</param>
public sealed record StyleHeadSyntax(Token Keyword) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Keyword];
}
