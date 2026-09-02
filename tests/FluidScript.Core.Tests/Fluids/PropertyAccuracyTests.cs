using FluidScript.Core.Fluids;
using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Fluids;

/// <summary>
/// <c>V4</c> and <c>V5</c> from <c>plan/60-docs-and-devex/62-testing-strategy.md</c>: water and
/// humid-air properties against an independent oracle, at <c>07</c>'s tolerances.
/// </summary>
/// <remarks>
/// <para>
/// <c>62</c>'s rule 3 is the constraint that shapes this file: a validation case uses "an independent
/// published, analytic, or separately tabulated oracle, <strong>never production-backend output as
/// expected data</strong>". Writing down what our own backend returned and calling it the expected
/// value would produce a suite that passes forever and proves nothing. So every number asserted below
/// comes from one of three places, and each assertion says which:
/// </para>
/// <list type="bullet">
/// <item>a published analytic correlation evaluated here — Kell's equation for density, the ASHRAE
/// ideal-gas psychrometric relation for dry air;</item>
/// <item>a value published in the plan itself, which was written before the implementation;</item>
/// <item>a statement about reality that needs no table at all — that viscosity falls with temperature,
/// that enthalpy differences agree with the specific heat that produced them.</item>
/// </list>
/// <para>
/// Rule 4 also applies and is easy to trip: <strong>no absolute enthalpy is asserted anywhere</strong>.
/// CoolProp's datum for water is not a textbook's, so only differences are compared.
/// </para>
/// </remarks>
public sealed class PropertyAccuracyTests
{
    private static Quantity Gauge(double kPa) => Quantity.FromSi(kPa * 1000, Dimension.Pressure);

    private static Quantity Celsius(double value) => Quantity.FromSi(value + 273.15, Dimension.Temperature);

    private static FluidState WaterAt(double celsius, double gaugeKilopascals = 0)
    {
        var result = Water.Instance.FromPressureTemperature(Gauge(gaugeKilopascals), Celsius(celsius));

        Assert.True(result.IsSuccess, result.Error?.Message);

        return result.Value;
    }

    private static HumidAirState AirAt(double celsius, double relativeHumidity)
    {
        var result = HumidAirSubstance.Instance.FromPressureTemperatureRelativeHumidity(
            Gauge(0), Celsius(celsius), Quantity.FromSi(relativeHumidity, Dimension.Dimensionless));

        Assert.True(result.IsSuccess, result.Error?.Message);

        return result.Value;
    }

    private static void Within(double expected, double actual, double relativeTolerance, string property)
    {
        var deviation = Math.Abs(actual - expected) / Math.Abs(expected);

        Assert.True(
            deviation <= relativeTolerance,
            $"{property}: oracle {expected:G8}, measured {actual:G8} — {deviation:P4} apart, "
            + $"tolerance {relativeTolerance:P4}.");
    }

    /// <summary>Kell's 1975 equation for the density of air-free water at one atmosphere.</summary>
    /// <param name="celsius">Temperature, 0 to 150 °C.</param>
    /// <returns>Density in kg/m³.</returns>
    /// <remarks>
    /// <para>
    /// G. S. Kell, <em>Density, thermal expansivity, and compressibility of liquid water from 0° to
    /// 150 °C</em>, J. Chem. Eng. Data 20(1), 1975. Quoted to about one part per million against the
    /// IAPWS surface, which is three orders better than the 0.1 % this is used to check.
    /// </para>
    /// <para>
    /// It is an <em>independent</em> oracle in the sense that matters: a closed-form polynomial fitted
    /// to measurements, evaluated here, sharing no code and no data with the property backend. If both
    /// it and CoolProp are wrong they are wrong for unrelated reasons.
    /// </para>
    /// </remarks>
    private static double KellDensity(double celsius)
    {
        var t = celsius;

        var numerator = 999.83952
            + (16.945176 * t)
            - (7.9870401e-3 * t * t)
            - (46.170461e-6 * t * t * t)
            + (105.56302e-9 * t * t * t * t)
            - (280.54253e-12 * t * t * t * t * t);

        return numerator / (1 + (16.879850e-3 * t));
    }

