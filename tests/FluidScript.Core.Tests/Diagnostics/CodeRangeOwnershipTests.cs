using System.Collections.Immutable;
using System.Text.RegularExpressions;

using FluidScript.Core.Diagnostics;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Diagnostics;

/// <summary>
/// M1's criterion that every emitted code appears in <c>plan/10-language/16-diagnostics.md</c>'s
/// table, read as what that table actually is: a map from each <c>FSnnxx</c> range to the document
/// that owns its codes.
/// </summary>
/// <remarks>
/// <para>
/// Two directions, and the second is the one that catches things. A code in no range is unreachable
/// documentation; a code whose owning document never mentions it is worse, because the range table
/// says where to read about it and there is nothing there. <c>D-53</c> made every range name its
/// <em>subject</em> rather than the stage that emits it, which is exactly what makes this checkable:
/// the owning document is a property of the code, not of the class that happens to declare it.
/// </para>
/// <para>
/// This is a text check over the plan, so it belongs with the other drift checks rather than with the
/// message-style rules. A registry compared only against itself agrees with itself.
/// </para>
/// </remarks>
public sealed partial class CodeRangeOwnershipTests
{
    /// <summary>One row of <c>16</c>'s range table.</summary>
    /// <param name="Prefix">The two digits after <c>FS</c>, such as <c>15</c>.</param>
    /// <param name="Document">The path the row links to, or <see langword="null"/> when it names none.</param>
    private readonly record struct RangeRow(string Prefix, string? Document);

    private static readonly Lazy<ImmutableArray<RangeRow>> LazyRanges = new(Ranges);

    private static string Plan => Path.Combine(RepositoryLayout.Root, "plan");

    [Fact]
    [Trait("Category", "Unit")]
    public void TheRangeTableIsActuallyRead()
    {
        // A regex that matches nothing makes both tests below pass while checking nothing, which is
        // the failure mode of every drift check read out of a document. Twenty-three ranges are in
        // `16` today; the floor is well under that so an added range does not fail this, and well
        // over zero so a broken pattern does.
        Assert.True(
            LazyRanges.Value.Length >= 20,
            $"16's range table parsed as {LazyRanges.Value.Length} rows, which means the pattern "
            + "stopped matching it rather than the table having shrunk that far.");

        Assert.Contains(LazyRanges.Value, static row => row is { Prefix: "15", Document: not null });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryRegisteredCodeFallsInADocumentedRange()
    {
        var prefixes = LazyRanges.Value.Select(static row => row.Prefix).ToHashSet(StringComparer.Ordinal);

        var orphans = DiagnosticRegistry.All
            .Where(descriptor => !prefixes.Contains(descriptor.Code[2..4]))
            .Select(static descriptor => descriptor.Code)
            .ToArray();

        Assert.True(
            orphans.Length == 0,
            "16-diagnostics.md's range table is where a user looks a code up. Not in any range: "
            + string.Join(", ", orphans));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryRegisteredCodeAppearsInTheDocumentItsRangeNames()
    {
        var byPrefix = LazyRanges.Value.ToDictionary(
            static row => row.Prefix, static row => row.Document, StringComparer.Ordinal);

        var missing = new List<string>();
        var checkedCodes = new List<string>();

        foreach (var descriptor in DiagnosticRegistry.All)
        {
            if (!byPrefix.TryGetValue(descriptor.Code[2..4], out var document) || document is null)
            {
                // Ranges owned by `16` itself, or by a whole tier, name no single file. The first test
                // already asserted the range exists; there is nothing further to read here.
                continue;
            }

            var path = Path.Combine(Plan, "10-language", document);

            if (!File.Exists(path))
            {
                path = Path.Combine(Plan, document.Replace("../", string.Empty, StringComparison.Ordinal));
            }

            if (!File.Exists(path))
            {
                missing.Add($"{descriptor.Code} (no document at {document})");
                continue;
            }

            checkedCodes.Add(descriptor.Code);

            if (!File.ReadAllText(path).Contains(descriptor.Code, StringComparison.Ordinal))
            {
                missing.Add($"{descriptor.Code} (absent from {document})");
            }
        }

        // Guards the same failure as above from the other end: a loop that `continue`d on every code
        // would report nothing missing.
        Assert.NotEmpty(checkedCodes);

        Assert.True(
            missing.Count == 0,
            "A code's range names the document that owns it, and that document has to say what the "
            + "code means: " + string.Join("; ", missing));
    }

    private static ImmutableArray<RangeRow> Ranges()
    {
        var text = File.ReadAllText(
            Path.Combine(Plan, "10-language", "16-diagnostics.md"));

        var rows = ImmutableArray.CreateBuilder<RangeRow>();

        foreach (Match match in RangeRowPattern().Matches(text))
        {
            var link = match.Groups["link"];

            rows.Add(new RangeRow(match.Groups["prefix"].Value, link.Success ? link.Value : null));
        }

        return rows.ToImmutable();
    }

    // A range row is `| `FS15xx` | subject | [`15-semantic-model`](15-semantic-model.md) |`, and the
    // link is optional: two ranges are owned by `16` itself and one by a whole tier.
    [GeneratedRegex(@"^\|\s*`FS(?<prefix>\d{2})xx`\s*\|[^|]*\|(?:[^|(]*\((?<link>[^)]+\.md)\))?[^|]*\|",
        RegexOptions.Multiline)]
    private static partial Regex RangeRowPattern();
}
