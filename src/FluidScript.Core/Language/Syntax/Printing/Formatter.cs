using System.Collections.Immutable;
using System.Text;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Compatibility;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Language.Syntax.Printing;

/// <summary>The formatter (<c>17</c>): language 2's canonical layout, on request, as edits.</summary>
/// <remarks>
/// <para>
/// The printer never changes whitespace; this does, and only when the user asks. The layout is this project's own,
/// since nothing outside it fixes one (<c>17</c> §The formatter's layout). A line is indented two spaces for each block
/// it sits in, as the parse reads it (<c>19</c> §Lines, blocks and names). Within a line, its <em>fields</em> -- a
/// declaration's name, kind, <c>at</c> and each <c>name = value</c>, each setting on a shared line, each property of a
/// pipe, an event's target -- are two spaces apart, because in language 2 a value runs to the next <c>name =</c> and
/// one space would run the pairs together to the eye; a pipe's first property is three spaces past its link, which
/// sets the pipe apart from the connection it sits on, as the samples were written. An <c>=</c> has one space on each side, a <c>:</c> none before and
/// one after, and a connection's <c>-</c> one on each side; inside a value the writer's spacing stands, collapsed to one
/// space at most, with none inside brackets or before a comma and one after it. Within a run of consecutive
/// statement lines the trailing comments share one column, two spaces past the run's longest content, and
/// consecutive <c>let</c>s, and consecutive one-setting lines of one block, pad their names so the <c>=</c> signs line
/// up -- as a project, a controller or an exchanger's block is written in <c>19</c>'s reference and the syntax tour.
/// </para>
/// <para>
/// Blank lines, full-line comments, a curve's rows (a table the user aligns) and a line the parser could not read are
/// left exactly as written; token text and comment text are never changed. A file that declares another major, or two,
/// is left alone: this layout would say something else in it.
/// </para>
/// <para>
/// Idempotent by construction (<c>17</c> invariant 7): every rule reads the tokens and the tree, not the spacing,
/// except the zero-or-one rule inside a value, which a formatted line satisfies already. The tree a formatted file
/// parses to is the tree the original parsed to, block for block, because the indentation written is the depth read.
/// </para>
/// </remarks>
public static class Formatter
{
    private const int IndentWidth = 2;

    /// <summary>Computes the edits that bring a script to the canonical layout.</summary>
    /// <param name="source">The script.</param>
    /// <returns>
    /// One edit per line that changes, in document order; none for a script already formatted, and none for a file
    /// whose version line names a major other than 2.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static ImmutableArray<TextEdit> Format(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var compatibility = ScriptCompatibility.Inspect(source);
        var languageTwo = compatibility.DetectedMajor is { Value: 2 }
            || compatibility.Disposition == CompatibilityDisposition.UnversionedDraft;
        if (!languageTwo)
        {
            return [];
        }

        var lines = Analyse(source, FluidScript2Parser.Parse(source));
        AlignRuns(lines);

        var edits = ImmutableArray.CreateBuilder<TextEdit>();
        foreach (var line in lines)
        {
            var formatted = line.Render();
            if (formatted is not null && !string.Equals(formatted, line.Original, StringComparison.Ordinal))
            {
                edits.Add(new TextEdit(new TextSpan(line.Start, line.Original.Length), formatted));
            }
        }

