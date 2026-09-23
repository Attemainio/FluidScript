using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Primitives;

namespace FluidScript.Core.Physics.Cycles;

/// <summary>How much of a compressor's electrical input reaches the shaft, and where the rest goes.</summary>
/// <param name="Efficiency">Shaft power divided by electrical input, from 0 to 1.</param>
/// <param name="ReachesRefrigerant">
/// <see langword="true"/> for a hermetic machine, whose motor sits in the suction stream so its losses
/// are recovered in the condenser; <see langword="false"/> for an open drive, whose losses warm the
/// plant room instead.
/// </param>
/// <remarks>
/// <c>D-82</c>'s loss destination, in the one place it changes an answer by more than a rounding.
/// A hermetic machine at <c>η_motor</c> = 0.92 is worth about 2.5 % of heating COP over an open one,
/// because the same watts are billed either way and only one of the two arrangements sells them back.
/// </remarks>
public readonly record struct MotorLoss(double Efficiency, bool ReachesRefrigerant)
{
    /// <summary>Gets the case with no motor modelled: the shaft is the input, and nothing is lost.</summary>
    /// <value>Efficiency 1, so a result is on a shaft basis and the identities below hold exactly.</value>
    public static MotorLoss None => new(1.0, false);
}

/// <summary>How a high-side exchanger's duty divides between its zones.</summary>
/// <param name="Desuperheat">The fraction above the saturated-vapour enthalpy.</param>
/// <param name="Latent">The fraction between the two saturation enthalpies.</param>
/// <param name="Subcool">The fraction below the saturated-liquid enthalpy.</param>
/// <remarks>
/// <c>D-80</c>'s split, measured from the backend's own saturation enthalpies rather than estimated
/// from a vapour specific heat. <strong>These are duty shares and not area shares</strong>: gas-side
/// <c>U</c> is roughly a sixth of the condensing value, so a zone carrying a fifth of the duty can
/// carry a third of the area. A transcritical cycle has no saturation line, so its whole gas cooler is
/// desuperheat by construction.
/// </remarks>
public readonly record struct ZoneShares(double Desuperheat, double Latent, double Subcool);

/// <summary>The four state points of one revolution, and what they add up to.</summary>
/// <remarks>
/// Every state here is real and inspectable, which is the point of modelling the circuit rather than
/// correlating its COP (<c>D-78</c>): a discharge temperature, a pressure ratio and a zone split are
/// things an engineer checks a machine against, and a correlation has none of them.
/// </remarks>
public sealed record CycleResult
{
    /// <summary>Gets state 1 — compressor suction, superheated vapour.</summary>
    public required FluidState Suction { get; init; }

    /// <summary>Gets state 2s — where an ideal compressor would discharge.</summary>
    /// <remarks>Carried rather than discarded because it is what the isentropic efficiency is measured against.</remarks>
    public required FluidState IsentropicDischarge { get; init; }

    /// <summary>Gets state 2 — the real discharge.</summary>
    public required FluidState Discharge { get; init; }

    /// <summary>Gets state 3 — the high side's outlet, subcooled liquid or cooled supercritical gas.</summary>
    public required FluidState HighSideOutlet { get; init; }

    /// <summary>Gets the enthalpy after the expansion valve.</summary>
    /// <value>
    /// J/kg, and equal to <see cref="HighSideOutlet"/>'s by definition: the expansion is isenthalpic,
    /// so state 4 is an assignment rather than a measurement, and nothing evaluates a property there.
    /// </value>
    public required double ExpansionOutletEnthalpy { get; init; }

    /// <summary>Gets the shaft work.</summary>
    /// <value>J/kg of refrigerant, always positive.</value>
    public required double SpecificShaftWork { get; init; }

    /// <summary>Gets the electrical input.</summary>
    /// <value>J/kg of refrigerant. Equal to <see cref="SpecificShaftWork"/> when no motor is modelled.</value>
    public required double SpecificElectricalWork { get; init; }

    /// <summary>Gets the heat rejected on the high side.</summary>
    /// <value>J/kg of refrigerant, positive out of the cycle.</value>
    public required double SpecificHeatRejected { get; init; }

    /// <summary>Gets the heat absorbed in the evaporator.</summary>
    /// <value>J/kg of refrigerant, positive into the cycle.</value>
    public required double SpecificHeatAbsorbed { get; init; }

    /// <summary>Gets the heating coefficient of performance.</summary>
    /// <value>Heat rejected divided by electrical input. Never below 1 for a physical cycle.</value>
    public required double HeatingCop { get; init; }

    /// <summary>Gets the cooling coefficient of performance.</summary>
    /// <value>Heat absorbed divided by electrical input.</value>
    public required double CoolingCop { get; init; }

    /// <summary>Gets the ratio of discharge to suction pressure, absolute.</summary>
    /// <value>Dimensionless, above 1. Above roughly 7 a single stage is marginal on most refrigerants.</value>
    public required double PressureRatio { get; init; }

    /// <summary>Gets how the high-side duty divides between its zones.</summary>
    public required ZoneShares HighSideZones { get; init; }
}

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
