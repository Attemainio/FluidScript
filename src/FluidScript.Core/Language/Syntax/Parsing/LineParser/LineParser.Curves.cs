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
    /// <summary>Parses a <c>curve</c> header and opens its section (<c>D-57</c>).</summary>
    /// <remarks>
    /// Three fixed positions — keyword, name, driver — then modifiers and named arguments. There is no
    /// preposition because the language has none anywhere; the positions are asymmetric enough that a
    /// transposition is caught downstream, where a name that already exists is <c>FS1501</c> and a
    /// driver that does not is <c>FS1527</c>.
    /// </remarks>
    private StatementSyntax ParseCurveHeader(FluidScriptParser.ScriptState state)
    {
        var keyword = Advance();

        // File-wide, unlike every other section (`D-52` does not apply): a curve is read by every
        // circuit that names it, so it is declared with the other file-wide statements.
        if (state.SeenCircuit)
        {
            Report(
                ParserDiagnostics.GlobalDirectiveOutOfPlace,
                keyword.Span,
                new DiagnosticArgument("word", keyword.Text));
        }

        var name = TakeIdentifier();
        if (name is null)
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        // Opened before the driver is checked, so the rows below a header that is missing one still
        // land in a curve section rather than each becoming its own FS1115.
        state.Section = ScriptSection.Curve;

        IdentifierSyntax? driver = null;
        if (Current is { Kind: TokenKind.Identifier }
            && tokens.ElementAtOrDefault(_index + 1) is not { Kind: TokenKind.Equals })
        {
            driver = new IdentifierSyntax(Advance());
        }

        if (driver is null)
        {
            Report(
                ParserDiagnostics.CurveWithoutDriver,
                LineSpan,
                new DiagnosticArgument("name", name.Text));
        }

        var modifiers = ImmutableArray.CreateBuilder<IdentifierSyntax>();
        while (Current is { Kind: TokenKind.Identifier }
            && tokens.ElementAtOrDefault(_index + 1) is not { Kind: TokenKind.Equals })
        {
            modifiers.Add(new IdentifierSyntax(Advance()));
        }

        var arguments = ParseParameters(out var failed);

        return failed
            ? Malformed()
            : new CurveHeaderSyntax(keyword, name, driver, modifiers.ToImmutable(), arguments);
    }

    /// <summary>Takes one <c>x y</c> row whole, without interpreting either column.</summary>
    /// <remarks>
    /// The split is the binder's, because <c>x</c> may be a timestamp and a timestamp is not one
    /// token: <c>2026-01-01T00:00:00</c> lexes as six tokens and an identifier, and there is no
    /// context-free way to lex it as a unit, since <c>2026-01-01</c> is also a valid subtraction. The
    /// parser's job here is to keep every token on the line so the printer stays exact.
    /// </remarks>
    private StatementSyntax ParseCurveRow()
    {
        // Counted in whitespace-separated parts, not tokens, because that is how the binder splits the
        // row: `-26` is two tokens and one value, and `01/01/2026 00:00:00 -1` is many tokens and
        // three parts, of which the first two are one timestamp.
        if (source.ToString(LineSpan).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            return Fail(ParserDiagnostics.MalformedCurveRow, LineSpan);
        }

        _index = tokens.Length;
        return new CurveRowSyntax(tokens);
    }

    /// <summary>Parses a <c>design</c> directive (<c>D-58</c>).</summary>
    private StatementSyntax ParseDesign(FluidScriptParser.ScriptState state)
    {
        var keyword = Advance();

        if (state.SeenCircuit)
        {
            Report(
                ParserDiagnostics.GlobalDirectiveOutOfPlace,
                keyword.Span,
                new DiagnosticArgument("word", keyword.Text));
        }

        // `design winter` names a scenario; `design tout=-26` gives a driver a value (`D-143`). One
        // token tells them apart -- a scenario name is the whole line, a driver is followed by `=` --
        // and the check is the one `ParseParameters` already makes, so neither form needs lookahead
        // it does not already have.
        if (Current is { Kind: TokenKind.Identifier } && !EqualsFollowsName())
        {
            var scenario = TakeIdentifier();

            return scenario is null || !AtEnd
                ? Fail(ParserDiagnostics.MalformedDesignDirective, LineSpan)
                : new DesignDirectiveSyntax(keyword, [], scenario);
        }

        var arguments = ParseParameters(out var failed);
        if (failed)
        {
            return Malformed();
        }

        // Unlike `project` and `spacing`, a second `design` line is legal: one driver per line reads
        // better than a long one, and the binder merges them.
        if (arguments.IsEmpty)
        {
            return Fail(ParserDiagnostics.MalformedDesignDirective, LineSpan);
        }

        return new DesignDirectiveSyntax(keyword, arguments);
    }

    /// <summary>Parses a <c>scenarios</c> directive (<c>D-143</c>).</summary>
    /// <remarks>
    /// Bare identifiers, not parameters: a scenario has a name and no value of its own. Duplicates
    /// and a missing <c>design</c> are the binder's (<c>FS1544</c>, <c>FS1543</c>) -- both are
    /// questions about the set, and the parser reads one line.
    /// </remarks>
    private StatementSyntax ParseScenarios(FluidScriptParser.ScriptState state)
    {
        var keyword = Advance();

        if (state.SeenCircuit)
        {
            Report(
                ParserDiagnostics.GlobalDirectiveOutOfPlace,
                keyword.Span,
                new DiagnosticArgument("word", keyword.Text));
        }

        var names = ImmutableArray.CreateBuilder<IdentifierSyntax>();

        while (!AtEnd)
        {
            var name = TakeIdentifier();
            if (name is null)
            {
                return Malformed();
            }

            names.Add(name);
        }

        if (names.Count == 0)
        {
            return Fail(ParserDiagnostics.MalformedScenariosDirective, LineSpan);
        }

        return new ScenariosDirectiveSyntax(keyword, names.ToImmutable());
    }

    /// <summary>Parses the bracketed value list a parameter may take after <c>=</c> (<c>D-143</c>).</summary>
    /// <returns>The list, or <see langword="null"/> after reporting <c>FS1121</c>.</returns>
    /// <remarks>
    /// The <c>[</c> is already known to be here, and the elements are ordinary values, so this is a
    /// comma loop around <see cref="ParseExpression"/>. It never recurses into itself: a nested list
    /// would be an element whose first token is <c>[</c>, and <c>ParseExpression</c> has no rule for
    /// one, so it fails there with its own message rather than parsing into a shape with no meaning.
    /// </remarks>
    private ScenarioListSyntax? ParseScenarioList()
    {
        var open = Advance();
        var elements = ImmutableArray.CreateBuilder<ArgumentSyntax>();
        Token? comma = null;

        while (true)
        {
            if (Current is not { } token || token.Kind == TokenKind.CloseBracket)
            {
                // `[]`, `[30,]` and an unterminated `[30` all land here: every one of them is a slot
                // with no value in it, and none is worth its own message.
                Report(ParserDiagnostics.MalformedScenarioList, (comma ?? open).Span);
                return null;
            }

            var explained = diagnostics.Count;
            var value = ParseExpression();

            if (value is null)
            {
                if (diagnostics.Count == explained)
                {
                    Report(ParserDiagnostics.MalformedScenarioList, token.Span);
                }

                return null;
            }

            elements.Add(new ArgumentSyntax(comma, value));

            if (Current is { Kind: TokenKind.Comma })
            {
                comma = Advance();
                continue;
            }

            if (Current is { Kind: TokenKind.CloseBracket })
            {
                return new ScenarioListSyntax(open, elements.ToImmutable(), Advance());
            }

            // Two values with nothing between them -- `[30 10]` -- or a line that ended open.
            Report(ParserDiagnostics.MalformedScenarioList, (Current ?? open).Span);
            return null;
        }
    }
}
