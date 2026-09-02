using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Fixtures;

/// <summary>
/// Every implementable contract is scheduled: each document in tiers 10 through 50 is named somewhere
/// in <c>plan/00-foundation/08-implementation-sequence.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// This exists because two documents turned out not to be — the formatter (<c>F-6</c>) and script
/// compatibility (<c>F-10</c>) — and both were found by working backwards from a milestone criterion
/// that had nothing to deliver it, not by reading <c>08</c>. Its package tables are a good plan and a
/// poor inventory: nothing in the document's structure notices a file no row names, and the cost of
/// the omission is only paid at the milestone that needed it.
/// </para>
/// <para>
/// Tier 00 is the plan's own foundation, tier 60 is process, and tier 70 is explicitly future, so none
/// of the three is a contract anything implements against. A document deliberately deferred is still
/// named — <c>35-evolutionary-sizing</c> is named in <c>08</c>'s P8 paragraph — which is what lets
/// this tell "deferred on purpose" apart from "nobody scheduled it".
/// </para>
/// </remarks>
public sealed class SequenceCoverageTests
{
    private static readonly string[] ImplementableTiers =
        ["10-language", "20-core-domain", "30-solver", "40-api", "50-frontend"];

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryImplementableDocumentIsNamedInTheSequence()
    {
        var plan = Path.Combine(RepositoryLayout.Root, "plan");
        var sequence = File.ReadAllText(
            Path.Combine(plan, "00-foundation", "08-implementation-sequence.md"));

        var unscheduled = new List<string>();
        var checkedDocuments = 0;

        foreach (var tier in ImplementableTiers)
        {
            var directory = Path.Combine(plan, tier);

            Assert.True(Directory.Exists(directory), $"plan/{tier} is missing.");

            foreach (var path in Directory.EnumerateFiles(directory, "*.md").Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(path);

                // A tier's own defect record is not a contract; it is what implementing the contracts
                // produced.
                if (name is "defects.md" or "observations.md" or "README.md")
                {
                    continue;
                }

                checkedDocuments++;

                if (!sequence.Contains(Path.GetFileNameWithoutExtension(name), StringComparison.Ordinal))
                {
                    unscheduled.Add($"{tier}/{name}");
                }
            }
        }

        // The same self-check every drift test here needs: a glob that matched nothing would pass.
        Assert.True(checkedDocuments >= 25, $"Only {checkedDocuments} documents were examined.");

        Assert.True(
            unscheduled.Count == 0,
            "08-implementation-sequence.md names no package for: "
            + string.Join(", ", unscheduled)
            + ". Either schedule it, or name it where it is deferred and say why.");
    }
}
