using FluidScript.Core.Physics.Fluids;

namespace FluidScript.Core.Tests.Physics.Fluids;

/// <summary>Region 4 against the verification values IAPWS publishes with the formulation (R7-97(2012), tables 35 and 36).</summary>
public sealed class If97SaturationTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(300, 0.353658941e-2)]
    [InlineData(500, 0.263889776e1)]
    [InlineData(600, 0.123443146e2)]
    public void TheSaturationPressureReproducesTable35(double kelvin, double megapascals)
    {
        Assert.Equal(megapascals * 1e6, If97Saturation.Pressure(kelvin), megapascals * 1e6 * 1e-8);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(0.1, 0.372755919e3)]
    [InlineData(1, 0.453035632e3)]
    [InlineData(10, 0.584149488e3)]
    public void TheSaturationTemperatureReproducesTable36(double megapascals, double kelvin)
    {
        Assert.Equal(kelvin, If97Saturation.Temperature(megapascals * 1e6), 1e-6);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(280)]
    [InlineData(373.15)]
    [InlineData(450)]
    public void TheTwoDirectionsAreExactInversesOfEachOther(double kelvin)
    {
        Assert.Equal(kelvin, If97Saturation.Temperature(If97Saturation.Pressure(kelvin)), 1e-9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OutsideTheLineTheAnswerIsNotANumber()
    {
        Assert.True(double.IsNaN(If97Saturation.Pressure(200)));
        Assert.True(double.IsNaN(If97Saturation.Pressure(700)));
        Assert.True(double.IsNaN(If97Saturation.Temperature(100)));
        Assert.True(double.IsNaN(If97Saturation.Temperature(double.NaN)));
    }
}
