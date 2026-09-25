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
    /// <summary>Gets the circuit's role as a language 2 file states it, <c>role = heating</c>.</summary>
    /// <value>
    /// <see langword="null"/> for language 1, whose role is read from <see cref="Name"/> (<c>D-35</c>), and for a
    /// language 2 circuit that states none. Set only by the language 2 translation, and not one of
    /// <see cref="Tokens"/>: the setting's own tokens are in the language 2 tree, which is the one printed.
    /// </value>
    public IdentifierSyntax? Role { get; init; }

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        Number is null ? [Keyword, .. Name.Tokens] : [Keyword, .. Name.Tokens, .. Number.Tokens];
}
