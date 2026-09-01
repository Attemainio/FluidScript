namespace FluidScript.Core.Tests.Documentation;

/// <summary>
/// Reads and replaces the marked regions of a partly generated <c>/docs</c> page.
/// </summary>
/// <remarks>
/// Some reference pages are prose around a table that must not be maintained by hand. The markers let
/// the two coexist in one file: the prose is written and reviewed, the table between the markers is
/// rendered from whatever the code actually does.
/// </remarks>
public static class GeneratedRegion
{
    /// <summary>Reads the current content of one generated region.</summary>
    /// <param name="document">The whole page.</param>
    /// <param name="region">The region identifier, as it appears in the marker comments.</param>
    /// <returns>The text between the markers, trimmed of the blank lines that surround it.</returns>
    /// <exception cref="InvalidOperationException">The page is missing one of the markers.</exception>
    public static string Read(string document, string region)
    {
        ArgumentNullException.ThrowIfNull(document);

        var (start, end) = Bounds(document, region);
        return document[start..end].Trim('\n', '\r');
    }

    /// <summary>Replaces the content of one generated region.</summary>
    /// <param name="document">The whole page.</param>
    /// <param name="region">The region identifier, as it appears in the marker comments.</param>
    /// <param name="content">The rendered replacement, without surrounding blank lines.</param>
    /// <returns>The page with that region's content replaced and the markers left in place.</returns>
    /// <exception cref="InvalidOperationException">The page is missing one of the markers.</exception>
    public static string Write(string document, string region, string content)
    {
        ArgumentNullException.ThrowIfNull(document);

        var (start, end) = Bounds(document, region);
        return document[..start] + "\n" + content + "\n" + document[end..];
    }

    private static (int Start, int End) Bounds(string document, string region)
    {
        var open = $"<!-- BEGIN GENERATED: {region} -->";
        var close = $"<!-- END GENERATED: {region} -->";

        var openIndex = document.IndexOf(open, StringComparison.Ordinal);
        var closeIndex = document.IndexOf(close, StringComparison.Ordinal);

        if (openIndex < 0 || closeIndex < openIndex)
        {
            throw new InvalidOperationException(
                $"The page has no '{region}' generated region. It needs both '{open}' and '{close}'.");
        }

        return (openIndex + open.Length, closeIndex);
    }
}
