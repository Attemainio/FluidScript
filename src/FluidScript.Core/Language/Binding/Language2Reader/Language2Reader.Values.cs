using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Binding;

internal sealed partial class Language2Reader
{
    /// <summary>Translates a value into what language 1's evaluator reads.</summary>
    /// <remarks>
    /// One difference is resolved here: a list's trailing unit belongs to each item (<c>19</c> §Values, <c>L-75</c>).
    /// An exchanger's <c>primary</c> and <c>secondary</c> are the registry's spellings, which the lookup resolves
    /// (<c>D-179</c>).
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
    /// A bare number becomes a quantity in the unit, <c>-26</c> keeps its sign, and a bare name takes the unit as
    /// language 1's <c>heating kW</c> does. An item that states its own unit, or is an expression, is left as
    /// written: a unit after the bracket describes the bare numbers, and has no say over <c>a + 5</c>.
    /// </remarks>
    private ScenarioListSyntax WithUnit(UnitListSyntax list)
    {
        var unit = list.UnitText;

        return list.List with
        {
            Elements = [.. list.List.Elements.Select(element => element with { Value = Unit(element.Value, unit, list.Unit) })],
        };
    }

    private ExpressionSyntax Unit(ExpressionSyntax item, string unit, ImmutableArray<Token> unitTokens) => item switch
    {
        NumberLiteralSyntax number => Quantity(number, unit),
        UnaryExpressionSyntax { Operand: NumberLiteralSyntax number } negative => negative with { Operand = Quantity(number, unit) },
        ReferenceSyntax reference => new QuantityReferenceSyntax(reference, unitTokens),
        _ => Value(item),
    };

    /// <summary>A number given a unit it was not written with, keeping its own text and span.</summary>
    private static QuantityLiteralSyntax Quantity(NumberLiteralSyntax number, string unit) =>
        new(number.Token with
        {
            Kind = TokenKind.QuantityLiteral,
            Unit = unit,
            NumberText = number.Token.NumberText ?? number.Token.Text,
        });

    /// <summary>Translates <c>20..90 C</c> into language 1's range, whose ends each carry their own unit.</summary>
    /// <remarks>A unit written on the upper end only applies to both (<c>19</c> §Values): <c>30..40 min</c> is thirty minutes to forty.</remarks>
    private RangeSyntax Range(RangeExpressionSyntax range)
    {
        var (from, to) = Ends(range);
        return new RangeSyntax(from, range.DotDot, to);
    }

    /// <summary>Both ends of <c>10..100 %</c>, the upper end's unit on a bare lower end, wherever the range is written.</summary>
    private (ExpressionSyntax From, ExpressionSyntax To) Ends(RangeExpressionSyntax range)
    {
        var to = Value(range.To);
        var from = Value(range.From);

        if (to is QuantityLiteralSyntax { Token.Unit: { } unit })
        {
            from = from switch
            {
                NumberLiteralSyntax number => Quantity(number, unit),
                UnaryExpressionSyntax { Operand: NumberLiteralSyntax number } negative => negative with { Operand = Quantity(number, unit) },
                _ => from,
            };
        }

        return (from, to);
    }
}
