using System.Text;

using FluidScript.Core.Syntax;

namespace FluidScript.Core.Tests.Documentation;

/// <summary>Renders the generated region of <c>docs/functions/syntax.md</c> from the reserved-word list.</summary>
/// <remarks>
/// The list is generated for the reason the unit and diagnostic tables are: it is data, and a
/// hand-written copy of it goes stale the first time a word is added. It matters more than most,
/// because adding a reserved word is a breaking language change — a page that omits one tells a reader
/// a name is available when it is not.
/// </remarks>
public static class SyntaxPage
{
    /// <summary>Identifies the generated region listing every reserved word.</summary>
    public const string ReservedWordsRegion = "reserved-words";

    /// <summary>Renders the reserved-word list.</summary>
    /// <returns>A markdown list of every word that may not be used as a name.</returns>
    public static string Render()
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Word | Introduces |");
        builder.AppendLine("|---|---|");

        foreach (var word in Enum.GetValues<ReservedWord>().Where(static word => word != ReservedWord.None))
        {
            builder.AppendLine($"| `{ReservedWords.TextOf(word)}` | {Introduces(word)} |");
        }

        return builder.ToString().TrimEnd();
    }

    private static string Introduces(ReservedWord word) => word switch
    {
        ReservedWord.Fluidscript => "the version line every script opens with",
        ReservedWord.Project => "the project name, and the default for how the file is solved",
        ReservedWord.Circuit => "a circuit, and everything that follows until the next one",
        ReservedWord.Fluid => "what a circuit carries, and how it is solved",
        ReservedWord.Dynamic => "solving in time — qualifies `project` or `fluid`",
        ReservedWord.Static => "solving as a steady state — qualifies `project` or `fluid`",
        ReservedWord.Spacing => "how far apart components are drawn",
        ReservedWord.Style => "how the following components are drawn",
        ReservedWord.Show => "which property the colour scale follows",
        ReservedWord.Let => "a name for a value you use more than once",
        ReservedWord.Catalog => "which catalogue sizes are chosen from",
        ReservedWord.Connections => "a circuit's topology",
        ReservedWord.Schedule => "what changes, and when, during a run",
        ReservedWord.Supply => "where a subcircuit takes flow from its parent",
        ReservedWord.Return => "where a subcircuit gives that flow back",
        ReservedWord.Control => "which controller drives what, measuring what",
        _ => throw new ArgumentOutOfRangeException(nameof(word), word, "Every reserved word needs a description."),
    };
}
