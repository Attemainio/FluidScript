using System.Collections.Immutable;
using System.Text;

using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Tests.Documentation;

/// <summary>
/// Renders the generated regions of <c>docs/functions/units.md</c> from the unit table.
/// </summary>
/// <remarks>
/// Generated for the same reason the diagnostic page is: a hand-maintained copy of a unit table
/// diverges, and a reference page that is wrong about a conversion is worse than one that omits it —
/// a reader who trusts it gets an answer wrong by a constant factor with no sign that anything failed.
/// </remarks>
public static class UnitsPage
{
    /// <summary>Identifies the generated region listing what a bare number means per dimension.</summary>
    public const string DimensionsRegion = "unit-dimensions";

    /// <summary>Identifies the generated region listing every accepted spelling.</summary>
    public const string SymbolsRegion = "unit-symbols";

    /// <summary>Renders the table of dimensions.</summary>
    /// <returns>A markdown table: what each dimension is stored in, what a bare number means, and how it is shown.</returns>
    public static string Render()
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Quantity | Stored as | A bare number means | Shown as |");
        builder.AppendLine("|---|---|---|---|");

        foreach (var dimension in Dimension.All)
        {
            var canonical = UnitTable.CanonicalUnitFor(dimension);
            var bare = canonical is null
                ? (dimension.SiUnit.Length == 0 ? "the value itself" : $"`{dimension.SiUnit}`")
                : $"`{canonical.Text}`{(dimension.CanonicalDiffersFromSi ? " **" : string.Empty)}";

            builder.AppendLine(
                $"| {Spaced(dimension.Name)} | {Code(dimension.SiUnit)} | {bare} | {Code(dimension.DisplayUnit)} |");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Renders the table of accepted spellings.</summary>
    /// <returns>A markdown table, one row per dimension that accepts a spelling.</returns>
    public static string RenderSymbols()
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Quantity | You can write |");
        builder.AppendLine("|---|---|");

        foreach (var dimension in Dimension.All)
        {
            var symbols = Writable(dimension);
            var accepted = Dimension.All
                .Where(other => other != dimension && Quantity.TryAssign(Quantity.FromSi(1.0, other), dimension, out _))
                .ToImmutableArray();
            var spellings = symbols.IsEmpty && accepted.IsEmpty
                ? "*a bare number only*"
                : string.Join(", ", symbols.Select(static s => $"`{s.Text}`"));

            // A head has no symbol of its own but takes a length (`D-126`); the table reads the binder's rule, not a list (`L-93`).
            foreach (var other in accepted)
            {
                spellings += $"{(spellings.Length == 0 ? "a" : ", or a")} {Spaced(other.Name).ToLowerInvariant()}: "
                    + string.Join(", ", Writable(other).Select(static s => $"`{s.Text}`"));
            }

            builder.AppendLine($"| {Spaced(dimension.Name)} | {spellings} |");
        }

        return builder.ToString().TrimEnd();
    }

    // `in` and `t` convert but cannot be written after a number (`19`), so the page does not offer them (`A-9`).
    private static ImmutableArray<UnitSymbol> Writable(Dimension dimension) =>
        [.. UnitTable.For(dimension).Where(static s => !Lexer.ExcludedUnitSymbols.Contains(s.Text, StringComparer.Ordinal))];

    private static string Code(string? text) =>
        string.IsNullOrEmpty(text) ? "—" : $"`{text}`";

    private static string Spaced(string name)
    {
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
