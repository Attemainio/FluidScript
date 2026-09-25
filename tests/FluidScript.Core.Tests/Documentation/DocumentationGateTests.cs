using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Lexing;
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
    private static IReadOnlyCollection<string> RegisteredComponentKinds =>
        [.. ComponentRegistry.Default.Kinds.Select(static kind => kind.Keyword)];

    /// <summary>
    /// Reserved words that introduce a statement, which the gate must cover as well as the component
    /// registry.
    /// </summary>
    /// <remarks>
    /// Enumerating these matters as much as enumerating the kinds: <c>D-33</c>, <c>D-37</c> and
    /// <c>D-40</c> added five statements that are not component kinds, and a gate walking only the
    /// registry would have passed all five undocumented. <c>dynamic</c> and <c>static</c> are absent
    /// because they qualify another directive and introduce nothing — they are documented on the page
    /// of the directive they qualify, which is where a reader meets them.
    /// </remarks>
    private static IReadOnlyCollection<string> StatementReservedWords =>
        [.. Enum.GetValues<ReservedWord>()
            .Where(static word => word is not (ReservedWord.None or ReservedWord.Dynamic or ReservedWord.Static))
            .Select(ReservedWords.TextOf)];

    // `heat_exchanger` is documented at `heat-exchanger.md`: the file names use hyphens, because a URL
    // does. `inlet` and `outlet` share one page, since neither is meaningful without the other.
    private static bool HasPage(string name)
    {
        var slug = name switch
        {
            "inlet" or "outlet" => "inlet-outlet",
            _ => name.Replace('_', '-'),
        };

        return RequiredCategories.Any(category =>
            File.Exists(Path.Combine(DocsRoot, category, $"{slug}.md")));
    }

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
    public void TheDiagnosticsPageIsGeneratedFromTheRegistry() =>
        AssertGenerated(
            "diagnostics.md",
            (DiagnosticsPage.CodesRegion, DiagnosticsPage.RenderCodes()),
            (DiagnosticsPage.Language2Region, DiagnosticsPage.RenderLanguage2()),
            (DiagnosticsPage.RetiredRegion, DiagnosticsPage.RenderRetired()));

    [Fact]
    [Trait("Category", "Docs")]
    public void TheSyntaxPageListsEveryReservedWord() =>
        AssertGenerated("syntax.md", (SyntaxPage.ReservedWordsRegion, SyntaxPage.Render()));

    [Fact]
    [Trait("Category", "Docs")]
    public void TheUnitsPageIsGeneratedFromTheUnitTable() =>
        AssertGenerated(
            "units.md",
            (UnitsPage.DimensionsRegion, UnitsPage.Render()),
            (UnitsPage.SymbolsRegion, UnitsPage.RenderSymbols()));

    [Fact]
    [Trait("Category", "Docs")]
    public void ThePropertiesPageIsGeneratedFromTheRegistry() =>
        AssertGenerated("properties.md", (RegistryPages.PropertiesRegion, RegistryPages.RenderProperties()));

    [Fact]
    [Trait("Category", "Docs")]
    public void ThePropertiesPageCoversEveryNameAReferenceCanResolve()
    {
        // `T-2`: the gate above holds the page to its generator, which is blind to what the generator
        // leaves out -- a tank's indexed families were missing for a phase while it stayed green. This
        // one holds the generator to the resolver: every spelling a reference may write -- each fixed
        // name, its retired spellings, each family at both ends of its range and in its retired pattern,
        // and each state quantity by its long name and its aliases (`D-120`) -- must resolve through
        // `ResolveProperty`, and what it resolves to must have a row.
        var rendered = RegistryPages.RenderProperties();

        foreach (var kind in ComponentRegistry.Default.Kinds)
        {
            var probes = new List<(string Written, string Row)>();

            foreach (var property in kind.Properties.Values)
            {
                probes.Add((property.Name, property.Name));
                probes.AddRange(property.LegacySpellings.Select(legacy => (legacy, property.Name)));

                // `in[2].t` may be written `in[2].temperature`; a bare `t` as `temperature`.
                var dot = property.Name.LastIndexOf('.');
                var prefix = dot < 0 ? string.Empty : property.Name[..(dot + 1)];
                var symbol = dot < 0 ? property.Name : property.Name[(dot + 1)..];

                if (PropertyTable.Find(symbol) is { } entry && entry.Symbol == symbol)
                {
                    probes.Add((prefix + entry.Name, property.Name));
                    probes.AddRange(entry.Aliases.Select(alias => (prefix + alias, property.Name)));
                }
            }

            foreach (var family in kind.IndexedPropertyFamilies)
            {
                probes.Add((IndexedName.Spell(family.Pattern, family.MinIndex), family.Pattern));

                if (family.MaxIndex is { } max)
                {
                    probes.Add((IndexedName.Spell(family.Pattern, max), family.Pattern));
                }

                if (family.LegacyPattern is { } legacy)
                {
                    probes.Add((IndexedName.Spell(legacy, family.MinIndex), family.Pattern));
                }
            }

            foreach (var (written, row) in probes)
            {
                Assert.True(
                    kind.ResolveProperty(written) is not null,
                    $"R-28: `{kind.Keyword}.{written}` is a spelling the registry offers and the resolver refuses.");
                Assert.True(
                    rendered.Contains($"| `{kind.Keyword}` | `{row}`", StringComparison.Ordinal),
                    $"R-28: `{kind.Keyword}.{written}` resolves, and the properties page has no row for `{row}`.");
            }
        }
    }

    [Fact]
    [Trait("Category", "Docs")]
    public void TheTagsPageIsGeneratedFromTheRegistry() =>
        AssertGenerated("tags.md", (RegistryPages.TagsRegion, RegistryPages.RenderTags()));

    [Fact]
    [Trait("Category", "Docs")]
    public void TheModelContractPageIsGeneratedFromTheWireRecords() =>
        AssertGenerated(
            "model-contract.md",
            (SymbolsPage.Region, SymbolsPage.Render()),
            (ContractPage.FieldsRegion, ContractPage.Render()));

    private static void AssertGenerated(string page, params (string Region, string Content)[] regions)
    {
        var path = Path.Combine(DocsRoot, "functions", page);
        Assert.True(
            File.Exists(path),
            $"R-28: {page} is missing. Expected {RepositoryLayout.ToRelative(path)}.");

        var committed = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        var current = regions.Aggregate(
            committed,
            static (document, region) => GeneratedRegion.Write(document, region.Region, region.Content));

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
            $"R-28: {RepositoryLayout.ToRelative(path)} did not match what the code generates and has "
            + "been regenerated in place. Review the change and run the tests again.");
    }
}
