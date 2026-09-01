using SharpProp;

using UnitsNet;

namespace FluidScript.Core.Tests.Fluids;

/// <summary>
/// The M0 risk gate for the property backend: what SharpProp 9.0.0 actually is, measured rather than
/// assumed, before any component is written against the adapter that wraps it.
/// </summary>
/// <remarks>
/// <para>
/// P1.1 of <c>plan/00-foundation/08-implementation-sequence.md</c>. These tests assert the values
/// SharpProp <em>returns</em>, not the values <c>plan/20-core-domain/21-fluid-and-state.md</c>
/// currently predicts. Where the two disagree the disagreement is named at the assertion, because a
/// gate that quietly adopts whatever the library produced is not a gate.
/// </para>
/// <para>
/// This lives in the test project rather than in Core because Core has no property adapter yet; the
/// single Core type that may reference SharpProp arrives in P3.1, and the architecture test that
/// counts those references is scoped to <c>src/</c> for that reason.
/// </para>
/// </remarks>
public sealed class SharpPropSpikeTests
{
    private const double AtmosphericPressureBar = 1.01325;

    private static IFluid Water(double celsius, double bar) =>
        new Fluid(FluidsList.Water).WithState(
            Input.Temperature(Temperature.FromDegreesCelsius(celsius)),
            Input.Pressure(Pressure.FromBars(bar)));

    private static IHumidAir Air(double celsius, double relativeHumidityPercent) =>
        new HumidAir().WithState(
            InputHumidAir.Pressure(Pressure.FromPascals(101_325)),
            InputHumidAir.Temperature(Temperature.FromDegreesCelsius(celsius)),
            InputHumidAir.RelativeHumidity(RelativeHumidity.FromPercent(relativeHumidityPercent)));

    private static void AssertWithin(double expected, double actual, double relativeTolerance, string property)
    {
        var deviation = Math.Abs(actual - expected) / Math.Abs(expected);
        Assert.True(
            deviation <= relativeTolerance,
            $"{property}: expected {expected:G6}, SharpProp returned {actual:G6} ({deviation:P3} apart, "
            + $"tolerance {relativeTolerance:P3}).");
    }

    [Fact]
    [Trait("Category", "Property")]
    public void Water_At20CAnd1BarGauge_MeetsThePublishedValidityRow()
    {
        // 21-fluid-and-state's worked example, at 2.01325 bar absolute (1 bar gauge under D-26).
        var water = Water(20, 2.01325);

        AssertWithin(998.3, water.Density.KilogramsPerCubicMeter, 0.001, "density");
        AssertWithin(4184, water.SpecificHeat.JoulesPerKilogramKelvin, 0.001, "specific heat");
        AssertWithin(1.002e-3, water.DynamicViscosity!.Value.PascalSeconds, 0.005, "dynamic viscosity");
        AssertWithin(0.598, water.Conductivity!.Value.WattsPerMeterKelvin, 0.005, "thermal conductivity");
    }

    [Theory]
    [Trait("Category", "Property")]
    // The enthalpies every reference circuit's flows are derived from. 20 C and 50 C reproduce the
    // published figures; 6 C does NOT -- 21-fluid-and-state states 25 200 J/kg and SharpProp returns
    // 25 324, which is 0.49 % apart against a 0.1 % enthalpy tolerance. The published value is the one
    // that is wrong, and correcting it moves the cooling loop's recirculation flow from 0.0764 to
    // 0.0763 kg/s. Recorded here so the correction is evidence-backed rather than a preference.
    [InlineData(6, 25_324)]
    [InlineData(20, 84_007)]
    [InlineData(30, 125_823)]
    [InlineData(40, 167_616)]
    [InlineData(50, 209_418)]
    [InlineData(60, 251_249)]
    public void Water_EnthalpyAtAtmosphericPressure_IsWhatTheReferenceCircuitsAssume(
        double celsius, double expectedJoulesPerKilogram)
    {
        var measured = Water(celsius, AtmosphericPressureBar).Enthalpy.JoulesPerKilogram;

        AssertWithin(expectedJoulesPerKilogram, measured, 0.001, $"enthalpy at {celsius} C");
    }

