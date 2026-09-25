using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Translation;

internal sealed partial class TranslationRun
{
    /// <summary>Translates a value into what language 1's evaluator reads.</summary>
    /// <remarks>
    /// Three differences, each resolved here so the evaluator stays language 1's: a bare <c>K</c> is a difference
    /// (<c>D-172</c>), a list's trailing unit belongs to each item (<c>19</c> §Values), and an exchanger's
    /// <c>primary</c> and <c>secondary</c> are language 1's first and second side.
    /// </remarks>
    private ExpressionSyntax Value(ExpressionSyntax value) => value switch
    {
        QuantityLiteralSyntax quantity => Kelvin(quantity),
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
        ReferenceSyntax reference => Reference(reference),
        RangeExpressionSyntax range => range with { From = Value(range.From), To = Value(range.To) },
        _ => value,
    };

    /// <summary>Reads a bare <c>K</c> as a temperature difference (<c>D-172</c>).</summary>
    /// <remarks>
    /// The token keeps its text, <c>20 K</c>, so a message quotes what was written; only the unit the evaluator
    /// looks up changes, to language 1's <c>dK</c>. A compound unit that holds a <c>K</c> is another spelling and
    /// is left alone.
    /// </remarks>
    private static QuantityLiteralSyntax Kelvin(QuantityLiteralSyntax quantity) =>
        quantity.Token.Unit == "K" ? new QuantityLiteralSyntax(quantity.Token with { Unit = "dK" }) : quantity;

    private static string Language1Unit(string unit) => unit == "K" ? "dK" : unit;

    /// <summary>Gives each item of <c>[85, 70] C</c> the list's unit, where the item states none.</summary>
    /// <remarks>
    /// A bare number becomes a quantity in the unit, <c>-26</c> keeps its sign, and a bare name takes the unit as
    /// language 1's <c>heating kW</c> does. An item that states its own unit, or is an expression, is left as
    /// written: a unit after the bracket describes the bare numbers, and has no say over <c>a + 5</c>.
    /// </remarks>
    private ScenarioListSyntax WithUnit(UnitListSyntax list)
    {
        var unit = Language1Unit(list.UnitText);

        return list.List with
        {
            Elements = [.. list.List.Elements.Select(element => element with { Value = Unit(element.Value, unit, list.Unit) })],
        };
    }

    private ExpressionSyntax Unit(ExpressionSyntax item, string unit, ImmutableArray<Token> unitTokens) => item switch
    {
        NumberLiteralSyntax number => Quantity(number, unit),
        UnaryExpressionSyntax { Operand: NumberLiteralSyntax number } negative => negative with { Operand = Quantity(number, unit) },
        ReferenceSyntax reference => new QuantityReferenceSyntax(Reference(reference), unitTokens),
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

        return new RangeSyntax(from, range.DotDot, to);
    }

    /// <summary>A reference with an exchanger's side written as language 1's port: <c>HX1.secondary.out.t</c> is <c>HX1.out[2].t</c>.</summary>
    private static ReferenceSyntax Reference(ReferenceSyntax reference)
    {
        if (reference.Parts.Length < 2 || Side(reference.Parts[0].Name) is not { } side)
        {
            return reference;
        }

        var port = SidePort(reference.Parts[1].Name, side);
        return port is null
            ? reference
            : reference with { Parts = [reference.Parts[1] with { Name = port }, .. reference.Parts[2..]] };
    }

    /// <summary>A parameter or port name with an exchanger's side written as language 1's port: <c>secondary.in.t</c> is <c>in[2].t</c>.</summary>
    private static QualifiedNameSyntax PortName(QualifiedNameSyntax name)
    {
        if (name.Parts.IsDefaultOrEmpty || Side(name.Head) is not { } side)
        {
            return name;
        }

        var port = SidePort(name.Parts[0].Name, side);
        return port is null ? name : new QualifiedNameSyntax(port, name.Parts[1..]);
    }

    /// <summary>Which side a name spells: 1 for <c>primary</c>, 2 for <c>secondary</c>, or none.</summary>
    private static int? Side(IndexedNameSyntax name)
    {
        if (name.Index is not null)
        {
            return null;
        }

        return NameResolution.Normalize(name.Name.Text) switch
        {
            "primary" => 1,
            "secondary" => 2,
            _ => null,
        };
    }

    /// <summary>The port <c>in</c> or <c>out</c> of a side: itself on the first side, indexed <c>[2]</c> on the second.</summary>
    /// <returns>The port, or <see langword="null"/> when the name is not a bare <c>in</c> or <c>out</c>.</returns>
    private static IndexedNameSyntax? SidePort(IndexedNameSyntax port, int side)
    {
        if (port.Index is not null || port.Name.Text is not ("in" or "out"))
        {
            return null;
        }

        if (side == 1)
        {
            return port;
        }

        var at = port.Span.End;
        var two = new Token
        {
            Kind = TokenKind.NumberLiteral,
            Text = "2",
            NumberText = "2",
            Value = 2,
            Span = new TextSpan(at, 0),
        };

        return port with
        {
            Index = new IndexSyntax(Made(TokenKind.OpenBracket, "[", new TextSpan(at, 0)), two, Made(TokenKind.CloseBracket, "]", new TextSpan(at, 0))),
        };
    }
}
