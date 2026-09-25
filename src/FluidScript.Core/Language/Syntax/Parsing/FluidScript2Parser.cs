using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Language.Syntax.Parsing;

/// <summary>Turns language 2 script text into a syntax tree, keeping every line.</summary>
/// <remarks>
/// <para>
/// <c>plan/10-language/19-fluidscript-2.md</c>. Language 1's parser (<see cref="FluidScriptParser"/>) is
/// untouched and still parses every file that does not declare <c>fluidscript 2</c>; the two share the lexer,
/// the line splitting, the reading of names and expressions, and the tree's statement and expression nodes.
/// </para>
/// <para>
/// <strong>Never throws, for any input</strong> (principle P4), and recovers per line as language 1 does.
/// What language 2 adds is the block: a head line and the lines indented deeper than it
/// (<see cref="BlockSyntax"/>). A block is a statement, so the root is language 1's
/// <see cref="ScriptSyntax"/> and every token appears once, in source order — the printer prints either
/// language without knowing which it is.
/// </para>
/// </remarks>
public static class FluidScript2Parser
{
    /// <summary>Parses language 2 source text into a syntax tree.</summary>
    /// <param name="source">The script source. Any characters at all; may be empty.</param>
    /// <returns>
    /// The tree, always non-null, together with every diagnostic the lexer and the parser produced. A tree
    /// containing <see cref="MalformedStatementSyntax"/> nodes is a normal result, not a failure.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static ParseResult Parse(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var lex = Lexer.Lex(source, LexerOptions.Language2);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        diagnostics.AddRange(lex.Diagnostics);

        var open = new Stack<OpenBlock>();
        open.Push(new OpenBlock(Language2Block.TopLevel, null, null, HeadWidth: -1));

        foreach (var line in FluidScriptParser.SplitLines(lex.Tokens))
        {
            var indent = Indentation(source, line[0]);

            // A line at its head's indentation or less is outside the block (`19` §Lines, blocks and names).
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
            else if (block.Kind != Language2Block.Curve
                && !string.Equals(indent, block.BodyIndent, StringComparison.Ordinal))
            {
                block = Misindented(open, block, indent, line, diagnostics);
            }

            var parser = new LineParser(source, line, diagnostics, language2: true);
            var statement = parser.ParseLanguage2(block.Kind);

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
                Language2Diagnostics.HeadWithoutColon,
                declaration.Span,
                new DiagnosticArgument("head", "declaration")));

            block.Body.RemoveAt(block.Body.Count - 1);
            var nested = new OpenBlock(Language2Block.Declaration, declaration, null, block.BodyIndent.Length)
            {
                BodyIndent = indent,
            };
            open.Push(nested);
            return nested;
        }

        diagnostics.Add(Diagnostic.Create(
            Language2Diagnostics.InconsistentIndentation,
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
    private sealed record OpenBlock(Language2Block Kind, StatementSyntax? Head, Token? Colon, int HeadWidth)
    {
        /// <summary>Gets or sets the indentation of the body's first line, which every later line must repeat.</summary>
        public string? BodyIndent { get; set; }

        /// <summary>Gets the body read so far.</summary>
        public ImmutableArray<StatementSyntax>.Builder Body { get; } = ImmutableArray.CreateBuilder<StatementSyntax>();
    }
}
