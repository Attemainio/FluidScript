using System.Text;

using FluidScript.Core.Language.Registry;

namespace FluidScript.Core.Tests.Documentation;

/// <summary>Renders the generated region of <c>docs/functions/syntax.md</c> from the statement words.</summary>
/// <remarks>
/// The list is generated for the reason the unit and diagnostic tables are: it is data, and a hand-written copy of
/// it goes stale the first time a word is added (<c>T-7</c>). It matters more than most, because a statement word may
/// not name a component -- a page that omits one tells a reader a name is available when it is not.
/// </remarks>
public static class SyntaxPage
{
    /// <summary>Identifies the generated region listing every statement word.</summary>
    public const string StatementWordsRegion = "statement-words";

    /// <summary>Renders the statement words and the run's event words.</summary>
    /// <returns>A markdown table of every word that opens a statement.</returns>
    public static string Render()
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Word | Opens |");
        builder.AppendLine("|---|---|");

        foreach (var word in SettingRegistry.StatementWords.Concat(SettingRegistry.EventWords))
        {
            builder.AppendLine($"| `{word}` | {Opens(word)} |");
        }

        return builder.ToString().TrimEnd();
    }

    private static string Opens(string word) => word switch
    {
        "fluidscript" => "the version line every script opens with",
        "project" => "the project block: its title, its cases, its catalogue and its presentation",
        "let" => "a named value; a list makes it a driver, one value per case",
        "curve" => "a named table read against its driver, interpolated between its rows",
        "circuit" => "a circuit block: its fluid, its components and their connections",
        "run" => "a run block: what happens to the plant in time",
        "at" => "a step, inside a run only",
        "over" => "a ramp, inside a run only",
        _ => throw new ArgumentOutOfRangeException(nameof(word), word, "Every statement word needs a description."),
    };
}
