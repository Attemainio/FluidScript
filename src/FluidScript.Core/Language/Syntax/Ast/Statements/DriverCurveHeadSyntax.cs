using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>A language 2 curve header, <c>curve heat_demand: outdoor extrapolated</c>.</summary>
/// <param name="Keyword">The word <c>curve</c>, an identifier recognised by position.</param>
/// <param name="Name">The curve's name.</param>
/// <param name="Colon">The colon between the name and the driver.</param>
/// <param name="Driver">The driver: a <c>let</c> whose value varies per case, or <c>time</c> (<c>D-167</c>).</param>
/// <param name="Modifiers">
/// What follows the driver, in source order: the flag <c>extrapolated</c> as an <see cref="IdentifierSyntax"/>
/// and <c>format = "…"</c> as a <see cref="ParameterSyntax"/> (<c>D-60</c>).
/// </param>
/// <remarks>
/// The curve's rows are the body of the <see cref="BlockSyntax"/> this heads, each a
/// <see cref="CurveRowSyntax"/>. Language 1's header, <see cref="CurveHeaderSyntax"/>, has no colon and ends
/// its rows by a blank line; this one's rows end where their indentation does.
/// </remarks>
public sealed record DriverCurveHeadSyntax(
    Token Keyword,
    IdentifierSyntax Name,
    Token Colon,
    IdentifierSyntax Driver,
    ImmutableArray<SyntaxNode> Modifiers) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
    [
        Keyword,
        .. Name.Tokens,
        Colon,
        .. Driver.Tokens,
        .. Modifiers.SelectMany(static modifier => modifier.Tokens),
    ];
}
