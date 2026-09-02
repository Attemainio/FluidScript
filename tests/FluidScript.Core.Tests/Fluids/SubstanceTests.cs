using FluidScript.Core.Fluids;
using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Fluids;

/// <summary>
/// The substance abstraction from <c>plan/20-core-domain/21-fluid-and-state.md</c>: the interface's
/// invariants, the gauge boundary, the validated domain, and both test doubles.
/// </summary>
/// <remarks>
/// The accuracy of the numbers is <c>P3.2</c>'s subject and is asserted in
/// <c>PropertyAccuracyTests</c> against <c>21</c>'s two published tables. What is asserted here is the
/// contract: that nothing throws, that a failure carries a code instead of emitting one, that a state
/// outside the domain fails rather than extrapolating, and that the two doubles differ in the one way
/// they are meant to.
/// </remarks>
public sealed class SubstanceTests
{
    private static Quantity Gauge(double kPa) => Quantity.FromSi(kPa * 1000, Dimension.Pressure);

    private static Quantity Celsius(double value) =>
        Quantity.FromSi(value + 273.15, Dimension.Temperature);

    /// <summary>Every substance, so a contract is asserted against all of them rather than one.</summary>
    public static TheoryData<ISubstance> Substances =>
        [Water.Instance, ConstantPropertyWater.Instance, LinearPropertyWater.Instance];

    // ---- the contract -------------------------------------------------------------------------

