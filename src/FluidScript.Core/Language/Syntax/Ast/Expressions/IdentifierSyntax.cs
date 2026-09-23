using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A bare identifier: a component name, a kind name, a parameter name, a circuit name.</summary>
/// <param name="Token">The word as written.</param>
public sealed record IdentifierSyntax(Token Token) : SyntaxNode
{
    /// <summary>Gets the spelling exactly as written.</summary>
    /// <value>
    /// Normalisation for kind and parameter resolution happens at bind time (<c>D-15</c>) and never
    /// rewrites this.
    /// </value>
    public string Text => Token.Text;

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Token];
}
