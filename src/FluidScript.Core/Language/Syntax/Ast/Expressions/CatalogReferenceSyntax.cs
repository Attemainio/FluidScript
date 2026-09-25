using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A language 2 catalogue pin as a value: <c>catalog = steel_en10255@2026.1</c>.</summary>
/// <param name="Catalog">The catalogue's identifier.</param>
/// <param name="Version">The <c>@</c> and the version number, touching the identifier.</param>
/// <remarks>
/// Language 1 writes the same pin as a directive, <see cref="CatalogDirectiveSyntax"/>; language 2 makes it a
/// setting of the project block (<c>plan/10-language/19-fluidscript-2.md</c> §The project block), and a
/// setting's value is an expression. A catalogue named without a version is an ordinary
/// <see cref="ReferenceSyntax"/>.
/// </remarks>
public sealed record CatalogReferenceSyntax(IdentifierSyntax Catalog, CatalogVersionSyntax Version) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [.. Catalog.Tokens, .. Version.Tokens];
}