    [Theory]
    [Trait("Category", "Unit")]
    [MemberData(nameof(Substances))]
    public void AStateInsideTheDomainIsFixed(ISubstance substance)
    {
        var result = substance.FromPressureTemperature(Gauge(100), Celsius(20));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(Phase.Liquid, result.Value.Phase);
        Assert.True(result.Value.Density.SiValue > 900);
        Assert.True(result.Value.SpecificHeat.SiValue > 4000);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [MemberData(nameof(Substances))]
    public void AStateOutsideTheDomainFailsRatherThanExtrapolating(ISubstance substance)
    {
        // `21`'s acceptance criterion, and the trap the M0 spike measured: above its upper bound the
        // backend returns a number for water at 500 °C without complaining. A silently extrapolated
        // property is indistinguishable from a good one at the call site.
        var result = substance.FromPressureTemperature(Gauge(0), Celsius(500));

        Assert.False(result.IsSuccess);
        Assert.Equal("FS2003", result.Error!.Code);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [MemberData(nameof(Substances))]
    public void AFailureCarriesItsCodeAndEmitsNothing(ISubstance substance)
    {
        // Invariant 4. The error holds everything a diagnostic needs and is not one yet, because only a
        // caller knows whether this was a rejected Newton trial point or the converged answer.
        var error = substance.FromPressureTemperature(Gauge(0), Celsius(500)).Error!;

        Assert.Contains(substance.Name, error.Message, StringComparison.Ordinal);
        Assert.Equal("FS2003", error.At(null).Code);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [MemberData(nameof(Substances))]
    public void FixingByEnthalpyAndByTemperatureRoundTrip(ISubstance substance)
    {
        // Invariant 5, at 1e-6 relative. Measured against the real backend the gap is 1.16e-14, so the
        // tolerance has eight orders of magnitude of headroom; the doubles are exact by construction.
        var byTemperature = substance.FromPressureTemperature(Gauge(100), Celsius(50)).Value;
        var byEnthalpy = substance.FromPressureEnthalpy(Gauge(100), byTemperature.Enthalpy).Value;

        var relative = Math.Abs(byEnthalpy.Temperature.SiValue - byTemperature.Temperature.SiValue)
            / byTemperature.Temperature.SiValue;

        Assert.True(relative < 1e-6, $"{substance.Name}: round trip was {relative:E3} apart.");
    }

    [Theory]
    [Trait("Category", "Unit")]
    [MemberData(nameof(Substances))]
    public void NothingThrowsForAStateThatDoesNotExist(ISubstance substance)
    {
        // Invariant 3, over the range a Newton step actually wanders into. Every one of these is a
        // normal event during a solve that converges.
        foreach (var celsius in new[] { -300d, -50, 0, 20, 500, 5000, double.NaN })
        {
            foreach (var kPa in new[] { -1000d, 0, 100, 1e6, double.NaN })
            {
                var exception = Record.Exception(
                    () => substance.FromPressureTemperature(Gauge(kPa), Celsius(celsius)));

                Assert.Null(exception);
            }
        }
    }

    // ---- the gauge boundary -------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void AGaugePressureReachesTheBackendAsAbsolute()
    {
        // `21`'s last acceptance criterion, and `D-26`: `p=100 kPa` is 201.325 kPa absolute. Asserted
        // through the saturation line, which is the one property sensitive enough to see it — water
        // boils at 99.6 °C at 100 kPa absolute and 120.2 °C at 200 kPa.
        var atAtmospheric = Water.Instance.FromPressureTemperature(Gauge(0), Celsius(110));
        var underPressure = Water.Instance.FromPressureTemperature(Gauge(100), Celsius(110));

        Assert.False(atAtmospheric.IsSuccess);
        Assert.True(underPressure.IsSuccess, underPressure.Error?.Message);
        Assert.Equal(Phase.Liquid, underPressure.Value.Phase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APressureIsCarriedBackAsTheGaugeValueItArrivedAs()
    {
        var state = Water.Instance.FromPressureTemperature(Gauge(300), Celsius(20)).Value;

        Assert.Equal(300_000, state.Pressure.SiValue, 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASaturationPressureComesBackGaugeToo()
    {
        // So a cavitation check can compare it against a node's pressure without either side having to
        // remember which datum it is on. Water boils at 100 °C at atmospheric, so gauge is about zero.
        var saturation = Water.Instance.SaturationPressure(Celsius(99.974)).Value;

        Assert.True(Math.Abs(saturation.SiValue) < 200, $"Expected about 0 Pa gauge, got {saturation.SiValue}.");
    }

    // ---- the domain is liquid water, and it is not a rectangle --------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void WaterAboveItsBoilingPointIsRefusedEvenInsideTheStatedRectangle()
    {
        // `F-13`. `07` states the domain as 0-120 °C and 100-1000 kPa absolute, and that rectangle is
        // not all liquid: at 100 kPa absolute water boils at 99.61 °C. The backend hands back steam at
        // 0.573 kg/m³ against liquid's ~950 without complaint — a factor of 1600, inside a range the
        // substance calls valid. The phase is the only thing that catches it.
        var result = Water.Instance.FromPressureTemperature(Gauge(-1.325), Celsius(110));

        Assert.False(result.IsSuccess);
        Assert.Equal("FS2003", result.Error!.Code);
        Assert.Contains("99.6", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OnTheBoilingLinePressureAndTemperatureAreNotIndependent()
    {
        // `FS2002`, which was written off as unreachable. At 101.325 kPa absolute and 99.974 °C the
        // backend refuses outright: the two inputs are one constraint, and nothing says which phase is
        // meant. Water's own validated domain contains this line.
        var result = Water.Instance.FromPressureTemperature(Gauge(0), Celsius(99.9743));

        Assert.False(result.IsSuccess);
        Assert.Equal("FS2002", result.Error!.Code);
    }

    // ---- the two doubles ----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void TheConstantDoubleIsConstantAndHandCheckable()
    {
        var cold = ConstantPropertyWater.Instance.FromPressureTemperature(Gauge(100), Celsius(20)).Value;
        var hot = ConstantPropertyWater.Instance.FromPressureTemperature(Gauge(100), Celsius(80)).Value;

        Assert.Equal(cold.SpecificHeat.SiValue, hot.SpecificHeat.SiValue, 9);
        Assert.Equal(cold.Density.SiValue, hot.Density.SiValue, 9);

        // h = cp * (T - 273.15), so 20 °C is 4184 * 20 exactly. Its datum is 0 °C, not CoolProp's.
        Assert.Equal(4184 * 20.0, cold.Enthalpy.SiValue, 9);
        Assert.Equal(4184 * 80.0, hot.Enthalpy.SiValue, 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheLinearDoubleIsNotConstant()
    {
        // The whole reason there are two. A component written as though cp were fixed agrees with the
        // constant double and disagrees here, and running only the first is a false sense of coverage.
        var cold = LinearPropertyWater.Instance.FromPressureTemperature(Gauge(100), Celsius(20)).Value;
        var hot = LinearPropertyWater.Instance.FromPressureTemperature(Gauge(100), Celsius(80)).Value;

        Assert.NotEqual(cold.SpecificHeat.SiValue, hot.SpecificHeat.SiValue, 6);
        Assert.NotEqual(cold.Density.SiValue, hot.Density.SiValue, 6);
        Assert.Equal(4180, cold.SpecificHeat.SiValue, 9);
        Assert.Equal(4210, hot.SpecificHeat.SiValue, 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheLinearDoublesEnthalpyIsTheIntegralOfItsSpecificHeat()
    {
        // Not cp × ΔT — that is the assumption this double exists to break. Over 20 to 80 °C the
        // difference is 0.5 × 60² / 2 = 900 J/kg, which a caller using the reference cp misses.
        var enthalpy = LinearPropertyWater.EnthalpyAt(353.15) - LinearPropertyWater.EnthalpyAt(293.15);

        Assert.Equal((4180 * 60) + 900, enthalpy, 6);
        Assert.NotEqual(4180 * 60.0, enthalpy, 6);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(63.7)]
    [InlineData(120)]
    public void TheLinearDoubleInvertsItsOwnEnthalpyExactly(double celsius)
    {
        // The quadratic has a closed form, so the double needs no iteration and the round trip is exact
        // rather than merely inside a tolerance.
        var state = LinearPropertyWater.Instance.FromPressureTemperature(Gauge(100), Celsius(celsius)).Value;
        var back = LinearPropertyWater.Instance.FromPressureEnthalpy(Gauge(100), state.Enthalpy).Value;

        Assert.Equal(celsius + 273.15, back.Temperature.SiValue, 9);
    }

    // ---- the registry -------------------------------------------------------------------------

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("water")]
    [InlineData("Water")]
    [InlineData("WATER")]
    public void ANameResolvesWhateverItsCasing(string written)
    {
        Assert.Equal("water", SubstanceRegistry.Default.Resolve(written).Value.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnUnknownNameFailsWithTheListOfWhatExists()
    {
        // No similarity stage, deliberately: a kind resolved by similarity draws a slightly wrong
        // symbol, and a working fluid resolved by similarity changes every density in the model while
        // the results stay plausible.
        var error = SubstanceRegistry.Default.Resolve("watr").Error!;

        Assert.Equal("FS2001", error.Code);
        Assert.Contains("water", error.Message, StringComparison.Ordinal);
        Assert.Contains("air", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GlycolIsNotRegistered()
    {
        // `D-28`. Accepting a concentration before the mixture contract and the freezing basis are
        // validated would overstate the physics, which is worse than refusing the name.
        Assert.False(SubstanceRegistry.Default.Resolve("glycol").IsSuccess);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ATestRegistryAnswersToTheSameNameAsTheRealOne()
    {
        // So a fixture reads identically whichever backend is behind it.
        Assert.Equal("water", SubstanceRegistry.Constant.Resolve("water").Value.Name);
        Assert.IsType<ConstantPropertyWater>(SubstanceRegistry.Constant.Resolve("water").Value);
        Assert.IsType<LinearPropertyWater>(SubstanceRegistry.Linear.Resolve("water").Value);
    }
}
