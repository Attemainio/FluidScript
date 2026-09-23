using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>One line of topology.</summary>
/// <param name="First">The first endpoint.</param>
/// <param name="Links">
/// The rest. <c>A - B - C</c> is held as a first endpoint and two links, and desugars to two
/// connections at bind time (rule I6), so the printer can reproduce the chain the user wrote.
/// </param>
/// <param name="Parameters">
/// The pipe properties the line ends in (<c>D-110</c>): empty for a bare connection. Otherwise they apply
/// to every connection on the line, and each becomes an implicit pipe at bind time (rule I7).
/// </param>
public sealed record ConnectionSyntax(
    EndpointSyntax First,
    ImmutableArray<ConnectionLinkSyntax> Links,
    ImmutableArray<ParameterSyntax> Parameters) : StatementSyntax
{
    /// <summary>Gets every endpoint on the line, in order.</summary>
    public ImmutableArray<EndpointSyntax> Endpoints =>
        [First, .. Links.Select(static link => link.Endpoint)];

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        [.. First.Tokens, .. Links.SelectMany(static link => link.Tokens), .. Parameters.SelectMany(static parameter => parameter.Tokens)];
}
