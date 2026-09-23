using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A reference followed by a unit symbol: <c>heating kW</c>, <c>demand kg/s</c> (<c>L-35</c>, <c>D-57</c>).</summary>
/// <param name="Reference">The value referenced.</param>
/// <param name="UnitTokens">The unit as written: one identifier, or an identifier, a slash and an identifier written together.</param>
/// <remarks>
/// The form exists for a curve, whose numbers are bare (<c>D-57</c>): <c>power=heating</c> reads them
/// in the parameter's canonical unit (<c>D-14</c>) and <c>power=heating W</c> in watts. The parser
/// takes the unit only where an identifier could not otherwise follow a reference inside an expression
/// -- never before <c>=</c>, <c>[</c> or a dotted name, which is how <c>power=heating dp=20</c> keeps
/// <c>dp</c> as the next parameter -- and only when the text is a spelling the unit table holds.
/// </remarks>
public sealed record QuantityReferenceSyntax(
    ReferenceSyntax Reference,
    ImmutableArray<Token> UnitTokens) : ExpressionSyntax
{
    /// <summary>Gets the unit symbol as written, the tokens joined: <c>kW</c>, <c>kg/s</c>.</summary>
    public string Unit => string.Concat(UnitTokens.Select(static token => token.Text));

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [.. Reference.Tokens, .. UnitTokens];
}
