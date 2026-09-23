using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>One entry of the schedule section: a change applied at a time or over an interval.</summary>
/// <param name="Keyword">The <c>at</c> or <c>over</c> word, which is an ordinary identifier.</param>
/// <param name="When">A point for the <c>at</c> form, a range for the <c>over</c> form.</param>
/// <param name="Target">The <c>component.parameter</c> being changed.</param>
/// <param name="EqualsToken">The <c>=</c>.</param>
/// <param name="Value">A point for a step, a range for a ramp.</param>
/// <remarks>
/// All four combinations are legal. <c>over</c> with a single value ramps nothing and steps at the
/// end, which is occasionally what a user means and is cheaper to allow than to diagnose.
/// </remarks>
public sealed record DisturbanceSyntax(
    Token Keyword,
    RangeOrPointSyntax When,
    EndpointSyntax Target,
    Token EqualsToken,
    RangeOrPointSyntax Value) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        [Keyword, .. When.Tokens, .. Target.Tokens, EqualsToken, .. Value.Tokens];
}
