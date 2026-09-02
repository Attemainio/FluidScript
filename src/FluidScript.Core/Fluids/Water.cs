using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Units;

namespace FluidScript.Core.Fluids;

/// <summary>Shared plumbing every substance needs: the atmosphere, the range check, the failures.</summary>
/// <remarks>
/// <para>
/// <strong>The gauge/absolute boundary lives here and nowhere else.</strong> <c>13</c> defines
/// <see cref="Dimension.Pressure"/> as gauge in SI — the absolute spellings <c>bara</c> and <c>kPaa</c>
/// normalise to it with an offset at the language boundary — so every pressure the model carries is
/// gauge, and the atmosphere is added once, immediately before a measurement (<c>D-26</c>).
/// </para>
/// <para>
/// <c>21</c>'s snippet names the parameter <c>absolutePressure</c>, which contradicts both that
/// definition and its own "the single adapter adds the model's recorded atmosphere". Recorded as
/// <c>C-10</c>.
/// </para>
/// </remarks>
public abstract class SubstanceBase : ISubstance
{
    /// <summary>The atmosphere a gauge pressure is measured from.</summary>
    /// <value>Pa. 101 325 in v1, per <c>D-26</c>; a model-recorded value is post-v1.</value>
    protected const double Atmosphere = UnitTable.StandardAtmosphere;

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public abstract StateRange ValidRange { get; }

    /// <inheritdoc/>
    public abstract Result<FluidState> FromPressureTemperature(Quantity gaugePressure, Quantity temperature);

    /// <inheritdoc/>
    public abstract Result<FluidState> FromPressureEnthalpy(Quantity gaugePressure, Quantity enthalpy);

    /// <inheritdoc/>
    public abstract Result<Quantity> FreezingPoint(Quantity gaugePressure);

    /// <inheritdoc/>
    public abstract Result<Quantity> SaturationPressure(Quantity temperature);

    /// <summary>Converts a gauge pressure the model holds into the absolute one a backend needs.</summary>
    /// <param name="gaugePressure">The pressure, gauge.</param>
    /// <returns>Pa absolute.</returns>
    protected static double Absolute(Quantity gaugePressure) => gaugePressure.SiValue + Atmosphere;

    /// <summary>Builds the <c>FS2003</c> a state outside the validated domain fails with.</summary>
    /// <param name="quantity">What was out of range: a temperature in K, or a pressure in Pa absolute.</param>
    /// <param name="low">The bound's lower end, in the same terms.</param>
    /// <param name="high">The bound's upper end.</param>
    /// <param name="unit">How to spell the three of them in the message.</param>
    /// <returns>The failure, carrying everything a diagnostic needs and emitting nothing.</returns>
    protected ResultError OutsideRange(double quantity, double low, double high, string unit) =>
        ResultError.From(
            FluidDiagnostics.StateOutsideValidRange,
            ("name", Name),
            ("lo", Format(low, unit)),
            ("hi", Format(high, unit)),
            ("value", Format(quantity, unit)));

    /// <summary>Builds the <c>FS2004</c> a non-finite measurement fails with.</summary>
    /// <param name="property">Which property could not be evaluated.</param>
    /// <param name="state">The state it was asked for, spelled for a reader.</param>
    /// <returns>The failure.</returns>
    protected ResultError NotEvaluable(string property, string state) =>
        ResultError.From(
            FluidDiagnostics.PropertyNotEvaluable,
            ("property", property),
            ("name", Name),
            ("state", state));

    /// <summary>Checks a state against <see cref="ValidRange"/>.</summary>
    /// <param name="temperature">K.</param>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <returns>
    /// The failure naming whichever bound was crossed, or <see langword="null"/> when the state is
    /// inside the domain. Temperature is reported first when both are out, because it is the one that
    /// almost always is.
    /// </returns>
    protected ResultError? OutOfRange(double temperature, double absolutePressure)
    {
        var range = ValidRange;

        if (temperature < range.MinimumTemperature || temperature > range.MaximumTemperature)
        {
            return OutsideRange(
                temperature - 273.15, range.MinimumTemperature - 273.15, range.MaximumTemperature - 273.15, "C");
        }

        if (absolutePressure < range.MinimumAbsolutePressure || absolutePressure > range.MaximumAbsolutePressure)
        {
            return OutsideRange(
                absolutePressure / 1000, range.MinimumAbsolutePressure / 1000,
                range.MaximumAbsolutePressure / 1000, "kPa absolute");
        }

        return null;
    }

