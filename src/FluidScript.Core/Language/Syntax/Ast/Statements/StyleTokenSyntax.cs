using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>One positional token of a <c>style</c> directive.</summary>
/// <param name="Kind">Its lexical shape.</param>
/// <param name="Parts">
/// The tokens it is made of. One for every shape but a pattern, which the lexer produced as two —
/// <c>--</c>, <c>..</c>, <c>-.</c> — and which is recombined here.
/// </param>
public sealed record StyleTokenSyntax(StyleTokenKind Kind, ImmutableArray<Token> Parts) : SyntaxNode
{
    /// <summary>Gets the token as written.</summary>
    public string Text => string.Concat(Parts.Select(static part => part.Text));

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => Parts;
}
