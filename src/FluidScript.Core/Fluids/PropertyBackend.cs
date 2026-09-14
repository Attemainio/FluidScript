using SharpProp;

using UnitsNet;

namespace FluidScript.Core.Fluids;

/// <summary>Raw property measurements, in SI, from the property backend.</summary>
/// <param name="Temperature">K.</param>
/// <param name="Enthalpy">J/kg of whatever the caller asked about.</param>
/// <param name="Entropy">J/(kg·K) of the same.</param>
/// <param name="Density">kg/m³.</param>
/// <param name="DynamicViscosity">Pa·s.</param>
/// <param name="SpecificHeat">J/(kg·K).</param>
/// <param name="ThermalConductivity">W/(m·K).</param>
/// <param name="Phase">The phase the backend reported.</param>
internal readonly record struct BackendState(
    double Temperature,
    double Enthalpy,
    double Entropy,
    double Density,
    double DynamicViscosity,
    double SpecificHeat,
    double ThermalConductivity,
    Phase Phase);

/// <summary>Psychrometric measurements, in SI, from the property backend.</summary>
/// <param name="Temperature">Dry-bulb, K.</param>
/// <param name="DryAirBasisEnthalpy">J per kg of dry air.</param>
/// <param name="DryAirBasisEntropy">J per kg of dry air per K, the same basis.</param>
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
    double DryAirBasisEntropy,
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
/// <strong>One <c>Fluid</c> per thread, updated in place — never <c>WithState</c>.</strong> <c>WithState</c>
/// returns a new instance, and every instance owns a native CoolProp state of about 540 KB that the
/// managed heap knows nothing about: 20 000 evaluations measured +10.8 GB of working set, and disposing
/// the wrapper freed none of it. A Newton solve reads a state at every node on every one of its N+1
/// residual sweeps, so the 30-consumer header took the machine to 31 GB in seconds and the kernel
/// killed the process (<c>C-76</c>). <c>Update</c> on one instance measured +0 MB over the same 20 000
/// calls and five times the speed, and it recomputes every property — SharpProp clears its lazy cache
/// on update, and a rejected state leaves the instance ready for the next. The instance is thread-static
/// because updating is a mutation and the API solves concurrently; the earlier sharing of a single
/// static across threads was only ever safe because <c>WithState</c> cloned, which is the leak.
/// <strong>Humid air is the exception, and measured to be one.</strong> CoolProp's psychrometrics
/// (<c>HAPropsSI</c>) are a stateless function of their inputs; a <c>HumidAir</c> owns no native
/// state, so its <c>WithState</c> clones a few managed fields and nothing else. Two thousand reads
/// measure no working-set growth (<c>NativeMemoryTests</c>), and a stateless clone is what makes the
/// one shared static safe across threads. It stays on <c>WithState</c> for that reason, not by
/// oversight; migrate it if a future SharpProp gives it native state.
/// </para>
/// <para>
/// <strong>Fixing a state is expensive and reading one is free</strong>, which is what shapes
/// everything above. Measured on the same build: <c>(T, ρ)</c> 321 µs, <c>(p, T)</c> 336 µs and
/// <c>(p, h)</c> 388 µs to fix, then 0.003 µs to read a property off the result and 0.025 µs to read
/// all seven. CoolProp's own documentation gives the reason — "the equations of state are based on T
/// and ρ as state variables, so T, ρ will always be the fastest inputs", and "P,T will be a bit
/// slower (3-10 times), followed by input pairs where neither T nor ρ are specified, like P,H".
/// Those ratios are about the flash; most of the ~320 µs was the clone, and an in-place update
/// measures 64 µs for a <c>(p, h)</c> fix and three reads. Two consequences stand: <see cref="FluidState"/>
/// reads every property at once rather than lazily, and <c>21</c>'s per-solve cache is a requirement
/// rather than an optimisation.
/// </para>
/// </remarks>
internal static class PropertyBackend
{
    [ThreadStatic]
    private static Fluid? water;

    [ThreadStatic]
    private static Dictionary<RefrigerantKind, Fluid>? refrigerants;

    private static readonly HumidAir SharedAir = new();

    /// <summary>This thread's water instance, constructed on first use.</summary>
    private static Fluid Water => water ??= new Fluid(FluidsList.Water);


