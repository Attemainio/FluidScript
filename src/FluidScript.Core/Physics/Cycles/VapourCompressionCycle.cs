using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Primitives;

namespace FluidScript.Core.Physics.Cycles;

/// <summary>The four-point vapour-compression cycle, evaluated against real fluid properties.</summary>
/// <remarks>
/// <para>
/// <strong>Three identities hold for any refrigerant at any two temperatures, and they are the test.</strong>
/// With <c>w = w_s/η</c>, <c>h₂ = h₁ + w</c> and a fixed <c>h₃</c>:
/// </para>
/// <code>
/// COP_heating(η) = η · COP_heating(1) + (1 − η)
/// COP_cooling(η) = η · COP_cooling(1)
/// COP_heating    = COP_cooling + 1
/// </code>
/// <para>
/// The first is the one a Carnot-fraction model cannot express: <strong>isentropic loss degrades
/// heating COP far less than cooling COP, because the loss is delivered as heat.</strong> Its limit is
/// the proof — at <c>η → 0</c> a heat pump degrades to <c>COP_h = 1</c>, an immersion heater, and never
/// below. Asserting them costs no stored golden number, which is the style <c>24</c> already uses for
/// <c>UA</c> against <c>Q̇/LMTD</c> (<c>D-82</c>).
/// </para>
/// <para>
/// <strong>Nothing here is interpolated.</strong> Every state is fixed by the backend at the point
/// asked for, which is what <c>D-79</c> requires and what makes the two-phase side of the circuit
/// tractable at all: a linearisation in temperature across a region whose phase structure can change
/// is the one thing this calculation must not do.
/// </para>
/// </remarks>
public static class VapourCompressionCycle
{
    /// <summary>Evaluates a subcritical cycle between two saturation temperatures.</summary>
    /// <param name="refrigerant">The working fluid.</param>
    /// <param name="evaporatingTemperature">K. The saturation temperature the evaporator holds.</param>
    /// <param name="condensingTemperature">K. The saturation temperature the condenser holds.</param>
    /// <param name="superheat">K above <paramref name="evaporatingTemperature"/> at the suction. Positive.</param>
    /// <param name="subcooling">K below <paramref name="condensingTemperature"/> at the outlet. Positive.</param>
    /// <param name="isentropicEfficiency">Shaft work an ideal compressor would need, over what this one does. In (0, 1].</param>
    /// <param name="motor">Where the motor's losses go, or <see cref="MotorLoss.None"/> for a shaft-basis answer.</param>
    /// <returns>
    /// The cycle, or the substance's own failure. A condensing temperature at or above the critical one
    /// fails here rather than being special-cased: there is no saturation pressure to look up, and the
    /// substance says so (<c>D-81</c> — that case is <see cref="Transcritical"/>, which carries one more
    /// unknown).
    /// </returns>
    /// <remarks>
    /// Both offsets must be strictly positive, and not for tidiness: on the saturation line a pressure
    /// and a temperature are one constraint rather than two, so the pair fixes no state. Real machines
    /// carry both for the matching hardware reason — liquid must not reach the compressor and vapour
    /// must not reach the expansion valve.
    /// </remarks>
    public static Result<CycleResult> Subcritical(
        Refrigerant refrigerant,
        double evaporatingTemperature,
        double condensingTemperature,
        double superheat,
        double subcooling,
        double isentropicEfficiency,
        MotorLoss motor)
    {
        ArgumentNullException.ThrowIfNull(refrigerant);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(superheat);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(subcooling);

        var low = refrigerant.SaturationPressure(Kelvin(evaporatingTemperature));

        if (!low.IsSuccess)
        {
            return Result.Failure<CycleResult>(low.Error);
        }

        var high = refrigerant.SaturationPressure(Kelvin(condensingTemperature));

        if (!high.IsSuccess)
        {
            return Result.Failure<CycleResult>(high.Error);
        }

        var outlet = refrigerant.FromPressureTemperature(
            high.Value, Kelvin(condensingTemperature - subcooling));

        return !outlet.IsSuccess
            ? Result.Failure<CycleResult>(outlet.Error)
            : Compress(
                refrigerant,
                low.Value,
                high.Value,
                evaporatingTemperature + superheat,
                outlet.Value,
                isentropicEfficiency,
                motor);
    }

