using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Names the project and sets the file-wide default solve mode.</summary>
/// <param name="Keyword">The <c>project</c> word.</param>
/// <param name="ModeToken">The <c>dynamic</c> or <c>static</c> word, if one was written.</param>
/// <param name="Name">The project name.</param>
/// <param name="Arguments">Named arguments after the name: <c>start=</c>, where a run's t = 0 sits on a time curve (<c>D-149</c>).</param>
public sealed record ProjectDirectiveSyntax(
    Token Keyword,
    Token? ModeToken,
    IdentifierSyntax Name,
    ImmutableArray<ParameterSyntax> Arguments) : StatementSyntax
{
    /// <summary>Gets the stated solve mode.</summary>
    /// <value>
    /// <see langword="null"/> when neither word was written, which leaves every circuit's own
    /// directive to decide (<c>D-37</c>).
    /// </value>
    public FluidMode? Mode => ModeToken.ToFluidMode();

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        ModeToken is null
            ? [Keyword, .. Name.Tokens, .. Arguments.SelectMany(static argument => argument.Tokens)]
            : [Keyword, ModeToken, .. Name.Tokens, .. Arguments.SelectMany(static argument => argument.Tokens)];
}
