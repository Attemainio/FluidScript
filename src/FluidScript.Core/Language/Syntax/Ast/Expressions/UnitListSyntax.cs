using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A language 2 list followed by one unit for all its items: <c>[85, 70] C</c>.</summary>
/// <param name="List">The bracketed list, one item per case in the order <c>cases</c> names them.</param>
/// <param name="Unit">
/// The unit's tokens as written: one identifier (<c>C</c>, <c>kW</c>), or three touching tokens for a slashed
/// unit (<c>kg</c>, <c>/</c>, <c>s</c>). The spelling is a unit symbol of <c>13</c>'s table.
/// </param>
/// <remarks>
/// The unit applies to every item that states none; an item written with its own unit keeps it. What a bare
/// item means without this suffix is the parameter's canonical unit, as for any bare number.
/// </remarks>
public sealed record UnitListSyntax(ScenarioListSyntax List, ImmutableArray<Token> Unit) : ExpressionSyntax
{
    /// <summary>Gets the unit symbol as written, its tokens joined.</summary>
    /// <value>For example <c>C</c> or <c>kg/s</c>.</value>
    public string UnitText => string.Concat(Unit.Select(static token => token.Text));

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [.. List.Tokens, .. Unit];
}