    /// <summary>Spells a state for a message.</summary>
    /// <param name="temperature">K.</param>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <returns>Something like <c>20 C, 201.3 kPa absolute</c>.</returns>
    protected static string Describe(double temperature, double absolutePressure) =>
        $"{Format(temperature - 273.15, "C")}, {Format(absolutePressure / 1000, "kPa absolute")}";

    private static string Format(double value, string unit) =>
        $"{value.ToString("0.###", CultureInfo.InvariantCulture)} {unit}";
}

/// <summary>Liquid water, measured by the property backend.</summary>
/// <remarks>
/// The v1 hydronic working fluid (<c>D-28</c>). Its validated domain is <c>07</c>'s engineering
/// validity row — 0 to 120 °C and 100 to 1000 kPa absolute — and the domain is enforced here rather
/// than by the backend, which returns a plausible density for water at 5000 °C.
/// </remarks>
public sealed class Water : SubstanceBase
{
    /// <summary>Gets the shared instance.</summary>
    /// <remarks>
    /// Stateless, so one instance serves every model. It holds no cache: <c>21</c>'s invariant 7 is
    /// that no property cache outlives a solve, and a cache on a shared substance would outlive every
    /// solve there has ever been.
    /// </remarks>
    public static Water Instance { get; } = new();

    /// <inheritdoc/>
    public override string Name => "water";

    /// <inheritdoc/>
    /// <value>0 to 120 °C, 100 to 1000 kPa absolute — <c>07</c>'s water-properties row verbatim.</value>
    public override StateRange ValidRange { get; } = new(273.15, 393.15, 100_000, 1_000_000);

    /// <inheritdoc/>
    public override Result<FluidState> FromPressureTemperature(Quantity gaugePressure, Quantity temperature)
    {
        var absolute = Absolute(gaugePressure);
        var kelvin = temperature.SiValue;

        if (OutOfRange(kelvin, absolute) is { } failure)
        {
            return Result.Failure<FluidState>(failure);
        }

        var measured = PropertyBackend.WaterFromPressureTemperature(absolute, kelvin);

        // On a phase boundary pressure and temperature are not independent, and the backend refuses
        // rather than choosing a side. That is `FS2002` and not a range failure: the state exists, and
        // this pair simply cannot say which of the two phases is meant. Both boundaries of the liquid
        // domain do it, and the *lower* one is the surprise: `07` states 0 °C as an endpoint of water's
        // domain, and 0 °C is the melting line, so the endpoint it claims is not itself a state (`F-14`).
        if (measured is null && (OnTheSaturationLine(kelvin, absolute) || OnTheMeltingLine(kelvin)))
        {
            return Result.Failure<FluidState>(ResultError.From(
                FluidDiagnostics.PairDoesNotFixAState,
                ("a", "pressure"),
                ("b", "temperature")));
        }

        return Build(gaugePressure, absolute, measured);
    }

    /// <summary>Determines whether a state sits on the boiling line, where p and T are one constraint.</summary>
    /// <param name="temperature">K.</param>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <returns><see langword="true"/> when the temperature is the saturation temperature there.</returns>
    /// <remarks>
    /// The tolerance matches the backend's own: it refuses when the saturation pressure is within
    /// 1e-4 % of the pressure given, which near atmospheric is about 0.0004 K of temperature. A
    /// millikelvin is comfortably wider and still far narrower than any state a script states on
    /// purpose.
    /// </remarks>
    private static bool OnTheSaturationLine(double temperature, double absolutePressure) =>
        PropertyBackend.WaterSaturationTemperature(absolutePressure) is { } boiling
        && Math.Abs(temperature - boiling) < 1e-3;

    /// <summary>Determines whether a state sits on the melting line, where liquid water meets ice.</summary>
    /// <param name="temperature">K.</param>
    /// <returns><see langword="true"/> when the temperature is water's melting point.</returns>
    /// <remarks>
    /// Fixed at 273.15 K rather than measured, for the reason <see cref="FreezingPoint"/> gives: the
    /// melting line moves about 0.0074 K per bar, so over this whole domain it stays well inside the
    /// tolerance below. The backend has no melting-line query to ask instead.
    /// </remarks>
    private static bool OnTheMeltingLine(double temperature) => Math.Abs(temperature - 273.15) < 1e-3;

