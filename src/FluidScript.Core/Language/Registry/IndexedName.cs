using System.Globalization;

namespace FluidScript.Core.Language.Registry;

/// <summary>Matches an indexed family pattern such as <c>t{index}</c> against a written name.</summary>
/// <remarks>
/// The one implementation of the pattern rule, used by both family kinds and by the binder. It was
/// the binder's private helper first, which is why the parameter path still reaches it through a
/// forwarder rather than calling it directly — there is one rule, in one place, either way.
/// </remarks>
public static class IndexedName
{
    private const string Placeholder = "{index}";

    /// <summary>Tells whether a written name is a member of a pattern's family, and which one.</summary>
    /// <param name="pattern">The canonical pattern, with one <c>{index}</c> placeholder.</param>
    /// <param name="written">The name to test.</param>
    /// <param name="index">The index it carries, or zero when it is not a member.</param>
    /// <returns><see langword="true"/> when the name matches the pattern.</returns>
    /// <remarks>
    /// The index must be digits only: <c>NumberStyles.None</c> rejects <c>t+3</c> and <c>t 3</c>,
    /// which <see cref="int.TryParse(string, out int)"/>'s default would accept.
    /// </remarks>
    public static bool Matches(string pattern, string written, out int index)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(written);

        index = 0;

        var placeholder = pattern.IndexOf(Placeholder, StringComparison.Ordinal);
        if (placeholder < 0)
        {
            return false;
        }

        var prefix = pattern[..placeholder];
        var suffix = pattern[(placeholder + Placeholder.Length)..];

        if (!written.StartsWith(prefix, StringComparison.Ordinal)
            || !written.EndsWith(suffix, StringComparison.Ordinal)
            || written.Length <= prefix.Length + suffix.Length)
        {
            return false;
        }

        var digits = written[prefix.Length..(written.Length - suffix.Length)];

        return int.TryParse(
            digits, NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    /// <summary>Writes one member of a pattern's family.</summary>
    /// <param name="pattern">A pattern with one <c>{index}</c> placeholder.</param>
    /// <param name="index">The member's index.</param>
    /// <returns><c>in[2].level</c> for <c>in[{index}].level</c> and 2.</returns>
    public static string Spell(string pattern, int index)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        return pattern.Replace(
            Placeholder, index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>Folds a bracketed first index away: <c>in[1].t</c> reads as <c>in.t</c>, <c>in[1]</c> as <c>in</c> (<c>D-120</c>).</summary>
    /// <param name="written">A name as the script wrote it.</param>
    /// <returns>The name with every <c>[1]</c> removed; unchanged when there is none.</returns>
    /// <remarks>
    /// The bare word and the first index are one port, so they are one spelling to the registry, and
    /// neither draws a suggestion. Only <c>[1]</c> folds: <c>in[2]</c> is a different port.
    /// </remarks>
    public static string FoldFirstIndex(string written)
    {
        ArgumentNullException.ThrowIfNull(written);

        return written.Contains("[1]", StringComparison.Ordinal)
            ? written.Replace("[1]", string.Empty, StringComparison.Ordinal)
            : written;
    }
}
