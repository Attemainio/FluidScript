using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Language.Syntax.Parsing;

internal sealed partial class LineParser
{
    private const string AtTopLevel = "at the top level, not indented under a block";

    private StatementSyntax ParseLanguage2Version(Language2Block context)
    {
        Place(context == Language2Block.TopLevel, "The version line", AtTopLevel);
        return ParseVersion();
    }

    /// <summary>Parses a <c>project</c>, <c>circuit</c> or <c>run</c> head: the word, an optional quoted title, and <c>:</c>.</summary>
    private StatementSyntax ParseTitledHead(Language2Block context, Language2Block kind)
    {
        var keyword = Current!;

        // `run pump` is a component named after a statement word (`19`).
        if (tokens.ElementAtOrDefault(1) is { Kind: not (TokenKind.StringLiteral or TokenKind.Colon) })
        {
            return StatementWordAsName(keyword);
        }

        Advance();
        Opens = kind;
        Place(context == Language2Block.TopLevel, $"A '{keyword.Text}' block", AtTopLevel);

        var title = Current is { Kind: TokenKind.StringLiteral } ? Advance() : null;
        TakeOpeningColon(keyword.Text);

        return kind switch
        {
            Language2Block.Project => new ProjectHeadSyntax(keyword, title),
            Language2Block.Circuit => new CircuitHeadSyntax(keyword, title),
            _ => new RunHeadSyntax(keyword, title),
        };
    }

    private StyleHeadSyntax ParseStyleHead(Language2Block context)
    {
        var keyword = Advance();
        Opens = Language2Block.Style;
        Place(
            context is Language2Block.Project or Language2Block.Circuit,
            "A 'style:' block",
            "inside the project block or a circuit's");

        TakeOpeningColon("style");
        return new StyleHeadSyntax(keyword);
    }

    /// <summary>Takes the <c>:</c> that ends a head line, or reports <c>FS1812</c> when the line ends without one.</summary>
    /// <param name="head">The head's word, for the message.</param>
    /// <remarks>A colon followed by more text is left for the caller, whose line then ends in <c>FS1114</c>.</remarks>
    private void TakeOpeningColon(string head)
    {
        if (Current is { Kind: TokenKind.Colon } && _index == tokens.Length - 1)
        {
            OpeningColon = Advance();
            return;
        }

        if (AtEnd)
        {
            Report(Language2Diagnostics.HeadWithoutColon, LineSpan, new DiagnosticArgument("head", head));
        }
    }

    private StatementSyntax ParseLanguage2Let(Language2Block context)
    {
        Place(context == Language2Block.TopLevel, "A 'let' line", AtTopLevel);

        var keyword = Advance();
        var name = TakeIdentifier();
        if (name is null || Current is not { Kind: TokenKind.Equals })
        {
            return Malformed();
        }

        var equals = Advance();
        var value = ParseLanguage2Value();
        return value is null ? Malformed() : new LetBindingSyntax(keyword, name, equals, value);
    }

    /// <summary>Parses <c>curve NAME: DRIVER</c> and what follows the driver (<c>D-167</c>).</summary>
    /// <remarks>
    /// The block opens before anything can fail, so the rows under a malformed header are still read as rows
    /// rather than each reported for being outside a curve.
    /// </remarks>
    private StatementSyntax ParseDriverCurveHead(Language2Block context)
    {
        Opens = Language2Block.Curve;
        Place(context == Language2Block.TopLevel, "A curve", AtTopLevel);

        var keyword = Advance();
        var name = TakeIdentifier();

        // `curve heating` and `curve heating:` both lack what the curve depends on, which is the thing to say.
        if (name is not null && (AtEnd || (Current is { Kind: TokenKind.Colon } && _index == tokens.Length - 1)))
        {
            return Fail(ParserDiagnostics.CurveWithoutDriver, LineSpan, new DiagnosticArgument("name", name.Text));
        }

        if (name is null || Current is not { Kind: TokenKind.Colon })
        {
            return Malformed();
        }

        var colon = Advance();
        var driver = TakeIdentifier();
        if (driver is null)
        {
            return Malformed();
        }

        // `extrapolated` is a flag and `format = "…"` a pair (`D-60`); both follow the driver in any order.
        var modifiers = ImmutableArray.CreateBuilder<SyntaxNode>();
        while (Current is { Kind: TokenKind.Identifier } word)
        {
            if (!StartsParameter(_index))
            {
                modifiers.Add(new IdentifierSyntax(Advance()));
                continue;
            }

            var parameter = ParseParameter(word);
            if (parameter is null)
            {
                return Malformed();
            }

            modifiers.Add(parameter);
        }

        return new DriverCurveHeadSyntax(keyword, name, colon, driver, modifiers.ToImmutable());
    }

