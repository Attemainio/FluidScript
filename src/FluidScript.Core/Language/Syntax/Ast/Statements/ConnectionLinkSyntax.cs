using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>One <c>- endpoint</c> step of a connection.</summary>
/// <param name="Dash">The connection operator.</param>
/// <param name="Endpoint">The endpoint after it.</param>
public sealed record ConnectionLinkSyntax(Token Dash, EndpointSyntax Endpoint) : SyntaxNode
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Dash, .. Endpoint.Tokens];
}