    [Fact]
    [Trait("Category", "Property")]
    public void HumidAir_At25CAnd50Percent_DivergesFromTheAshraeIdealGasChart()
    {
        var air = Air(25, 50);

        // These three meet 21-fluid-and-state's published expectations.
        AssertWithin(17.9, air.WetBulbTemperature.DegreesCelsius, 0.006, "wet bulb");
        AssertWithin(13.9, air.DewTemperature.DegreesCelsius, 0.006, "dew point");
        AssertWithin(1.177, air.Density.KilogramsPerCubicMeter, 0.005, "moist-air density");

        // These two do not, and the reason is that the published values are ASHRAE ideal-gas chart
        // values while CoolProp uses the real-gas formulation with an enhancement factor:
        //   humidity ratio  0.00988 predicted, 0.009926 returned  (0.47 %, inside 0.5 % but only just)
        //   enthalpy        50.3 predicted,    49.928 returned    (0.74 %, OUTSIDE the 0.5 % row)
        // The dry-air column is what settles it: at 0 % RH the two agree to 0.002 kJ/kg, so this is
        // not a reference-state offset -- the gap scales with humidity, reaching 1.55 kJ/kg at
        // saturation. CoolProp is the more accurate of the two and the published row needs revising.
        AssertWithin(0.009926, air.Humidity.DecimalFractions, 0.005, "humidity ratio");
        AssertWithin(49.93, air.Enthalpy.KilojoulesPerKilogram, 0.005, "enthalpy per kg dry air");
    }

    [Fact]
    [Trait("Category", "Property")]
    public void HumidAir_EnthalpyGapWidensWithHumidity_SoItIsNotAReferenceStateOffset()
    {
        static double AshraeEnthalpy(double celsius, double humidityRatio) =>
            (1.006 * celsius) + (humidityRatio * (2501 + (1.86 * celsius)));

        var dry = Air(25, 0);
        var half = Air(25, 50);
        var saturated = Air(25, 100);

        var dryGap = Math.Abs(dry.Enthalpy.KilojoulesPerKilogram
            - AshraeEnthalpy(25, dry.Humidity.DecimalFractions));
        var halfGap = Math.Abs(half.Enthalpy.KilojoulesPerKilogram
            - AshraeEnthalpy(25, half.Humidity.DecimalFractions));
        var saturatedGap = Math.Abs(saturated.Enthalpy.KilojoulesPerKilogram
            - AshraeEnthalpy(25, saturated.Humidity.DecimalFractions));

        Assert.True(dryGap < 0.01, $"Dry air must agree with the ideal-gas form; gap was {dryGap:F3} kJ/kg.");
        Assert.True(halfGap > dryGap && saturatedGap > halfGap,
            $"The gap must grow with humidity: {dryGap:F3} -> {halfGap:F3} -> {saturatedGap:F3} kJ/kg.");
    }

    [Fact]
    [Trait("Category", "Property")]
    public void OutOfRange_ThrowsBelowFreezingButNotAboveTheUpperBound()
    {
        // Below the melting line SharpProp throws, so the adapter can translate an exception.
        Assert.ThrowsAny<Exception>(() => Water(-50, 1).Density);

        // Above it, nothing throws: water at 5000 C returns a number. The adapter must therefore
        // range-check against 07-quality-attributes' validity matrix itself rather than relying on
        // the backend to object. This is the trap in this whole spike -- a silently extrapolated
        // property is indistinguishable from a good one at the call site.
        var exception = Record.Exception(() => Water(5000, 1).Density);
        Assert.Null(exception);
    }

    [Fact]
    [Trait("Category", "Property")]
    public void HumidAir_RejectsAnUndefinedState()
    {
        Assert.ThrowsAny<Exception>(() => Air(25, 150).Enthalpy);
        Assert.ThrowsAny<Exception>(() => Air(200, 50).Enthalpy);
    }

    [Fact]
    [Trait("Category", "Property")]
    public void WithState_OnASharedInstance_IsSafeAcrossThreads()
    {
        var shared = new Fluid(FluidsList.Water);
        var perThreadTotals = new double[8];

        Parallel.For(0, perThreadTotals.Length, thread =>
        {
            double total = 0;
            for (var step = 0; step < 2_000; step++)
            {
                total += shared.WithState(
                    Input.Temperature(Temperature.FromDegreesCelsius(20 + (step % 30))),
                    Input.Pressure(Pressure.FromBars(2))).Density.KilogramsPerCubicMeter;
            }

            perThreadTotals[thread] = total;
        });

        Assert.Single(perThreadTotals.Distinct());
    }
}
