using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>One <c>.name</c> step of a qualified reference.</summary>
/// <param name="Dot">The separator.</param>
/// <param name="Name">The name after it, with its index when it has one (<c>HX1.in[2].t</c>).</param>
public sealed record QualifiedNamePart(Token Dot, IndexedNameSyntax Name) : SyntaxNode
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Dot, .. Name.Tokens];
}
