using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Documentation;

/// <summary>
/// The documentation gate from <c>plan/60-docs-and-devex/61-documentation-plan.md</c>: every
/// registered component kind, every statement-introducing reserved word and every diagnostic code has
/// a page in <c>/docs</c>, or the build fails.
/// </summary>
/// <remarks>
/// <para>
/// It is a test rather than a shell script for the same reason the architecture assertions are, and
/// it was wired at M0 rather than when there was something to document. The diagnostic half now does
/// real work: the code table on the reference page is rendered from the registry and compared, so a
/// code added without a documented meaning fails here. The component and reserved-word registries are
/// still empty, and start failing the day their first entry is registered.
/// </para>
/// <para>
/// A gate added after the first three features have shipped is a gate first met by writing three
/// pages nobody wanted to write, which is how documentation gates come to be disabled.
/// </para>
/// </remarks>
public sealed class DocumentationGateTests
{
    /// <summary>The three <c>/docs</c> categories that every page must fall into.</summary>
    private static readonly string[] RequiredCategories = ["tutorial", "advanced", "functions"];

    private static string DocsRoot => Path.Combine(RepositoryLayout.Root, "docs");

    /// <summary>Component kinds the registry has registered, each of which needs a function page.</summary>
    /// <remarks>Empty until the component registry lands in P2.6.</remarks>
    private static IReadOnlyCollection<string> RegisteredComponentKinds => [];

    /// <summary>
    /// Reserved words that introduce a statement, which the gate must cover as well as the component
    /// registry.
    /// </summary>
    /// <remarks>
    /// Enumerating these matters as much as enumerating the kinds: <c>D-33</c>, <c>D-37</c> and
    /// <c>D-40</c> added five statements that are not component kinds, and a gate walking only the
    /// registry would have passed all five undocumented. Empty until the grammar lands in P2.4.
    /// </remarks>
    private static IReadOnlyCollection<string> StatementReservedWords => [];

    private static bool HasPage(string slug) =>
        RequiredCategories.Any(category =>
            File.Exists(Path.Combine(DocsRoot, category, $"{slug}.md")));

    [Fact]
    [Trait("Category", "Docs")]
    public void DocsTreeHasItsThreeCategories()
    {
        foreach (var category in RequiredCategories)
        {
            var path = Path.Combine(DocsRoot, category);
            Assert.True(
                Directory.Exists(path),
                $"R-28: /docs must carry a {category} category. Missing: {RepositoryLayout.ToRelative(path)}");
        }
    }

    [Fact]
    [Trait("Category", "Docs")]
    public void EveryRegisteredComponentKindHasItsPage()
    {
        var undocumented = RegisteredComponentKinds.Where(kind => !HasPage(kind)).ToArray();

        Assert.True(
            undocumented.Length == 0,
            $"R-28: every component kind ships with its page. Missing: {string.Join(", ", undocumented)}");
    }

    [Fact]
    [Trait("Category", "Docs")]
    public void EveryStatementReservedWordHasItsPage()
    {
        var undocumented = StatementReservedWords.Where(word => !HasPage(word)).ToArray();

        Assert.True(
            undocumented.Length == 0,
            $"R-28: every statement-introducing word ships with its page. Missing: {string.Join(", ", undocumented)}");
    }

    [Fact]
    [Trait("Category", "Docs")]
    public void TheDiagnosticsPageIsGeneratedFromTheRegistry()
    {
        var path = Path.Combine(DocsRoot, "functions", "diagnostics.md");
        Assert.True(
            File.Exists(path),
            $"R-28: the diagnostic reference page is missing. Expected {RepositoryLayout.ToRelative(path)}.");

        var committed = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        var current = DiagnosticsPage.WriteRegion(
            committed, DiagnosticsPage.CodesRegion, DiagnosticsPage.RenderCodes());
        current = DiagnosticsPage.WriteRegion(
            current, DiagnosticsPage.RetiredRegion, DiagnosticsPage.RenderRetired());

        if (string.Equals(committed, current, StringComparison.Ordinal))
        {
            return;
        }

        // Rewriting the page here is deliberate: locally the next run passes and the diff is in the
        // working tree to review, and in CI the failure plus a dirty tree is exactly the signal that
        // a generated page was committed stale. The alternative -- printing several hundred lines of
        // expected markdown into an assertion message -- is a diff nobody reads.
        File.WriteAllText(path, current);
        Assert.Fail(
            $"R-28: {RepositoryLayout.ToRelative(path)} did not match the diagnostic registry and has "
            + "been regenerated in place. Review the change and run the tests again.");
    }
}
