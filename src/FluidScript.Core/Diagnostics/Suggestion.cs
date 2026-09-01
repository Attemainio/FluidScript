namespace FluidScript.Core.Diagnostics;

/// <summary>
/// A concrete edit that resolves a diagnostic.
/// </summary>
/// <param name="Title">What the edit does, as the editor offers it — <c>Change 'pwor' to 'power'</c>.</param>
/// <param name="Span">The region of the script to replace.</param>
/// <param name="Replacement">The text to put there.</param>
/// <remarks>
/// Applied verbatim by the editor's quick-fix and offered to an agent correcting its own output.
/// <strong>A suggestion that is a guess is worse than none</strong>: populate it only when the
/// replacement is certainly correct. Where two readings are equally plausible — <c>20C + 30 dK</c>
/// and <c>20C + 30C - 273.15K</c> for the same mistake — the diagnostic carries no suggestion and
/// says what is wrong instead.
/// </remarks>
public sealed record Suggestion(string Title, TextSpan Span, string Replacement);