    /// <inheritdoc/>
    public override Result<FluidState> FromPressureEnthalpy(Quantity gaugePressure, Quantity enthalpy)
    {
        var absolute = Absolute(gaugePressure);
        var measured = PropertyBackend.WaterFromPressureEnthalpy(absolute, enthalpy.SiValue);

        // The range is checked on the temperature that comes back, because that is the only place an
        // enthalpy's temperature is known. An enthalpy outside the domain reads as a temperature
        // outside it, which is the message a user can act on.
        if (measured is { } state && OutOfRange(state.Temperature, absolute) is { } failure)
        {
            return Result.Failure<FluidState>(failure);
        }

        return Build(gaugePressure, absolute, measured);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Pressure-independent to well beyond this substance's validated domain: the melting line moves
    /// by about 0.0074 K per bar, so over 100 to 1000 kPa the whole variation is under 0.01 K against
    /// the 0.05 K tolerance <c>21</c>'s worked example states.
    /// </remarks>
    public override Result<Quantity> FreezingPoint(Quantity gaugePressure) =>
        Result.Success<Quantity>(Quantity.FromSi(273.15, Dimension.Temperature));

    /// <inheritdoc/>
    public override Result<Quantity> SaturationPressure(Quantity temperature)
    {
        if (PropertyBackend.WaterSaturationPressure(temperature.SiValue) is not { } absolute
            || !double.IsFinite(absolute))
        {
            return Result.Failure<Quantity>(
                NotEvaluable("saturation pressure", Describe(temperature.SiValue, Atmosphere)));
        }

        // Returned gauge, like every other pressure in the model, so a boiling or cavitation check can
        // compare it against a node's pressure without either side remembering which datum it is on.
        return Result.Success<Quantity>(Quantity.FromSi(absolute - Atmosphere, Dimension.Pressure));
    }

    private Result<FluidState> Build(Quantity gaugePressure, double absolute, BackendState? measured)
    {
        if (measured is not { } state)
        {
            return Result.Failure<FluidState>(
                NotEvaluable("a state", Describe(double.NaN, absolute)));
        }

        if (!double.IsFinite(state.Density) || !double.IsFinite(state.Enthalpy)
            || !double.IsFinite(state.SpecificHeat) || !double.IsFinite(state.DynamicViscosity)
            || !double.IsFinite(state.ThermalConductivity))
        {
            return Result.Failure<FluidState>(
                NotEvaluable("a property", Describe(state.Temperature, absolute)));
        }

        // The domain is liquid water, and the rectangle `07` states is not all liquid: at 100 kPa
        // absolute the boiling point is 99.61 C, so the corner above it is steam. The backend hands it
        // back without complaint at 0.573 kg/m3 against liquid's ~950 — a factor of 1600, silently,
        // inside a range this substance calls valid. The phase is the only thing that catches it
        // (`F-13`).
        if (state.Phase != Phase.Liquid)
        {
            var boiling = PropertyBackend.WaterSaturationTemperature(absolute) ?? ValidRange.MaximumTemperature;

            return Result.Failure<FluidState>(OutsideRange(
                state.Temperature - 273.15,
                ValidRange.MinimumTemperature - 273.15,
                boiling - 273.15,
                "C"));
        }

        return Result.Success<FluidState>(new FluidState
        {
            Substance = this,
            Pressure = gaugePressure,
            Temperature = Quantity.FromSi(state.Temperature, Dimension.Temperature),
            Enthalpy = Quantity.FromSi(state.Enthalpy, Dimension.Enthalpy),
            Density = Quantity.FromSi(state.Density, Dimension.Density),
            DynamicViscosity = Quantity.FromSi(state.DynamicViscosity, FluidDimensions.DynamicViscosity),
            SpecificHeat = Quantity.FromSi(state.SpecificHeat, Dimension.SpecificHeat),
            ThermalConductivity = Quantity.FromSi(
                state.ThermalConductivity, FluidDimensions.ThermalConductivity),
            Phase = state.Phase,
        });
    }
}
