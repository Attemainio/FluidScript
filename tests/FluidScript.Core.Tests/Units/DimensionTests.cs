using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Units;

public sealed class DimensionTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void TemperatureAndItsDifference_AreDistinctDimensions()
    {
        // The single most important assertion in the unit system. Modelling both as one dimension
        // makes 20 C + 30 C compile and return 596.3 K, silently.
        Assert.NotEqual(Dimension.Temperature, Dimension.TemperatureDelta);
        Assert.Equal(Dimension.Temperature.Vector, Dimension.TemperatureDelta.Vector);
        Assert.Equal(Dimension.TemperatureDelta, Dimension.Temperature.Delta);
        Assert.Equal(Dimension.Temperature, Dimension.TemperatureDelta.Absolute);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LengthAndHead_AreDistinctDespiteSharingTheMetre()
    {
        Assert.NotEqual(Dimension.Length, Dimension.Head);
        Assert.Equal(Dimension.Length.Vector, Dimension.Head.Vector);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromVector_ResolvesADerivedCombinationToItsName()
    {
        var vector = Dimension.Power.Vector - Dimension.Enthalpy.Vector;

        Assert.Equal(Dimension.MassFlow, Dimension.FromVector(vector));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromVector_NeverProducesAnAbsoluteDimension()
    {
        // Enthalpy over specific heat is kelvin, and what it means is a DIFFERENCE: arithmetic on
        // ratio quantities cannot manufacture a reading on an affine scale, because nothing in the
        // operands says where that scale's zero sits.
        var vector = Dimension.Enthalpy.Vector - Dimension.SpecificHeat.Vector;

        Assert.Equal(Dimension.TemperatureDelta, Dimension.FromVector(vector));
        Assert.NotEqual(Dimension.Temperature, Dimension.FromVector(vector));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromVector_NeverProducesADesignationOrAHead()
    {
        // Otherwise a length times a number would silently become a pump head, and a volume over a
        // time would become a valve coefficient.
        Assert.Equal(Dimension.Length, Dimension.FromVector(Dimension.Head.Vector));
        Assert.Equal(Dimension.VolumeFlow, Dimension.FromVector(Dimension.Kv.Vector));

        foreach (var dimension in Dimension.All.Where(static d => !d.IsSynthesisable))
        {
            Assert.NotEqual(dimension, Dimension.FromVector(dimension.Vector));
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromVector_LeavesAnUnnamedCombinationUnnamed()
    {
        var perKelvin = Dimension.FromVector(Dimension.Power.Vector - Dimension.TemperatureDelta.Vector);

        Assert.False(perKelvin.IsNamed);
        Assert.Equal(DimensionId.Unnamed, perKelvin.Id);
        Assert.Equal("W/K", perKelvin.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExactlyFiveDimensions_ReadABareNumberInSomethingOtherThanSi()
    {
        // Invariant 8. Five rows spelling four distinct units -- degC, kPa twice, kW and dm3.
        var exceptions = Dimension.All.Where(static d => d.CanonicalDiffersFromSi).ToArray();

        Assert.Equal(
            [Dimension.Temperature, Dimension.Pressure, Dimension.PressureDelta, Dimension.Power, Dimension.Volume],
            exceptions);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheDeltaSpelling_ChangesTypeRatherThanScale()
    {
        // dK is not a sixth exception: it differs from K in what it means, not what it is worth.
        Assert.False(Dimension.TemperatureDelta.CanonicalDiffersFromSi);
        Assert.Equal("dK", Dimension.TemperatureDelta.CanonicalUnit);
        Assert.Equal("K", Dimension.TemperatureDelta.SiUnit);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(DimensionId.Temperature, DimensionCategory.Absolute)]
    [InlineData(DimensionId.Pressure, DimensionCategory.Absolute)]
    [InlineData(DimensionId.TemperatureDelta, DimensionCategory.Delta)]
    [InlineData(DimensionId.PressureDelta, DimensionCategory.Delta)]
    [InlineData(DimensionId.Kv, DimensionCategory.Nominal)]
    [InlineData(DimensionId.NominalDiameter, DimensionCategory.Nominal)]
    [InlineData(DimensionId.Length, DimensionCategory.Linear)]
    [InlineData(DimensionId.Head, DimensionCategory.Linear)]
    public void Category_IsWhatDecidesHowADimensionBehaves(DimensionId id, DimensionCategory expected)
    {
        Assert.Equal(expected, Dimension.Named(id).Category);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void All_HoldsEveryNamedDimensionAndNoUnnamedOne()
    {
        Assert.Equal(Enum.GetValues<DimensionId>().Length - 1, Dimension.All.Length);
        Assert.All(Dimension.All, static d => Assert.True(d.IsNamed));
    }
}
