using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Parsing;

internal sealed partial class LineParser
{
    // ---- statements -------------------------------------------------------------------------

    private StatementSyntax ParseVersion()
    {
        var keyword = Advance();
        var major = TakeNumber();
        if (major is null)
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        return new VersionDirectiveSyntax(keyword, major);
    }

    private StatementSyntax ParseProject(FluidScriptParser.ScriptState state)
    {
        var keyword = Advance();

        // Both file-wide directives must precede the first circuit, because a global statement that
        // appears after the thing it governs reads as though it applied only from that point (D-37).
        if (state.SeenCircuit || state.SeenProject)
        {
            Report(
                ParserDiagnostics.GlobalDirectiveOutOfPlace,
                keyword.Span,
                new DiagnosticArgument("word", keyword.Text));
        }

        state.SeenProject = true;

        var mode = TakeModeKeyword();
        var name = TakeIdentifier();
        if (name is null)
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        return new ProjectDirectiveSyntax(keyword, mode, name);
    }

    private StatementSyntax ParseSpacing(FluidScriptParser.ScriptState state)
    {
        var keyword = Advance();

        if (state.SeenCircuit || state.SeenSpacing)
        {
            Report(
                ParserDiagnostics.GlobalDirectiveOutOfPlace,
                keyword.Span,
                new DiagnosticArgument("word", keyword.Text));
        }

        state.SeenSpacing = true;

        // World units have no physical dimension, so a quantity here is refused by name rather than
        // silently converted: `spacing 20 mm` would imply the canvas has a scale it does not have.
        if (Current is { Kind: TokenKind.QuantityLiteral } quantity)
        {
            Report(
                ParserDiagnostics.SpacingTakesABareNumber,
                quantity.Span,
                new DiagnosticArgument("n", quantity.NumberText ?? quantity.Text));
            return Malformed();
        }

        var value = TakeNumber();
        if (value is null)
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        return new SpacingDirectiveSyntax(keyword, value);
    }

    private StatementSyntax ParseCircuit()
    {
        var keyword = Advance();
        var name = TakeIdentifier();
        if (name is null)
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        // Absent stays distinguishable from written: the binder resolves an omitted number, and the
        // printer must not invent one that was never there (D-33).
        var number = Current is { Kind: TokenKind.NumberLiteral } ? TakeNumber() : null;

        return new CircuitHeaderSyntax(keyword, name, number);
    }

    private StatementSyntax ParseFluid()
    {
        var keyword = Advance();
        var mode = TakeModeKeyword();
        var substance = TakeIdentifier();
        if (substance is null)
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        return new FluidDirectiveSyntax(keyword, mode, substance, []);
    }

    private StatementSyntax ParseCatalog()
    {
        var keyword = Advance();
        var id = TakeIdentifier();
        if (id is null)
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        CatalogVersionSyntax? version = null;
        if (Current is { Kind: TokenKind.At } at)
        {
            _index++;

            // The version arrives as one number token, because the lexer's number rule consumes a dot
            // followed by a digit and has no context to do otherwise.
            if (Current is not { Kind: TokenKind.NumberLiteral } number
                || !number.Text.Contains('.', StringComparison.Ordinal))
            {
                return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
            }

            _index++;
            version = new CatalogVersionSyntax(at, number);
        }

        return new CatalogDirectiveSyntax(keyword, id, version);
    }

    private StyleDirectiveSyntax ParseStyle()
    {
        var keyword = Advance();
        Token? name = null;
        Token? equals = null;

        // `style hot = ...` defines a named style (D-104). The `=` is what tells a definition from an
        // application, so a bare `style hot` stays a token list and the binder decides whether `hot`
        // is a style it knows or a colour it does not.
        if (Current is { Kind: TokenKind.Identifier or TokenKind.Keyword }
            && tokens.ElementAtOrDefault(_index + 1) is { Kind: TokenKind.Equals })
        {
            name = Advance();
            equals = Advance();
        }

        var parts = ImmutableArray.CreateBuilder<StyleTokenSyntax>();

        while (!AtEnd)
        {
            parts.Add(TakeStyleToken());
        }

        // A directive whose whole token list was eaten by a comment beginning with a hex-shaped run is
        // legal, silent, and renders in the default colour. The lexer cannot know a colour was meant;
        // here we can see the comment sitting in the keyword's trailing trivia.
        if (parts.Count == 0 && HexComment(equals ?? keyword) is { } hex)
        {
            Report(ParserDiagnostics.BareHexColour, keyword.Span, new DiagnosticArgument("hex", hex));
        }

        return new StyleDirectiveSyntax(keyword, name, equals, parts.ToImmutable());
    }

