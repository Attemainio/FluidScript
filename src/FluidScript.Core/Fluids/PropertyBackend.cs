using SharpProp;

using UnitsNet;

namespace FluidScript.Core.Fluids;

/// <summary>Raw property measurements, in SI, from the property backend.</summary>
/// <param name="Temperature">K.</param>
/// <param name="Enthalpy">J/kg of whatever the caller asked about.</param>
/// <param name="Density">kg/m³.</param>
/// <param name="DynamicViscosity">Pa·s.</param>
/// <param name="SpecificHeat">J/(kg·K).</param>
/// <param name="ThermalConductivity">W/(m·K).</param>
/// <param name="Phase">The phase the backend reported.</param>
internal readonly record struct BackendState(
    double Temperature,
    double Enthalpy,
    double Density,
    double DynamicViscosity,
    double SpecificHeat,
    double ThermalConductivity,
    Phase Phase);

/// <summary>Psychrometric measurements, in SI, from the property backend.</summary>
/// <param name="Temperature">Dry-bulb, K.</param>
/// <param name="DryAirBasisEnthalpy">J per kg of dry air.</param>
/// <param name="Density">kg of moist air per m³.</param>
/// <param name="DynamicViscosity">Pa·s.</param>
/// <param name="SpecificHeat">J/(kg·K).</param>
/// <param name="ThermalConductivity">W/(m·K).</param>
/// <param name="HumidityRatio">kg water per kg dry air.</param>
/// <param name="RelativeHumidity">A fraction from 0 to 1.</param>
/// <param name="WetBulb">K.</param>
/// <param name="DewPoint">K.</param>
internal readonly record struct BackendHumidAirState(
    double Temperature,
    double DryAirBasisEnthalpy,
    double Density,
    double DynamicViscosity,
    double SpecificHeat,
    double ThermalConductivity,
    double HumidityRatio,
    double RelativeHumidity,
    double WetBulb,
    double DewPoint);

/// <summary>
/// The one type in Core that references the property package, and therefore the whole of its blast
/// radius.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Its name says what it does rather than which package it wraps</strong>, and the
/// architecture test is what asked for that: it searches <c>src/</c> for the string <c>SharpProp</c>,
/// so a type called <c>SharpPropBackend</c> put the package's name into every file that called it and
/// tripped the very rule it exists to satisfy. A blunt guard, and right — if one file is meant to own
/// the dependency, no other file should have reason to name it.
/// </para>
/// <para>
/// It measures and nothing else: absolute pressures in, SI doubles out, no <see cref="Quantity"/>, no
/// range checking, no diagnostics, no domain vocabulary. Everything a caller has to <em>decide</em> —
/// which atmosphere to add, whether the state is inside the validated domain, what to report — belongs
/// to the substances above it, which reference no package at all. An
/// architecture test asserts this file is the only one under <c>src/</c> naming SharpProp.
/// </para>
/// <para>
/// <strong>It returns <see langword="null"/> rather than throwing, and the M0 spike is why.</strong>
/// Below the melting line the backend throws; above its upper bound it returns a plausible number
/// without complaint. Neither can be relied on as a range check, so the caller checks first and this
/// catches whatever still escapes.
/// </para>
/// <para>
/// <strong>One <c>Fluid</c> is shared, because constructing one per call costs more than the
/// measurement does.</strong> On a debug build, a state fixed on a fresh instance took 535 µs and the
/// same state on a shared one 336 µs — the constructor was 37 % of the call. The M0 spike had already
/// measured that <c>WithState</c> on a shared instance is safe across threads: it returns a new
/// instance rather than mutating the receiver.
/// </para>
/// <para>
/// <strong>Fixing a state is expensive and reading one is free</strong>, which is what shapes
/// everything above. Measured on the same build: <c>(T, ρ)</c> 321 µs, <c>(p, T)</c> 336 µs and
/// <c>(p, h)</c> 388 µs to fix, then 0.003 µs to read a property off the result and 0.025 µs to read
/// all seven. CoolProp's own documentation gives the reason — "the equations of state are based on T
/// and ρ as state variables, so T, ρ will always be the fastest inputs", and "P,T will be a bit
/// slower (3-10 times), followed by input pairs where neither T nor ρ are specified, like P,H".
/// Those ratios are about the flash; here they are nearly hidden by the ~320 µs SharpProp charges per
/// <c>WithState</c> whatever the pair. Two consequences: <see cref="FluidState"/> reads every property
/// at once rather than lazily, and <c>21</c>'s per-solve cache is a requirement rather than an
/// optimisation.
/// </para>
/// </remarks>
internal static class PropertyBackend
{
    private static readonly Fluid SharedWater = new(FluidsList.Water);
    private static readonly HumidAir SharedAir = new();

