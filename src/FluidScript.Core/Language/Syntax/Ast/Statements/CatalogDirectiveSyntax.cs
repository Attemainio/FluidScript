using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Selects the catalogue auto-sizing draws from.</summary>
/// <param name="Keyword">The <c>catalog</c> word.</param>
/// <param name="CatalogId">The catalogue's name.</param>
/// <param name="Version">The pinned version, or <see langword="null"/> to track the shipped one.</param>
public sealed record CatalogDirectiveSyntax(
    Token Keyword,
    IdentifierSyntax CatalogId,
    CatalogVersionSyntax? Version) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        Version is null
            ? [Keyword, .. CatalogId.Tokens]
            : [Keyword, .. CatalogId.Tokens, .. Version.Tokens];
}
