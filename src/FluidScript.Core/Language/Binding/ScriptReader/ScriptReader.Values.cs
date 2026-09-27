using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Binding;

internal sealed partial class ScriptReader
{
    /// <summary>Reads a value as the evaluator reads it: a list's trailing unit, or a range's upper one, on each bare number it describes.</summary>
    /// <remarks>
    /// Nothing is respelled (<c>D-179</c>): a <c>K</c> is a difference in the unit table, an exchanger's
    /// <c>secondary.in</c> is the registry's spelling, and a shared unit is applied when the value is evaluated
    /// (<see cref="SharedUnitSyntax"/>), with no token made up to carry it (<c>L-75</c>).
    /// </remarks>
    private ExpressionSyntax Value(ExpressionSyntax value) => value switch
    {
        UnitListSyntax list => WithUnit(list),
        ScenarioListSyntax list => list with
        {
            Elements = [.. list.Elements.Select(element => element with { Value = Value(element.Value) })],
        },
        BinaryExpressionSyntax binary => binary with { Left = Value(binary.Left), Right = Value(binary.Right) },
        UnaryExpressionSyntax unary => unary with { Operand = Value(unary.Operand) },
        ParenthesizedExpressionSyntax parenthesized => parenthesized with { Inner = Value(parenthesized.Inner) },
        CallSyntax call => call with
        {
            Arguments = [.. call.Arguments.Select(argument => argument with { Value = Value(argument.Value) })],
        },
        RangeExpressionSyntax range => Ends(range) is var (from, to) ? range with { From = from, To = to } : range,
        _ => value,
    };

    /// <summary>Gives each item of <c>[85, 70] C</c> the list's unit, where the item states none.</summary>
    /// <remarks>
    /// A bare number, <c>-26</c> with its sign, takes the unit when it is evaluated (<see cref="SharedUnitSyntax"/>), and a
    /// bare name takes it as <c>heating kW</c> does, with the list's own unit tokens. An item that states its own unit,
    /// or is an expression, is left as written: a unit after the bracket describes the bare numbers, and has no say over
    /// <c>a + 5</c>.
    /// </remarks>
    private ScenarioListSyntax WithUnit(UnitListSyntax list)
    {
        var unit = list.UnitText;
        var at = TextSpan.FromBounds(list.Unit[0].Span.Start, list.Unit[^1].Span.End);

        return list.List with
        {
            Elements = [.. list.List.Elements.Select(element => element with { Value = element.Value is ReferenceSyntax reference
                ? new QuantityReferenceSyntax(reference, list.Unit)
                : Shared(Value(element.Value), unit, at) })],
        };
    }

    /// <summary>A bare number, or a negated one, reading a unit written elsewhere; anything else as it is.</summary>
    private static ExpressionSyntax Shared(ExpressionSyntax item, string unit, TextSpan at) => item switch
    {
        NumberLiteralSyntax or UnaryExpressionSyntax { Operand: NumberLiteralSyntax } => new SharedUnitSyntax(item, unit, at),
        _ => item,
    };

    /// <summary>Reads <c>20..90 C</c> as a range whose ends are evaluated one by one.</summary>
    /// <remarks>A unit written on the upper end only applies to both (<c>12</c> §Values): <c>30..40 min</c> is thirty minutes to forty.</remarks>
    private RangeSyntax Range(RangeExpressionSyntax range)
    {
        var (from, to) = Ends(range);
        return new RangeSyntax(from, range.DotDot, to);
    }

    /// <summary>Both ends of <c>10..100 %</c>, a bare lower end taking the upper end's unit, wherever the range is written.</summary>
    private (ExpressionSyntax From, ExpressionSyntax To) Ends(RangeExpressionSyntax range)
    {
        var to = Value(range.To);
        var from = Value(range.From);

        return to is QuantityLiteralSyntax { Token.Unit: { } unit } upper
            ? (Shared(from, unit, upper.Span), to)
            : (from, to);
    }
}
