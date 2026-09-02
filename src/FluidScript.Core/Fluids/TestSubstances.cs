using FluidScript.Core.Units;

namespace FluidScript.Core.Fluids;

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
    /// <value>The same domain the real substance claims, so a test cannot pass here and fail there.</value>
    public override StateRange ValidRange { get; } = new(273.15, 393.15, 100_000, 1_000_000);

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
        Density = Quantity.FromSi(DensityValue, Dimension.Density),
        DynamicViscosity = Quantity.FromSi(DynamicViscosityValue, FluidDimensions.DynamicViscosity),
        SpecificHeat = Quantity.FromSi(SpecificHeatValue, Dimension.SpecificHeat),
        ThermalConductivity = Quantity.FromSi(ThermalConductivityValue, FluidDimensions.ThermalConductivity),
        Phase = Phase.Liquid,
    };
}

/// <summary>Water whose properties vary linearly with temperature.</summary>
/// <remarks>
/// <para>
/// <strong>The one the constant fake cannot replace.</strong> A component written as though <c>cp</c>
/// were fixed gives the right answer against <see cref="ConstantPropertyWater"/> and the wrong one
/// here, and the difference is what a two-fake suite buys: one for speed, one for the class of defect
/// speed hides. Running only the constant one is a false sense of coverage, which is why <c>08</c>
/// says <em>both</em> in the same package.
/// </para>
/// <para>
/// <strong>Enthalpy is the integral of the specific heat, not a product of it.</strong> With
/// <c>cp(T) = cp₀ + b·(T − T₀)</c> the enthalpy is <c>cp₀·x + b·x²/2</c> for <c>x = T − T₀</c>, so
/// inverting it means solving a quadratic — which is exactly the step a caller that assumed constant
/// properties skipped. The quadratic has a closed form, so this fake stays exact and needs no
/// iteration of its own.
/// </para>
/// <para>
/// The coefficients are plausible for water over 0 to 120 °C and are <strong>not</strong> a property
/// model. Its job is to be non-constant and exactly invertible; accuracy is the real substance's job.
/// </para>
/// </remarks>
public sealed class LinearPropertyWater : SubstanceBase
{
    /// <summary>The temperature the coefficients are stated at.</summary>
    /// <value>293.15 K, which is 20 °C.</value>
    public const double ReferenceTemperature = 293.15;

    /// <summary>The specific heat at <see cref="ReferenceTemperature"/>.</summary>
    /// <value>4180 J/(kg·K).</value>
    public const double SpecificHeatAtReference = 4180;

    /// <summary>How the specific heat varies with temperature.</summary>
    /// <value>0.5 J/(kg·K) per K.</value>
    public const double SpecificHeatSlope = 0.5;

    /// <summary>Gets the shared instance.</summary>
    public static LinearPropertyWater Instance { get; } = new();

    /// <inheritdoc/>
    public override string Name => "water";

    /// <inheritdoc/>
    public override StateRange ValidRange { get; } = new(273.15, 393.15, 100_000, 1_000_000);

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
        // The positive root of cp0*x + (b/2)*x^2 = h, which is the exact inverse of Enthalpy below.
        var discriminant = (SpecificHeatAtReference * SpecificHeatAtReference)
            + (2 * SpecificHeatSlope * enthalpy.SiValue);

        if (discriminant < 0)
        {
            return Result.Failure<FluidState>(
                NotEvaluable("a temperature", $"{enthalpy.SiValue:0} J/kg"));
        }

        var kelvin = ReferenceTemperature
            + ((Math.Sqrt(discriminant) - SpecificHeatAtReference) / SpecificHeatSlope);

        return OutOfRange(kelvin, Absolute(gaugePressure)) is { } failure
            ? Result.Failure<FluidState>(failure)
            : Result.Success(At(gaugePressure, kelvin));
    }

    /// <inheritdoc/>
    public override Result<Quantity> FreezingPoint(Quantity gaugePressure) =>
        Result.Success(Quantity.FromSi(273.15, Dimension.Temperature));

    /// <inheritdoc/>
    public override Result<Quantity> SaturationPressure(Quantity temperature) =>
        ConstantPropertyWater.Instance.SaturationPressure(temperature);

    /// <summary>The specific heat at a temperature.</summary>
    /// <param name="kelvin">K.</param>
    /// <returns>J/(kg·K).</returns>
    public static double SpecificHeatAt(double kelvin) =>
        SpecificHeatAtReference + (SpecificHeatSlope * (kelvin - ReferenceTemperature));

    /// <summary>The specific enthalpy at a temperature, relative to <see cref="ReferenceTemperature"/>.</summary>
    /// <param name="kelvin">K.</param>
    /// <returns>
    /// J/kg, negative below the reference. The integral of <see cref="SpecificHeatAt"/>, so
    /// <c>dh/dT</c> is the specific heat at every point rather than only at the reference.
    /// </returns>
    public static double EnthalpyAt(double kelvin)
    {
        var x = kelvin - ReferenceTemperature;

        return (SpecificHeatAtReference * x) + (SpecificHeatSlope * x * x / 2);
    }

    private FluidState At(Quantity gaugePressure, double kelvin)
    {
        var x = kelvin - ReferenceTemperature;

        return new FluidState
        {
            Substance = this,
            Pressure = gaugePressure,
            Temperature = Quantity.FromSi(kelvin, Dimension.Temperature),
            Enthalpy = Quantity.FromSi(EnthalpyAt(kelvin), Dimension.Enthalpy),
            Density = Quantity.FromSi(998.2 - (0.45 * x), Dimension.Density),
            DynamicViscosity = Quantity.FromSi(
                Math.Max(1e-4, 1.002e-3 - (8e-6 * x)), FluidDimensions.DynamicViscosity),
            SpecificHeat = Quantity.FromSi(SpecificHeatAt(kelvin), Dimension.SpecificHeat),
            ThermalConductivity = Quantity.FromSi(
                0.598 + (0.0012 * x), FluidDimensions.ThermalConductivity),
            Phase = Phase.Liquid,
        };
    }
}
