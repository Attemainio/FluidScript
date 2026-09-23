using System.Collections.Frozen;
using System.Collections.Immutable;

namespace FluidScript.Core.Syntax;

/// <summary>The spelling of each reserved word, and the lookup from spelling to word.</summary>
/// <remarks>
/// Spellings are matched with ordinal case sensitivity. Kind and parameter names are normalised for
/// case at bind time (<c>D-15</c>) and reserved words deliberately are not: <c>Let</c> is an
/// identifier, and a script that declares a component called <c>Let</c> keeps working when a future
/// version adds a case-insensitive spelling of something else.
/// </remarks>
public static class ReservedWords
{
    private static readonly FrozenDictionary<string, ReservedWord> ByText =
        Enum.GetValues<ReservedWord>()
            .Where(static word => word != ReservedWord.None)
            .ToFrozenDictionary(TextOf, static word => word, StringComparer.Ordinal);

    /// <summary>Gets every reserved spelling, in the order the words are declared.</summary>
    /// <value>Sixteen words. Never empty.</value>
    public static ImmutableArray<string> All { get; } =
    [
        .. Enum.GetValues<ReservedWord>()
            .Where(static word => word != ReservedWord.None)
            .Select(TextOf),
    ];

    /// <summary>Gets the spelling of one reserved word.</summary>
    /// <param name="word">The word.</param>
    /// <returns>Its lowercase spelling, as it appears in a script.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="word"/> is <see cref="ReservedWord.None"/>.</exception>
    public static string TextOf(ReservedWord word) => word switch
    {
        ReservedWord.None => throw new ArgumentOutOfRangeException(
            nameof(word), word, "None is the absence of a reserved word and has no spelling."),
        _ => word.ToString().ToLowerInvariant(),
    };

    /// <summary>Classifies a word.</summary>
    /// <param name="text">The word as written.</param>
    /// <param name="word">The reserved word, or <see cref="ReservedWord.None"/>.</param>
    /// <returns><see langword="true"/> when the spelling is reserved.</returns>
    public static bool TryMatch(string text, out ReservedWord word)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ByText.TryGetValue(text, out word);
    }

    /// <summary>Determines whether a spelling is reserved.</summary>
    /// <param name="text">The word as written.</param>
    /// <returns><see langword="true"/> when it may not be used as an identifier.</returns>
    public static bool IsReserved(string text) => TryMatch(text, out _);
}
