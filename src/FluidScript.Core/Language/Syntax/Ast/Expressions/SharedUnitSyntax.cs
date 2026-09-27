using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A bare number that takes a unit written once for several values: <c>85</c> in <c>[85, 70] C</c>, <c>30</c> in <c>30..40 min</c>.</summary>
/// <param name="Operand">The number as written, or its negation: <c>85</c>, <c>-26</c>.</param>
/// <param name="Unit">The unit written after the list, or on the range's upper end: <c>C</c>, <c>min</c>.</param>
/// <param name="UnitSpan">Where that unit is written.</param>
/// <remarks>
/// <para>
/// The reader (<c>ScriptReader</c>) makes this node; the parser never does. A list's trailing unit belongs to each bare item and
/// a range's upper unit to a bare lower end (<c>12</c> §Values), and the evaluator applies it when the value is
/// evaluated (<c>D-179</c>) -- so the item stays what the author wrote, with no token made up to carry a unit it was
/// not written with (<c>L-75</c>).
/// </para>
/// <para>
/// Its tokens and span are the operand's: a message about the item points at the number, and the unit written
/// elsewhere is <paramref name="UnitSpan"/>.
/// </para>
/// </remarks>
public sealed record SharedUnitSyntax(ExpressionSyntax Operand, string Unit, TextSpan UnitSpan) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => Operand.Tokens;
}
