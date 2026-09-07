using FluidScript.Core.Cycles;
using FluidScript.Core.Fluids;
using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Cycles;

/// <summary>
/// One refrigeration cycle, evaluated against real fluid properties, and the identities that hold for
/// every refrigerant at every pair of temperatures.
/// </summary>
/// <remarks>
/// <para>
/// The reference case is <strong>ammonia at −7/40 °C</strong> with 5 K superheat, 3 K subcooling and
/// <c>η_is</c> = 0.7. It discharges at 152.5 °C against a pressure ratio of 4.74, rejects
/// 1424 kJ/kg for 333 kJ/kg of work, and splits its condenser 21.8 % / 77.2 % / 1.0 %.
/// </para>
/// <para>
/// <strong>Most of what is asserted here needs no stored number.</strong> The three identities in
/// <see cref="VapourCompressionCycle"/> are exact algebra over whatever the backend returns, so they
/// check the compressor model against itself rather than against a table someone transcribed — the
/// style <c>24</c> uses for <c>UA</c> against <c>Q̇/LMTD</c>. The measured values above appear in only
/// two tests, and both exist because <c>D-80</c> and <c>D-81</c> make claims about magnitudes that an
/// identity cannot reach.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class VapourCompressionCycleTests
{
    /// <summary>The reference ammonia cycle, at whatever efficiency and motor arrangement is asked.</summary>
    /// <param name="isentropicEfficiency">The compressor's.</param>
    /// <param name="motor">Where the motor's losses go.</param>
    /// <returns>The evaluated cycle.</returns>
    private static CycleResult Ammonia(double isentropicEfficiency, MotorLoss motor)
    {
        var result = VapourCompressionCycle.Subcritical(
            Refrigerant.Ammonia, 266.15, 313.15, 5, 3, isentropicEfficiency, motor);

        Assert.True(result.IsSuccess, result.Error?.Message);

        return result.Value;
    }

    // ---- the identities, which need no stored number ------------------------------------------

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.65)]
    [InlineData(0.7)]
    [InlineData(0.85)]
    public void IsentropicLossIsPartlyDeliveredAsHeatAndTheAlgebraSaysExactlyHowMuch(double efficiency)
    {
        var ideal = Ammonia(1.0, MotorLoss.None);
        var real = Ammonia(efficiency, MotorLoss.None);

        // COP_h(eta) = eta*COP_h(1) + (1 - eta). The (1 - eta) term is the compressor's own loss,
        // which lands in the refrigerant and leaves through the condenser like any other heat -- so a
        // worse compressor costs heating far less than it costs cooling, and at eta -> 0 this floors at
        // 1 rather than at 0. A Carnot-fraction model multiplies both by eta and cannot say that.
        Assert.Equal(
            (efficiency * ideal.HeatingCop) + (1 - efficiency),
            real.HeatingCop,
            real.HeatingCop * 1e-6);

        // Cooling has no such recovery: the loss is on the wrong side of the evaporator.
        Assert.Equal(efficiency * ideal.CoolingCop, real.CoolingCop, real.CoolingCop * 1e-6);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.7)]
    [InlineData(1.0)]
    public void HeatingAndCoolingDifferByExactlyTheWorkThatWentIn(double efficiency)
    {
        var cycle = Ammonia(efficiency, MotorLoss.None);

        // An energy balance on the whole circuit, so it holds whatever the fluid does in between.
        Assert.Equal(cycle.CoolingCop + 1, cycle.HeatingCop, cycle.HeatingCop * 1e-9);
        Assert.Equal(
            cycle.SpecificHeatAbsorbed + cycle.SpecificShaftWork,
            cycle.SpecificHeatRejected,
            cycle.SpecificHeatRejected * 1e-9);
    }

    [Fact]
    public void AnArbitrarilyBadCompressorDegradesToAnImmersionHeaterAndNeverBelowOne()
    {
        var ideal = Ammonia(1.0, MotorLoss.None);

        // The limit of the identity above, stated as the property it protects: no isentropic efficiency
        // makes a heat pump worse than resistance heating, because the work it wastes is the heat it
        // was selling. A model that got this wrong would show a heat pump losing to a kettle.
        foreach (var efficiency in new[] { 0.5, 0.25, 0.1, 0.01 })
        {
            Assert.True(
                (efficiency * ideal.HeatingCop) + (1 - efficiency) > 1,
                $"eta={efficiency} predicts a heating COP at or below 1.");
        }
    }

    [Fact]
    public void AMotorInTheSuctionStreamSellsItsLossesAndOneOutsideTheShellDoesNot()
    {
        var shaft = Ammonia(0.7, MotorLoss.None);
        var hermetic = Ammonia(0.7, new MotorLoss(0.92, ReachesRefrigerant: true));
        var open = Ammonia(0.7, new MotorLoss(0.92, ReachesRefrigerant: false));

        // `D-82`, and the same algebra one layer out: a loss that lands in the working fluid adds
        // (1 - eta) to the heating COP, and one that lands in the plant room adds nothing.
        Assert.Equal((0.92 * shaft.HeatingCop) + 0.08, hermetic.HeatingCop, hermetic.HeatingCop * 1e-9);
        Assert.Equal(0.92 * shaft.HeatingCop, open.HeatingCop, open.HeatingCop * 1e-9);

        // Worth about 2 % here, which is small, one-directional, and larger than most of the modelling
        // choices it would otherwise hide behind.
        Assert.InRange(hermetic.HeatingCop / open.HeatingCop, 1.015, 1.025);
    }

    // ---- what the states actually are -----------------------------------------------------------

    [Fact]
    public void TheExpansionValveDischargesIntoTheDomeAtTheEvaporatingTemperature()
    {
        var cycle = Ammonia(0.7, MotorLoss.None);

        // `D-78`'s claim, measured rather than argued: state 4 is two-phase, and (p, h) fixes it while
        // (p, T) could not -- its temperature is the saturation temperature and carries no information
        // beyond the pressure. This is why the solver's node unknown is enthalpy.
        Assert.Equal(cycle.ExpansionOutletEnthalpy, cycle.HighSideOutlet.Enthalpy.SiValue);

        var state = Refrigerant.Ammonia.FromPressureEnthalpy(
            cycle.Suction.Pressure,
            Quantity.FromSi(cycle.ExpansionOutletEnthalpy, Dimension.Enthalpy));

        Assert.True(state.IsSuccess, state.Error?.Message);
        Assert.Equal(Phase.TwoPhase, state.Value.Phase);
        Assert.Equal(266.15, state.Value.Temperature.SiValue, 0.01);

        // And the properties that do not exist there are absent rather than plausible (`C-52`).
        Assert.False(double.IsFinite(state.Value.DynamicViscosity.SiValue));
    }

    [Fact]
    public void TheCondenserSplitsIntoTheThreeZonesTheSizingRuleCounts()
    {
        var cycle = Ammonia(0.7, MotorLoss.None);
        var zones = cycle.HighSideZones;

        // The shares are cut at the backend's own saturation enthalpies, so they partition the duty
        // exactly rather than nearly.
        Assert.Equal(1.0, zones.Desuperheat + zones.Latent + zones.Subcool, 1e-12);

        // `D-80`'s measured split. 110 K of superheat carrying a fifth of the duty is the whole argument
        // for zoning: the temperature picture says "constant", the area picture does not.
        Assert.Equal(0.218, zones.Desuperheat, 0.005);
        Assert.Equal(0.772, zones.Latent, 0.005);
        Assert.Equal(0.010, zones.Subcool, 0.003);
        Assert.Equal(152.5, cycle.Discharge.Temperature.SiValue - 273.15, 0.5);
    }

    [Fact]
    public void ACondensingTemperatureAboveCriticalIsRefusedByTheSubstanceRatherThanSpecialCased()
    {
        // 132.25 C is ammonia's critical temperature, so there is no saturation pressure to look up and
        // the substance says so. No branch in the cycle tests for it: the absence of a saturation line
        // *is* the transcritical case, and `D-81` gives it its own entry point with one more argument.
        var result = VapourCompressionCycle.Subcritical(
            Refrigerant.Ammonia, 266.15, 410.15, 5, 3, 0.7, MotorLoss.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ASaturationOffsetOfZeroIsRefusedBecauseThePairWouldFixNoState()
    {
        // On the saturation line a pressure and a temperature are one constraint rather than two, so a
        // cycle sitting on it has no state 1 and no state 3. Real machines carry both offsets for the
        // matching hardware reason.
        Assert.Throws<ArgumentOutOfRangeException>(() => VapourCompressionCycle.Subcritical(
            Refrigerant.Ammonia, 266.15, 313.15, 0, 3, 0.7, MotorLoss.None));

        Assert.Throws<ArgumentOutOfRangeException>(() => VapourCompressionCycle.Subcritical(
            Refrigerant.Ammonia, 266.15, 313.15, 5, 0, 0.7, MotorLoss.None));
    }

    // ---- the transcritical case, which carries one more unknown ---------------------------------

    [Fact]
    public void ATranscriticalGasCoolerIsOneZoneBecauseItHasNoSaturationLineToCutAt()
    {
        var cycle = CarbonDioxide(90e5);

        Assert.Equal(1.0, cycle.HighSideZones.Desuperheat, 1e-12);
        Assert.Equal(0.0, cycle.HighSideZones.Latent, 1e-12);
        Assert.Equal(0.0, cycle.HighSideZones.Subcool, 1e-12);
    }

    [Fact]
    public void TheTranscriticalHighSidePressureHasAnInteriorOptimumAndTheOptimumIsFlat()
    {
        var sampled = Enumerable.Range(15, 10)
            .Select(step => (Bar: step * 5.0, Cop: CarbonDioxide(step * 5.0 * 1e5).HeatingCop))
            .ToArray();

        var best = sampled.MaxBy(static point => point.Cop);

        // `D-81`: this is the extra unknown a subcritical cycle does not have. Raising the pressure
        // costs compressor work and buys disproportionate enthalpy in the gas cooler, because the
        // supercritical isotherms are strongly curved -- so the maximum is interior, not at a bound.
        Assert.InRange(best.Bar, 85, 95);
        Assert.NotEqual(sampled[0].Bar, best.Bar);
        Assert.NotEqual(sampled[^1].Bar, best.Bar);

        // And it is flat, which is why a correlation is the right default: Liao-Zhao-Jakobsen gives
        // 89.1 bar here and Kauf 98.5 -- 10 % apart in pressure, and both within a couple of percent of
        // the peak. That split is the rule: accurate enough for the COP an optimizer reads, and not for
        // the pressure rating a compressor selection reads.
        foreach (var point in sampled.Where(point => point.Bar is >= 85 and <= 100))
        {
            Assert.True(
                point.Cop > best.Cop * 0.97,
                $"{point.Bar} bar gives {point.Cop:F4} against a peak of {best.Cop:F4}; the optimum is "
                + "sharper than a correlation can be trusted to hit.");
        }
    }

    private static CycleResult CarbonDioxide(double absolutePressure)
    {
        var result = VapourCompressionCycle.Transcritical(
            Refrigerant.CarbonDioxide,
            266.15,
            absolutePressure - UnitTable.StandardAtmosphere,
            308.15,
            5,
            0.65,
            MotorLoss.None);

        Assert.True(result.IsSuccess, result.Error?.Message);

        return result.Value;
    }
}
