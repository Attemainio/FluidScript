using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Printing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Language.Syntax.Ast;

/// <summary>One node of a parsed script.</summary>
/// <remarks>
/// <para>
/// <strong>A node holds the tokens it consumes</strong> — its keywords and its punctuation, not only
/// its structural children (<c>D-55</c>). Trivia hangs off those tokens, which is the only model that
/// round-trips: no node is a <c>let</c>, an <c>=</c>, a <c>-</c> or a <c>(</c>, so a tree that dropped
/// them would drop every run of whitespace around them with them.
/// </para>
/// <para>
/// Span and trivia are therefore <em>derived</em>, never supplied. The parser never computes a span,
/// which is what makes <c>plan/10-language/12-grammar.md</c>'s invariant 3 — a parent's span contains
/// every child's — hold by construction rather than by arithmetic being right at forty sites.
/// </para>
/// </remarks>
public abstract record SyntaxNode
{
    /// <summary>Gets every token this node and its descendants consume, in source order.</summary>
    /// <value>
    /// Never empty: a node that consumed no token could not have been parsed. Concatenating each
    /// token's leading trivia, its text and its trailing trivia, over the whole tree, reproduces the
    /// source byte for byte.
    /// </value>
    public abstract ImmutableArray<Token> Tokens { get; }

    /// <summary>Gets the span in the source text, excluding trivia.</summary>
    /// <value>From the first token's start to the last token's end.</value>
    public TextSpan Span
    {
        get
        {
            var tokens = Tokens;
            return TextSpan.FromBounds(tokens[0].Span.Start, tokens[^1].Span.End);
        }
    }

    /// <summary>Gets the span this node occupies including the trivia on its outermost tokens.</summary>
    /// <value>
    /// From the start of the first token's leading trivia to the end of the last token's trailing
    /// trivia, which for the root is the whole file. This is the span
    /// <see cref="SyntaxPrinter.Print(SourceText, SyntaxNode)"/> reproduces, and the one a text edit
    /// has to respect: replacing <see cref="Span"/> alone would leave a statement's indentation and
    /// its trailing comment behind.
    /// </value>
    public TextSpan FullSpan
    {
        get
        {
            var tokens = Tokens;
            var first = tokens[0];
            var last = tokens[^1];

            return TextSpan.FromBounds(
                first.LeadingTrivia.IsEmpty ? first.Span.Start : first.LeadingTrivia[0].Span.Start,
                last.TrailingTrivia.IsEmpty ? last.Span.End : last.TrailingTrivia[^1].Span.End);
        }
    }

    /// <summary>Gets the trivia before this node, in source order.</summary>
    /// <value>Its first token's leading trivia.</value>
    public ImmutableArray<Trivia> LeadingTrivia => Tokens[0].LeadingTrivia;

    /// <summary>Gets the trivia after this node up to the next line break, in source order.</summary>
    /// <value>Its last token's trailing trivia.</value>
    public ImmutableArray<Trivia> TrailingTrivia => Tokens[^1].TrailingTrivia;
}
