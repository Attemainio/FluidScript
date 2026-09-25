using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Syntax.Parsing;

internal sealed partial class LineParser
{
    /// <summary>Gets the block the line just parsed opens, or <see langword="null"/> when it opens none.</summary>
    /// <value>
    /// Set by <see cref="ParseLanguage2"/>. A head that failed still opens its block, so the lines indented under
    /// it are read as its body rather than each reported for being where no block allows them.
    /// </value>
    public Language2Block? Opens { get; private set; }

    /// <summary>Gets the <c>:</c> ending the head line just parsed, when it is a block head written with one.</summary>
    /// <value><see langword="null"/> when the line opens no block, lacks its colon, or failed.</value>
    public Token? OpeningColon { get; private set; }

    /// <summary>Parses one language 2 line, read in the block that encloses it.</summary>
    /// <param name="context">The block the line is indented under.</param>
    /// <returns>
    /// The statement, never null; a line that cannot be read is a <see cref="MalformedStatementSyntax"/> holding
    /// its tokens. <see cref="Opens"/> and <see cref="OpeningColon"/> say whether it heads a block.
    /// </returns>
    /// <remarks>
    /// A statement in the wrong block is reported (<c>FS1802</c>) and still parsed as what it is, so the
    /// binder sees it and the one misplaced line costs one message. Every line of a curve's body is a row.
    /// </remarks>
    public StatementSyntax ParseLanguage2(Language2Block context)
    {
        var explained = diagnostics.Count;

        var statement = context == Language2Block.Curve ? ParseCurveRow() : ClassifyLanguage2(context);

        if (!AtEnd)
        {
            statement = ExtraText();
        }

        if (statement is MalformedStatementSyntax)
        {
            // The colon is one of the malformed line's tokens, and a token appears in the tree once.
            OpeningColon = null;

            if (diagnostics.Count == explained)
            {
                Report(ParserDiagnostics.UnclassifiableStatement, LineSpan);
            }
        }

        return statement;
    }

    /// <summary>Decides what a language 2 line is and parses it.</summary>
    /// <remarks>
    /// <c>19</c> §Lines, blocks and names: a statement word at the line's start names its statement; otherwise
    /// the qualified name the line starts with is followed by <c>=</c> for a setting, <c>-</c> for a
    /// connection, and anything else for a declaration. The lookahead is bounded by the line.
    /// </remarks>
    private StatementSyntax ClassifyLanguage2(Language2Block context)
    {
        var first = tokens[0];
        var second = tokens.ElementAtOrDefault(1);

        // Only a curve row begins with a number, a minus or a date; outside a curve's body it is language
        // 1's FS1115, which says what the line is.
        if (first.Kind is TokenKind.NumberLiteral or TokenKind.QuantityLiteral or TokenKind.Minus
            or TokenKind.DateLiteral)
        {
            Report(ParserDiagnostics.CurveRowOutsideSection, LineSpan);
            return ParseCurveRow();
        }

        if (first.Kind != TokenKind.Identifier)
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        if (Language1Form(first, second) is { } instead)
        {
            return Fail(
                Language2Diagnostics.Language1Statement,
                LineSpan,
                new DiagnosticArgument("word", first.Text),
                new DiagnosticArgument("instead", instead));
        }

        switch (first.Text)
        {
            case "fluidscript":
                return second is { Kind: TokenKind.NumberLiteral }
                    ? ParseLanguage2Version(context)
                    : StatementWordAsName(first);

            case "project":
                return ParseTitledHead(context, Language2Block.Project);

            case "circuit":
                return ParseTitledHead(context, Language2Block.Circuit);

            case "run":
                return ParseTitledHead(context, Language2Block.Run);

            case "let":
                return second is { Kind: TokenKind.Identifier }
                    ? ParseLanguage2Let(context)
                    : StatementWordAsName(first);

            // `curve = heating` is a controller's setting (`19` §Controllers), and a setting's name is never a
            // statement word: the `=` settles it, as it does for `show =` in the project block.
            case "curve":
                return second switch
                {
                    { Kind: TokenKind.Identifier } => ParseDriverCurveHead(context),
                    { Kind: TokenKind.Equals } => ParseSettingLine(context),
                    _ => StatementWordAsName(first),
                };

            case "style" when second is null or { Kind: TokenKind.Colon }:
                return ParseStyleHead(context);

            case "at" or "over" when StartsEventTime(second):
                return ParseEvent(context);

            default:
                break;
        }

        return tokens.ElementAtOrDefault(SkipQualifiedName(0))?.Kind switch
        {
            TokenKind.Equals => ParseSettingLine(context),
            TokenKind.Minus => ParseChain(context),
            _ => ParseLanguage2Declaration(context),
        };
    }