    private StyleTokenSyntax TakeStyleToken()
    {
        var first = Advance();

        if (first.Kind is TokenKind.Minus or TokenKind.Dot or TokenKind.DotDot)
        {
            // The one place the grammar is not context-free: a run of adjacent dashes and dots is one
            // pattern token here and two operators everywhere else. Adjacency is what separates
            // `--` from `- -`, so it is read from the spans rather than from the text.
            var run = ImmutableArray.CreateBuilder<Token>();
            run.Add(first);
            var end = first.Span.End;

            while (Current is { Kind: TokenKind.Minus or TokenKind.Dot or TokenKind.DotDot } next
                   && next.Span.Start == end)
            {
                run.Add(next);
                end = next.Span.End;
                _index++;
            }

            return new StyleTokenSyntax(StyleTokenKind.Pattern, run.ToImmutable());
        }

        // `fill="#e8f1f8"`: the one keyed token (D-104), because a second positional colour would
        // break the order-independence the directive was decided on.
        if (first.Kind is TokenKind.Identifier or TokenKind.Keyword
            && Current is { Kind: TokenKind.Equals } equals
            && tokens.ElementAtOrDefault(_index + 1) is { Kind: not TokenKind.EndOfFile } value)
        {
            _index += 2;
            return new StyleTokenSyntax(StyleTokenKind.Keyed, [first, equals, value]);
        }

        var kind = first.Kind switch
        {
            TokenKind.NumberLiteral => StyleTokenKind.Number,
            TokenKind.QuantityLiteral => StyleTokenKind.Quantity,
            TokenKind.StringLiteral => StyleTokenKind.Quoted,
            _ => StyleTokenKind.Word,
        };

        return new StyleTokenSyntax(kind, [first]);
    }

    private string? HexComment(Token keyword)
    {
        foreach (var trivia in keyword.TrailingTrivia)
        {
            if (trivia.Kind != TriviaKind.Comment)
            {
                continue;
            }

            // The comment begins at the '#' the user meant as a colour, so the hex run is what
            // follows it. Three or six digits, and nothing else -- a prose comment is not a colour.
            var text = source.Slice(trivia.Span);
            var digits = text[1..];
            var end = 0;
            while (end < digits.Length && Uri.IsHexDigit(digits[end]))
            {
                end++;
            }

            if (end is 3 or 6 && (end == digits.Length || digits[end] is ' ' or '\t'))
            {
                return text[..(end + 1)].ToString();
            }
        }

        return null;
    }

    private StatementSyntax ParseShow()
    {
        var keyword = Advance();
        var properties = ImmutableArray.CreateBuilder<IdentifierSyntax>();

        while (Current is { Kind: TokenKind.Identifier })
        {
            properties.Add(new IdentifierSyntax(Advance()));
        }

        if (properties.Count == 0)
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        RangeSyntax? scale = null;
        if (!AtEnd)
        {
            var from = ParseExpression();
            if (from is null || Current is not { Kind: TokenKind.DotDot })
            {
                return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
            }

            var dots = Advance();
            var to = ParseExpression();
            if (to is null)
            {
                return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
            }

            scale = new RangeSyntax(from, dots, to);
        }

        return new ShowDirectiveSyntax(keyword, properties.ToImmutable(), scale);
    }

    private StatementSyntax ParseLet()
    {
        var keyword = Advance();
        var name = TakeIdentifier();
        if (name is null || Current is not { Kind: TokenKind.Equals })
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        var equals = Advance();
        var value = ParseExpression();
        if (value is null)
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        return new LetBindingSyntax(keyword, name, equals, value);
    }

    private StatementSyntax ParseSectionHeader(FluidScriptParser.ScriptState state, bool connections)
    {
        var keyword = Advance();
        var already = connections ? state.SeenConnections : state.SeenSchedule;

        if (already)
        {
            // Counted per circuit, not per file (D-52). The node is still built, because the printer
            // has to reproduce a line the binder is going to ignore.
            Report(
                ParserDiagnostics.DuplicateSectionHeader,
                keyword.Span,
                new DiagnosticArgument("section", keyword.Text));
        }
        else if (connections)
        {
            state.SeenConnections = true;
            state.Section = ScriptSection.Connections;
        }
        else
        {
            state.SeenSchedule = true;
            state.Section = ScriptSection.Schedule;
        }

        return connections ? new ConnectionsHeaderSyntax(keyword) : new ScheduleHeaderSyntax(keyword);
    }
}