    /// <summary>Parses a line of <c>name = value</c> pairs: a block's setting, a component's parameter line, or a run's override.</summary>
    private StatementSyntax ParseSettingLine(Language2Block context)
    {
        Place(
            context != Language2Block.TopLevel,
            "A setting",
            "inside the block it sets: a project, a circuit, a run, a style or a component");

        // `colour = #2f6f9f`: the `#` began a comment, so the value is missing; say so as language 1's style line does,
        // before the parameters are read, whose failure would add FS1104 for the same line.
        if (tokens[^1] is { Kind: TokenKind.Equals } equals && HexComment(equals) is { } hex)
        {
            return Fail(ParserDiagnostics.BareHexColour, LineSpan, new DiagnosticArgument("hex", hex));
        }

        var assignments = ParseParameters(out var failed);

        return failed || assignments.IsEmpty ? Malformed() : new SettingLineSyntax(assignments);
    }

    /// <summary>Parses <c>NAME kind</c>, its parameters, and the <c>:</c> that makes it a block's head.</summary>
    private StatementSyntax ParseLanguage2Declaration(Language2Block context)
    {
        Place(context == Language2Block.Circuit, "A component declaration", "inside a circuit block");

        var name = TakeIdentifier();
        if (name is null)
        {
            return Malformed();
        }

        if (Current is not { Kind: TokenKind.Identifier })
        {
            if (TryReadHyphenated(out var written, out var underscored))
            {
                return Fail(
                    ParserDiagnostics.HyphenInName,
                    LineSpan,
                    new DiagnosticArgument("text", written),
                    new DiagnosticArgument("underscored", underscored));
            }

            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        var kind = new IdentifierSyntax(Advance());

        // `TE1 temperature_sensor at N2` places an observer on a node, as in language 1 (`D-61`).
        Token? atKeyword = null;
        IdentifierSyntax? attachedTo = null;

        if (Current is { Kind: TokenKind.Identifier, Text: "at" }
            && tokens.ElementAtOrDefault(_index + 1) is { Kind: TokenKind.Identifier })
        {
            atKeyword = Advance();
            attachedTo = TakeIdentifier();
            if (attachedTo is null)
            {
                return Malformed();
            }
        }

        var parameters = ParseParameters(out var failed);
        if (failed)
        {
            return Malformed();
        }

        if (Current is { Kind: TokenKind.Colon } && _index == tokens.Length - 1)
        {
            OpeningColon = Advance();
            Opens = Language2Block.Declaration;
        }

        return new ComponentDeclarationSyntax(name, kind, atKeyword, attachedTo, parameters, null, []);
    }

    /// <summary>Parses a chain, <c>A - B - C</c>, and the pipe written after a one-link chain (<c>D-166</c>).</summary>
    /// <remarks>
    /// The pipe's description is recognised by form: a quantity is its length, a bare word its designation
    /// (<c>DN25</c>, checked against the catalogue by the binder), and <c>name = value</c> any other property.
    /// On a longer chain it is <c>FS1803</c> and the line is kept, so the chain still binds.
    /// </remarks>
    private StatementSyntax ParseChain(Language2Block context)
    {
        Place(context == Language2Block.Circuit, "A connection line", "inside a circuit block");

        // Where the endpoint before the latest dash began: `N1 - HX-1 - N2` fails at `1`, and the name is `HX-1`.
        var previous = _index;
        var first = TakeEndpoint();
        if (first is null)
        {
            return Malformed();
        }

        var links = ImmutableArray.CreateBuilder<ConnectionLinkSyntax>();
        while (Current is { Kind: TokenKind.Minus })
        {
            var dash = Advance();
            var next = _index;
            var endpoint = TakeEndpoint();
            if (endpoint is null)
            {
                return HyphenatedOrMalformed(previous);
            }

            links.Add(new ConnectionLinkSyntax(dash, endpoint));
            previous = next;
        }

        var properties = ImmutableArray.CreateBuilder<SyntaxNode>();
        while (Current is { } token)
        {
            if (token.Kind == TokenKind.QuantityLiteral)
            {
                properties.Add(new QuantityLiteralSyntax(Advance()));
            }
            else if (token.Kind == TokenKind.Identifier && StartsParameter(_index))
            {
                var parameter = ParseParameter(token);
                if (parameter is null)
                {
                    return Malformed();
                }

                properties.Add(parameter);
            }
            else if (token.Kind == TokenKind.Identifier)
            {
                properties.Add(new IdentifierSyntax(Advance()));
            }
            else
            {
                break;
            }
        }

        var connection = new ConnectionSyntax(first, links.ToImmutable(), []);
        if (properties.Count == 0)
        {
            return connection;
        }

        if (links.Count > 1)
        {
            var described = TextSpan.FromBounds(properties[0].Span.Start, properties[^1].Span.End);
            Report(
                Language2Diagnostics.PipeOnAChain,
                described,
                new DiagnosticArgument("links", links.Count.ToString(CultureInfo.InvariantCulture) + " links"),
                new DiagnosticArgument("first", source.ToString(first.Span)),
                new DiagnosticArgument("second", source.ToString(links[0].Endpoint.Span)),
                new DiagnosticArgument("properties", source.ToString(described)));
        }

        return new PipedConnectionSyntax(connection, properties.ToImmutable());
    }

    /// <summary>
    /// A line that read as a chain and failed: <c>HX-1 pump</c>, or <c>N1 - HX-1 - N2</c>, has a name with a hyphen in
    /// it, which <c>FS1108</c> names.
    /// </summary>
    /// <param name="start">Where the endpoint before the failing dash began.</param>
    /// <remarks>
    /// Only after the chain fails, since <c>N1-N2</c> written without spaces is a connection and reads as one.
    /// </remarks>
    private MalformedStatementSyntax HyphenatedOrMalformed(int start)
    {
        _index = start;
        return TryReadHyphenated(out var written, out var underscored)
            ? Fail(
                ParserDiagnostics.HyphenInName,
                LineSpan,
                new DiagnosticArgument("text", written),
                new DiagnosticArgument("underscored", underscored))
            : Malformed();
    }

    /// <summary>Parses an event, <c>at T target = value</c> or <c>over T1..T2 target = v1..v2</c>.</summary>
    /// <remarks>
    /// Built on language 1's <see cref="DisturbanceSyntax"/>. A ramp needs both ends of both spans
    /// (<c>FS1807</c>), where language 1 reads a single value as a step at the span's end; the line is kept,
    /// so the target still resolves.
    /// </remarks>
    private StatementSyntax ParseEvent(Language2Block context)
    {
        Place(context == Language2Block.Run, "An event", "inside a run block");

        var keyword = Advance();
        var when = ParsePointOrRange();
        var target = when is null ? null : TakeEndpoint();
        if (target is null || Current is not { Kind: TokenKind.Equals })
        {
            return Malformed();
        }

        var equals = Advance();
        var value = ParsePointOrRange();
        if (value is null)
        {
            return Malformed();
        }

        if (keyword.Text == "over" && (when is PointSyntax || value is PointSyntax))
        {
            var written = source.ToString(target.Span);
            var (half, example) = when is PointSyntax
                ? ("its time", $"over 30..40 min {written} = {source.ToString(value.Span)}")
                : ("its value", $"{written} = 30..45");
            Report(
                Language2Diagnostics.RampWithOneValue,
                LineSpan,
                new DiagnosticArgument("half", half),
                new DiagnosticArgument("example", example));
        }

        return new DisturbanceSyntax(keyword, when!, target, equals, value);
    }
}
