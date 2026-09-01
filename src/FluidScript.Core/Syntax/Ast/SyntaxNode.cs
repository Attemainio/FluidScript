using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Syntax.Ast;

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

    /// <summary>Gets the trivia before this node, in source order.</summary>
    /// <value>Its first token's leading trivia.</value>
    public ImmutableArray<Trivia> LeadingTrivia => Tokens[0].LeadingTrivia;

    /// <summary>Gets the trivia after this node up to the next line break, in source order.</summary>
    /// <value>Its last token's trailing trivia.</value>
    public ImmutableArray<Trivia> TrailingTrivia => Tokens[^1].TrailingTrivia;
}

/// <summary>Base of the statement hierarchy: one line of a script.</summary>
public abstract record StatementSyntax : SyntaxNode;

/// <summary>Base of the expression hierarchy.</summary>
public abstract record ExpressionSyntax : SyntaxNode;

/// <summary>Whether a fluid or a project is solved as an equilibrium or in time.</summary>
public enum FluidMode
{
    /// <summary>Solved as a steady state.</summary>
    Static = 1,

    /// <summary>Solved in time.</summary>
    Dynamic,
}

/// <summary>Which side of a subcircuit's attachment a statement declares.</summary>
/// <remarks><c>D-33</c>. <c>in</c> and <c>out</c> were the obvious spelling and are lexically
/// impossible, which is why <c>FS1109</c> exists.</remarks>
public enum AttachmentDirection
{
    /// <summary>Takes flow from the parent circuit.</summary>
    Supply = 1,

    /// <summary>Returns flow to the parent circuit.</summary>
    Return,
}

/// <summary>The lexical shape of one <c>style</c> token.</summary>
/// <remarks>
/// Shape only. Which category a token belongs to — colour, width, corner treatment, line pattern —
/// needs the colour and corner registries and is decided at bind time, along with <c>FS1201</c> and
/// <c>FS1202</c>.
/// </remarks>
public enum StyleTokenKind
{
    /// <summary>A bare word: a colour name, or a corner keyword.</summary>
    Word = 1,

    /// <summary>A number with no unit.</summary>
    Number,

    /// <summary>A number with a unit, such as a stroke width in <c>px</c>.</summary>
    Quantity,

    /// <summary>Quoted text, which is how a hex colour is written (<c>D-13</c>).</summary>
    Quoted,

    /// <summary>A line pattern: <c>-</c>, <c>--</c>, <c>..</c> or <c>-.</c>.</summary>
    /// <remarks>Recombined here from the two tokens the lexer produced. It is the one place the
    /// grammar is not context-free, and it is contained to this production deliberately.</remarks>
    Pattern,
}