        return edits.ToImmutable();
    }

    /// <summary>Formats a script whole, for a test or a tool that wants the text rather than the edits.</summary>
    /// <param name="text">The script.</param>
    /// <returns>The formatted text.</returns>
    public static string FormatText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var source = new SourceText(text);
        var builder = new StringBuilder(text);

        // Edits never overlap, so applying them last to first keeps every earlier offset valid.
        foreach (var edit in Format(source).Reverse())
        {
            builder.Remove(edit.Span.Start, edit.Span.Length).Insert(edit.Span.Start, edit.NewText);
        }

        return builder.ToString();
    }

    private static List<Line> Analyse(SourceText source, ParseResult parse)
    {
        var lines = new List<Line>(source.LineCount);

        for (var index = 0; index < source.LineCount; index++)
        {
            var start = source.GetLineStart(index);
            var end = index + 1 < source.LineCount ? source.GetLineStart(index + 1) : source.Length;
            var content = source.Text.AsSpan(start, end - start).TrimEnd("\r\n").ToString();
            lines.Add(new Line(start, content));
        }

        foreach (var statement in parse.Root.Statements)
        {
            Visit(statement, 0, source, lines);
        }

        foreach (var token in parse.Root.Tokens)
        {
            foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
            {
                if (trivia.Kind == TriviaKind.Comment)
                {
                    var line = lines[source.GetLinePosition(trivia.Span.Start).Line];

                    // A comment on a line with tokens trails them; one alone on its line is left as written.
                    line.Comment = source.ToString(trivia.Span);
                }
            }
        }

        return lines;
    }

    private static void Visit(StatementSyntax statement, int depth, SourceText source, List<Line> lines)
    {
        switch (statement)
        {
            case BlockSyntax block:
                var head = block.Colon is { } colon ? [.. block.Head.Tokens, colon] : block.Head.Tokens;
                Register(block.Head, head, depth, source, lines);

                var rows = block.Head is DriverCurveHeadSyntax or CurveHeaderSyntax;
                foreach (var child in block.Body)
                {
                    if (rows && child is CurveRowSyntax)
                    {
                        Leave(child, source, lines);
                    }
                    else
                    {
                        Visit(child, depth + 1, source, lines);
                    }
                }

                break;

            case CurveRowSyntax or MalformedStatementSyntax:
                Leave(statement, source, lines);
                break;

            default:
                Register(statement, statement.Tokens, depth, source, lines);
                break;
        }
    }

    private static void Leave(StatementSyntax statement, SourceText source, List<Line> lines)
    {
        foreach (var token in statement.Tokens)
        {
            lines[source.GetLinePosition(token.Span.Start).Line].Leave = true;
        }
    }

    private static void Register(
        StatementSyntax statement, ImmutableArray<Token> tokens, int depth, SourceText source, List<Line> lines)
    {
        if (tokens.IsEmpty)
        {
            return;
        }

        var line = lines[source.GetLinePosition(tokens[0].Span.Start).Line];
        if (line.Tokens.Count > 0
            || tokens.Any(token => source.GetLinePosition(token.Span.Start).Line != source.GetLinePosition(tokens[0].Span.Start).Line))
        {
            // One statement to a line is what the grammar guarantees; anything else is left as written.
            line.Leave = true;
            return;
        }

        line.Depth = depth;
        line.Shape = statement switch
        {
            LetBindingSyntax => LineShape.Let,
            SettingLineSyntax { Assignments.Length: 1 } => LineShape.Setting,
            ConnectionSyntax or PipedConnectionSyntax => LineShape.Connection,
            _ => LineShape.Statement,
        };

        foreach (var field in FieldStarts(statement))
        {
            line.FieldStarts.Add(field);
        }

        if (statement is PipedConnectionSyntax { Properties: [var first, ..] })
        {
            line.PipeStart = first.Tokens[0];
        }

        var previousEnd = -1;
        foreach (var token in tokens)
        {
            line.Tokens.Add(token);
            line.SpacedBefore.Add(previousEnd >= 0 && token.Span.Start > previousEnd);
            previousEnd = token.Span.End;
        }
    }

    /// <summary>The tokens that open a field of their line: two spaces go before each.</summary>
    private static IEnumerable<Token> FieldStarts(StatementSyntax statement)
    {
        switch (statement)
        {
            case ComponentDeclarationSyntax declaration:
                yield return declaration.Kind.Tokens[0];

                if (declaration.AtKeyword is { } at)
                {
                    yield return at;
                }

                foreach (var parameter in declaration.Parameters)
                {
                    yield return parameter.Tokens[0];
                }

                if (declaration.SizedAtKeyword is { } sizedAt)
                {
                    yield return sizedAt;
                }

                foreach (var point in declaration.SizingPoint.Skip(declaration.SizedAtKeyword is null ? 0 : 1))
                {
                    yield return point.Tokens[0];
                }

                break;

            case SettingLineSyntax setting:
                foreach (var assignment in setting.Assignments.Skip(1))
                {
                    yield return assignment.Tokens[0];
                }

                break;

            case PipedConnectionSyntax piped:
                foreach (var property in piped.Properties)
                {
                    yield return property.Tokens[0];
                }

                break;

            case DisturbanceSyntax change:
                yield return change.Target.Tokens[0];
                break;
        }
    }

    private static void AlignRuns(List<Line> lines)
    {
        var run = new List<Line>();

        void Close()
        {
            if (run.Count == 0)
            {
                return;
            }

            // Consecutive lets, or consecutive one-setting lines at one depth, line up their `=`.
            var group = new List<Line>();
            void Align()
            {
                var width = group.Select(static l => l.NameWidth()).DefaultIfEmpty(0).Max();
                foreach (var member in group)
                {
                    member.NameColumn = width;
                }

                group.Clear();
            }

            foreach (var line in run)
            {
                var aligns = line.Shape is LineShape.Let or LineShape.Setting && line.EqualsIndex > 0;
                if (group.Count > 0 && (!aligns || group[0].Shape != line.Shape || group[0].Depth != line.Depth))
                {
                    Align();
                }

                if (aligns)
                {
                    group.Add(line);
                }
            }

            Align();

            var commented = run.Where(static l => l.Comment is not null).ToList();
            if (commented.Count > 0)
            {
                var column = commented.Max(static l => l.RenderContent().Length) + 2;
                foreach (var line in commented)
                {
                    line.CommentColumn = column;
                }
            }

            run.Clear();
        }

        foreach (var line in lines)
        {
            if (line.Tokens.Count == 0 || line.Leave)
            {
                Close();
            }
            else
            {
                run.Add(line);
            }
        }

        Close();
    }

    private enum LineShape
    {
        Statement,
        Let,
        Setting,
        Connection,
    }

    private sealed class Line(int start, string original)
    {
        public int Start { get; } = start;

        public string Original { get; } = original;

        public List<Token> Tokens { get; } = [];

        public List<bool> SpacedBefore { get; } = [];

        public HashSet<Token> FieldStarts { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Gets or sets the first token of a pipe's properties, which stands three spaces past its link.</summary>
        public Token? PipeStart { get; set; }

        public LineShape Shape { get; set; }

        public int Depth { get; set; }

        /// <summary>Gets or sets whether the line is written as it stands: a curve row, or one the parser could not read.</summary>
        public bool Leave { get; set; }

        public string? Comment { get; set; }

        /// <summary>Gets or sets how wide the part before the <c>=</c> is padded, indentation aside; 0 for no padding.</summary>
        public int NameColumn { get; set; }

        /// <summary>Gets the index of the line's first <c>=</c>, or -1.</summary>
        public int EqualsIndex => Tokens.FindIndex(static token => token.Kind == TokenKind.Equals);

        /// <summary>The width of what precedes the <c>=</c>, as rendered and without indentation.</summary>
        public int NameWidth()
        {
            var builder = new StringBuilder();
            for (var i = 0; i < EqualsIndex; i++)
            {
                if (i > 0)
                {
                    builder.Append(Separator(Tokens[i - 1], Tokens[i], SpacedBefore[i]));
                }

                builder.Append(Tokens[i].Text);
            }

            return builder.Length;
        }

        public int CommentColumn { get; set; }

        /// <summary>The line as formatted, or <see langword="null"/> for one the formatter leaves alone.</summary>
        public string? Render()
        {
            if (Tokens.Count == 0 || Leave)
            {
                // A blank line, a full-line comment, a curve's data row, a malformed line: exactly as written.
                return null;
            }

            var content = RenderContent();
            if (Comment is null)
            {
                return content;
            }

            var column = Math.Max(CommentColumn, content.Length + 2);
            return content + new string(' ', column - content.Length) + Comment;
        }

        public string RenderContent()
        {
            var indent = IndentWidth * Depth;
            var builder = new StringBuilder(new string(' ', indent));
            var equals = EqualsIndex;

            for (var i = 0; i < Tokens.Count; i++)
            {
                var token = Tokens[i];
                if (i == equals && builder.Length - indent < NameColumn)
                {
                    builder.Append(' ', NameColumn - (builder.Length - indent));
                }

                if (i > 0)
                {
                    builder.Append(Separator(Tokens[i - 1], token, SpacedBefore[i]));
                }

                builder.Append(token.Text);
            }

            return builder.ToString();
        }

        private string Separator(Token previous, Token current, bool spaced)
        {
            if (ReferenceEquals(current, PipeStart))
            {
                return "   ";
            }

            if (FieldStarts.Contains(current))
            {
                return "  ";
            }

            if (current.Kind == TokenKind.Colon)
            {
                return string.Empty;
            }

            if (previous.Kind is TokenKind.Colon or TokenKind.Equals || current.Kind == TokenKind.Equals)
            {
                return " ";
            }

            if (Shape == LineShape.Connection && (previous.Kind == TokenKind.Minus || current.Kind == TokenKind.Minus))
            {
                return " ";
            }

            if (previous.Kind is TokenKind.Dot || current.Kind is TokenKind.Dot)
            {
                return string.Empty;
            }

            if (previous.Kind is TokenKind.OpenParenthesis or TokenKind.OpenBracket
                || current.Kind is TokenKind.CloseParenthesis or TokenKind.CloseBracket or TokenKind.Comma)
            {
                return string.Empty;
            }

            if (previous.Kind == TokenKind.Comma)
            {
                return " ";
            }

            // Two words or literals in a row always had a space and keep one; between an operator and
            // its operand the writer's choice stands, collapsed to one space at most.
            var bothWords = IsWordLike(previous) && IsWordLike(current);
            return bothWords || spaced ? " " : string.Empty;
        }

        private static bool IsWordLike(Token token) => token.Kind is TokenKind.Identifier or TokenKind.Keyword
            or TokenKind.NumberLiteral or TokenKind.QuantityLiteral or TokenKind.StringLiteral or TokenKind.DateLiteral;
    }
}