    /// <summary>Measures water at an absolute pressure and a temperature.</summary>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <param name="temperature">K.</param>
    /// <returns>The measurements, or <see langword="null"/> when the backend could not take them.</returns>
    public static BackendState? WaterFromPressureTemperature(double absolutePressure, double temperature)
    {
        try
        {
            return Read(SharedWater.WithState(
                Input.Pressure(Pressure.FromPascals(absolutePressure)),
                Input.Temperature(UnitsNet.Temperature.FromKelvins(temperature))));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Measures water at an absolute pressure and a specific enthalpy.</summary>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <param name="enthalpy">J/kg.</param>
    /// <returns>The measurements, or <see langword="null"/> when the backend could not take them.</returns>
    public static BackendState? WaterFromPressureEnthalpy(double absolutePressure, double enthalpy)
    {
        try
        {
            return Read(SharedWater.WithState(
                Input.Pressure(Pressure.FromPascals(absolutePressure)),
                Input.Enthalpy(SpecificEnergy.FromJoulesPerKilogram(enthalpy))));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Finds water's saturation pressure at a temperature.</summary>
    /// <param name="temperature">K.</param>
    /// <returns>Pa absolute, or <see langword="null"/> when the backend could not say.</returns>
    public static double? WaterSaturationPressure(double temperature)
    {
        try
        {
            return SharedWater.WithState(
                Input.Temperature(UnitsNet.Temperature.FromKelvins(temperature)),
                Input.Quality(Ratio.FromPercent(0))).Pressure.Pascals;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Finds water's boiling temperature at an absolute pressure.</summary>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <returns>K, or <see langword="null"/> when the backend could not say.</returns>
    /// <remarks>
    /// The upper edge of the liquid domain, which a rectangular temperature bound does not describe:
    /// it moves from 99.61 °C at 100 kPa absolute to 179.88 °C at 1000 kPa.
    /// </remarks>
    public static double? WaterSaturationTemperature(double absolutePressure)
    {
        try
        {
            return SharedWater.WithState(
                Input.Pressure(Pressure.FromPascals(absolutePressure)),
                Input.Quality(Ratio.FromPercent(0))).Temperature.Kelvins;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Measures humid air from pressure, dry bulb and one humidity input.</summary>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <param name="dryBulb">K.</param>
    /// <param name="humidity">The humidity ratio, or the relative humidity as a fraction.</param>
    /// <param name="humidityIsRelative">
    /// <see langword="true"/> when <paramref name="humidity"/> is a relative humidity.
    /// </param>
    /// <returns>The measurements, or <see langword="null"/> when the backend could not take them.</returns>
    public static BackendHumidAirState? HumidAirFromTemperature(
        double absolutePressure, double dryBulb, double humidity, bool humidityIsRelative)
    {
        try
        {
            return Read((SharpProp.IHumidAir)SharedAir.WithState(
                InputHumidAir.Pressure(Pressure.FromPascals(absolutePressure)),
                InputHumidAir.Temperature(UnitsNet.Temperature.FromKelvins(dryBulb)),
                humidityIsRelative
                    ? InputHumidAir.RelativeHumidity(UnitsNet.RelativeHumidity.FromPercent(humidity * 100))
                    : InputHumidAir.Humidity(Ratio.FromDecimalFractions(humidity))));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Measures humid air from pressure, dry-air-basis enthalpy and humidity ratio.</summary>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <param name="dryAirBasisEnthalpy">J per kg of dry air.</param>
    /// <param name="humidityRatio">kg water per kg dry air.</param>
    /// <returns>The measurements, or <see langword="null"/> when the backend could not take them.</returns>
    public static BackendHumidAirState? HumidAirFromEnthalpy(
        double absolutePressure, double dryAirBasisEnthalpy, double humidityRatio)
    {
        try
        {
            return Read((SharpProp.IHumidAir)SharedAir.WithState(
                InputHumidAir.Pressure(Pressure.FromPascals(absolutePressure)),
                InputHumidAir.Enthalpy(SpecificEnergy.FromJoulesPerKilogram(dryAirBasisEnthalpy)),
                InputHumidAir.Humidity(Ratio.FromDecimalFractions(humidityRatio))));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    private static BackendState Read(IFluid fluid) =>
        new(fluid.Temperature.Kelvins,
            fluid.Enthalpy.JoulesPerKilogram,
            fluid.Density.KilogramsPerCubicMeter,
            fluid.DynamicViscosity?.PascalSeconds ?? double.NaN,
            fluid.SpecificHeat.JoulesPerKilogramKelvin,
            fluid.Conductivity?.WattsPerMeterKelvin ?? double.NaN,
            PhaseOf(fluid.Phase));

    private static BackendHumidAirState Read(SharpProp.IHumidAir air) =>
        new(air.Temperature.Kelvins,
            air.Enthalpy.JoulesPerKilogram,
            air.Density.KilogramsPerCubicMeter,
            air.DynamicViscosity.PascalSeconds,
            air.SpecificHeat.JoulesPerKilogramKelvin,
            air.Conductivity.WattsPerMeterKelvin,
            air.Humidity.DecimalFractions,
            air.RelativeHumidity.Percent / 100,
            air.WetBulbTemperature.Kelvins,
            air.DewTemperature.Kelvins);

    private static Phase PhaseOf(Phases phase) => phase switch
    {
        Phases.Liquid or Phases.SupercriticalLiquid => Fluids.Phase.Liquid,
        Phases.Gas or Phases.SupercriticalGas => Fluids.Phase.Gas,
        Phases.TwoPhase => Fluids.Phase.TwoPhase,
        Phases.Supercritical => Fluids.Phase.Supercritical,
        _ => Fluids.Phase.Unknown,
    };
}
