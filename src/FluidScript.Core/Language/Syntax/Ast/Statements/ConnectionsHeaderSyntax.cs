using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Begins a circuit's topology.</summary>
/// <param name="Keyword">The <c>connections</c> word.</param>
public sealed record ConnectionsHeaderSyntax(Token Keyword) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Keyword];
}
