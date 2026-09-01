using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Syntax.Ast;

namespace FluidScript.Core.Syntax;

/// <summary>
/// Parses one line into one statement. A line that cannot be read becomes a
/// <see cref="MalformedStatementSyntax"/> holding its tokens, so the printer can reproduce it and every
/// other line is unaffected.
/// </summary>
internal sealed class LineParser(
    SourceText source,
    ImmutableArray<Token> tokens,
    ImmutableArray<Diagnostic>.Builder diagnostics)
{
    private int _index;

    private Token? Current => _index < tokens.Length ? tokens[_index] : null;

    private bool AtEnd => _index >= tokens.Length;

    private TextSpan LineSpan => TextSpan.FromBounds(tokens[0].Span.Start, tokens[^1].Span.End);

    public StatementSyntax Parse(StatementKind kind, FluidScriptParser.ScriptState state)
    {
        // A circuit header ends whatever section the previous circuit was in and opens a new
        // declaration section (D-52), so it is checked against no section at all.
        if (kind == StatementKind.Circuit)
        {
            state.BeginCircuit();
        }
        else
        {
            CheckSection(kind, state);
        }
        var explained = diagnostics.Count;

        var statement = kind switch
        {
            StatementKind.Version => ParseVersion(),
            StatementKind.Project => ParseProject(state),
            StatementKind.Spacing => ParseSpacing(state),
            StatementKind.Circuit => ParseCircuit(),
            StatementKind.Fluid => ParseFluid(),
            StatementKind.Catalog => ParseCatalog(),
            StatementKind.Style => ParseStyle(),
            StatementKind.Show => ParseShow(),
            StatementKind.Let => ParseLet(),
            StatementKind.ConnectionsHeader => ParseSectionHeader(state, connections: true),
            StatementKind.ScheduleHeader => ParseSectionHeader(state, connections: false),
            StatementKind.Attachment => ParseAttachment(state),
            StatementKind.Control => ParseControl(),
            StatementKind.Connection => ParseConnection(),
            StatementKind.Disturbance => ParseDisturbance(),
            StatementKind.Declaration => ParseDeclaration(),
            _ => Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan),
        };

        // A statement that parsed without consuming its line has left text the line has no place for.
        // Every token has to stay in the tree whatever else happens, so the whole line becomes one
        // malformed statement rather than a node that silently drops the remainder -- which is the bug
        // the losslessness assertion caught, and the reason it runs over the tree and not the tokens.
        if (!AtEnd)
        {
            return ExtraText();
        }

        // A line marked wrong with no sentence beside it is worse than no marking at all: the editor
        // shows a squiggle the user cannot act on. If nothing more specific explained this line, say
        // the general thing.
        if (statement is MalformedStatementSyntax && diagnostics.Count == explained)
        {
            Report(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        return statement;
    }

    // ---- section legality -------------------------------------------------------------------

    private void CheckSection(StatementKind kind, FluidScriptParser.ScriptState state)
    {
        var section = state.Section;

        switch (kind)
        {
            case StatementKind.Connection when section != ScriptSection.Connections:
                Report(ParserDiagnostics.ConnectionOutsideSection, LineSpan);
                return;

            // A `schedule` header is legal in both of the sections that can precede it, and a second
            // one is FS1101 from the header parser rather than FS1103, so neither header is in the
            // list below on its own account (D-56).
            case StatementKind.Declaration or StatementKind.Attachment or StatementKind.Control
                when section == ScriptSection.Schedule:
            case StatementKind.ConnectionsHeader when section == ScriptSection.Schedule:
            case StatementKind.Version or StatementKind.Project or StatementKind.Spacing
                or StatementKind.Fluid or StatementKind.Catalog or StatementKind.Style
                or StatementKind.Show or StatementKind.Let
                when section != ScriptSection.Declaration:
                Report(
                    ParserDiagnostics.StatementInWrongSection,
                    LineSpan,
                    new DiagnosticArgument("statement", NameOf(kind)),
                    new DiagnosticArgument("section", section == ScriptSection.Schedule ? "schedule" : "connections"));
                return;

            default:
                return;
        }
    }

    private static string NameOf(StatementKind kind) => kind switch
    {
        StatementKind.Version => "version line",
        StatementKind.Project => "project directive",
        StatementKind.Spacing => "spacing directive",
        StatementKind.Fluid => "fluid directive",
        StatementKind.Catalog => "catalog directive",
        StatementKind.Style => "style directive",
        StatementKind.Show => "show directive",
        StatementKind.Let => "let binding",
        StatementKind.ConnectionsHeader => "connections line",
        StatementKind.ScheduleHeader => "schedule line",
        StatementKind.Attachment => "supply or return line",
        StatementKind.Control => "control line",
        StatementKind.Declaration => "component declaration",
        _ => "statement",
    };

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
        var parts = ImmutableArray.CreateBuilder<StyleTokenSyntax>();

        while (!AtEnd)
        {
            parts.Add(TakeStyleToken());
        }

        // A directive whose whole token list was eaten by a comment beginning with a hex-shaped run is
        // legal, silent, and renders in the default colour. The lexer cannot know a colour was meant;
        // here we can see the comment sitting in the keyword's trailing trivia.
        if (parts.Count == 0 && HexComment(keyword) is { } hex)
        {
            Report(ParserDiagnostics.BareHexColour, keyword.Span, new DiagnosticArgument("hex", hex));
        }

        return new StyleDirectiveSyntax(keyword, parts.ToImmutable());
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

    private StatementSyntax ParseAttachment(FluidScriptParser.ScriptState state)
    {
        var keyword = Advance();
        var isSupply = keyword.Keyword == ReservedWord.Supply;
        var repeated = isSupply ? state.SeenSupply : state.SeenReturn;

        var endpoint = AtEnd ? null : TakeEndpoint();
        if (endpoint is null || repeated)
        {
            Report(
                ParserDiagnostics.MalformedAttachment,
                keyword.Span,
                new DiagnosticArgument("word", keyword.Text));

            if (endpoint is null)
            {
                return Malformed();
            }
        }

        if (isSupply)
        {
            state.SeenSupply = true;
        }
        else
        {
            state.SeenReturn = true;
        }

        return new AttachmentSyntax(keyword, endpoint);
    }

    private StatementSyntax ParseControl()
    {
        var keyword = Advance();
        var arguments = ParseParameters(out var failed);

        if (failed)
        {
            return Malformed();
        }

        if (arguments.Length == 0)
        {
            Report(ParserDiagnostics.MalformedControlBinding, LineSpan);
            return Malformed();
        }

        return new ControlBindingSyntax(keyword, arguments);
    }

    private StatementSyntax ParseConnection()
    {
        var first = TakeEndpoint();
        if (first is null)
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        var links = ImmutableArray.CreateBuilder<ConnectionLinkSyntax>();
        while (Current is { Kind: TokenKind.Minus })
        {
            var dash = Advance();
            var endpoint = TakeEndpoint();
            if (endpoint is null)
            {
                return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
            }

            links.Add(new ConnectionLinkSyntax(dash, endpoint));
        }

        return new ConnectionSyntax(first, links.ToImmutable());
    }

    private StatementSyntax ParseDisturbance()
    {
        var keyword = Advance();
        var isRamp = string.Equals(keyword.Text, "over", StringComparison.Ordinal);

        RangeOrPointSyntax? when = isRamp ? ParseRange() : ParsePoint();
        if (when is null)
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        var target = TakeEndpoint();
        if (target is null || Current is not { Kind: TokenKind.Equals })
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        var equals = Advance();

        // All four combinations are legal: `over` with a single value ramps nothing and steps at the
        // end, which is occasionally what a user means and is cheaper to allow than to diagnose.
        var value = ParsePointOrRange();
        if (value is null)
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        return new DisturbanceSyntax(keyword, when, target, equals, value);
    }

    private StatementSyntax ParseDeclaration()
    {
        var nameToken = Current;
        var name = TakeIdentifier();
        if (name is null)
        {
            return Malformed();
        }

        // `in N3` is a legal component declaration -- a component named `in` of kind `N3` -- so
        // without this the user gets an unknown-kind message pointing at N3, or none at all and a
        // subcircuit that never attaches. This is the only place the parser reads a spelling.
        if (tokens.Length == 2
            && nameToken is not null
            && (nameToken.Text is "in" or "out")
            && tokens[1].Kind == TokenKind.Identifier)
        {
            Report(
                ParserDiagnostics.InOutIsNotAnAttachment,
                LineSpan,
                new DiagnosticArgument("word", nameToken.Text),
                new DiagnosticArgument("node", tokens[1].Text));
            return Malformed();
        }

        if (Current is not { Kind: TokenKind.Identifier })
        {
            // A hyphenated kind name is what a reader coming from HTML, CSS or a /docs filename types.
            if (TryReadHyphenated(out var written, out var underscored))
            {
                Report(
                    ParserDiagnostics.HyphenInName,
                    LineSpan,
                    new DiagnosticArgument("text", written),
                    new DiagnosticArgument("underscored", underscored));
                return Malformed();
            }

            // `at`/`over` are classified by section, so outside a schedule they land here.
            if (nameToken?.Text is "at" or "over")
            {
                Report(ParserDiagnostics.DisturbanceOutsideSchedule, LineSpan);
                return Malformed();
            }

            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        var kind = new IdentifierSyntax(Advance());
        var parameters = ParseParameters(out var failed);
        if (failed)
        {
            return Malformed();
        }

        return new ComponentDeclarationSyntax(name, kind, parameters);
    }

    // ---- pieces -----------------------------------------------------------------------------

    private ImmutableArray<ParameterSyntax> ParseParameters(out bool failed)
    {
        var parameters = ImmutableArray.CreateBuilder<ParameterSyntax>();
        failed = false;

        while (Current is { Kind: TokenKind.Identifier } nameToken)
        {
            if (tokens.ElementAtOrDefault(_index + 1) is not { Kind: TokenKind.Equals })
            {
                Report(
                    ParserDiagnostics.ParameterWithoutValue,
                    nameToken.Span,
                    new DiagnosticArgument("token", nameToken.Text));
                failed = true;
                return [];
            }

            var name = new IdentifierSyntax(Advance());
            var equals = Advance();
            var value = ParseExpression();
            if (value is null)
            {
                Report(ParserDiagnostics.UnclassifiableStatement, LineSpan);
                failed = true;
                return [];
            }

            parameters.Add(new ParameterSyntax(name, equals, value));
        }

        return parameters.ToImmutable();
    }

    private EndpointSyntax? TakeEndpoint()
    {
        var component = TakeIdentifier();
        if (component is null)
        {
            return null;
        }

        if (Current is { Kind: TokenKind.Dot }
            && tokens.ElementAtOrDefault(_index + 1) is { Kind: TokenKind.Identifier })
        {
            var dot = Advance();
            var port = new IdentifierSyntax(Advance());
            return new EndpointSyntax(component, dot, port);
        }

        return new EndpointSyntax(component, null, null);
    }

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
        var operand = ParseUnary();
        return operand is null ? null : new UnaryExpressionSyntax(op, operand);
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
                var inner = ParseExpression();
                if (inner is null || Current is not { Kind: TokenKind.CloseParenthesis })
                {
                    return null;
                }

                return new ParenthesizedExpressionSyntax(open, inner, Advance());
            }

            case TokenKind.Identifier:
            {
                var name = new IdentifierSyntax(Advance());
                return Current is { Kind: TokenKind.OpenParenthesis } ? ParseCall(name) : ParseReference(name);
            }

            default:
                return null;
        }
    }

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

            var value = ParseExpression();
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

    private ReferenceSyntax ParseReference(IdentifierSyntax head)
    {
        var parts = ImmutableArray.CreateBuilder<QualifiedNamePart>();

        while (Current is { Kind: TokenKind.Dot }
               && tokens.ElementAtOrDefault(_index + 1) is { Kind: TokenKind.Identifier })
        {
            var dot = Advance();
            parts.Add(new QualifiedNamePart(dot, new IdentifierSyntax(Advance())));
        }

        return new ReferenceSyntax(head, parts.ToImmutable());
    }

    // ---- primitives -------------------------------------------------------------------------

    private Token Advance() => tokens[_index++];

    private Token? TakeModeKeyword() =>
        Current is { Kind: TokenKind.Keyword, Keyword: ReservedWord.Dynamic or ReservedWord.Static }
            ? Advance()
            : null;

    private IdentifierSyntax? TakeIdentifier()
    {
        if (Current is not { } token)
        {
            return null;
        }

        switch (token.Kind)
        {
            case TokenKind.Identifier:
                return new IdentifierSyntax(Advance());

            case TokenKind.Keyword:
                Report(
                    ParserDiagnostics.ReservedWordAsName,
                    token.Span,
                    new DiagnosticArgument("word", token.Text));
                return null;

            case TokenKind.QuantityLiteral:
                // `3K` is three kelvin everywhere, including where that is correct, so only here --
                // where a name belongs -- can it be reported. Swapping the parts is the fix that works.
                Report(
                    ParserDiagnostics.NameReadsAsQuantity,
                    token.Span,
                    new DiagnosticArgument("name", token.Text),
                    new DiagnosticArgument(
                        "value",
                        (token.Value ?? 0).ToString(CultureInfo.InvariantCulture)),
                    new DiagnosticArgument("unit", token.Unit ?? string.Empty),
                    new DiagnosticArgument(
                        "suggestion",
                        (token.Unit ?? string.Empty) + (token.NumberText ?? string.Empty)));
                return null;

            default:
                return null;
        }
    }

    private NumberLiteralSyntax? TakeNumber() =>
        Current is { Kind: TokenKind.NumberLiteral } ? new NumberLiteralSyntax(Advance()) : null;

    private bool TryReadHyphenated(out string written, out string underscored)
    {
        written = string.Empty;
        underscored = string.Empty;

        if (Current is not { } first || !IsNamePart(first))
        {
            return false;
        }

        var text = first.Text;
        var end = first.Span.End;
        var index = _index + 1;
        var joins = 0;

        while (index + 1 < tokens.Length
               && tokens[index].Kind == TokenKind.Minus
               && tokens[index].Span.Start == end
               && tokens[index + 1].Span.Start == tokens[index].Span.End
               && IsNamePart(tokens[index + 1]))
        {
            text += "-" + tokens[index + 1].Text;
            end = tokens[index + 1].Span.End;
            index += 2;
            joins++;
        }

        if (joins == 0)
        {
            return false;
        }

        written = text;
        underscored = text.Replace('-', '_');
        return true;
    }

    private static bool IsNamePart(Token token) =>
        token.Kind is TokenKind.Identifier or TokenKind.NumberLiteral or TokenKind.Keyword;

    private MalformedStatementSyntax ExtraText()
    {
        var extra = TextSpan.FromBounds(tokens[_index].Span.Start, tokens[^1].Span.End);
        Report(
            ParserDiagnostics.ExtraTextOnLine,
            extra,
            new DiagnosticArgument("extra", source.ToString(extra)));
        return Malformed();
    }

    private MalformedStatementSyntax Fail(
        DiagnosticDescriptor descriptor,
        TextSpan span,
        params ReadOnlySpan<DiagnosticArgument> arguments)
    {
        Report(descriptor, span, arguments);
        return Malformed();
    }

    private MalformedStatementSyntax Malformed()
    {
        _index = tokens.Length;
        return new MalformedStatementSyntax(tokens);
    }

    private void Report(
        DiagnosticDescriptor descriptor,
        TextSpan span,
        params ReadOnlySpan<DiagnosticArgument> arguments) =>
        diagnostics.Add(Diagnostic.Create(descriptor, span, arguments));
}