    /// <summary>Measures water at an absolute pressure and a temperature.</summary>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <param name="temperature">K.</param>
    /// <returns>The measurements, or <see langword="null"/> when the backend could not take them.</returns>
    public static BackendState? WaterFromPressureTemperature(double absolutePressure, double temperature)
    {
        try
        {
            var fluid = Water;

            fluid.Update(
                Input.Pressure(Pressure.FromPascals(absolutePressure)),
                Input.Temperature(UnitsNet.Temperature.FromKelvins(temperature)));

            return Read(fluid);
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
            var fluid = Water;

            fluid.Update(
                Input.Pressure(Pressure.FromPascals(absolutePressure)),
                Input.Enthalpy(SpecificEnergy.FromJoulesPerKilogram(enthalpy)));

            return Read(fluid);
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
            var fluid = Water;

            fluid.Update(
                Input.Temperature(UnitsNet.Temperature.FromKelvins(temperature)),
                Input.Quality(Ratio.FromPercent(0)));

            return fluid.Pressure.Pascals;
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
            var fluid = Water;

            fluid.Update(
                Input.Pressure(Pressure.FromPascals(absolutePressure)),
                Input.Quality(Ratio.FromPercent(0)));

            return fluid.Temperature.Kelvins;
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

    /// <summary>Measures water at an absolute pressure and a specific entropy.</summary>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <param name="entropy">J/(kg·K).</param>
    /// <returns>The measurements, or <see langword="null"/> when the backend could not take them.</returns>
    public static BackendState? WaterFromPressureEntropy(double absolutePressure, double entropy)
    {
        try
        {
            var fluid = Water;

            fluid.Update(
                Input.Pressure(Pressure.FromPascals(absolutePressure)),
                Input.Entropy(SpecificEntropy.FromJoulesPerKilogramKelvin(entropy)));

            return Read(fluid);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Measures a refrigerant at an absolute pressure and a temperature.</summary>
    /// <param name="kind">Which refrigerant.</param>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <param name="temperature">K.</param>
    /// <returns>The measurements, or <see langword="null"/> when the backend could not take them.</returns>
    public static BackendState? RefrigerantFromPressureTemperature(
        RefrigerantKind kind, double absolutePressure, double temperature)
    {
        try
        {
            var fluid = Shared(kind);

            fluid.Update(
                Input.Pressure(Pressure.FromPascals(absolutePressure)),
                Input.Temperature(UnitsNet.Temperature.FromKelvins(temperature)));

            return Read(fluid);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Measures a refrigerant at an absolute pressure and a specific enthalpy.</summary>
    /// <param name="kind">Which refrigerant.</param>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <param name="enthalpy">J/kg.</param>
    /// <returns>The measurements, or <see langword="null"/> when the backend could not take them.</returns>
    /// <remarks>
    /// The one pair that reaches inside the two-phase dome, where it stays well posed while a pressure
    /// and a temperature would not (<c>D-78</c>).
    /// </remarks>
    public static BackendState? RefrigerantFromPressureEnthalpy(
        RefrigerantKind kind, double absolutePressure, double enthalpy)
    {
        try
        {
            var fluid = Shared(kind);

            fluid.Update(
                Input.Pressure(Pressure.FromPascals(absolutePressure)),
                Input.Enthalpy(SpecificEnergy.FromJoulesPerKilogram(enthalpy)));

            return Read(fluid);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Measures a refrigerant at an absolute pressure and a specific entropy.</summary>
    /// <param name="kind">Which refrigerant.</param>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <param name="entropy">J/(kg·K).</param>
    /// <returns>The measurements, or <see langword="null"/> when the backend could not take them.</returns>
    public static BackendState? RefrigerantFromPressureEntropy(
        RefrigerantKind kind, double absolutePressure, double entropy)
    {
        try
        {
            var fluid = Shared(kind);

            fluid.Update(
                Input.Pressure(Pressure.FromPascals(absolutePressure)),
                Input.Entropy(SpecificEntropy.FromJoulesPerKilogramKelvin(entropy)));

            return Read(fluid);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Finds a refrigerant's saturation pressure at a temperature.</summary>
    /// <param name="kind">Which refrigerant.</param>
    /// <param name="temperature">K.</param>
    /// <returns>Pa absolute, or <see langword="null"/> when the backend could not say.</returns>
    public static double? RefrigerantSaturationPressure(RefrigerantKind kind, double temperature)
    {
        try
        {
            var fluid = Shared(kind);

            fluid.Update(
                Input.Temperature(UnitsNet.Temperature.FromKelvins(temperature)),
                Input.Quality(Ratio.FromPercent(0)));

            return fluid.Pressure.Pascals;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Finds a refrigerant's saturation temperature at an absolute pressure.</summary>
    /// <param name="kind">Which refrigerant.</param>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <returns>K, or <see langword="null"/> when the backend could not say.</returns>
    public static double? RefrigerantSaturationTemperature(RefrigerantKind kind, double absolutePressure)
    {
        try
        {
            var fluid = Shared(kind);

            fluid.Update(
                Input.Pressure(Pressure.FromPascals(absolutePressure)),
                Input.Quality(Ratio.FromPercent(0)));

            return fluid.Temperature.Kelvins;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Reads a refrigerant's saturated liquid and vapour enthalpies at a pressure.</summary>
    /// <param name="kind">Which refrigerant.</param>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <returns>J/kg for each, or <see langword="null"/> when the backend could not say.</returns>
    /// <remarks>
    /// The two numbers that split a condenser into its zones: everything above the vapour value is
    /// desuperheat, everything between them is latent, everything below the liquid value is subcool.
    /// <c>D-80</c> sizes on the resulting shares, so they are measured here rather than estimated from
    /// a specific heat.
    /// </remarks>
    public static (double Liquid, double Vapour)? RefrigerantSaturationEnthalpies(
        RefrigerantKind kind, double absolutePressure)
    {
        try
        {
            var fluid = Shared(kind);
            var pressure = Input.Pressure(Pressure.FromPascals(absolutePressure));

            fluid.Update(pressure, Input.Quality(Ratio.FromPercent(0)));
            var liquid = fluid.Enthalpy.JoulesPerKilogram;

            fluid.Update(pressure, Input.Quality(Ratio.FromPercent(100)));

            return (liquid, fluid.Enthalpy.JoulesPerKilogram);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Reads a refrigerant's critical point.</summary>
    /// <param name="kind">Which refrigerant.</param>
    /// <returns>K and Pa absolute, or <see langword="null"/> when the backend could not say.</returns>
    /// <remarks>
    /// The line between a cycle with a condensing temperature and one without, which is a difference of
    /// one unknown rather than one correlation (<c>D-81</c>).
    /// </remarks>
    public static (double Temperature, double Pressure)? RefrigerantCriticalPoint(RefrigerantKind kind)
    {
        try
        {
            var fluid = Shared(kind);

            return fluid.CriticalTemperature is { } temperature && fluid.CriticalPressure is { } pressure
                ? (temperature.Kelvins, pressure.Pascals)
                : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Gets this thread's instance for one refrigerant.</summary>
    /// <param name="kind">Which refrigerant.</param>
    /// <returns>The instance, constructed once per kind per thread.</returns>
    /// <remarks>
    /// One instance per kind for the reason the water one is kept: the constructor was 37 % of a
    /// measurement. Per thread because it is updated in place (<c>C-76</c>).
    /// </remarks>
    private static Fluid Shared(RefrigerantKind kind)
    {
        refrigerants ??= [];

        if (!refrigerants.TryGetValue(kind, out var fluid))
        {
            fluid = new Fluid(kind switch
            {
                RefrigerantKind.Ammonia => FluidsList.Ammonia,
                RefrigerantKind.Propane => FluidsList.nPropane,
                _ => FluidsList.CarbonDioxide,
            });
            refrigerants[kind] = fluid;
        }

        return fluid;
    }

    private static BackendState Read(Fluid fluid) =>
        new(fluid.Temperature.Kelvins,
            fluid.Enthalpy.JoulesPerKilogram,
            fluid.Entropy.JoulesPerKilogramKelvin,
            fluid.Density.KilogramsPerCubicMeter,
            fluid.DynamicViscosity?.PascalSeconds ?? double.NaN,
            fluid.SpecificHeat.JoulesPerKilogramKelvin,
            fluid.Conductivity?.WattsPerMeterKelvin ?? double.NaN,
            PhaseOf(fluid.Phase));

    private static BackendHumidAirState Read(SharpProp.IHumidAir air) =>
        new(air.Temperature.Kelvins,
            air.Enthalpy.JoulesPerKilogram,
            air.Entropy.JoulesPerKilogramKelvin,
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
