using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Declares a named interpolated table and opens its section (<c>D-57</c>).</summary>
/// <param name="Keyword">The <c>curve</c> word.</param>
/// <param name="Name">The curve's name, unique across the model like every other identifier.</param>
/// <param name="Driver">
/// What the curve's <c>x</c> axis is: <c>time</c>, another curve, or a registered driver role. There
/// is no preposition before it — the language has none anywhere, and the two positions are asymmetric
/// enough that the binder catches a transposition (a name that already exists is <c>FS1501</c>, and a
/// driver that does not is <c>FS1527</c>).
/// </param>
/// <param name="Modifiers">
/// Positional words after the driver. <c>extrapolated</c> is the only one v1 reads; clamping is the
/// default because it cannot invent a number beyond the data.
/// </param>
/// <param name="Arguments">Named arguments, such as <c>format=</c>.</param>
public sealed record CurveHeaderSyntax(
    Token Keyword,
    IdentifierSyntax Name,
    IdentifierSyntax? Driver,
    ImmutableArray<IdentifierSyntax> Modifiers,
    ImmutableArray<ParameterSyntax> Arguments) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
    [
        Keyword,
        .. Name.Tokens,
        .. Driver?.Tokens ?? [],
        .. Modifiers.SelectMany(static modifier => modifier.Tokens),
        .. Arguments.SelectMany(static argument => argument.Tokens),
    ];
}
