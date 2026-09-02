using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Syntax.Ast;

namespace FluidScript.Core.Syntax;

/// <summary>Turns script text into a syntax tree, keeping every line.</summary>
/// <remarks>
/// <para>
/// <strong>Never throws, for any input</strong> (principle P4). A line that cannot be parsed becomes a
/// <see cref="MalformedStatementSyntax"/> holding its tokens, and parsing resumes at the next line.
/// Recovery is line-granular because the language is line-oriented: there is no construct spanning
/// lines to resynchronise into, so a token-level recovery would have nothing to aim at.
/// </para>
/// <para>
/// <strong>One token of lookahead classifies every statement</strong>
/// (<c>plan/10-language/11-language-overview.md</c>, invariant 7). <see cref="Classify"/> takes a
/// line's first token, at most one more, and the section it sits in — and nothing else, which is the
/// invariant expressed as a signature rather than as a comment.
/// </para>
/// </remarks>
public static class FluidScriptParser
{
    /// <summary>Parses source text into a syntax tree.</summary>
    /// <param name="source">The script source. Any characters at all; may be empty.</param>
    /// <returns>
    /// The tree, always non-null, together with every diagnostic the lexer and the parser produced. A
    /// tree containing <see cref="MalformedStatementSyntax"/> nodes is a normal result, not a failure.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static ParseResult Parse(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var lex = Lexer.Lex(source);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        diagnostics.AddRange(lex.Diagnostics);

        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        var state = new ScriptState();

        foreach (var line in SplitLines(lex.Tokens))
        {
            var kind = Classify(line[0], line.Length > 1 ? line[1] : null, state.Section);
            statements.Add(new LineParser(source, line, diagnostics).Parse(kind, state));
        }

        var root = new ScriptSyntax(statements.ToImmutable(), lex.Tokens[^1]);
        return new ParseResult(source, root, diagnostics.ToImmutable());
    }

    /// <summary>Decides what a line is, from its first token and at most one more.</summary>
    /// <param name="first">The line's first token.</param>
    /// <param name="second">The next token, or <see langword="null"/> for a one-token line.</param>
    /// <param name="section">Which of the circuit's three sections the line sits in.</param>
    /// <returns>What the line will be parsed as.</returns>
    /// <remarks>
    /// <para>
    /// The whole classification rule, in three clauses. If the first token is a reserved word, the
    /// statement is the one that word introduces. Otherwise, inside a <c>schedule</c> section every
    /// line is a disturbance — <c>at</c> and <c>over</c> are ordinary identifiers, classified by
    /// position, because reserving two common English words buys nothing. Otherwise a second token of
    /// <c>-</c> — or <c>.</c>, for a port-qualified first endpoint (<c>D-56</c>) — makes it a
    /// connection, and anything else a component declaration.
    /// </para>
    /// <para>
    /// That last clause is what makes <c>N1 - N2</c> and <c>N1 node t=6</c> different statements in the
    /// same section, and it is the only lookahead the parser has.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="first"/> is <see langword="null"/>.</exception>
    public static StatementKind Classify(Token first, Token? second, ScriptSection section)
    {
        ArgumentNullException.ThrowIfNull(first);

        if (first.Kind == TokenKind.Keyword)
        {
            return first.Keyword switch
            {
                ReservedWord.Fluidscript => StatementKind.Version,
                ReservedWord.Project => StatementKind.Project,
                ReservedWord.Spacing => StatementKind.Spacing,
                ReservedWord.Circuit => StatementKind.Circuit,
                ReservedWord.Fluid => StatementKind.Fluid,
                ReservedWord.Catalog => StatementKind.Catalog,
                ReservedWord.Style => StatementKind.Style,
                ReservedWord.Show => StatementKind.Show,
                ReservedWord.Let => StatementKind.Let,
                ReservedWord.Connections => StatementKind.ConnectionsHeader,
                ReservedWord.Schedule => StatementKind.ScheduleHeader,
                ReservedWord.Supply or ReservedWord.Return => StatementKind.Attachment,
                ReservedWord.Control => StatementKind.Control,
                ReservedWord.Curve => StatementKind.CurveHeader,
                ReservedWord.Design => StatementKind.Design,

                // `dynamic` and `static` qualify another directive and introduce nothing.
                _ => StatementKind.Unclassifiable,
            };
        }

        if (section == ScriptSection.Schedule)
        {
            return StatementKind.Disturbance;
        }

        // Every non-keyword line inside a curve section is one of its rows. Classified by section
        // A curve row is recognised by its own first token, in every section rather than only inside a
        // curve. Nothing else in the language begins with a number or a minus — an identifier may start
        // with a digit (`3WV`), but the lexer classifies that as an identifier, not a number — so this
        // costs no lookahead and stays inside invariant 7.
        //
        // Classifying by shape rather than by section is what lets both messages be specific: a row
        // outside a curve is FS1115 and says what the line is, and a declaration *inside* one is
        // FS1103 and says where it is. Reading every line in a curve section as a row would turn a
        // forgotten `circuit` header into a file of malformed rows, and a curve section sits at the
        // top of the file, so everything after it would be swallowed.
        if (first.Kind is TokenKind.NumberLiteral or TokenKind.Minus)
        {
            return StatementKind.CurveRow;
        }

        // `-` for `N1 - N2`; `.` for a port-qualified first endpoint, `3WV.b - N3` (D-56). Nothing
        // else can put a `.` there: a declaration is two identifiers, and a name holds no dot.
        return second?.Kind is TokenKind.Minus or TokenKind.Dot
            ? StatementKind.Connection
            : StatementKind.Declaration;
    }

    /// <summary>Groups tokens into lines.</summary>
    /// <remarks>
    /// A line break is a trivium, and no token spans one, so a line begins wherever a token's leading
    /// trivia holds an end-of-line. Blank lines produce no tokens at all — their newlines ride along in
    /// the next token's leading trivia — which is exactly the attachment rule the printer relies on.
    /// </remarks>
    private static ImmutableArray<ImmutableArray<Token>> SplitLines(ImmutableArray<Token> tokens)
    {
        var lines = ImmutableArray.CreateBuilder<ImmutableArray<Token>>();
        var current = ImmutableArray.CreateBuilder<Token>();

        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.EndOfFile)
            {
                break;
            }

            if (current.Count > 0
                && token.LeadingTrivia.Any(static trivia => trivia.Kind == TriviaKind.EndOfLine))
            {
                lines.Add(current.ToImmutable());
                current.Clear();
            }

            current.Add(token);
        }

        if (current.Count > 0)
        {
            lines.Add(current.ToImmutable());
        }

        return lines.ToImmutable();
    }

    /// <summary>What the parser remembers between lines.</summary>
    /// <remarks>
    /// Everything here is scoped to a circuit except the two file-wide directives (<c>D-52</c>). A
    /// <c>circuit</c> header resets the rest, which is what makes several circuits in one file legal
    /// and what makes a second <c>connections</c> section in the *same* circuit a warning.
    /// </remarks>
    internal sealed class ScriptState
    {
        public ScriptSection Section { get; set; } = ScriptSection.Declaration;

        public bool SeenCircuit { get; set; }

        public bool SeenProject { get; set; }

        public bool SeenSpacing { get; set; }

        public bool SeenConnections { get; set; }

        public bool SeenSchedule { get; set; }

        public bool SeenSupply { get; set; }

        public bool SeenReturn { get; set; }

        public void BeginCircuit()
        {
            Section = ScriptSection.Declaration;
            SeenCircuit = true;
            SeenConnections = false;
            SeenSchedule = false;
            SeenSupply = false;
            SeenReturn = false;
        }
    }
}
