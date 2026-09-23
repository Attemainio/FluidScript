using FluidScript.Core.Physics.Units;
using FluidScript.Core.Primitives;

namespace FluidScript.Core.Physics.Fluids;

/// <summary>Humid air, which needs three independent properties rather than two.</summary>
/// <remarks>
/// Pressure, one temperature-like property and one humidity-like property fix the state, so humid air
/// cannot be described by <see cref="ISubstance"/>'s two-property methods alone. It still <em>is</em>
/// a substance — it has a name, a validated range, and a freezing point — so it extends rather than
/// replaces the interface, and the two-property members below fix the state at zero humidity.
/// </remarks>
public interface IHumidAir : ISubstance
{
    /// <summary>Fixes a state from pressure, dry-bulb temperature and humidity ratio.</summary>
    /// <param name="gaugePressure">The pressure, gauge.</param>
    /// <param name="dryBulb">The dry-bulb temperature.</param>
    /// <param name="humidityRatio">kg of water per kg of dry air.</param>
    /// <returns>The state, or why it does not exist.</returns>
    Result<HumidAirState> FromPressureTemperatureHumidity(
        Quantity gaugePressure, Quantity dryBulb, Quantity humidityRatio);

    /// <summary>Fixes a state from pressure, dry-bulb temperature and relative humidity.</summary>
    /// <param name="gaugePressure">The pressure, gauge.</param>
    /// <param name="dryBulb">The dry-bulb temperature.</param>
    /// <param name="relativeHumidity">A fraction from 0 to 1, not a percentage.</param>
    /// <returns>The state, or why it does not exist.</returns>
    Result<HumidAirState> FromPressureTemperatureRelativeHumidity(
        Quantity gaugePressure, Quantity dryBulb, Quantity relativeHumidity);

    /// <summary>Fixes a state from pressure, enthalpy and humidity ratio.</summary>
    /// <param name="gaugePressure">The pressure, gauge.</param>
    /// <param name="dryAirBasisEnthalpy">
    /// The specific enthalpy, J per kg of <strong>dry air</strong>. Passing a per-kg-of-mixture value
    /// here is wrong by the humidity ratio and will not be detected.
    /// </param>
    /// <param name="humidityRatio">kg of water per kg of dry air.</param>
    /// <returns>The state, or why it does not exist.</returns>
    Result<HumidAirState> FromPressureEnthalpyHumidity(
        Quantity gaugePressure, Quantity dryAirBasisEnthalpy, Quantity humidityRatio);
}
