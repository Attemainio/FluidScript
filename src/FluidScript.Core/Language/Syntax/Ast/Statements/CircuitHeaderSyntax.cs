using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Begins a circuit. Every statement until the next header belongs to it.</summary>
/// <param name="Keyword">The <c>circuit</c> word.</param>
/// <param name="Name">
/// The circuit's name, which doubles as its role — resolved against the role registry at bind time
/// (<c>D-35</c>). The parser does not classify it.
/// </param>
/// <param name="Number">
/// The designation as written; <see langword="null"/> when omitted, in which case the binder resolves
/// one (<c>D-33</c>). The parser never invents a number: an absent one must stay distinguishable from
/// a written one so the printer can reproduce the source byte for byte.
/// </param>
public sealed record CircuitHeaderSyntax(
    Token Keyword,
    IdentifierSyntax Name,
    NumberLiteralSyntax? Number) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        Number is null ? [Keyword, .. Name.Tokens] : [Keyword, .. Name.Tokens, .. Number.Tokens];
}
