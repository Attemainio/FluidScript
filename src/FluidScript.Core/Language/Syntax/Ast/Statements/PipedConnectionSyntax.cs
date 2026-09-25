using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>A language 2 connection line whose link is a pipe: <c>PCV - HX1  12 m  DN25</c>.</summary>
/// <param name="Connection">The chain, with no parameters of its own.</param>
/// <param name="Properties">
/// The pipe's description after the chain, in source order and never empty: a length as a
/// <see cref="QuantityLiteralSyntax"/>, a designation such as <c>DN25</c> as an <see cref="IdentifierSyntax"/>,
/// and any other property as a <see cref="ParameterSyntax"/> (<c>roughness = 0.05 mm</c>).
/// </param>
/// <remarks>
/// <c>D-166</c>: the length and the designation need no name, because each is known by its form. They are
/// allowed on a line with one link only; on a longer chain the parser reports <c>FS1803</c> and keeps the
/// line, so the chain still binds.
/// </remarks>
public sealed record PipedConnectionSyntax(
    ConnectionSyntax Connection,
    ImmutableArray<SyntaxNode> Properties) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        [.. Connection.Tokens, .. Properties.SelectMany(static property => property.Tokens)];
}
