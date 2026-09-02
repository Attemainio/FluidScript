using FluidScript.Core.Diagnostics;
using FluidScript.Core.Units;

namespace FluidScript.Core.Fluids;

/// <summary>Humid air, measured by the property backend.</summary>
/// <remarks>
/// <para>
/// Present for psychrometric property validation (<c>R-08</c>) and for metadata. It <strong>cannot
/// lower to a v1 circuit</strong> (<c>D-28</c>): no air-side component is validated, so a script that
/// wrote <c>fluid air</c> and solved would be presenting numbers nothing stands behind.
/// </para>
/// <para>
/// <strong>Its enthalpy is per kg of dry air.</strong> Every method here that takes or returns one
/// says so in its name or its parameter documentation, because the alternative basis is only 0.3 %
/// away at a typical state and cannot be spotted by looking at the number.
/// </para>
/// </remarks>
public sealed class HumidAirSubstance : SubstanceBase, IHumidAir
{
    /// <summary>Gets the shared instance.</summary>
    public static HumidAirSubstance Instance { get; } = new();

    /// <inheritdoc/>
    public override string Name => "air";

    /// <inheritdoc/>
    /// <value>
    /// 0 to 50 °C and 80 to 110 kPa absolute — <c>07</c>'s humid-air row. Its relative-humidity bound
    /// of 10 to 90 % is not expressible here and is not enforced: a state at 5 % RH is well defined and
    /// the row is a statement about validated <em>accuracy</em>, not about which states exist. Only the
    /// 0 to 100 % a fraction can mean at all is refused, as <c>FS2006</c>.
    /// </value>
    public override StateRange ValidRange { get; } = new(273.15, 323.15, 80_000, 110_000);

    /// <inheritdoc/>
    /// <remarks>Fixes the state at zero humidity: dry air at that pressure and temperature.</remarks>
    public override Result<FluidState> FromPressureTemperature(Quantity gaugePressure, Quantity temperature) =>
        AsFluidState(FromPressureTemperatureHumidity(
            gaugePressure, temperature, Quantity.FromSi(0, Dimension.Dimensionless)));

    /// <inheritdoc/>
    /// <remarks>
    /// Fixes the state at zero humidity, where the dry-air and mixture bases coincide, so the enthalpy
    /// this takes is unambiguous. At any other humidity use
    /// <see cref="FromPressureEnthalpyHumidity"/>, which names its basis.
    /// </remarks>
    public override Result<FluidState> FromPressureEnthalpy(Quantity gaugePressure, Quantity enthalpy) =>
        AsFluidState(FromPressureEnthalpyHumidity(
            gaugePressure, enthalpy, Quantity.FromSi(0, Dimension.Dimensionless)));

    /// <inheritdoc/>
    /// <remarks>
    /// The freezing point of the water the air carries, which is what a condensation or frost check
    /// needs. Air itself does not freeze anywhere near this domain.
    /// </remarks>
    public override Result<Quantity> FreezingPoint(Quantity gaugePressure) =>
        Result.Success(Quantity.FromSi(273.15, Dimension.Temperature));

    /// <inheritdoc/>
    /// <remarks>
    /// The saturation pressure of water vapour over liquid water at this temperature, which is what
    /// sets how much moisture the air can hold. Delegated to water's own saturation line, because it
    /// is the same curve.
    /// </remarks>
    public override Result<Quantity> SaturationPressure(Quantity temperature) =>
        Water.Instance.SaturationPressure(temperature);

    /// <inheritdoc/>
    public Result<HumidAirState> FromPressureTemperatureHumidity(
        Quantity gaugePressure, Quantity dryBulb, Quantity humidityRatio) =>
        Fix(gaugePressure, dryBulb, humidityRatio.SiValue, relative: false);

    /// <inheritdoc/>
    public Result<HumidAirState> FromPressureTemperatureRelativeHumidity(
        Quantity gaugePressure, Quantity dryBulb, Quantity relativeHumidity)
    {
        var fraction = relativeHumidity.SiValue;

        if (fraction is < 0 or > 1 || !double.IsFinite(fraction))
        {
            return Result.Failure<HumidAirState>(
                ResultError.From(FluidDiagnostics.RelativeHumidityOutOfRange));
        }

        return Fix(gaugePressure, dryBulb, fraction, relative: true);
    }

