using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Units;

public sealed class UnitTableTests
{
    private static UnitSymbol Symbol(string text, Dimension dimension)
    {
        Assert.True(UnitTable.TryResolve(text, dimension, out var symbol), $"'{text}' is not a {dimension} unit.");
        return symbol;
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EverySpelling_RoundTripsThroughSi()
    {
        // Invariant 4. A conversion that is not its own inverse is a constant-factor error, which is
        // the hardest kind to see because every result stays plausible.
        foreach (var symbol in UnitTable.All)
        {
            foreach (var value in new[] { 0.0, 1.0, -3.5, 1234.5 })
            {
                var returned = symbol.FromSi(symbol.ToSi(value));
                var tolerance = Math.Max(1e-12 * Math.Abs(value), 1e-9);
                Assert.True(
                    Math.Abs(returned - value) <= tolerance,
                    $"{symbol.Text}: {value} -> {symbol.ToSi(value)} -> {returned}");
            }
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NoSpellingDenotesTwoDimensions_ExceptTheSharedPressureOnes()
    {
        var ambiguous = UnitTable.All
            .GroupBy(static symbol => symbol.Text, StringComparer.Ordinal)
            .Where(static group => group.Select(static s => s.Dimension).Distinct().Count() > 1)
            .ToArray();

        foreach (var group in ambiguous)
        {
            var dimensions = group.Select(static s => s.Dimension).Distinct().ToHashSet();
            Assert.True(
                dimensions.SetEquals([Dimension.Pressure, Dimension.PressureDelta]),
                $"'{group.Key}' denotes {string.Join(" and ", dimensions)}, which is not the documented exception.");
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HeadAndKv_HaveNoSpellingAtAll()
    {
        // D-50. Head is metres of the PUMPED FLUID; mH2O is metres of water column, a pressure of
        // 9806.65 Pa per metre. They coincide only for water, so one spelling for both would be wrong
        // by the density ratio in every glycol circuit -- and entirely plausible on the diagram.
        Assert.Empty(UnitTable.For(Dimension.Head));
        Assert.Empty(UnitTable.For(Dimension.Kv));
        Assert.Empty(UnitTable.For(Dimension.NominalDiameter));

        Assert.True(UnitTable.TryResolve("mH2O", Dimension.Pressure, out _));
        Assert.False(UnitTable.TryResolve("mH2O", Dimension.Head, out _));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Dimensionless_KeepsPercentAndRefusesTheHyphen()
    {
        // D-50. Under the whitespace rule a unit symbol is recognised after a number token, so '-'
        // would swallow the operator in `let x = 5 - 3` and strand the 3.
        Assert.True(UnitTable.TryResolve("%", out var percent));
        Assert.Equal(Dimension.Dimensionless, percent.Dimension);
        Assert.Empty(UnitTable.Candidates("-"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryDimension_HasASpellingOrIsBareOnly()
    {
        string[] bareOnly = ["Head", "Kv", "NominalDiameter", "Dimensionless"];

        foreach (var dimension in Dimension.All)
        {
            if (bareOnly.Contains(dimension.Name, StringComparer.Ordinal))
            {
                continue;
            }

            Assert.NotEmpty(UnitTable.For(dimension));
        }
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("mm", 0.001)]
    [InlineData("Mm", double.NaN)]
    public void SiSpellingsAreCaseSensitive(string text, double expectedFactor)
    {
        // 'mm' and 'Mm' differ by a factor of a billion, so case folding must never reach an SI
        // spelling. Mm is simply not in the table.
        if (double.IsNaN(expectedFactor))
        {
            Assert.Empty(UnitTable.Candidates(text));
            return;
        }

        Assert.Equal(expectedFactor, Symbol(text, Dimension.Length).Factor);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("bar")]
    [InlineData("BAR")]
    [InlineData("Bar")]
    public void MultiLetterNonSiNamesAreCaseInsensitive(string text)
    {
        Assert.True(UnitTable.TryResolve(text, Dimension.Pressure, out var symbol));
        Assert.Equal(1e5, symbol.Factor);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("kW", 30, 30_000)]
    [InlineData("W", 30_000, 30_000)]
    [InlineData("MW", 0.03, 30_000)]
    public void Power_ConvertsToWatts(string text, double value, double expected)
    {
        Assert.Equal(expected, Symbol(text, Dimension.Power).ToSi(value), 6);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("C", 20, 293.15)]
    [InlineData("°C", 20, 293.15)]
    [InlineData("K", 293.15, 293.15)]
    [InlineData("F", 68, 293.15)]
    public void Temperature_ConvertsToKelvin(string text, double value, double expected)
    {
        Assert.Equal(expected, Symbol(text, Dimension.Temperature).ToSi(value), 9);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("kPa", 300)]
    [InlineData("bar", 3)]
    [InlineData("kPag", 300)]
    [InlineData("barg", 3)]
    [InlineData("kPaa", 401.325)]
    [InlineData("bara", 4.01325)]
    public void Pressure_StoresGaugePascals(string text, double value)
    {
        // The SI representation is GAUGE, so an absolute spelling has the atmosphere removed on the
        // way in. 401.325 kPa absolute and 300 kPa gauge are the same hydraulic state.
        Assert.Equal(300_000, Symbol(text, Dimension.Pressure).ToSi(value), 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PressureDifference_HasNoGaugeOrAbsoluteSpelling()
    {
        // A difference has no datum to be measured against, so kPag and kPaa have no delta twin.
        Assert.True(UnitTable.TryResolve("kPa", Dimension.PressureDelta, out var delta));
        Assert.Equal(0, delta.Offset);
        Assert.False(UnitTable.TryResolve("kPag", Dimension.PressureDelta, out _));
        Assert.False(UnitTable.TryResolve("kPaa", Dimension.PressureDelta, out _));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryResolve_RefusesASharedSpellingWithoutAnExpectedDimension()
    {
        Assert.False(UnitTable.TryResolve("kPa", out _));
        Assert.Equal(2, UnitTable.Candidates("kPa").Length);

        Assert.True(UnitTable.TryResolve("kW", out _));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CanonicalUnitFor_IsTheUnitABareNumberMeans()
    {
        Assert.Equal("kW", UnitTable.CanonicalUnitFor(Dimension.Power)?.Text);
        Assert.Equal("°C", UnitTable.CanonicalUnitFor(Dimension.Temperature)?.Text);
        Assert.Equal("kPa", UnitTable.CanonicalUnitFor(Dimension.Pressure)?.Text);
        Assert.Equal("dm3", UnitTable.CanonicalUnitFor(Dimension.Volume)?.Text);
        Assert.Equal("m", UnitTable.CanonicalUnitFor(Dimension.Length)?.Text);
        Assert.Null(UnitTable.CanonicalUnitFor(Dimension.Head));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UnknownSpelling_IsNotAUnit()
    {
        Assert.Empty(UnitTable.Candidates("kWh/kg"));
        Assert.Empty(UnitTable.Candidates("furlong"));
        Assert.False(UnitTable.TryResolve("furlong", out _));
    }
}
