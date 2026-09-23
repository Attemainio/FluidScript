using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Where a subcircuit meets its parent.</summary>
/// <param name="Keyword">The <c>supply</c> or <c>return</c> word.</param>
/// <param name="Endpoint">
/// A node of the parent circuit. Whether it exists is a binder question, not a parser one.
/// </param>
public sealed record AttachmentSyntax(Token Keyword, EndpointSyntax Endpoint) : StatementSyntax
{
    /// <summary>Gets which side this declares.</summary>
    public AttachmentDirection Direction =>
        Keyword.Keyword == ReservedWord.Inlet ? AttachmentDirection.Inlet : AttachmentDirection.Outlet;

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Keyword, .. Endpoint.Tokens];
}
