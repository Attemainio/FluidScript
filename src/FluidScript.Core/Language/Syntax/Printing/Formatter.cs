using System.Collections.Immutable;
using System.Text;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Language.Syntax.Printing;

/// <summary>The formatter (<c>17</c>): the canonical layout, on request, as edits.</summary>
/// <remarks>
/// <para>
/// The printer never changes whitespace; this does, and only when the user asks. The layout is
/// this project's own, since nothing outside it fixes one: leading indentation goes; tokens are
/// separated by one space where they were separated at all, with none around a parameter's
/// <c>=</c>, one around a <c>let</c>'s, none inside brackets and one after a comma; within a run of
/// consecutive non-blank statement lines the trailing comments share one column, two spaces past
/// the run's longest content, and consecutive <c>let</c>s pad their names so the <c>=</c> signs line
/// up. A blank line or a full-line comment ends a run, so one long line cannot reflow a section
/// (<c>17</c> open questions). Comment text, token text and blank lines are never changed, and a
/// <c>curve</c>'s data rows are left exactly as written.
/// </para>
/// <para>
/// Idempotent by construction (<c>17</c> invariant 7): every rule reads the tokens, not the spacing,
/// except the zero-or-one rule, which a formatted line satisfies already.
/// </para>
/// </remarks>
public static class Formatter
{
    /// <summary>Computes the edits that bring a script to the canonical layout.</summary>
    /// <param name="source">The script.</param>
    /// <returns>One edit per line that changes, in document order; none for a script already formatted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static ImmutableArray<TextEdit> Format(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var lines = Analyse(source);
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

    private static List<Line> Analyse(SourceText source)
    {
        var lexed = Lexer.Lex(source);
        var lines = new List<Line>(source.LineCount);

        for (var index = 0; index < source.LineCount; index++)
        {
            var start = source.GetLineStart(index);
            var end = index + 1 < source.LineCount ? source.GetLineStart(index + 1) : source.Length;
            var content = source.Text.AsSpan(start, end - start).TrimEnd("\r\n").ToString();
            lines.Add(new Line(index, start, content));
        }

        var inCurve = false;
        foreach (var token in lexed.Tokens)
        {
            foreach (var trivia in token.LeadingTrivia)
            {
                if (trivia.Kind == TriviaKind.Comment)
                {
                    var line = lines[source.GetLinePosition(trivia.Span.Start).Line];
                    line.Comment = source.ToString(trivia.Span);
                    line.CommentIsWhole = true;
                }
            }

            if (token.Kind != TokenKind.EndOfFile)
            {
                var line = lines[source.GetLinePosition(token.Span.Start).Line];
                if (line.Tokens.Count == 0)
                {
                    // The first token settles the line's shape (12 statement disambiguation).
                    if (token.Kind == TokenKind.Keyword)
                    {
                        inCurve = token.Keyword == ReservedWord.Curve;
                        line.Shape = token.Keyword == ReservedWord.Let ? LineShape.Let : LineShape.Directive;
                    }
                    else
                    {
                        line.Shape = inCurve ? LineShape.CurveRow : LineShape.Statement;
                    }
                }

                var previousEnd = line.Tokens.Count == 0 ? -1 : line.Tokens[^1].Span.End;
                line.Tokens.Add(token);
                line.SpacedBefore.Add(previousEnd >= 0 && token.Span.Start > previousEnd);
            }

            foreach (var trivia in token.TrailingTrivia)
            {
                if (trivia.Kind == TriviaKind.Comment)
                {
                    var line = lines[source.GetLinePosition(trivia.Span.Start).Line];
                    line.Comment = source.ToString(trivia.Span);
                    line.CommentIsWhole = false;
                }
            }
        }

        return lines;
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

            var letWidth = run.Where(static l => l.Shape == LineShape.Let && l.Tokens.Count >= 2)
                .Select(static l => l.Tokens[1].Text.Length)
                .DefaultIfEmpty(0)
                .Max();
            foreach (var line in run.Where(static l => l.Shape == LineShape.Let))
            {
                line.NamePadding = letWidth;
            }

            var commented = run.Where(static l => l.Comment is not null && !l.CommentIsWhole).ToList();
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
            if (line.Tokens.Count == 0 || line.Shape == LineShape.CurveRow)
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
        Blank,
        Directive,
        Let,
        Statement,
        CurveRow,
    }

    private sealed class Line(int index, int start, string original)
    {
        public int Index { get; } = index;

        public int Start { get; } = start;

        public string Original { get; } = original;

        public List<Token> Tokens { get; } = [];

        public List<bool> SpacedBefore { get; } = [];

        public LineShape Shape { get; set; } = LineShape.Blank;

        public string? Comment { get; set; }

        public bool CommentIsWhole { get; set; }

        public int NamePadding { get; set; }

        public int CommentColumn { get; set; }

        /// <summary>The line as formatted, or <see langword="null"/> for one the formatter leaves alone.</summary>
        public string? Render()
        {
            if (Tokens.Count == 0 || Shape == LineShape.CurveRow)
            {
                // A blank line, a full-line comment, a curve's data row: exactly as written.
                return null;
            }

            var content = RenderContent();
            if (Comment is null || CommentIsWhole)
            {
                return content;
            }

            var column = Math.Max(CommentColumn, content.Length + 2);
            return content + new string(' ', column - content.Length) + Comment;
        }

        public string RenderContent()
        {
            var builder = new StringBuilder();

            for (var i = 0; i < Tokens.Count; i++)
            {
                var token = Tokens[i];
                if (i > 0)
                {
                    builder.Append(Separator(Tokens[i - 1], token, SpacedBefore[i]));
                }

                builder.Append(token.Text);

                if (Shape == LineShape.Let && i == 1 && NamePadding > token.Text.Length)
                {
                    builder.Append(' ', NamePadding - token.Text.Length);
                }
            }

            return builder.ToString();
        }

        private string Separator(Token previous, Token current, bool spaced)
        {
            var isEquals = current.Kind == TokenKind.Equals || previous.Kind == TokenKind.Equals;
            if (isEquals)
            {
                return Shape == LineShape.Let ? " " : string.Empty;
            }

            if (previous.Kind == TokenKind.OpenParenthesis || current.Kind is TokenKind.CloseParenthesis or TokenKind.Comma)
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
            or TokenKind.NumberLiteral or TokenKind.QuantityLiteral or TokenKind.StringLiteral;
    }
}
