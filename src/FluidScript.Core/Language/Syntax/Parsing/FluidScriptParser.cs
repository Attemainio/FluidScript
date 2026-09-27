using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Language.Syntax.Parsing;

/// <summary>Turns script text into a syntax tree, keeping every line.</summary>
/// <remarks>
/// <para>
/// <c>plan/10-language/12-grammar.md</c> is the grammar it reads, and <c>plan/10-language/19-fluidscript-2.md</c>
/// the reasoning behind it.
/// </para>
/// <para>
/// <strong>Never throws, for any input</strong> (principle P4), and recovers per line: a line that cannot be
/// read is a <see cref="MalformedStatementSyntax"/> and every other line is unaffected. A block is a head line
/// and the lines indented deeper than it (<see cref="BlockSyntax"/>); a block is a statement, so every token
/// appears once in the <see cref="ScriptSyntax"/>, in source order, which is what the printer relies on.
/// </para>
/// </remarks>
public static class FluidScriptParser
{
    /// <summary>Parses source text into a syntax tree.</summary>
    /// <param name="source">The script source. Any characters at all; may be empty.</param>
    /// <returns>
    /// The tree, always non-null, together with every diagnostic the lexer and the parser produced. A tree
    /// containing <see cref="MalformedStatementSyntax"/> nodes is a normal result, not a failure.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static ParseResult Parse(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var lex = Lexer.Lex(source);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        diagnostics.AddRange(lex.Diagnostics);

        var open = new Stack<OpenBlock>();
        open.Push(new OpenBlock(LineBlock.TopLevel, null, null, HeadWidth: -1));

        foreach (var line in SplitLines(lex.Tokens))
        {
            var indent = Indentation(source, line[0]);

            // A line at its head's indentation or less is outside the block (`12` §Lines and blocks).
            while (open.Count > 1 && indent.Length <= open.Peek().HeadWidth)
            {
                Close(open);
            }

            // A curve's rows are a table whose columns the user aligns, `  -26   85` over `   18   65`, so
            // a row needs only to be deeper than its header.
            var block = open.Peek();
            if (block.BodyIndent is null)
            {
                block.BodyIndent = indent;
            }
            else if (block.Kind != LineBlock.Curve
                && !string.Equals(indent, block.BodyIndent, StringComparison.Ordinal))
            {
                block = Misindented(open, block, indent, line, diagnostics);
            }

            var parser = new LineParser(source, line, diagnostics);
            var statement = parser.ParseLine(block.Kind);

            if (parser.Opens is { } kind)
            {
                open.Push(new OpenBlock(kind, statement, parser.OpeningColon, indent.Length));
            }
            else
            {
                block.Body.Add(statement);
            }
        }

        while (open.Count > 1)
        {
            Close(open);
        }

        var root = new ScriptSyntax(open.Peek().Body.ToImmutable(), lex.Tokens[^1]);
        return new ParseResult(source, root, diagnostics.ToImmutable());
    }

    /// <summary>Decides where a line indented unlike its block's body belongs.</summary>
    /// <returns>The block the line is read in.</returns>
    /// <remarks>
    /// <para>
    /// A line indented deeper than a declaration written without its <c>:</c> is that declaration's parameter
    /// line: the declaration becomes the head of a block and the missing colon is <c>FS1812</c>, reported
    /// once on the declaration rather than once per line under it.
    /// </para>
    /// <para>
    /// Anything else is <c>FS1801</c>, and the line stays in the block it is indented under — deeper than the
    /// block's head, so inside it by the block rule, which is the nearer level.
    /// </para>
    /// </remarks>
    private static OpenBlock Misindented(
        Stack<OpenBlock> open,
        OpenBlock block,
        string indent,
        ImmutableArray<Token> line,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (indent.Length > block.BodyIndent!.Length
            && block.Body.Count > 0
            && block.Body[^1] is ComponentDeclarationSyntax declaration)
        {
            diagnostics.Add(Diagnostic.Create(
                BlockDiagnostics.HeadWithoutColon,
                declaration.Span,
                new DiagnosticArgument("head", "declaration")));

            block.Body.RemoveAt(block.Body.Count - 1);
            var nested = new OpenBlock(LineBlock.Declaration, declaration, null, block.BodyIndent.Length)
            {
                BodyIndent = indent,
            };
            open.Push(nested);
            return nested;
        }

        diagnostics.Add(Diagnostic.Create(
            BlockDiagnostics.InconsistentIndentation,
            TextSpan.FromBounds(line[0].Span.Start, line[^1].Span.End)));
        return block;
    }

    /// <summary>The whitespace between a line's start and its first token, as written.</summary>
    /// <remarks>
    /// Compared as text, so a tab and a space are different indentation (<c>FS1801</c>), and measured in
    /// characters to decide nesting. Only whitespace can precede a line's first token: a comment runs to the
    /// end of its line.
    /// </remarks>
    private static string Indentation(SourceText source, Token first)
    {
        var lineStart = source.GetLineStart(source.GetLinePosition(first.Span.Start).Line);

        // A byte-order mark is not indentation: the first line is at the top level with or without one.
        if (lineStart == 0 && source.Length > 0 && source[0] == '\uFEFF')
        {
            lineStart = 1;
        }

        return source.ToString(TextSpan.FromBounds(lineStart, first.Span.Start));
    }

    private static void Close(Stack<OpenBlock> open)
    {
        var closed = open.Pop();
        open.Peek().Body.Add(new BlockSyntax(closed.Head!, closed.Colon, closed.Body.ToImmutable()));
    }

    /// <summary>A block whose body is still being read.</summary>
    /// <param name="Kind">What the block is, which decides what its lines may be.</param>
    /// <param name="Head">The head line; <see langword="null"/> only for the top level.</param>
    /// <param name="Colon">The head's <c>:</c>, when it was written.</param>
    /// <param name="HeadWidth">The head's indentation in characters; −1 for the top level, which nothing closes.</param>
    private sealed record OpenBlock(LineBlock Kind, StatementSyntax? Head, Token? Colon, int HeadWidth)
    {
        /// <summary>Gets or sets the indentation of the body's first line, which every later line must repeat.</summary>
        public string? BodyIndent { get; set; }

        /// <summary>Gets the body read so far.</summary>
        public ImmutableArray<StatementSyntax>.Builder Body { get; } = ImmutableArray.CreateBuilder<StatementSyntax>();
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
}
