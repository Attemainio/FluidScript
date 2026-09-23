using System.Collections.Immutable;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Syntax.Parsing;

internal sealed partial class LineParser
{
    private PointSyntax? ParsePoint()
    {
        var value = ParseExpression();
        return value is null ? null : new PointSyntax(value);
    }

    private RangeSyntax? ParseRange()
    {
        var from = ParseExpression();
        if (from is null || Current is not { Kind: TokenKind.DotDot })
        {
            return null;
        }

        var dots = Advance();
        var to = ParseExpression();
        return to is null ? null : new RangeSyntax(from, dots, to);
    }

    private RangeOrPointSyntax? ParsePointOrRange()
    {
        var from = ParseExpression();
        if (from is null)
        {
            return null;
        }

        if (Current is not { Kind: TokenKind.DotDot })
        {
            return new PointSyntax(from);
        }

        var dots = Advance();
        var to = ParseExpression();
        return to is null ? null : new RangeSyntax(from, dots, to);
    }

    // ---- expressions ------------------------------------------------------------------------

    private ExpressionSyntax? ParseExpression() => ParseAdditive();

    private ExpressionSyntax? ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (left is not null && Current is { Kind: TokenKind.Plus or TokenKind.Minus })
        {
            var op = Advance();
            var right = ParseMultiplicative();
            if (right is null)
            {
                return null;
            }

            left = new BinaryExpressionSyntax(left, op, right);
        }

        return left;
    }

    private ExpressionSyntax? ParseMultiplicative()
    {
        var left = ParseUnary();
        while (left is not null && Current is { Kind: TokenKind.Star or TokenKind.Slash })
        {
            var op = Advance();
            var right = ParseUnary();
            if (right is null)
            {
                return null;
            }

            left = new BinaryExpressionSyntax(left, op, right);
        }

        return left;
    }

    private ExpressionSyntax? ParseUnary()
    {
        if (Current is not { Kind: TokenKind.Minus })
        {
            return ParsePrimary();
        }

        var op = Advance();
        var operand = Nested(static parser => parser.ParseUnary());
        return operand is null ? null : new UnaryExpressionSyntax(op, operand);
    }

    /// <summary>Runs one nested expression parse, or yields <c>null</c> when the nesting bound is reached.</summary>
    /// <remarks>The parser never throws, so the depth is restored on every return path without a <c>finally</c>.</remarks>
    private T? Nested<T>(Func<LineParser, T?> parse)
        where T : class
    {
        if (_depth >= MaxExpressionDepth)
        {
            return null;
        }

        _depth++;
        var result = parse(this);
        _depth--;
        return result;
    }

    private ExpressionSyntax? ParsePrimary()
    {
        if (Current is not { } token)
        {
            return null;
        }

        switch (token.Kind)
        {
            case TokenKind.NumberLiteral:
                return new NumberLiteralSyntax(Advance());

            case TokenKind.QuantityLiteral:
                return new QuantityLiteralSyntax(Advance());

            case TokenKind.StringLiteral:
                return new StringLiteralSyntax(Advance());

            case TokenKind.OpenParenthesis:
            {
                var open = Advance();
                var inner = Nested(static parser => parser.ParseExpression());
                if (inner is null || Current is not { Kind: TokenKind.CloseParenthesis })
                {
                    return null;
                }

                return new ParenthesizedExpressionSyntax(open, inner, Advance());
            }

            case TokenKind.Identifier:
            {
                var name = new IdentifierSyntax(Advance());
                if (Current is { Kind: TokenKind.OpenParenthesis })
                {
                    return ParseCall(name);
                }

                return ParseReference(name) is { } reference ? WithUnit(reference) : null;
            }

            default:
                return null;
        }
    }

    /// <summary>Takes a unit symbol written after a reference, where one follows (<c>L-35</c>): <c>heating kW</c>, <c>demand kg/s</c>.</summary>
    /// <param name="reference">The reference just parsed.</param>
    /// <returns>The reference with its unit, or the reference alone.</returns>
    /// <remarks>
    /// The lexer's rule 5 for a unit after a number, applied to a reference: the identifier is a spelling
    /// the unit table holds and is not the start of the next parameter (<c>=</c>, <c>[</c>, or a <c>.</c>
    /// before a word follows it). A slashed unit is three tokens written together, <c>kg/s</c>, and is
    /// taken only when the three touch and the joined text is a spelling.
    /// </remarks>
    private ExpressionSyntax WithUnit(ReferenceSyntax reference)
    {
        if (Current is not { Kind: TokenKind.Identifier } first || StartsNextParameter(_index + 1))
        {
            return reference;
        }

        if (tokens.ElementAtOrDefault(_index + 1) is { Kind: TokenKind.Slash } slash
            && tokens.ElementAtOrDefault(_index + 2) is { Kind: TokenKind.Identifier } second
            && slash.Span.Start == first.Span.End
            && second.Span.Start == slash.Span.End
            && !StartsNextParameter(_index + 3)
            && UnitTable.IsSymbol(first.Text + slash.Text + second.Text))
        {
            return new QuantityReferenceSyntax(reference, [Advance(), Advance(), Advance()]);
        }

        return UnitTable.IsSymbol(first.Text)
            ? new QuantityReferenceSyntax(reference, [Advance()])
            : reference;
    }

    /// <summary>Whether the token at an index begins a parameter rather than continuing an expression: <c>=</c>, <c>[</c>, or <c>.</c> before a word.</summary>
    private bool StartsNextParameter(int index) =>
        tokens.ElementAtOrDefault(index) is { } next
        && (next.Kind is TokenKind.Equals or TokenKind.OpenBracket
            || (next.Kind == TokenKind.Dot && tokens.ElementAtOrDefault(index + 1) is { Kind: TokenKind.Identifier }));

    private CallSyntax? ParseCall(IdentifierSyntax name)
    {
        var open = Advance();
        var arguments = ImmutableArray.CreateBuilder<ArgumentSyntax>();

        while (Current is not null and not { Kind: TokenKind.CloseParenthesis })
        {
            Token? comma = null;
            if (arguments.Count > 0)
            {
                if (Current is not { Kind: TokenKind.Comma })
                {
                    return null;
                }

                comma = Advance();
            }

            var value = Nested(static parser => parser.ParseExpression());
            if (value is null)
            {
                return null;
            }

            arguments.Add(new ArgumentSyntax(comma, value));
        }

        return Current is { Kind: TokenKind.CloseParenthesis }
            ? new CallSyntax(name, open, arguments.ToImmutable(), Advance())
            : null;
    }

    private ReferenceSyntax? ParseReference(IdentifierSyntax head)
    {
        var parts = ImmutableArray.CreateBuilder<QualifiedNamePart>();

        while (Current is { Kind: TokenKind.Dot }
               && tokens.ElementAtOrDefault(_index + 1) is { Kind: TokenKind.Identifier })
        {
            var dot = Advance();
            var part = TakeIndexedName();
            if (part is null)
            {
                // `FS1119` is reported; the line becomes malformed, which is what keeps the consumed
                // tokens in the tree (losslessness) and what makes the reference one diagnostic.
                return null;
            }

            parts.Add(new QualifiedNamePart(dot, part));
        }

        return new ReferenceSyntax(head, parts.ToImmutable());
    }
}
