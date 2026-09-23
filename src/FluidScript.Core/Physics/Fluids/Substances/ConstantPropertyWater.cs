using FluidScript.Core.Physics.Units;
using FluidScript.Core.Primitives;

namespace FluidScript.Core.Physics.Fluids.Substances;

/// <summary>Water with properties that never change, for tests that are not about properties.</summary>
/// <remarks>
/// <para>
/// The reason <see cref="ISubstance"/> exists at all. A property call through the real backend costs
/// several hundred microseconds; this one is arithmetic, so a component or solver test runs in
/// microseconds and loads no property tables. Nothing here is a claim about water — it is a claim
/// about <em>constancy</em>, which is what makes the arithmetic hand-checkable.
/// </para>
/// <para>
/// <strong>Its enthalpy datum is 0 °C</strong>, chosen so <c>h = cp × (T − 273.15)</c> exactly. That
/// makes <c>(p, h)</c> the exact inverse of <c>(p, T)</c> with no iteration, so a test asserting the
/// round trip is asserting the caller's arithmetic rather than the backend's convergence. The real
/// substance's datum is CoolProp's and is not this one; only differences are comparable.
/// </para>
/// </remarks>
public sealed class ConstantPropertyWater : SubstanceBase
{
    /// <summary>The density used at every state.</summary>
    /// <value>998.2 kg/m³, water near 20 °C.</value>
    public const double DensityValue = 998.2;

    /// <summary>The specific heat used at every state.</summary>
    /// <value>4184 J/(kg·K).</value>
    public const double SpecificHeatValue = 4184;

    /// <summary>The dynamic viscosity used at every state.</summary>
    /// <value>1.002 × 10⁻³ Pa·s.</value>
    public const double DynamicViscosityValue = 1.002e-3;

    /// <summary>The thermal conductivity used at every state.</summary>
    /// <value>0.598 W/(m·K).</value>
    public const double ThermalConductivityValue = 0.598;

    /// <summary>Gets the shared instance.</summary>
    public static ConstantPropertyWater Instance { get; } = new();

    /// <inheritdoc/>
    public override string Name => "water";

    /// <inheritdoc/>
    /// <value>The real substance's own domain, so a test cannot pass here and fail there (<c>D-121</c> moved its floor, and a copy here missed it).</value>
    public override StateRange ValidRange => Water.Instance.ValidRange;

    /// <inheritdoc/>
    public override Result<FluidState> FromPressureTemperature(Quantity gaugePressure, Quantity temperature)
    {
        var kelvin = temperature.SiValue;

        return OutOfRange(kelvin, Absolute(gaugePressure)) is { } failure
            ? Result.Failure<FluidState>(failure)
            : Result.Success(At(gaugePressure, kelvin));
    }

    /// <inheritdoc/>
    public override Result<FluidState> FromPressureEnthalpy(Quantity gaugePressure, Quantity enthalpy)
    {
        var kelvin = 273.15 + (enthalpy.SiValue / SpecificHeatValue);

        return OutOfRange(kelvin, Absolute(gaugePressure)) is { } failure
            ? Result.Failure<FluidState>(failure)
            : Result.Success(At(gaugePressure, kelvin));
    }

    /// <inheritdoc/>
    public override Result<Quantity> FreezingPoint(Quantity gaugePressure) =>
        Result.Success(Quantity.FromSi(273.15, Dimension.Temperature));

    /// <inheritdoc/>
    /// <remarks>
    /// The Antoine correlation over 1 to 100 °C, not a constant: a constant would make every cavitation
    /// test pass or fail together and prove nothing about the check being tested.
    /// </remarks>
    public override Result<Quantity> SaturationPressure(Quantity temperature)
    {
        var celsius = temperature.SiValue - 273.15;
        var mmHg = Math.Pow(10, 8.07131 - (1730.63 / (233.426 + celsius)));

        return Result.Success(
            Quantity.FromSi((mmHg * 133.322) - Atmosphere, Dimension.Pressure));
    }

    private FluidState At(Quantity gaugePressure, double kelvin) => new()
    {
        Substance = this,
        Pressure = gaugePressure,
        Temperature = Quantity.FromSi(kelvin, Dimension.Temperature),
        Enthalpy = Quantity.FromSi(SpecificHeatValue * (kelvin - 273.15), Dimension.Enthalpy),

        // s = integral of cp/T, which for a constant cp is cp*ln(T/T_ref) exactly. Real rather than a
        // placeholder, so the double stays internally consistent: dh/ds is T at every point, as it must
        // be.
        Entropy = Quantity.FromSi(SpecificHeatValue * Math.Log(kelvin / 273.15), Dimension.SpecificHeat),
        Density = Quantity.FromSi(DensityValue, Dimension.Density),
        DynamicViscosity = Quantity.FromSi(DynamicViscosityValue, FluidDimensions.DynamicViscosity),
        SpecificHeat = Quantity.FromSi(SpecificHeatValue, Dimension.SpecificHeat),
        ThermalConductivity = Quantity.FromSi(ThermalConductivityValue, FluidDimensions.ThermalConductivity),
        Phase = Phase.Liquid,
    };
}
