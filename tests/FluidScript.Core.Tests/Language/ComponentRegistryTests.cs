using FluidScript.Core.Language;
using FluidScript.Core.Syntax;
using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Language;

/// <summary>
/// The registry and kind resolution from <c>plan/10-language/15-semantic-model.md</c>: what resolves
/// silently, what resolves loudly, what refuses to resolve, and the rules the registry data itself
/// must obey.
/// </summary>
public sealed class ComponentRegistryTests
{
    private static ComponentRegistry Registry => ComponentRegistry.Default;

    [Fact]
    [Trait("Category", "Unit")]
    public void TheRegistryBuilds()
    {
        // Building runs every self-check: duplicate spellings, aliases that are reserved words,
        // duplicate tag codes, and tag codes that would lex as quantities. A throw here is the data
        // being wrong, which is why the constructor does the work rather than a test repeating it.
        Assert.Equal(11, Registry.Kinds.Length);
        Assert.NotNull(Registry.ByKeyword("three_way_valve"));
    }

    [Theory]
    [InlineData("node", "node")]
    [InlineData("junction", "node")]
    [InlineData("heat_exchanger", "heat_exchanger")]
    [InlineData("HeatExchanger", "heat_exchanger")]
    [InlineData("heat exchanger", "heat_exchanger")]
    [InlineData("HEAT_EXCHANGER", "heat_exchanger")]
    [InlineData("radiator", "heat_exchanger")]
    [InlineData("boiler", "heat_exchanger")]
    [InlineData("3WayValve", "three_way_valve")]
    [InlineData("3wv", "three_way_valve")]
    [InlineData("mixing_valve", "three_way_valve")]
    [InlineData("circulator", "pump")]
    [InlineData("container", "tank")]
    [InlineData("pid", "controller")]
    [InlineData("thermostat", "controller")]
    [Trait("Category", "Unit")]
    public void AKnownSpellingResolvesSilently(string written, string keyword)
    {
        // Stage 2. A spelling the registry knows is not a guess, so there is nothing to report: the
        // result is Exact, and the binder emits no diagnostic for it.
        var exact = Assert.IsType<KindResolution.Exact>(Registry.Resolve(written));

        Assert.Equal(keyword, exact.Kind.Keyword);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ATypoResolvesAndSaysSo()
    {
        // Stage 3, and 15's own example: pmp scores 0.75 against pump, clear of everything else.
        var similar = Assert.IsType<KindResolution.Similar>(Registry.Resolve("pmp"));

        Assert.Equal("pump", similar.Kind.Keyword);
        Assert.Equal(0.75, similar.Score, 3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ATranspositionCostsOneKeystrokeNotTwo()
    {
        // Why Damerau rather than plain Levenshtein: under plain edit distance `pmup` is two
        // substitutions and scores 0.50, below the threshold, so the commonest typing error there is
        // would not resolve.
        var similar = Assert.IsType<KindResolution.Similar>(Registry.Resolve("pmup"));

        Assert.Equal("pump", similar.Kind.Keyword);
        Assert.Equal(0.75, similar.Score, 3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SomethingUnrelatedDoesNotResolve()
    {
        // 15's other example: `valve` against `pipe` scores 0.20 and must not resolve. The word here
        // is a real one a user might write for an air-side component, and D-28 wants it to fail
        // clearly rather than produce a hydronic answer wearing air-side names.
        var unknown = Assert.IsType<KindResolution.Unknown>(Registry.Resolve("fan"));

        Assert.Null(unknown.SuggestedKeyword);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AWordCloseToNothingCarriesNoSuggestion()
    {
        // FS1502's message offers a suggestion, and offering `pipe` for this would read as the tool
        // guessing. Below the suggestion floor the code says only that the kind is unknown.
        Assert.Null(Assert.IsType<KindResolution.Unknown>(Registry.Resolve("qqqqqqqq")).SuggestedKeyword);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AWordCloseToOneKindButNotCloseEnoughSuggestsIt()
    {
        // Three edits in nine characters: too far to act on, plainly aimed at one kind.
        var unknown = Assert.IsType<KindResolution.Unknown>(Registry.Resolve("exchan"));

        Assert.Equal("heat_exchanger", unknown.SuggestedKeyword);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TwoKindsAnEqualDistanceAwayResolveToNeither()
    {
        // The ambiguity margin, on the case that actually arises: a four-way valve is a real device
        // this version does not model, and `4_way_valve` is exactly one substitution from both
        // `2_way_valve` and `3_way_valve`. They mean very different circuits, and picking the higher
        // of two equal scores is a coin flip whose result the user has no way to see.
        var ambiguous = Assert.IsType<KindResolution.Ambiguous>(Registry.Resolve("4_way_valve"));

        Assert.Equal(
            ["three_way_valve", "valve"],
            ambiguous.Candidates.Select(static kind => kind.Keyword).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AKindIsNeverAmbiguousWithItself()
    {
        // A kind reached through several of its own aliases scores once. Counting each alias as a
        // separate candidate would make the kinds with the most aliases the hardest to resolve.
        var similar = Assert.IsType<KindResolution.Similar>(Registry.Resolve("exchangr"));

        Assert.Equal("heat_exchanger", similar.Kind.Keyword);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResolutionNeverThrowsOnAnythingAUserCanType()
    {
        foreach (var written in new[] { "", " ", "_", "___", "3", "3wv", new string('x', 500), "é", "😀" })
        {
            Assert.NotNull(Registry.Resolve(written));
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NoAliasIsAReservedWord()
    {
        // D-40 reserved `control`, which silently invalidated it as an alias of `controller`: a
        // reserved word never reaches kind position, so the alias was unwriteable. This is the check
        // that makes the next reserved word fail the build instead of repeating it.
        foreach (var kind in Registry.Kinds)
        {
            foreach (var alias in kind.Aliases)
            {
                Assert.False(
                    ReservedWords.TryMatch(alias, out _),
                    $"'{alias}' is a reserved word and can never appear in kind position.");
            }
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NoTagCodeMakesATagLexAsAQuantity()
    {
        // `400PU01` is safe because `PU01` is not a unit symbol. A code sharing a symbol with the unit
        // table would produce tags the language reads as a number and a unit.
        foreach (var kind in Registry.Kinds.Where(static kind => kind.TagCode is not null))
        {
            var tag = $"400{kind.TagCode}01";
            var tokens = Lexer.Lex(new SourceText(tag)).Tokens;

            Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
            Assert.Equal(tag, tokens[0].Text);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TagCodesAreUniqueAndOnlyNodeAndPipeLackOne()
    {
        var tagged = Registry.Kinds.Where(static kind => kind.TagCode is not null).ToArray();
        var untagged = Registry.Kinds.Where(static kind => kind.TagCode is null).Select(static k => k.Keyword);

        Assert.Equal(tagged.Length, tagged.Select(static kind => kind.TagCode).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["node", "pipe"], untagged.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OnlyThePumpDrivesFlow()
    {
        Assert.Equal(
            ["pump"],
            Registry.Kinds.Where(static kind => kind.DrivesFlow).Select(static kind => kind.Keyword));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OnlyTheNodeTakesUnlimitedPorts()
    {
        Assert.Equal(
            ["node"],
            Registry.Kinds.Where(static kind => kind.HasUnlimitedPorts).Select(static kind => kind.Keyword));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RangesAreHeldInSiWhateverTheTableWrites()
    {
        // 22's tables are written the way a user writes a value — a bare number in the canonical unit.
        // A temperature range of -50 … 300 is °C, and holding it as -50 K would put every plausible
        // temperature outside its own range.
        var node = Registry.ByKeyword("node")!;
        var temperature = node.Parameters["t"].UsualRange!.Value;

        Assert.Equal(223.15, temperature.Min, 2);
        Assert.Equal(573.15, temperature.Max, 2);
        Assert.True(temperature.Contains(Quantity.FromBareNumber(20, Dimension.Temperature).SiValue));

        // Volume is the other one that would pass unnoticed: 300 dm³ is 0.3 m³.
        var tank = Registry.ByKeyword("tank")!;
        Assert.Equal(1e-3, tank.Parameters["volume"].UsualRange!.Value.Min, 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryDefaultedParameterStatesItsBasis()
    {
        // D-02: absence means sizing or a default that is visible and explained. A default with no
        // stated reason is a magic number the user cannot argue with.
        foreach (var kind in Registry.Kinds)
        {
            foreach (var parameter in kind.Parameters.Values)
            {
                if (parameter.OmissionBehavior == ParameterOmissionBehavior.Default)
                {
                    Assert.False(string.IsNullOrWhiteSpace(parameter.DefaultLiteral), $"{kind.Keyword}.{parameter.Name}");
                    Assert.False(string.IsNullOrWhiteSpace(parameter.DefaultBasis), $"{kind.Keyword}.{parameter.Name}");
                }
                else
                {
                    Assert.Null(parameter.DefaultLiteral);
                    Assert.Null(parameter.DefaultBasis);
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASymbolParameterListsWhatItAccepts()
    {
        var valve = Registry.ByKeyword("valve")!;
        var characteristic = valve.Parameters["characteristic"];

        Assert.Equal(ParameterValueKind.Symbol, characteristic.ValueKind);
        Assert.Equal(["linear", "equal_percentage", "quick_open"], characteristic.AcceptedSymbols);

        foreach (var kind in Registry.Kinds)
        {
            foreach (var parameter in kind.Parameters.Values)
            {
                Assert.Equal(
                    parameter.ValueKind == ParameterValueKind.Symbol,
                    !parameter.AcceptedSymbols.IsDefaultOrEmpty);
            }
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheTankCarriesItsAliasAndItsFamilies()
    {
        var tank = Registry.ByKeyword("tank")!;

        Assert.Equal(["v"], tank.Parameters["volume"].Aliases);
        Assert.Equal("300 dm3", tank.Parameters["volume"].DefaultLiteral);
        Assert.Equal("5", tank.Parameters["layers"].DefaultLiteral);

        Assert.Equal(
            ["in{index}_elevation", "out{index}_elevation", "t{index}"],
            tank.IndexedParameterFamilies.Select(static family => family.Pattern).Order(StringComparer.Ordinal));

        // The layer count is a parameter, so the family's maximum is not a constant.
        var layers = tank.IndexedParameterFamilies.Single(static f => f.Pattern == "t{index}");
        Assert.Equal("layers", layers.MaxIndexParameter);
        Assert.Null(layers.MaxIndex);

        Assert.Equal(16, tank.PortFamilies.Single(static f => f.Prefix == "in").MaxIndex);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheThreeWayValvesPortsAreAllBidirectional()
    {
        // Mixing and diverting are both real, and which one a valve is comes from the topology. Fixed
        // roles made a mixing valve expressible only by relying on reverse flow, which put FS4009 on a
        // correct design.
        var valve = Registry.ByKeyword("three_way_valve")!;

        Assert.Equal(["a", "b", "c"], valve.Ports.Select(static port => port.Name));
        Assert.All(valve.Ports, static port => Assert.Equal(PortRole.Bidirectional, port.Role));
        Assert.Equal(["c"], valve.Ports.Where(static p => p.IsOptional).Select(static p => p.Name));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheExchangersSecondSideIsOptional()
    {
        var exchanger = Registry.ByKeyword("heat_exchanger")!;

        Assert.Equal(["in2", "out2"], exchanger.Ports.Where(static p => p.IsOptional).Select(static p => p.Name));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryPropertyReportsAUnit()
    {
        foreach (var kind in Registry.Kinds)
        {
            foreach (var property in kind.Properties.Values)
            {
                // A designation is the one dimension with nothing to report a unit in: `dn` reads back
                // DN25, which is a name for a pipe size and not 25 of anything.
                Assert.False(
                    string.IsNullOrWhiteSpace(property.CanonicalUnit) && property.Dimension.SiUnit.Length > 0,
                    $"{kind.Keyword}.{property.Name} has a dimension and no unit to report it in.");
            }
        }
    }
}
