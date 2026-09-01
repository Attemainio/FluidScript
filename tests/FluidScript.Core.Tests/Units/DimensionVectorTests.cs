using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Units;

public sealed class DimensionVectorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Addition_IsWhatMultiplyingQuantitiesDoes()
    {
        // Specific heat x temperature difference is a specific enthalpy.
        var product = Dimension.SpecificHeat.Vector + Dimension.TemperatureDelta.Vector;

        Assert.Equal(Dimension.Enthalpy.Vector, product);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Subtraction_IsWhatDividingQuantitiesDoes()
    {
        // Power divided by specific enthalpy is a mass flow -- the plan's own worked combination.
        var quotient = Dimension.Power.Vector - Dimension.Enthalpy.Vector;

        Assert.Equal(Dimension.MassFlow.Vector, quotient);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Negation_IsAReciprocal()
    {
        Assert.Equal(DimensionVector.None - Dimension.Time.Vector, -Dimension.Time.Vector);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(1, 2, -3, 0, "W")]
    [InlineData(1, 2, -2, 0, "J")]
    [InlineData(1, -1, -2, 0, "Pa")]
    [InlineData(0, 0, 0, 1, "K")]
    [InlineData(1, 0, -1, 0, "kg/s")]
    public void ToSiUnitString_UsesTheNamedUnitWhenTheVectorIsOne(
        int mass, int length, int time, int temperature, string expected)
    {
        Assert.Equal(expected, new DimensionVector(mass, length, time, temperature).ToSiUnitString());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToSiUnitString_PrefersANamedUnitOverBaseUnits()
    {
        // FS1304 has to name the unit a value arrived with. 'W/K' tells a user what they wrote;
        // 'kg.m^2/(s^3.K)' tells them to go and derive it.
        var perKelvin = Dimension.Power.Vector - Dimension.TemperatureDelta.Vector;

        Assert.Equal("W/K", perKelvin.ToSiUnitString());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToSiUnitString_FallsBackToBaseUnits()
    {
        Assert.Equal("kg²", new DimensionVector(2, 0, 0, 0).ToSiUnitString());
        Assert.Equal("m³/s²", new DimensionVector(0, 3, -2, 0).ToSiUnitString());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToSiUnitString_IsEmptyForADimensionlessVector()
    {
        Assert.True(DimensionVector.None.IsNone);
        Assert.Equal(string.Empty, DimensionVector.None.ToSiUnitString());
    }
}
