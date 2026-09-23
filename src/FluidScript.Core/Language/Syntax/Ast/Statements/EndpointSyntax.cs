using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>One end of a connection: a component, optionally a named port.</summary>
/// <param name="Component">The component's name.</param>
/// <param name="Dot">The separator before a port, if there is one.</param>
/// <param name="Port">
/// The port — <c>b</c>, <c>in[2]</c> — or <see langword="null"/> for "the next free port, in the
/// component's declared port order" — which is what makes the reference circuits work with no port
/// names at all. A schedule target reuses the shape with a parameter here, <c>in[2].t</c>, which
/// is why the name may carry a dotted step; a connection's port never does, and the binder says so.
/// </param>
public sealed record EndpointSyntax(
    IdentifierSyntax Component,
    Token? Dot,
    QualifiedNameSyntax? Port) : SyntaxNode
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        Dot is null || Port is null
            ? Component.Tokens
            : [.. Component.Tokens, Dot, .. Port.Tokens];
}
