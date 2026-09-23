using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using FluidScript.Core.Components;
using FluidScript.Core.Sizing;

namespace FluidScript.Core.Diagnostics;

/// <summary>Writes what sizing a plant over its scenarios did, and what it cost (<c>D-143</c>, <c>24</c>).</summary>
/// <remarks>
/// <para>
/// Three questions, in the order an engineer asks them. <strong>Which case decided each size</strong>,
/// because a number with no case behind it cannot be argued with — that is <c>24</c>'s invariant 2
/// applied across cases. <strong>What each case does in the merged plant</strong>, which is step 3's
/// output and the only solve that describes the plant actually built. And <strong>what it cost</strong>,
/// which is what turns the sequential-versus-parallel question into a reading rather than an argument.
/// </para>
/// <para>
/// Text, and deliberately not a wire format. It sits beside <see cref="SolveExplanation"/>: both are
/// read by a person deciding whether to believe a number.
/// </para>
/// </remarks>
public static class ScenarioExplanation
{
    /// <summary>Renders one scenario sizing run.</summary>
    /// <param name="result">What the pipeline produced.</param>
    /// <param name="scenarios">The declared case names, in order.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static string Explain(ScenarioSizingResult result, ImmutableArray<string> scenarios)
    {
        ArgumentNullException.ThrowIfNull(result);

        var text = new StringBuilder();

        text.Append("=== scenarios  ")
            .Append(result.Operating.Length.ToString(CultureInfo.InvariantCulture))
            .Append(result.Operating.Length == 1 ? " case" : " cases")
            .Append(scenarios.IsEmpty ? string.Empty : ": " + string.Join(", ", scenarios))
            .Append(" — design ")
            .Append(result.Design.Name)
            .AppendLine();

        text.Append("    merge      ")
            .Append(result.Rounds.ToString(CultureInfo.InvariantCulture))
            .Append(result.Rounds == 1 ? " round, " : " rounds, ")
            .AppendLine(result.Converged ? "settled" : "still moving at the cap — the sizes are the last merge");

        if (!result.Governing.IsEmpty)
        {
            text.AppendLine().AppendLine("    size                            value  governed by");

            foreach (var (component, sizes) in Ordered(result.Sizes))
            {
                foreach (var (parameter, value) in sizes.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    var key = Ownership.Key(component, parameter);

                    text.Append("    ")
                        .Append(key.PadRight(22))
                        .Append(value.ToString().PadLeft(14))
                        .Append("  ")
                        .AppendLine(result.Governing.GetValueOrDefault(key, "—"));
                }
            }
        }

        text.AppendLine().AppendLine("    case            iterations  passes  settled     ms");

        var total = 0.0;
        var longest = 0.0;

        foreach (var solve in result.Operating)
        {
            var milliseconds = solve.Elapsed.TotalMilliseconds;

            total += milliseconds;
            longest = Math.Max(longest, milliseconds);

            text.Append("    ")
                .Append(solve.Name.PadRight(16))
                .Append(solve.Result.Iterations.ToString(CultureInfo.InvariantCulture).PadLeft(10))
                .Append(solve.Result.Passes.ToString(CultureInfo.InvariantCulture).PadLeft(8))
                .Append((solve.Result.Settled ? "yes" : "no").PadLeft(9))
                .Append(milliseconds.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(7))
                .AppendLine();
        }

        // The line that decides whether the chunked workers `24` describes are ever worth building:
        // what a perfect fan-out would save is the total less the longest case, and nothing more.
        text.Append("    total ")
            .Append(total.ToString("0.0", CultureInfo.InvariantCulture))
            .Append(" ms over ")
            .Append(result.Operating.Length.ToString(CultureInfo.InvariantCulture))
            .Append(" cases, longest ")
            .Append(longest.ToString("0.0", CultureInfo.InvariantCulture))
            .Append(" ms — a perfect fan-out would save ")
            .Append((total - longest).ToString("0.0", CultureInfo.InvariantCulture))
            .AppendLine(" ms");

        foreach (var note in result.Notes)
        {
            text.Append("    note  ").AppendLine(note);
        }

        foreach (var said in result.Said)
        {
            text.Append("    ").Append(said.Code).Append("  ").AppendLine(said.Message);
        }

        return text.ToString();
    }

    /// <summary>Orders the merged sizes by component so two runs of one plant read the same.</summary>
    /// <param name="sizes">The merged overlay.</param>
    /// <returns>Its components in name order.</returns>
    private static IEnumerable<KeyValuePair<string, ImmutableDictionary<string, Units.Quantity>>> Ordered(
        SizingOverlay sizes) =>
        sizes.Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal);
}
