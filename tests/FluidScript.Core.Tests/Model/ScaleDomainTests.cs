using FluidScript.Core.Model;

namespace FluidScript.Core.Tests.Model;

/// <summary>A colour scale's ends are settled to the legend's precision before they are rounded outward (<c>C-112</c>).</summary>
public sealed class ScaleDomainTests
{
    /// <summary>The storage header: inlets stated at 45 and 60 °C solve a hair either side of them, and the legend must read 45 to 60, not 40 to 65.</summary>
    [Theory]
    [InlineData(44.999999999, 60.000000001)]
    [InlineData(45.000000001, 59.999999999)]
    [InlineData(45, 60)]
    public void AStatedTemperatureSolvedAHairOffItDoesNotOpenAnEmptyBand(double min, double max)
    {
        Assert.Equal((45.0, 60.0), ScaleDomain.Settle(min, max, resolution: 0, diverging: false));
    }

    /// <summary>The substation: its lowest node is on the datum, and the sign of a nanopascal must not move the floor by 200 kPa.</summary>
    [Theory]
    [InlineData(-7e-10)]
    [InlineData(7e-10)]
    [InlineData(0)]
    public void APressureOnTheDatumIsZeroWhicheverSideOfItTheSolveLanded(double atDatum)
    {
        var resolution = 1e-6; // kPa: newton.residual_tol × scale.pressure
        Assert.Equal((0.0, 600.0), ScaleDomain.Settle(atDatum, 560.4, resolution, diverging: false));
    }

    /// <summary>A pump-free header sits on its datum everywhere: the domain is degenerate at zero, and the legend says all 0 kPa.</summary>
    [Fact]
    public void APlantOnItsDatumIsDegenerateAtZeroNotAtANanounit()
    {
        Assert.Equal((0.0, 0.0), ScaleDomain.Settle(6.55127e-10, 6.55127e-10, 1e-6, diverging: false));
        Assert.Equal((0.0, 0.0), ScaleDomain.Settle(6.5e-10, 6.6e-10, 1e-6, diverging: false));
    }

    /// <summary>A range narrower than the sixth significant digit is no range: it is degenerate at the value.</summary>
    [Fact]
    public void ARangeUnderTheDisplayPrecisionIsDegenerate()
    {
        Assert.Equal((100.0, 100.0), ScaleDomain.Settle(100.0000001, 100.0000002, 0, diverging: false));
        Assert.NotEqual(ScaleDomain.Settle(100, 100.001, 0, diverging: false).Min, ScaleDomain.Settle(100, 100.001, 0, diverging: false).Max);
    }

    /// <summary>A diverging scale is still symmetric about zero after settling.</summary>
    [Fact]
    public void ADivergingDomainStaysSymmetric()
    {
        Assert.Equal((-8.0, 8.0), ScaleDomain.Settle(-3, 7.0000000001, 0, diverging: true));
    }

    /// <summary>The ticks are clean doubles: a step product one ulp off its tick would print seventeen digits.</summary>
    [Fact]
    public void TheEndsAreTheTicksThemselves()
    {
        var (min, max) = ScaleDomain.Settle(0.0811, 0.1199, 0, diverging: false);
        Assert.Equal(0.08, min);
        Assert.Equal(0.12, max);
    }
}
