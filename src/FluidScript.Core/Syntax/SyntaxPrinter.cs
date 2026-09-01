using System.Collections.Immutable;
using System.Text;

using FluidScript.Core.Syntax.Ast;

namespace FluidScript.Core.Syntax;

/// <summary>Turns a syntax tree back into the exact text it was parsed from.</summary>
/// <remarks>
/// <para>
/// <c>Print(Parse(x)) == x</c>, byte for byte, for every input including malformed ones
/// (<c>plan/10-language/17-formatting-and-round-trip.md</c>, invariant 1). <strong>The printer never
/// tidies.</strong> Alignment, blank lines, comment columns, a unit written against its number or
/// spaced from it — all of it survives, because canvas write-back re-emits text through here and a
/// printer that reformats turns a one-character edit into a forty-line diff. Producing canonical
/// layout is the formatter's job, and the formatter is a command the user runs, never a side effect
/// of printing.
/// </para>
/// <para>
/// It reconstructs from the tree rather than slicing the source over the node's span, and that is the
/// whole point of it: a slice would reproduce the file no matter what the tree had lost. Walking the
/// tokens and their trivia is what makes a dropped token, or a gap between two spans, show up as a
/// difference instead of hiding until write-back mangles someone's script.
/// </para>
/// <para>
/// A trivium addresses its text rather than holding it (<see cref="Trivia"/>), so printing needs the
/// <see cref="SourceText"/> the tree was parsed from. <see cref="Print(ParseResult)"/> is the form that
/// cannot be handed the wrong one; the overloads taking a source exist for printing one node out of a
/// tree, and pairing a node with a different source than it was parsed from is a caller error that
/// produces nonsense rather than an exception.
/// </para>
/// </remarks>
public static class SyntaxPrinter
{
    /// <summary>Prints a whole parsed script.</summary>
    /// <param name="result">The parse to print.</param>
    /// <returns>The source text the parse was produced from, reconstructed from its tree.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public static string Print(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return Print(result.Source, result.Root);
    }

    /// <summary>Prints one node, including the trivia on its first and last tokens.</summary>
    /// <param name="source">The text <paramref name="node"/> was parsed from.</param>
    /// <param name="node">The node to print.</param>
    /// <returns>
    /// Exactly the characters of <see cref="SyntaxNode.FullSpan"/> — so a statement prints with its
    /// indentation and its trailing comment, and the root prints as the whole file.
    /// </returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static string Print(SourceText source, SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(node);

        var destination = new StringBuilder(node.FullSpan.Length);
        Print(source, node, destination);

        return destination.ToString();
    }

    /// <summary>Appends one node's text to a builder, for printing several without an intermediate string.</summary>
    /// <param name="source">The text <paramref name="node"/> was parsed from.</param>
    /// <param name="node">The node to print.</param>
    /// <param name="destination">The builder to append to.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static void Print(SourceText source, SyntaxNode node, StringBuilder destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(destination);

        foreach (var token in node.Tokens)
        {
            Append(source, token.LeadingTrivia, destination);
            destination.Append(token.Text);
            Append(source, token.TrailingTrivia, destination);
        }
    }

    private static void Append(SourceText source, ImmutableArray<Trivia> trivia, StringBuilder destination)
    {
        foreach (var trivium in trivia)
        {
            destination.Append(source.Slice(trivium.Span));
        }
    }
}
