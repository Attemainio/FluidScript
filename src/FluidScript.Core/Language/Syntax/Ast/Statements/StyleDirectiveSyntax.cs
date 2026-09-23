using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Presentation for the statements that follow, or a named style's definition (<c>D-104</c>).</summary>
/// <param name="Keyword">The <c>style</c> word.</param>
/// <param name="Name">The style's name when the directive defines one; <see langword="null"/> otherwise.</param>
/// <param name="Assign">The <c>=</c> after the name of a definition; <see langword="null"/> otherwise.</param>
/// <param name="Parts">The style tokens, in source order. Order carries no meaning.</param>
public sealed record StyleDirectiveSyntax(
    Token Keyword,
    Token? Name,
    Token? Assign,
    ImmutableArray<StyleTokenSyntax> Parts) : StatementSyntax
{
    /// <summary>Gets whether this is <c>style name = tokens</c> rather than an application.</summary>
    public bool IsDefinition => Name is not null;

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        Name is { } name
            ? [Keyword, name, Assign!, .. Parts.SelectMany(static part => part.Tokens)]
            : [Keyword, .. Parts.SelectMany(static part => part.Tokens)];
}