    /// <summary>Names the language 2 form of a language 1 statement, when the line is one (<c>FS1806</c>).</summary>
    /// <returns>The advice completing <c>FS1806</c>'s message, or <see langword="null"/> when the line is not language 1's.</returns>
    /// <remarks>
    /// Each shape is language 1's exactly, so a language 2 line that merely starts with the same word is not
    /// caught: <c>control - N1</c> is a connection, <c>fluid = water</c> a setting. The cost is that a
    /// component may not be declared under one of these words — <c>design valve</c> reads as language 1's
    /// <c>design</c> — which is the reading far more likely to be meant.
    /// </remarks>
    private string? Language1Form(Token first, Token? second)
    {
        var third = tokens.ElementAtOrDefault(2);
        var wordAfter = second is { Kind: TokenKind.Identifier };

        return first.Text switch
        {
            "connections" when second is null =>
                "connection lines go anywhere in the circuit's block, and no header opens them",
            "schedule" when second is null =>
                "events go in a run block, 'run \"Name\":', as 'at 10 min RAD.power = 100 kW'",
            "control" when wordAfter =>
                "a controller is one declaration, 'TC1 controller:', with 'moves =', 'reads =' and 'setpoint =' below it",
            "scenarios" when wordAfter =>
                "the cases are named in the project block, 'cases = [winter, mild]'",
            "design" when wordAfter =>
                "there is no design line: sizing covers every case, the canvas chooses the case it shows, and a run starts from its 'from'",
            "project" when wordAfter =>
                "a project is 'project \"Title\":' with its settings indented below it, and the solve mode belongs to a run",
            "circuit" when wordAfter =>
                "a circuit is 'circuit \"Title\":' with 'fluid =' and 'number =' indented below it",
            "curve" when wordAfter && third is { Kind: TokenKind.Identifier } =>
                "the driver follows a colon, 'curve NAME: DRIVER'",
            "fluid" when second is { Kind: not (TokenKind.Equals or TokenKind.Minus or TokenKind.Dot) } =>
                "the fluid is a setting of its circuit, 'fluid = water'",
            "show" or "spacing" or "catalog"
                when second is { Kind: not (TokenKind.Equals or TokenKind.Minus or TokenKind.Dot) } =>
                $"it is a setting of the project block, '{first.Text} = …'",
            "inlet" or "outlet" when wordAfter && tokens.Length == 2 =>
                "circuits join through a component both name, so write the connection line in either circuit",
            "style" when wordAfter =>
                "a style is a 'style:' block in the project or a circuit, with 'colour =' and 'width =' below it",
            _ => null,
        };
    }

    /// <summary>Whether a line's first word is one of language 2's statement words, which may not name anything.</summary>
    /// <remarks><c>19</c> §Lines, blocks and names. <c>at</c> and <c>over</c> are statement words only inside a run, so they are not here.</remarks>
    private static bool IsStatementWord(string text) =>
        text is "fluidscript" or "project" or "let" or "curve" or "circuit" or "run";

    /// <summary>Reports a statement word written where a name belongs, <c>run pump</c>, with language 1's <c>FS1004</c>.</summary>
    private MalformedStatementSyntax StatementWordAsName(Token word) =>
        Fail(ParserDiagnostics.ReservedWordAsName, word.Span, new DiagnosticArgument("word", word.Text));

    /// <summary>Whether the token after <c>at</c> or <c>over</c> starts a time, which is what makes the line an event.</summary>
    /// <remarks>Anywhere else <c>at</c> is an ordinary name, so <c>at N2</c> stays a declaration.</remarks>
    private static bool StartsEventTime(Token? token) =>
        token is { Kind: TokenKind.NumberLiteral or TokenKind.QuantityLiteral or TokenKind.DateLiteral
            or TokenKind.Minus or TokenKind.OpenParenthesis };

    /// <summary>Reports <c>FS1802</c> when a statement sits outside the block it belongs in.</summary>
    /// <param name="legal">Whether the enclosing block allows the statement.</param>
    /// <param name="statement">What the line is, capitalised as the start of a sentence.</param>
    /// <param name="place">Where it belongs, completing "belongs …".</param>
    private void Place(bool legal, string statement, string place)
    {
        if (!legal)
        {
            Report(
                Language2Diagnostics.StatementOutsideItsBlock,
                LineSpan,
                new DiagnosticArgument("statement", statement),
                new DiagnosticArgument("place", place));
        }
    }

    // ---- names and values -------------------------------------------------------------------

