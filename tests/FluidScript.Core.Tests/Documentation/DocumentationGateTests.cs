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
/// it is wired at M0 rather than when there is something to document. Today all three registries are
/// empty, so the coverage assertions pass over nothing; the day the first component kind is
/// registered in P2.6 this test starts failing until its page exists. A gate added after the first
/// three features have shipped is a gate that is first met by writing three pages nobody wanted to
/// write, which is how documentation gates come to be disabled.
/// </para>
/// <para>
/// The registries are represented as empty sequences here rather than being read from Core, because
/// the types that will own them do not exist yet. P2.6 replaces each with the real registry, and the
/// assertions do not change.
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

    /// <summary>Diagnostic codes that are reachable, each of which needs a generated entry.</summary>
    /// <remarks>
    /// Retired codes are exempt and must be, or the gate demands a page for <c>FS1509</c>. Empty until
    /// the diagnostic registry lands in P2.1.
    /// </remarks>
    private static IReadOnlyCollection<string> ReachableDiagnosticCodes => [];

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
    public void EveryReachableDiagnosticCodeHasItsEntry()
    {
        var undocumented = ReachableDiagnosticCodes.Where(code => !HasPage(code)).ToArray();

        Assert.True(
            undocumented.Length == 0,
            $"R-28: every reachable diagnostic code has a generated entry. Missing: {string.Join(", ", undocumented)}");
    }
}
