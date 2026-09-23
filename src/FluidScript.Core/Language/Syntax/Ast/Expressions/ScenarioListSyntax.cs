using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>One value per declared scenario, written in brackets after <c>=</c> (<c>D-143</c>).</summary>
/// <param name="OpenBracket">The <c>[</c>.</param>
/// <param name="Elements">The values, in order, each carrying the comma before it.</param>
/// <param name="CloseBracket">The <c>]</c>.</param>
/// <remarks>
/// <para>
/// <strong>An <see cref="ExpressionSyntax"/> because <c>12</c>'s <c>parameter-value</c> is the union
/// a parameter may take, and the tree already models that union as this type.</strong> It is not an
/// expression in any other sense: nothing composes two lists, and the parser admits one only directly
/// after a parameter's <c>=</c>, never nested, which is what keeps <c>[1,2] + 3</c> from parsing into
/// something with no meaning.
/// </para>
/// <para>
/// Unambiguous with an indexed name (<c>in[2]</c>) without lookahead: an index's bracket follows an
/// identifier, and a list's follows <c>=</c>.
/// </para>
/// <para>
/// Each element is an ordinary value, so a unit suffix, an expression and a curve reference all work
/// inside one, and a dimension error is reported against the element that caused it (<c>15</c>).
/// </para>
/// </remarks>
public sealed record ScenarioListSyntax(
    Token OpenBracket,
    ImmutableArray<ArgumentSyntax> Elements,
    Token CloseBracket) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
    [
        OpenBracket,
        .. Elements.SelectMany(static element => element.Tokens),
        CloseBracket,
    ];
}
