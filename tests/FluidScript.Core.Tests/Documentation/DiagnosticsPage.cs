using System.Globalization;
using System.Text;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Tests.Documentation;

/// <summary>
/// Renders the generated regions of <c>docs/functions/diagnostics.md</c> from the registry.
/// </summary>
/// <remarks>
/// <para>
/// The page is generated because a hand-maintained copy of a hundred and fifty codes diverges from
/// the code that emits them, and a reference page that is wrong about a code is worse than one that
/// omits it. Only the tables are generated; the prose around them is written.
/// </para>
/// <para>
/// This lives in the test project rather than in Core deliberately. Core owns the diagnostic model,
/// not the rendering of documentation, and the build-time generator the documentation plan calls for
/// has no home yet. What exists today is the drift check, which is the half that has to be in place
/// before the first code is written; moving the renderer behind a real generator later changes
/// nothing about the check.
/// </para>
/// </remarks>
public static class DiagnosticsPage
{
    /// <summary>Identifies the generated region listing every live code.</summary>
    public const string CodesRegion = "diagnostic-codes";

    /// <summary>Identifies the generated region listing every withdrawn code.</summary>
    public const string RetiredRegion = "retired-diagnostic-codes";

    /// <summary>Renders the table of live codes.</summary>
    /// <returns>
    /// A markdown table, one row per code, ordered by code. When no stage emits a diagnostic yet, a
    /// sentence saying so — an empty table with a header and no rows reads as a broken page.
    /// </returns>
    public static string RenderCodes()
    {
        if (DiagnosticRegistry.All.IsEmpty)
        {
            return "_No codes yet: this page fills in as each part of FluidScript starts reporting._";
        }

        var builder = new StringBuilder();
        builder.AppendLine("| Code | Severity | Reported by | Message |");
        builder.AppendLine("|---|---|---|---|");

        foreach (var descriptor in DiagnosticRegistry.All)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| `{descriptor.Code}` | {descriptor.Severity} | {Readable(descriptor.Stage)} | {Cell(descriptor.MessageTemplate)} |");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Renders the table of withdrawn codes.</summary>
    /// <returns>A markdown table, one row per retired code, ordered by code.</returns>
    public static string RenderRetired()
    {
        if (DiagnosticRegistry.Retired.IsEmpty)
        {
            return "_No codes have been withdrawn._";
        }

        var builder = new StringBuilder();
        builder.AppendLine("| Code | Why it is no longer reported |");
        builder.AppendLine("|---|---|");

        foreach (var retired in DiagnosticRegistry.Retired)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"| `{retired.Code}` | {Cell(retired.Reason)} |");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Reads the current content of one generated region.</summary>
    /// <param name="document">The whole page.</param>
    /// <param name="region">The region identifier, as it appears in the marker comments.</param>
    /// <returns>The text between the markers, trimmed of the blank lines that surround it.</returns>
    /// <exception cref="InvalidOperationException">
    /// The page is missing one of the markers. That is a page that can never be regenerated, so it
    /// fails loudly rather than being silently skipped.
    /// </exception>
    public static string ReadRegion(string document, string region)
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
    public static string WriteRegion(string document, string region, string content)
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
                $"docs/functions/diagnostics.md has no '{region}' generated region. It needs both "
                + $"'{open}' and '{close}'.");
        }

        return (openIndex + open.Length, closeIndex);
    }

    private static string Cell(string text) => text.Replace("|", @"\|", StringComparison.Ordinal);

    private static string Readable(DiagnosticStage stage)
    {
        var name = stage.ToString();
        var builder = new StringBuilder(name.Length + 2);

        for (var index = 0; index < name.Length; index++)
        {
            if (index > 0 && char.IsUpper(name[index]))
            {
                builder.Append(' ').Append(char.ToLowerInvariant(name[index]));
                continue;
            }

            builder.Append(name[index]);
        }

        return builder.ToString();
    }
}
