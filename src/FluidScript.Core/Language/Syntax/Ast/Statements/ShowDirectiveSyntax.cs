using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Which fluid properties drive the canvas colour scale.</summary>
/// <param name="Keyword">The <c>show</c> word.</param>
/// <param name="Properties">The property names, resolved against the property registry at bind time.</param>
/// <param name="Scale">An explicit range for the scale, or <see langword="null"/> to derive one.</param>
public sealed record ShowDirectiveSyntax(
    Token Keyword,
    ImmutableArray<IdentifierSyntax> Properties,
    RangeSyntax? Scale) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
    [
        Keyword,
        .. Properties.SelectMany(static property => property.Tokens),
        .. Scale?.Tokens ?? [],
    ];
}