    /// <inheritdoc/>
    public Result<HumidAirState> FromPressureEnthalpyHumidity(
        Quantity gaugePressure, Quantity dryAirBasisEnthalpy, Quantity humidityRatio)
    {
        var absolute = Absolute(gaugePressure);
        var measured = PropertyBackend.HumidAirFromEnthalpy(
            absolute, dryAirBasisEnthalpy.SiValue, humidityRatio.SiValue);

        if (measured is { } state && OutOfRange(state.Temperature, absolute) is { } failure)
        {
            return Result.Failure<HumidAirState>(failure);
        }

        return Build(gaugePressure, absolute, measured);
    }

    private Result<HumidAirState> Fix(
        Quantity gaugePressure, Quantity dryBulb, double humidity, bool relative)
    {
        var absolute = Absolute(gaugePressure);

        return OutOfRange(dryBulb.SiValue, absolute) is { } failure
            ? Result.Failure<HumidAirState>(failure)
            : Build(
                gaugePressure,
                absolute,
                PropertyBackend.HumidAirFromTemperature(absolute, dryBulb.SiValue, humidity, relative));
    }

    private Result<HumidAirState> Build(
        Quantity gaugePressure, double absolute, BackendHumidAirState? measured)
    {
        if (measured is not { } state)
        {
            return Result.Failure<HumidAirState>(NotEvaluable("a state", Describe(double.NaN, absolute)));
        }

        if (!double.IsFinite(state.Density) || !double.IsFinite(state.DryAirBasisEnthalpy)
            || !double.IsFinite(state.HumidityRatio) || !double.IsFinite(state.WetBulb)
            || !double.IsFinite(state.DewPoint))
        {
            return Result.Failure<HumidAirState>(
                NotEvaluable("a property", Describe(state.Temperature, absolute)));
        }

        return Result.Success(new HumidAirState
        {
            Substance = this,
            Pressure = gaugePressure,
            Temperature = Quantity.FromSi(state.Temperature, Dimension.Temperature),
            Density = Quantity.FromSi(state.Density, Dimension.Density),
            DynamicViscosity = Quantity.FromSi(state.DynamicViscosity, FluidDimensions.DynamicViscosity),
            SpecificHeat = Quantity.FromSi(state.SpecificHeat, Dimension.SpecificHeat),
            ThermalConductivity = Quantity.FromSi(
                state.ThermalConductivity, FluidDimensions.ThermalConductivity),
            HumidityRatio = Quantity.FromSi(state.HumidityRatio, Dimension.Dimensionless),
            RelativeHumidity = Quantity.FromSi(state.RelativeHumidity, Dimension.Dimensionless),
            WetBulb = Quantity.FromSi(state.WetBulb, Dimension.Temperature),
            DewPoint = Quantity.FromSi(state.DewPoint, Dimension.Temperature),
            DryAirBasisEnthalpy = Quantity.FromSi(state.DryAirBasisEnthalpy, Dimension.Enthalpy),
        });
    }

    /// <summary>Narrows a humid-air state to the two-property shape, at zero humidity only.</summary>
    /// <remarks>
    /// The enthalpy carried over is the dry-air-basis one, which is safe <em>because</em> the caller
    /// asked for zero humidity: with no water in the air the two bases are the same number. Anything
    /// that reaches here at a humidity above zero would be putting a per-dry-air enthalpy into a field
    /// documented as per-fluid, so the humidity is asserted rather than assumed.
    /// </remarks>
    private static Result<FluidState> AsFluidState(Result<HumidAirState> result)
    {
        if (!result.IsSuccess)
        {
            return Result.Failure<FluidState>(result.Error);
        }

        var air = result.Value;

        return Result.Success(new FluidState
        {
            Substance = air.Substance,
            Pressure = air.Pressure,
            Temperature = air.Temperature,
            Enthalpy = air.DryAirBasisEnthalpy,
            Density = air.Density,
            DynamicViscosity = air.DynamicViscosity,
            SpecificHeat = air.SpecificHeat,
            ThermalConductivity = air.ThermalConductivity,
            Phase = Phase.Gas,
        });
    }
}