    /// <summary>Returns the index just past the qualified name starting at an index: a word, its index, its dotted parts.</summary>
    /// <remarks>For classification only, so it asks for no touching: a malformed name is the parser's to report, not this lookahead's.</remarks>
    private int SkipQualifiedName(int index)
    {
        index = SkipIndexedName(index);

        while (tokens.ElementAtOrDefault(index) is { Kind: TokenKind.Dot }
               && tokens.ElementAtOrDefault(index + 1) is { Kind: TokenKind.Identifier })
        {
            index = SkipIndexedName(index + 1);
        }

        return index;
    }

    private int SkipIndexedName(int index) =>
        tokens.ElementAtOrDefault(index + 1) is { Kind: TokenKind.OpenBracket }
        && tokens.ElementAtOrDefault(index + 3) is { Kind: TokenKind.CloseBracket }
            ? index + 4
            : index + 1;

    /// <summary>Whether the tokens from an index are a <c>name = value</c> pair's start.</summary>
    private bool StartsParameter(int index) =>
        tokens.ElementAtOrDefault(index) is { Kind: TokenKind.Identifier }
        && tokens.ElementAtOrDefault(SkipQualifiedName(index)) is { Kind: TokenKind.Equals };

    /// <summary>Reads a language 2 value: an expression, a range of two, or a list with an optional unit after it.</summary>
    /// <remarks>
    /// <c>19</c> §Values: <c>[85, 70] C</c> and <c>30..40 min</c>. The unit after a list is read by
    /// <see cref="TakeUnitSuffix"/>; the unit on a range's upper end is lexed with its number, and applying it
    /// to both ends is the binder's.
    /// </remarks>
    private ExpressionSyntax? ParseLanguage2Value()
    {
        if (Current is { Kind: TokenKind.OpenBracket })
        {
            var list = ParseScenarioList();
            if (list is null)
            {
                return null;
            }

            var unit = TakeUnitSuffix();
            return unit.IsEmpty ? list : new UnitListSyntax(list, unit);
        }

        var from = ParseExpression();
        if (from is null || Current is not { Kind: TokenKind.DotDot })
        {
            return from;
        }

        var dots = Advance();
        var to = ParseExpression();
        return to is null ? null : new RangeExpressionSyntax(from, dots, to);
    }

    /// <summary>Takes the unit written after a list's closing bracket, when one is: <c>C</c>, or <c>kg/s</c> as three touching tokens.</summary>
    /// <returns>The unit's tokens, or an empty array when the next word is not a unit or starts the next parameter.</returns>
    /// <remarks>
    /// The lexer's unit rule, applied after a bracket: a spelling of <c>13</c>'s table, not one of the symbols
    /// language 2 drops (<see cref="LexerOptions.ExcludedUnitSymbols"/>), and not followed by <c>=</c>, <c>[</c>,
    /// or a <c>.</c> before a word.
    /// </remarks>
    private ImmutableArray<Token> TakeUnitSuffix()
    {
        if (Current is not { Kind: TokenKind.Identifier } first || StartsNextParameter(_index + 1))
        {
            return [];
        }

        if (tokens.ElementAtOrDefault(_index + 1) is { Kind: TokenKind.Slash } slash
            && tokens.ElementAtOrDefault(_index + 2) is { Kind: TokenKind.Identifier } second
            && slash.Span.Start == first.Span.End
            && second.Span.Start == slash.Span.End
            && !StartsNextParameter(_index + 3)
            && IsLanguage2Unit(first.Text + slash.Text + second.Text))
        {
            return [Advance(), Advance(), Advance()];
        }

        return IsLanguage2Unit(first.Text) ? [Advance()] : [];
    }

    private static bool IsLanguage2Unit(string symbol) =>
        UnitTable.IsSymbol(symbol) && !LexerOptions.Language2.ExcludedUnitSymbols.Contains(symbol);

    /// <summary>Reads what follows a name in a language 2 value: a reference, or a catalogue pinned to a version.</summary>
    /// <param name="name">The name, already consumed.</param>
    /// <remarks>
    /// Language 2 writes no unit after a reference (language 1's <c>L-35</c>, <c>heating kW</c>): a unit follows
    /// a number and nothing else (<c>19</c> §Values), so <c>moves = TV1 reads = TE1</c> can never read a name
    /// as a unit.
    /// </remarks>
    private ExpressionSyntax? ParseLanguage2Name(IdentifierSyntax name)
    {
        if (Current is not { Kind: TokenKind.At } at || at.Span.Start != name.Span.End)
        {
            return ParseReference(name);
        }

        // The version arrives as one number token, as in language 1's `catalog` line.
        if (tokens.ElementAtOrDefault(_index + 1) is not { Kind: TokenKind.NumberLiteral } number
            || number.Span.Start != at.Span.End
            || !number.Text.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        return new CatalogReferenceSyntax(name, new CatalogVersionSyntax(Advance(), Advance()));
    }
}