    /// <summary>Evaluates a transcritical cycle, whose high side is a pressure rather than a temperature.</summary>
    /// <param name="refrigerant">The working fluid — in practice <see cref="Refrigerant.CarbonDioxide"/>.</param>
    /// <param name="evaporatingTemperature">K. The saturation temperature the evaporator holds.</param>
    /// <param name="highSidePressure">Pa gauge at the gas cooler.</param>
    /// <param name="gasCoolerOutletTemperature">K leaving the gas cooler, set by what it is cooled against.</param>
    /// <param name="superheat">K above <paramref name="evaporatingTemperature"/> at the suction. Positive.</param>
    /// <param name="isentropicEfficiency">As above.</param>
    /// <param name="motor">As above.</param>
    /// <returns>The cycle, or the substance's own failure.</returns>
    /// <remarks>
    /// <strong>The extra argument is the whole difference.</strong> Above the critical point there is no
    /// saturation line, so the outlet temperature stops determining the pressure and the machine carries
    /// one more unknown than a subcritical one — which is a row in a counting table, not a correlation.
    /// <c>D-81</c> owns what closes it, and deliberately not this method: a cycle evaluated at a stated
    /// pressure is what both the correlation and the search call.
    /// </remarks>
    public static Result<CycleResult> Transcritical(
        Refrigerant refrigerant,
        double evaporatingTemperature,
        double highSidePressure,
        double gasCoolerOutletTemperature,
        double superheat,
        double isentropicEfficiency,
        MotorLoss motor)
    {
        ArgumentNullException.ThrowIfNull(refrigerant);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(superheat);

        var low = refrigerant.SaturationPressure(Kelvin(evaporatingTemperature));

        if (!low.IsSuccess)
        {
            return Result.Failure<CycleResult>(low.Error);
        }

        var high = Quantity.FromSi(highSidePressure, Dimension.Pressure);
        var outlet = refrigerant.FromPressureTemperature(high, Kelvin(gasCoolerOutletTemperature));

        return !outlet.IsSuccess
            ? Result.Failure<CycleResult>(outlet.Error)
            : Compress(
                refrigerant,
                low.Value,
                high,
                evaporatingTemperature + superheat,
                outlet.Value,
                isentropicEfficiency,
                motor);
    }

    private static Result<CycleResult> Compress(
        Refrigerant refrigerant,
        Quantity lowPressure,
        Quantity highPressure,
        double suctionTemperature,
        FluidState outlet,
        double isentropicEfficiency,
        MotorLoss motor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(isentropicEfficiency);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(isentropicEfficiency, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(motor.Efficiency);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(motor.Efficiency, 1);

        var suction = refrigerant.FromPressureTemperature(lowPressure, Kelvin(suctionTemperature));

        if (!suction.IsSuccess)
        {
            return Result.Failure<CycleResult>(suction.Error);
        }

        // State 2s: the same entropy at the high pressure, which is what an isentropic efficiency is
        // defined against and the only reason `FromPressureEntropy` exists (`D-78`).
        var ideal = refrigerant.FromPressureEntropy(highPressure, suction.Value.Entropy);

        if (!ideal.IsSuccess)
        {
            return Result.Failure<CycleResult>(ideal.Error);
        }

        var h1 = suction.Value.Enthalpy.SiValue;
        var reversible = ideal.Value.Enthalpy.SiValue - h1;
        var shaft = reversible / isentropicEfficiency;
        var discharge = refrigerant.FromPressureEnthalpy(
            highPressure, Quantity.FromSi(h1 + shaft, Dimension.Enthalpy));

        if (!discharge.IsSuccess)
        {
            return Result.Failure<CycleResult>(discharge.Error);
        }

        var h2 = discharge.Value.Enthalpy.SiValue;
        var h3 = outlet.Enthalpy.SiValue;
        var electrical = shaft / motor.Efficiency;

        // `D-82`: a loss that lands in the working fluid is sold on as heat, and one that lands in the
        // room is not. The same statement covers a hermetic compressor and a wet-rotor pump.
        var rejected = (h2 - h3) + (motor.ReachesRefrigerant ? electrical - shaft : 0);
        var absorbed = h1 - h3;

        var zones = Zones(refrigerant, highPressure, h2, h3);

        return !zones.IsSuccess
            ? Result.Failure<CycleResult>(zones.Error)
            : Result.Success(new CycleResult
            {
                Suction = suction.Value,
                IsentropicDischarge = ideal.Value,
                Discharge = discharge.Value,
                HighSideOutlet = outlet,
                ExpansionOutletEnthalpy = h3,
                SpecificShaftWork = shaft,
                SpecificElectricalWork = electrical,
                SpecificHeatRejected = rejected,
                SpecificHeatAbsorbed = absorbed,
                HeatingCop = rejected / electrical,
                CoolingCop = absorbed / electrical,
                PressureRatio = Absolute(highPressure) / Absolute(lowPressure),
                HighSideZones = zones.Value,
            });
    }

    private static Result<ZoneShares> Zones(
        Refrigerant refrigerant, Quantity highPressure, double h2, double h3)
    {
        var total = h2 - h3;
        var saturation = refrigerant.SaturationEnthalpies(highPressure);

        // Above the critical pressure there is no saturation line to cut at, and the gas cooler is one
        // sensible zone end to end. That is the answer, not a missing one.
        if (!saturation.IsSuccess)
        {
            return Result.Success(new ZoneShares(1, 0, 0));
        }

        var liquid = saturation.Value.Liquid.SiValue;
        var vapour = saturation.Value.Vapour.SiValue;

        return Result.Success(new ZoneShares(
            (h2 - vapour) / total,
            (vapour - liquid) / total,
            (liquid - h3) / total));
    }

    private static Quantity Kelvin(double value) => Quantity.FromSi(value, Dimension.Temperature);

    private static double Absolute(Quantity gaugePressure) =>
        gaugePressure.SiValue + UnitTable.StandardAtmosphere;
}
