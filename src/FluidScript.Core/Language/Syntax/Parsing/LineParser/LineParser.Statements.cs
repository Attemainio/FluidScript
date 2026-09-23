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
    private StatementSyntax ParseAttachment(FluidScriptParser.ScriptState state)
    {
        var keyword = Advance();
        var isSupply = keyword.Keyword == ReservedWord.Inlet;
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

        // `control TV1 with TE1 by PID1 setpoint=21` (`D-61`). The two forms are told apart on the
        // token after `control`: a named argument is followed by `=`, and an actuator is not.
        if (Current is { Kind: TokenKind.Identifier }
            && tokens.ElementAtOrDefault(_index + 1) is not { Kind: TokenKind.Equals })
        {
            return ParseShortControl(keyword);
        }

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

    private StatementSyntax ParseShortControl(Token keyword)
    {
        var actuator = TakeEndpoint();
        var with = TakeWord("with");
        var sensor = with is null ? null : TakeEndpoint();
        var by = sensor is null ? null : TakeWord("by");
        var controller = by is null ? null : TakeIdentifier();

        if (controller is null)
        {
            // One code for the whole shape. Reporting which of `with` or `by` was missing would need a
            // message per position for a line the user will rewrite whole anyway.
            Report(ParserDiagnostics.MalformedControlBinding, LineSpan);
            return Malformed();
        }

        var arguments = ParseParameters(out var failed);

        return failed
            ? Malformed()
            : new ControlBindingSyntax(keyword, arguments)
            {
                Actuator = actuator,
                WithKeyword = with,
                Sensor = sensor,
                ByKeyword = by,
                Controller = controller,
            };
    }

    /// <summary>Takes one position-classified word, such as <c>with</c> or <c>by</c>.</summary>
    /// <remarks>
    /// Neither is reserved. Each is an ordinary identifier whose position inside a statement the first
    /// token already identified gives it meaning — the same trade `at` and `over` make.
    /// </remarks>
    private Token? TakeWord(string text) =>
        Current is { Kind: TokenKind.Identifier } token && token.Text == text ? Advance() : null;

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

        // D-110: the line may end in pipe properties, `N1 - HE1 dn=25 length=12`, spelled as on a
        // declaration. They apply to every connection on the line, and the binder makes each an
        // implicit pipe (rule I7). A bare line stays what it was.
        var parameters = ParseParameters(out var failed);
        if (failed)
        {
            return Malformed();
        }

        return new ConnectionSyntax(first, links.ToImmutable(), parameters);
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

        // A kind may be a reserved word. `S1 inlet t=5 flow=2.3 l/s` declares a boundary (`D-64`, `D-115`), and
        // `inlet` is reserved because `inlet N3` attaches a subcircuit -- the two are told apart by
        // whether the line starts with an identifier, which is decided before this method is reached.
        // Which keywords name kinds is the registry's business and not the parser's: anything accepted
        // here that names no kind meets the binder's unknown-kind message, which is the better one.
        if (Current is not { Kind: TokenKind.Identifier or TokenKind.Keyword })
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

        // `TE1 t_sensor at N2` places an observer on a node (`D-61`). `at` is position-classified, not
        // reserved: it is already an ordinary identifier inside a schedule section, and reserving a
        // common English word to buy nothing is the trade `P6` refuses.
        Token? atKeyword = null;
        IdentifierSyntax? attachedTo = null;

        if (Current is { Kind: TokenKind.Identifier, Text: "at" }
            && tokens.ElementAtOrDefault(_index + 1) is { Kind: TokenKind.Identifier })
        {
            atKeyword = Advance();
            attachedTo = new IdentifierSyntax(Advance());
        }

        var parameters = ParseParameters(out var failed);
        if (failed)
        {
            return Malformed();
        }

        // `HP1 heater power=heating sized_at tout=-5` names the point this component is sized at
        // (`D-94`). Position-classified like `at`: it is only this word, after the parameters, followed
        // by at least one `driver=value` pair. `ParseParameters` stops in front of it.
        Token? sizedAtKeyword = null;
        var sizingPoint = ImmutableArray<ParameterSyntax>.Empty;

        if (Current is { Kind: TokenKind.Identifier, Text: "sized_at" })
        {
            sizedAtKeyword = Advance();
            sizingPoint = ParseParameters(out failed);

            if (failed)
            {
                return Malformed();
            }

            if (sizingPoint.IsEmpty)
            {
                Report(
                    ParserDiagnostics.ParameterWithoutValue,
                    sizedAtKeyword.Span,
                    new DiagnosticArgument("token", sizedAtKeyword.Text));
                return Malformed();
            }
        }

        return new ComponentDeclarationSyntax(
            name, kind, atKeyword, attachedTo, parameters, sizedAtKeyword, sizingPoint);
    }

    // ---- pieces -----------------------------------------------------------------------------

    private ImmutableArray<ParameterSyntax> ParseParameters(out bool failed)
    {
        var parameters = ImmutableArray.CreateBuilder<ParameterSyntax>();
        failed = false;

        // `style=` is the one reserved word a parameter may be named after (D-104).
        while (Current is { Kind: TokenKind.Identifier } or { Kind: TokenKind.Keyword, Text: "style" } && Current is { } nameToken)
        {
            // The one bare identifier a parameter list may end in front of: a declaration's sizing
            // point begins here and the caller reads it (`D-94`).
            if (nameToken.Text is "sized_at"
                && tokens.ElementAtOrDefault(_index + 1) is not { Kind: TokenKind.Equals })
            {
                break;
            }

            if (!EqualsFollowsName())
            {
                Report(
                    ParserDiagnostics.ParameterWithoutValue,
                    nameToken.Span,
                    new DiagnosticArgument("token", nameToken.Text));
                failed = true;
                return [];
            }

            // `style=` is a keyword where a name belongs, so it bypasses the identifier check that
            // would report it as a reserved word.
            var name = nameToken.Kind == TokenKind.Keyword
                ? new QualifiedNameSyntax(new IndexedNameSyntax(new IdentifierSyntax(Advance()), null), [])
                : TakeQualifiedName();
            if (name is null)
            {
                failed = true;
                return [];
            }

            var equals = Advance();
            var explained = diagnostics.Count;

            // A bracket directly after `=` opens a scenario list and can be nothing else (`D-143`):
            // an indexed name puts its bracket after an identifier, and that name is already consumed.
            ExpressionSyntax? value = Current is { Kind: TokenKind.OpenBracket }
                ? ParseScenarioList()
                : ParseExpression();

            if (value is null)
            {
                // A value that failed with its own message (`FS1119`) does not also need the general one.
                if (diagnostics.Count == explained)
                {
                    Report(ParserDiagnostics.UnclassifiableStatement, LineSpan);
                }

                failed = true;
                return [];
            }

            parameters.Add(new ParameterSyntax(name, equals, value));
        }

        return parameters.ToImmutable();
    }

    /// <summary>Whether the name starting at the current token — a word, its index, its dotted quantity — is followed by <c>=</c>.</summary>
    /// <remarks>
    /// The one-token lookahead of <c>12</c> is over statements, not over a name: a parameter name is
    /// one lexical unit to the grammar and several tokens to the lexer, so deciding "is this a
    /// parameter" reads to the end of the name. Nothing else can start with a word and a bracket.
    /// </remarks>
    private bool EqualsFollowsName()
    {
        var index = _index + 1;

        while (tokens.ElementAtOrDefault(index) is { } token
               && token.Kind is TokenKind.OpenBracket or TokenKind.NumberLiteral or TokenKind.CloseBracket
                   or TokenKind.Dot or TokenKind.Identifier)
        {
            index++;
        }

        return tokens.ElementAtOrDefault(index) is { Kind: TokenKind.Equals };
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
            var port = TakeQualifiedName();
            return port is null ? null : new EndpointSyntax(component, dot, port);
        }

        return new EndpointSyntax(component, null, null);
    }
}
