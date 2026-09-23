using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>What a circuit carries, and how it is solved.</summary>
/// <param name="Keyword">The <c>fluid</c> word.</param>
/// <param name="ModeToken">The <c>dynamic</c> or <c>static</c> word, if one was written.</param>
/// <param name="Substance">The substance name, resolved at bind time.</param>
/// <param name="Arguments">Empty in v1; mixtures are deferred.</param>
public sealed record FluidDirectiveSyntax(
    Token Keyword,
    Token? ModeToken,
    IdentifierSyntax Substance,
    ImmutableArray<ExpressionSyntax> Arguments) : StatementSyntax
{
    /// <summary>Gets the stated solve mode.</summary>
    /// <value>
    /// <see langword="null"/> when neither word was written, which leaves the project's default to
    /// decide (<c>D-37</c>, <c>D-54</c>). It must not default to <see cref="FluidMode.Static"/>: that
    /// loses the difference between <c>fluid water</c> and <c>fluid static water</c>, which breaks the
    /// round trip and makes every circuit in a <c>project dynamic</c> file warn about a word its
    /// author never wrote.
    /// </value>
    public FluidMode? Mode => ModeToken.ToFluidMode();

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
    [
        Keyword,
        .. ModeToken is null ? ImmutableArray<Token>.Empty : [ModeToken],
        .. Substance.Tokens,
        .. Arguments.SelectMany(static argument => argument.Tokens),
    ];
}
