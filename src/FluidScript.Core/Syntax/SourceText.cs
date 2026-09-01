using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Syntax;

/// <summary>
/// The text of one script, together with the line boundaries a diagnostic needs to be shown against.
/// </summary>
/// <remarks>
/// <para>
/// A pipeline stage takes this rather than a bare <see cref="string"/> so that the line index is
/// computed once per edit instead of once per diagnostic. A script under editing produces diagnostics
/// continuously, and scanning the whole text to find a line number for each one is quadratic in the
/// number of errors — which is worst exactly when the script is most broken.
/// </para>
/// <para>
/// Positions are offsets into <see cref="Text"/>, and every <see cref="TextSpan"/> in the pipeline is
/// in those coordinates. Lines and characters are zero-based; the wire contract converts, because the
/// two conventions disagree and the boundary is the one place that can state which it uses.
/// </para>
/// </remarks>
public sealed class SourceText
{
    private readonly ImmutableArray<int> _lineStarts;

    /// <summary>Initializes source text and indexes its lines.</summary>
    /// <param name="text">The script as written. May be empty; may hold any characters at all.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public SourceText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Text = text;
        _lineStarts = IndexLines(text);
    }

    /// <summary>Gets the script as written.</summary>
    /// <value>Exactly the text handed in, unnormalised: line endings, trailing whitespace and all.</value>
    public string Text { get; }

    /// <summary>Gets the number of characters in the script.</summary>
    /// <value>Zero for empty text.</value>
    public int Length => Text.Length;

    /// <summary>Gets the number of lines.</summary>
    /// <value>
    /// At least one, even for empty text: a script with no characters still has one empty line, which
    /// is where a diagnostic about the missing version directive has to point.
    /// </value>
    public int LineCount => _lineStarts.Length;

    /// <summary>Gets the character at a position.</summary>
    /// <param name="position">A zero-based offset into <see cref="Text"/>.</param>
    /// <returns>The character.</returns>
    /// <exception cref="IndexOutOfRangeException"><paramref name="position"/> is outside the text.</exception>
    public char this[int position] => Text[position];

    /// <summary>Gets the characters a span covers, without copying them.</summary>
    /// <param name="span">A span inside the text.</param>
    /// <returns>A view over <see cref="Text"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The span reaches past the end of the text.</exception>
    public ReadOnlySpan<char> Slice(TextSpan span) => Text.AsSpan(span.Start, span.Length);

    /// <summary>Copies the characters a span covers.</summary>
    /// <param name="span">A span inside the text.</param>
    /// <returns>The substring.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The span reaches past the end of the text.</exception>
    public string ToString(TextSpan span) => Text.Substring(span.Start, span.Length);

    /// <summary>Finds the line and character a position falls on.</summary>
    /// <param name="position">
    /// A zero-based offset into <see cref="Text"/>. <see cref="Length"/> itself is allowed, so that the
    /// end of an empty span at the end of the file has a position.
    /// </param>
    /// <returns>The zero-based line, and the zero-based character offset within that line.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is negative or past the end.</exception>
    public LinePosition GetLinePosition(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, Length);

        // Binary search: the index of the last line start at or before the position. A linear scan
        // would make rendering a diagnostic list O(lines x diagnostics).
        var index = _lineStarts.BinarySearch(position);
        var line = index >= 0 ? index : ~index - 1;
        return new LinePosition(line, position - _lineStarts[line]);
    }

    /// <summary>Gets the position at which a line begins.</summary>
    /// <param name="line">A zero-based line index.</param>
    /// <returns>The offset of the line's first character.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="line"/> is outside the text.</exception>
    public int GetLineStart(int line)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, LineCount);

        return _lineStarts[line];
    }

    /// <summary>Returns the script as written.</summary>
    /// <returns><see cref="Text"/>.</returns>
    public override string ToString() => Text;

    private static ImmutableArray<int> IndexLines(string text)
    {
        var starts = ImmutableArray.CreateBuilder<int>();
        starts.Add(0);

        for (var i = 0; i < text.Length; i++)
        {
            // All three conventions are indexed, because a script may be edited on either platform and
            // pasted from a third. The lexer treats them identically as one end-of-line trivium, and
            // the printer reproduces whichever was there.
            var c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                starts.Add(i + 1);
            }
            else if (c == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts.ToImmutable();
    }
}