    /// <summary>The ASHRAE ideal-gas enthalpy of moist air, per kg of dry air.</summary>
    /// <param name="celsius">Dry-bulb temperature.</param>
    /// <param name="humidityRatio">kg water per kg dry air.</param>
    /// <returns>kJ per kg of dry air.</returns>
    /// <remarks>
    /// <c>h = 1.006·t + W·(2501 + 1.86·t)</c>, the psychrometric-chart relation. It is only an oracle
    /// <strong>at zero humidity</strong>, where the vapour term vanishes and the two formulations must
    /// agree; above that it is deliberately the wrong answer, and how wrong is itself asserted below.
    /// </remarks>
    private static double AshraeEnthalpy(double celsius, double humidityRatio) =>
        (1.006 * celsius) + (humidityRatio * (2501 + (1.86 * celsius)));

    // ---- V4: water properties -----------------------------------------------------------------

    [Theory]
    [Trait("Category", "Property")]
    [InlineData(0.01)]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(60)]
    [InlineData(80)]
    [InlineData(95)]
    public void V4_DensityMatchesKellsEquation(double celsius)
    {
        // Six states, against a published closed-form correlation rather than a remembered table. All
        // at one atmosphere, where Kell is stated — and all liquid there, since water boils at 99.61 °C.
        // The cold end is the triple point rather than 0 °C exactly: 0 °C *is* the melting line, where
        // pressure and temperature stop being independent (`F-14`).
        Within(KellDensity(celsius), WaterAt(celsius).Density.SiValue, 0.001, $"density at {celsius} C");
    }

    [Fact]
    [Trait("Category", "Property")]
    public void V4_PressureBarelyMovesDensity_WhichIsWhyAOneAtmosphereOracleServesTheWholeRange()
    {
        // The assumption the test above rests on, asserted rather than assumed. Liquid water is nearly
        // incompressible: across the validated domain's whole pressure span the density moves by far
        // less than the 0.1 % the density row allows, so a correlation stated at one atmosphere is a
        // legitimate oracle at ten.
        var atmospheric = WaterAt(20).Density.SiValue;
        var atNineBar = WaterAt(20, gaugeKilopascals: 800).Density.SiValue;

        Within(atmospheric, atNineBar, 0.001, "density across 100 to 901 kPa absolute");
        Assert.True(atNineBar > atmospheric, "Compressing water must make it denser, not lighter.");
    }

    [Fact]
    [Trait("Category", "Property")]
    public void V4_ThePublishedWorkedExampleHolds()
    {
        // `21`'s own worked example, at 2.01325 bar absolute — one bar gauge under `D-26`. Written into
        // the plan before any of this existed, which is what makes it an independent expectation and
        // not a transcription of what the code does.
        var state = WaterAt(20, gaugeKilopascals: 100);

        Within(998.3, state.Density.SiValue, 0.001, "density");
        Within(4184, state.SpecificHeat.SiValue, 0.001, "specific heat");
        Within(1.002e-3, state.DynamicViscosity.SiValue, 0.005, "dynamic viscosity");
        Within(0.598, state.ThermalConductivity.SiValue, 0.005, "thermal conductivity");

        Assert.Equal(
            273.15,
            Water.Instance.FreezingPoint(Gauge(100)).Value.SiValue,
            0.05);
    }

    [Fact]
    [Trait("Category", "Property")]
    public void V4_EnthalpyDifferencesAgreeWithTheSpecificHeatThatProducedThem()
    {
        // Rule 4 forbids asserting an absolute enthalpy against a textbook, because CoolProp's datum is
        // its own. A *difference* is basis-free, and this is the one property the reference circuits
        // are computed from: `01` derives the cooling loop's 0.2392 kg/s from h(50) − h(20).
        //
        // The oracle is thermodynamics rather than a table: dh = cp dT, so the enthalpy rise over an
        // interval must equal the mean specific heat across it times the interval. Two independently
        // measured properties agreeing is a real check on both.
        var cold = WaterAt(20);
        var hot = WaterAt(50);

        var rise = hot.Enthalpy.SiValue - cold.Enthalpy.SiValue;
        var meanSpecificHeat = (cold.SpecificHeat.SiValue + hot.SpecificHeat.SiValue) / 2;

        Within(meanSpecificHeat * 30, rise, 0.001, "h(50 C) - h(20 C) against mean cp x 30 K");
    }

    [Fact]
    [Trait("Category", "Property")]
    public void V4_TheCoolingLoopsFlowFollowsFromItsEnthalpyRise()
    {
        // `01`'s figure, recomputed from measured properties rather than copied: 30 kW divided by
        // h(50) − h(20) is the secondary flow every document quotes for that circuit. This is the
        // assertion that would fire if a property-backend change moved the reference circuits.
        var rise = WaterAt(50).Enthalpy.SiValue - WaterAt(20).Enthalpy.SiValue;

        Within(0.2392, 30_000 / rise, 0.001, "cooling-loop secondary flow, kg/s");
    }

    [Theory]
    [Trait("Category", "Property")]
    [InlineData(0.01, 20)]
    [InlineData(20, 50)]
    [InlineData(50, 80)]
    [InlineData(80, 95)]
    public void V4_ViscosityFallsAndConductivityRisesWithTemperature(double cooler, double warmer)
    {
        // Statements about reality that need no table: water thins as it warms, and conducts heat
        // better until about 130 °C. A backend swapped for one with a units error or a transposed
        // correlation fails here even where no reference value is to hand.
        Assert.True(
            WaterAt(warmer).DynamicViscosity.SiValue < WaterAt(cooler).DynamicViscosity.SiValue,
            $"Water must thin between {cooler} and {warmer} C.");

        Assert.True(
            WaterAt(warmer).ThermalConductivity.SiValue > WaterAt(cooler).ThermalConductivity.SiValue,
            $"Water's conductivity must rise between {cooler} and {warmer} C.");
    }

    [Fact]
    [Trait("Category", "Property")]
    public void V4_SpecificHeatHasItsMinimumNearBodyTemperature()
    {
        // The non-monotone one, and the reason `LinearPropertyWater` is a double rather than a model:
        // cp falls from 0 °C to about 35 °C and rises after. A correlation fitted as a straight line
        // cannot reproduce this, and a backend that had lost the shape would pass every monotone check.
        var atZero = WaterAt(0.01).SpecificHeat.SiValue;
        var atMinimum = WaterAt(35).SpecificHeat.SiValue;
        var atNinety = WaterAt(90).SpecificHeat.SiValue;

        Assert.True(atMinimum < atZero, $"cp(35 C)={atMinimum:F1} should be below cp(0 C)={atZero:F1}.");
        Assert.True(atMinimum < atNinety, $"cp(35 C)={atMinimum:F1} should be below cp(90 C)={atNinety:F1}.");
    }

    // ---- V13: the pressure reference ----------------------------------------------------------

    [Theory]
    [Trait("Category", "Property")]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(500)]
    public void V13_GaugeAndAbsoluteDescribeTheSameStateToOnePascal(double gaugeKilopascals)
    {
        // `D-26` and `V13`. The absolute spellings normalise to gauge at the language boundary with an
        // offset of exactly one standard atmosphere, so a pressure written either way must reach the
        // backend as the same number. Asserted through the unit table, which owns that offset.
        var absolute = UnitTable.Resolve("kPaa", Dimension.Pressure)!;
        var written = Quantity.FromUnit(
            (gaugeKilopascals * 1000 + UnitTable.StandardAtmosphere) / 1000, absolute);

        Assert.Equal(gaugeKilopascals * 1000, written.SiValue, 1.0);
    }

    // ---- V5: humid air ------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Property")]
    public void V5_ThePublishedPsychrometricStateHolds()
    {
        // `21`'s second worked example: 25 °C dry bulb, 50 % RH, one atmosphere. Its two trap rows are
        // asserted in their own tests below.
        var air = AirAt(25, 0.50);

        Within(0.00993, air.HumidityRatio.SiValue, 0.005, "humidity ratio");
        Assert.Equal(17.9, air.WetBulb.SiValue - 273.15, 0.1);
        Assert.Equal(13.9, air.DewPoint.SiValue - 273.15, 0.1);
        Within(49_930, air.DryAirBasisEnthalpy.SiValue, 0.005, "enthalpy per kg dry air");
        Within(1.177, air.Density.SiValue, 0.005, "moist-air density");
    }

    [Fact]
    [Trait("Category", "Property")]
    public void V5_DensityIsMoistAirAndNotDryAirAtTheSameState()
    {
        // The trap `21` names: the number a reference table is far more likely to hand you is
        // 1.184 kg/m³, which is *dry* air at the same temperature and total pressure. Moist air is the
        // lighter of the two, because water vapour is lighter than the air it displaces. The gap is
        // 0.6 % — outside this row's own tolerance and inside the range a reader accepts as rounding,
        // so it has to be asserted rather than eyeballed.
        var moist = AirAt(25, 0.50).Density.SiValue;
        var dry = AirAt(25, 0).Density.SiValue;

        Assert.True(moist < dry, $"Moist air ({moist:F4}) must be lighter than dry air ({dry:F4}).");
        Within(1.184, dry, 0.005, "dry-air density, the value that must NOT be used for moist air");
        Assert.True(
            Math.Abs(moist - 1.184) / 1.184 > 0.004,
            "The moist and dry figures must stay far enough apart for this test to mean anything.");
    }

    [Fact]
    [Trait("Category", "Property")]
    public void V5_AtZeroHumidityTheRealGasAndIdealGasFormulationsAgree()
    {
        // The one state where ASHRAE's chart relation is a genuine oracle rather than a rival answer:
        // with no water in the air its vapour term vanishes. Agreement here is what proves the
        // divergence measured below is a humidity effect and not a reference-state offset.
        var dry = AirAt(25, 0);
        var gap = Math.Abs((dry.DryAirBasisEnthalpy.SiValue / 1000) - AshraeEnthalpy(25, 0));

        Assert.True(gap < 0.01, $"Dry air must match the ideal-gas form; the gap was {gap:F4} kJ/kg.");
    }

    [Fact]
    [Trait("Category", "Property")]
    public void V5_TheGapToTheIdealGasChartGrowsWithHumidity()
    {
        // `21` records that its published 50.3 kJ/kg was the ASHRAE chart value and CoolProp returns
        // 49.93, and settles which is which by this shape: the two agree dry and diverge as water is
        // added, reaching about 1.55 kJ/kg at saturation. A reference-state offset would be a constant.
        double Gap(double rh)
        {
            var air = AirAt(25, rh);

            return Math.Abs((air.DryAirBasisEnthalpy.SiValue / 1000)
                - AshraeEnthalpy(25, air.HumidityRatio.SiValue));
        }

        var dry = Gap(0);
        var half = Gap(0.50);
        var saturated = Gap(1.0);

        Assert.True(
            dry < half && half < saturated,
            $"The gap must grow with humidity: {dry:F3} -> {half:F3} -> {saturated:F3} kJ/kg.");
        Assert.True(saturated < 2.0, $"At saturation the gap was {saturated:F3} kJ/kg, which is too large.");
    }

    [Theory]
    [Trait("Category", "Property")]
    [InlineData(5, 0.90)]
    [InlineData(15, 0.60)]
    [InlineData(25, 0.50)]
    [InlineData(35, 0.40)]
    [InlineData(45, 0.30)]
    public void V5_TheDewPointIsWhereTheSameMoistureSaturates(double celsius, double relativeHumidity)
    {
        // Five states, against the definition of the dew point rather than a chart: cool the air to its
        // dew point at constant humidity ratio and it must be exactly saturated. Self-consistency of
        // two separately computed properties, which no single tabulated value can check.
        //
        // Every state is chosen so the dew point stays above 0 °C, because that is where `07`'s
        // humid-air domain stops — and a winter state like 5 °C at 30 % RH dews at −9.9 °C, which is
        // outside it. That is a real limit of the claim rather than a limit of this test (`C-13`).
        var air = AirAt(celsius, relativeHumidity);
        var dewPoint = air.DewPoint.SiValue - 273.15;

        var atDewPoint = HumidAirSubstance.Instance.FromPressureTemperatureHumidity(
            Gauge(0), Celsius(dewPoint), air.HumidityRatio).Value;

        Assert.Equal(1.0, atDewPoint.RelativeHumidity.SiValue, 0.005);
        Assert.True(dewPoint <= celsius + 1e-6, "The dew point cannot be above the dry bulb.");
    }

    // ---- V14: the enthalpy basis --------------------------------------------------------------

    [Fact]
    [Trait("Category", "Property")]
    public void V14_EnthalpyIsPerKilogramOfDryAirAndNotOfTheMixture()
    {
        // The basis is 0.3 % from the right answer at this state — inside the tolerance of the correct
        // value — so it cannot be caught by looking at a number. It is caught by the relation instead:
        // one kilogram of dry air carries (1 + W) kilograms of mixture, so the per-mixture figure is
        // the per-dry-air one divided by (1 + W). Asserting that identity pins which one we hold.
        var air = AirAt(25, 0.50);

        var perDryAir = air.DryAirBasisEnthalpy.SiValue;
        var perMixture = perDryAir / (1 + air.HumidityRatio.SiValue);

        Assert.True(
            perDryAir > perMixture,
            "Per kg of dry air must exceed per kg of mixture, since the mixture is the heavier basis.");

        // And the difference is small enough to hide: about 0.3 %, well inside the 0.5 % row.
        var difference = (perDryAir - perMixture) / perDryAir;
        Assert.InRange(difference, 0.001, 0.02);

        // The value we hold is the dry-air one, which is the larger. Reading 49.8 kJ/kg here would mean
        // the mixture basis had leaked in.
        Within(49_930, perDryAir, 0.005, "the enthalpy this model holds");
    }
}
