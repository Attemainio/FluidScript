namespace FluidScript.Core.Diagnostics;

/// <summary>
/// A contiguous region of a script, addressed by a character offset and a length.
/// </summary>
/// <remarks>
/// <para>
/// Offsets are into the script as a sequence of UTF-16 code units — the same unit
/// <see cref="string"/> is indexed by — counted from the first character of the file, not from the
/// start of a line. Line and column are a presentation of a span, computed where a message is
/// rendered; nothing in the pipeline carries them, because an edit anywhere above a component would
/// invalidate every line number below it.
/// </para>
/// <para>
/// A span is a half-open interval <c>[Start, End)</c>. An empty span is legal and meaningful: it is
/// where something is missing, which is what a diagnostic about an omitted token points at.
/// </para>
/// </remarks>
public readonly record struct TextSpan
{
    /// <summary>Initializes a span at <paramref name="start"/> covering <paramref name="length"/> characters.</summary>
    /// <param name="start">Zero-based offset of the first character.</param>
    /// <param name="length">Number of characters covered. Zero denotes a position rather than a range.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="start"/> or <paramref name="length"/> is negative, or their sum would overflow.
    /// Both are programming errors — a stage computing a span from user input is what produces them —
    /// and neither is reachable from a malformed script, which is why this throws rather than
    /// clamping.
    /// </exception>
    public TextSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, int.MaxValue - start);

        Start = start;
        Length = length;
    }

    /// <summary>Gets the offset of the first character in the span.</summary>
    /// <value>Zero-based, in UTF-16 code units from the start of the script.</value>
    public int Start { get; }

    /// <summary>Gets the number of characters the span covers.</summary>
    /// <value>Zero or more, in UTF-16 code units.</value>
    public int Length { get; }

    /// <summary>Gets the offset one past the last character in the span.</summary>
    /// <value><see cref="Start"/> plus <see cref="Length"/>. The span does not include this position.</value>
    public int End => Start + Length;

    /// <summary>Gets a value indicating whether the span covers no characters.</summary>
    /// <value><see langword="true"/> when the span denotes a position rather than a range.</value>
    public bool IsEmpty => Length == 0;

    /// <summary>Creates a span from a start and an end offset.</summary>
    /// <param name="start">Zero-based offset of the first character.</param>
    /// <param name="end">Offset one past the last character.</param>
    /// <returns>The span covering <c>[start, end)</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="end"/> is before <paramref name="start"/>, or either is negative.</exception>
    public static TextSpan FromBounds(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);

        return new TextSpan(start, end - start);
    }

    /// <summary>Determines whether a character offset falls inside the span.</summary>
    /// <param name="position">A zero-based character offset.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="position"/> is at or after <see cref="Start"/> and
    /// before <see cref="End"/>. An empty span contains no position, so a caret sitting exactly on one
    /// is not inside it.
    /// </returns>
    public bool Contains(int position) => position >= Start && position < End;

    /// <summary>Determines whether another span lies entirely within this one.</summary>
    /// <param name="other">The span to test.</param>
    /// <returns><see langword="true"/> when <paramref name="other"/> starts at or after this span's start and ends at or before its end.</returns>
    public bool Contains(TextSpan other) => other.Start >= Start && other.End <= End;

    /// <summary>Determines whether two spans share at least one character.</summary>
    /// <param name="other">The span to test.</param>
    /// <returns>
    /// <see langword="true"/> when the intersection is non-empty. Two spans that merely touch — one
    /// ending exactly where the other begins — do not overlap, and neither does an empty span with
    /// anything.
    /// </returns>
    public bool OverlapsWith(TextSpan other) =>
        Math.Max(Start, other.Start) < Math.Min(End, other.End);

    /// <summary>Renders the span as its half-open interval.</summary>
    /// <returns>A string of the form <c>[12..17)</c>, chosen so a failing assertion states the convention it uses.</returns>
    public override string ToString() => $"[{Start}..{End})";
}
