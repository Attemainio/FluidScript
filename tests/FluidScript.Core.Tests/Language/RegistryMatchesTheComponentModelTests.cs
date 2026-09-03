using System.Collections.Immutable;
using System.Text.RegularExpressions;

using FluidScript.Core.Language;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Language;

/// <summary>
/// <c>plan/20-core-domain/22-component-model.md</c>'s parameter registry section asks for exactly this
/// test: the registry's parameter set is compared against that document's tables.
/// </summary>
/// <remarks>
/// Without it the two diverge on the first component change, and the divergence is invisible until a
/// user writes a parameter the documentation promises and the binder rejects. Reading the tables is
/// worth the parsing: a hand-copied list in a test is a third place to keep in step.
/// </remarks>
public sealed partial class RegistryMatchesTheComponentModelTests
{
    /// <summary>
    /// Parameters <c>22</c> documents that the registry deliberately does not accept, with the reason.
    /// </summary>
    /// <remarks>
    /// Both are placeholders in <c>22</c>'s tables, with no dimension and no range, for work that is
    /// out of scope in v1. Registering them would make <c>FS1503</c> accept a name nothing reads, and
    /// the user would get silence where they expect an effect.
    /// </remarks>
    private static readonly ImmutableDictionary<string, string> DeliberatelyUnregistered =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pipe.insulation"] = "reserved; heat loss is post-v1",
            ["pump.curve"] = "named curves arrive with the catalogue in P3.5",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryDocumentedParameterIsRegistered()
    {
        var documented = ReadComponentModel();
        var missing = new List<string>();

        foreach (var (keyword, section) in documented)
        {
            var kind = ComponentRegistry.Default.ByKeyword(keyword);
            Assert.NotNull(kind);

            foreach (var parameter in section.Parameters)
            {
                if (DeliberatelyUnregistered.ContainsKey($"{keyword}.{parameter}"))
                {
                    continue;
                }

                // An alias counts as covered: `22` writes the tank's row as ``volume` (`v` alias)`, and
                // `v` is a spelling of `volume` rather than a parameter of its own (D-32).
                var covered = kind.Parameters.ContainsKey(parameter)
                    || kind.Parameters.Values.Any(known => known.Aliases.Contains(parameter))
                    || IsFamilyMember(kind, parameter);

                if (!covered)
                {
                    missing.Add($"{keyword}.{parameter}");
                }
            }
        }

        Assert.True(missing.Count == 0, $"22 documents parameters the registry has not: {string.Join(", ", missing)}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryRegisteredParameterIsDocumented()
    {
        var documented = ReadComponentModel();
        var undocumented = new List<string>();

        foreach (var kind in ComponentRegistry.Default.Kinds)
        {
            // The controller's gains live in 34, not 22, which describes flow components.
            if (!documented.TryGetValue(kind.Keyword, out var section))
            {
                continue;
            }

            foreach (var parameter in kind.Parameters.Keys)
            {
                if (!section.Parameters.Contains(parameter))
                {
                    undocumented.Add($"{kind.Keyword}.{parameter}");
                }
            }
        }

        Assert.True(
            undocumented.Count == 0,
            $"The registry accepts parameters 22 does not document: {string.Join(", ", undocumented)}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryDocumentedPropertyIsRegistered()
    {
        var documented = ReadComponentModel();
        var missing = new List<string>();

        foreach (var (keyword, section) in documented)
        {
            var kind = ComponentRegistry.Default.ByKeyword(keyword)!;

            foreach (var property in section.Properties)
            {
                // `t1`…`tN` and `inN_t` are per-layer and per-port, materialized from `layers` and
                // from whichever ports a script actually names; they cannot be a fixed dictionary.
                if (kind.Properties.ContainsKey(property) || IsFamilyMember(kind, property) || IsPortProperty(property))
                {
                    continue;
                }

                missing.Add($"{keyword}.{property}");
            }
        }

        Assert.True(missing.Count == 0, $"22 documents properties the registry has not: {string.Join(", ", missing)}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheTagCodesAreTheOnesDocumented()
    {
        // 22's tag-code table, transcribed once here because it is six rows and parsing it would test
        // the parser rather than the data.
        var expected = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["pump"] = "PU",
            ["heat_exchanger"] = "HE",
            ["valve"] = "V",
            ["three_way_valve"] = "TV",
            ["tank"] = "S",
            ["controller"] = "PID",
            ["t_sensor"] = "TE",
            ["p_sensor"] = "PE",
            ["flow_sensor"] = "FE",
            // A tag names a piece of equipment (`D-34`), and a state point is not one. A boundary is a
            // node, so it carries no tag either.
            ["node"] = null,
            ["supply"] = null,
            ["return"] = null,
            ["pipe"] = null,
        };

        foreach (var kind in ComponentRegistry.Default.Kinds)
        {
            Assert.Equal(expected[kind.Keyword], kind.TagCode);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheDocumentWasActuallyRead()
    {
        // A parser that silently matches nothing turns all three comparisons above into tautologies.
        var documented = ReadComponentModel();

        Assert.Equal(
            ["flow_sensor", "heat_exchanger", "node", "p_sensor", "pipe", "pump", "return", "supply",
             "t_sensor", "tank", "three_way_valve", "valve"],
            documented.Keys.Order(StringComparer.Ordinal));

        Assert.Contains("power", documented["heat_exchanger"].Parameters);
        Assert.Contains("velocity", documented["pipe"].Properties);
    }

    // `22` writes a family as `t1`…`tN`, which reaches here as the two names `t1` and `tN`. Both stand
    // for the same registry family, whose index runs to whatever `layers` says.
    private static bool IsFamilyMember(ComponentKindInfo kind, string documented) =>
        kind.IndexedParameterFamilies.Any(family =>
            IndexPattern(family.Pattern).IsMatch(documented)
            || IndexPattern(family.Pattern).IsMatch(documented.Replace('N', '1')));

    // `inN_t` and `outN_t` exist per materialized port, so they are neither a fixed property nor a
    // parameter family: which of them exist is decided by the connections a script writes.
    private static bool IsPortProperty(string documented) =>
        documented.EndsWith("N_t", StringComparison.Ordinal);

    // `t1`…`tN` in the document stands for a family the registry writes as `t{index}`.
    private static Regex IndexPattern(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace(@"\{index}", @"\d+", StringComparison.Ordinal) + "$",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

    private static ImmutableDictionary<string, DocumentedKind> ReadComponentModel()
    {
        var text = File.ReadAllText(
            Path.Combine(RepositoryLayout.Root, "plan", "20-core-domain", "22-component-model.md"));

        var sections = ImmutableDictionary.CreateBuilder<string, DocumentedKind>(StringComparer.Ordinal);

        // Every second-level heading, not only the numbered ones: a component section is bounded by
        // whatever comes next, and the last of them is followed by `## Parameter registry` and
        // `## Error cases`, whose tables would otherwise be read as the tank's parameters.
        var headings = AnyHeading().Matches(text);

        for (var i = 0; i < headings.Count; i++)
        {
            if (SectionHeading().Match(headings[i].Value) is not { Success: true } section)
            {
                continue;
            }

            var start = headings[i].Index;
            var end = i + 1 < headings.Count ? headings[i + 1].Index : text.Length;
            var body = text[start..end];

            var parameters = ParameterTable().Match(body) is { Success: true } table
                ? ParameterRow().Matches(table.Groups[1].Value)
                    .SelectMany(static row => Backticked().Matches(row.Groups[1].Value))
                    .Select(static name => name.Groups[1].Value)
                    .Where(static name => !name.Contains('…', StringComparison.Ordinal))
                    .ToImmutableHashSet(StringComparer.Ordinal)
                : [];

            var properties = PropertiesLine().Match(body) is { Success: true } line
                ? Backticked().Matches(line.Groups[1].Value)
                    .Select(static name => name.Groups[1].Value)
                    .ToImmutableHashSet(StringComparer.Ordinal)
                : [];

            foreach (Match keyword in Backticked().Matches(section.Groups[1].Value))
            {
                sections[keyword.Groups[1].Value] = new DocumentedKind(parameters, properties);
            }
        }

        return sections.ToImmutable();
    }

    [GeneratedRegex(@"^## \d+ · (.+)$")]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@"^## .+$", RegexOptions.Multiline)]
    private static partial Regex AnyHeading();

    // The parameter table only, from its header to the blank line that ends it. A section holds other
    // tables whose first cell is a backticked name — the exchanger's arrangement formulae are one —
    // and reading those as parameters is how `counter` became a parameter of `heat_exchanger`.
    [GeneratedRegex(@"^\| Parameter(?: pattern)? \| Dimension \|.*\n\|[-| ]+\|\n((?:\|.*\n)+)", RegexOptions.Multiline)]
    private static partial Regex ParameterTable();

    // A row of that table: the first cell, which may hold several names.
    [GeneratedRegex(@"^\| (`[^|]+) \|", RegexOptions.Multiline)]
    private static partial Regex ParameterRow();

    [GeneratedRegex(@"^\*\*Properties:\*\* (.+(?:\n[^\n*].*)?)$", RegexOptions.Multiline)]
    private static partial Regex PropertiesLine();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Backticked();
}

/// <summary>One component section of <c>22</c>, as the document states it.</summary>
/// <param name="Parameters">Every parameter named in the section's parameter table.</param>
/// <param name="Properties">Every property named on its <c>**Properties:**</c> line.</param>
internal readonly record struct DocumentedKind(
    ImmutableHashSet<string> Parameters,
    ImmutableHashSet<string> Properties);
