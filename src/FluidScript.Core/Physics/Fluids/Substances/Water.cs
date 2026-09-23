using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Primitives;

namespace FluidScript.Core.Physics.Fluids.Substances;

/// <summary>Liquid water, measured by the property backend.</summary>
/// <remarks>
/// The v1 hydronic working fluid (<c>D-28</c>). Its validated domain is <c>07</c>'s engineering
/// validity row — liquid water from 0 to 120 °C, from the triple-point pressure up to 1000 kPa absolute
/// and below its boiling line — and the domain is enforced here rather than by the backend, which
/// returns a plausible density for water at 5000 °C.
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
    /// <value>
    /// 0 to 120 °C, <see cref="TriplePointPressure"/> to 1000 kPa absolute — <c>07</c>'s water-properties
    /// row. The rectangle's low-pressure edge is a formality: what bounds liquid water from below is the
    /// boiling line, which <see cref="Build"/> enforces by phase, and IAPWS-IF97 Region 1 is valid from
    /// the saturation pressure at every temperature here. The edge sat at 100 kPa absolute until
    /// <c>D-121</c>, which put every closed circuit's arbitrary zero exactly on it (<c>S-29</c>, <c>S-62</c>).
    /// </value>
    public override StateRange ValidRange { get; } = new(273.15, 393.15, TriplePointPressure, 1_000_000);

    /// <summary>The pressure of water's triple point, the lowest pressure at which liquid water exists.</summary>
    /// <value>Pa absolute. 611.657 Pa, IAPWS-IF97 (2007 revision), section 1.</value>
    public const double TriplePointPressure = 611.657;

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
        // IF97 answers on the line itself, from region 1's side, so the line is checked before the
        // measurement is trusted; the melting line is still only asked about when the backend refused.
        if (OnTheSaturationLine(kelvin, absolute) || (measured is null && OnTheMeltingLine(kelvin)))
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

    /// <inheritdoc/>
    /// <remarks>
    /// Water has a vapour phase, so the pair means something here even though no hydronic circuit asks
    /// it. The range check runs on the temperature that comes back, exactly as
    /// <see cref="FromPressureEnthalpy"/> does and for the same reason: an entropy outside the domain
    /// is only recognisable as the temperature it implies.
    /// </remarks>
    public override Result<FluidState> FromPressureEntropy(Quantity gaugePressure, Quantity entropy)
    {
        var absolute = Absolute(gaugePressure);
        var measured = PropertyBackend.WaterFromPressureEntropy(absolute, entropy.SiValue);

        if (measured is { } state && OutOfRange(state.Temperature, absolute) is { } failure)
        {
            return Result.Failure<FluidState>(failure);
        }

        return Build(gaugePressure, absolute, measured);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The boiling line, which is where <see cref="ValidRange"/>'s rectangle stops describing the
    /// domain: it moves from 99.61 °C at 100 kPa absolute to 179.88 °C at 1000 kPa, so the corner above
    /// it is steam rather than an out-of-range liquid.
    /// </remarks>
    public override Result<Quantity> SaturationTemperature(Quantity gaugePressure)
    {
        var absolute = Absolute(gaugePressure);

        if (PropertyBackend.WaterSaturationTemperature(absolute) is not { } boiling
            || !double.IsFinite(boiling))
        {
            return Result.Failure<Quantity>(
                NotEvaluable("a saturation temperature", Describe(double.NaN, absolute)));
        }

        return Result.Success<Quantity>(Quantity.FromSi(boiling, Dimension.Temperature));
    }

    private Result<FluidState> Build(Quantity gaugePressure, double absolute, BackendState? measured)
    {
        if (measured is not { } state)
        {
            return Result.Failure<FluidState>(
                NotEvaluable("a state", Describe(double.NaN, absolute)));
        }

        if (!double.IsFinite(state.Density) || !double.IsFinite(state.Enthalpy)
            || !double.IsFinite(state.Entropy) || !double.IsFinite(state.SpecificHeat)
            || !double.IsFinite(state.DynamicViscosity)
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
            Entropy = Quantity.FromSi(state.Entropy, Dimension.SpecificHeat),
            Density = Quantity.FromSi(state.Density, Dimension.Density),
            DynamicViscosity = Quantity.FromSi(state.DynamicViscosity, FluidDimensions.DynamicViscosity),
            SpecificHeat = Quantity.FromSi(state.SpecificHeat, Dimension.SpecificHeat),
            ThermalConductivity = Quantity.FromSi(
                state.ThermalConductivity, FluidDimensions.ThermalConductivity),
            Phase = state.Phase,
        });
    }
}
