using FluidScript.Core.Units;

namespace FluidScript.Core.Fluids;

/// <summary>The two dimensions a fluid property has that the language never names.</summary>
/// <remarks>
/// Neither viscosity nor thermal conductivity can be written in a script, so neither earns an entry in
/// <c>13</c>'s closed <see cref="DimensionId"/> set — adding one would be a language change made to
/// express a value the language cannot express. They are built from their exponent vectors instead,
/// which is exactly what <see cref="Dimension.FromVector"/> is for and which keeps the dimensional
/// algebra over them correct: dividing a conductivity by a viscosity gives a real vector, not an
/// error.
/// </remarks>
public static class FluidDimensions
{
    /// <summary>Gets the dimension of a dynamic viscosity, Pa·s.</summary>
    /// <value><c>M L⁻¹ T⁻¹</c>.</value>
    public static Dimension DynamicViscosity { get; } = Dimension.FromVector(new DimensionVector(1, -1, -1, 0));

    /// <summary>Gets the dimension of a thermal conductivity, W/(m·K).</summary>
    /// <value><c>M L T⁻³ Θ⁻¹</c>.</value>
    public static Dimension ThermalConductivity { get; } = Dimension.FromVector(new DimensionVector(1, 1, -3, -1));
}

/// <summary>A substance whose thermodynamic properties can be evaluated.</summary>
/// <remarks>
/// <para>
/// Core depends on this and on nothing beneath it. Exactly one type in Core touches the property
/// backend, for three reasons in order of how much they matter: property calls dominate every solver
/// iteration and a constant-property double makes component tests run in microseconds; humid air,
/// incompressible approximations and future mixtures all satisfy one shape; and a backend's packaging
/// is a risk best confined to one file.
/// </para>
/// <para>
/// <strong>Every method returns <see cref="Result{T}"/> and none throws for a state that does not
/// exist.</strong> A solver overshoots during iteration and asks for something impossible, then
/// backtracks — that is a normal run, not an error, and a failure there emits no diagnostic.
/// </para>
/// </remarks>
public interface ISubstance
{
    /// <summary>Gets the name a script writes, such as <c>water</c>.</summary>
    string Name { get; }

    /// <summary>Gets the domain this substance's data is validated over.</summary>
    /// <remarks>
    /// Checked before every call, so a caller gets a failure rather than a silently extrapolated
    /// number. The backend cannot be relied on for this: above its upper bound CoolProp returns a
    /// number without complaint.
    /// </remarks>
    StateRange ValidRange { get; }

    /// <summary>Fixes a state from pressure and temperature.</summary>
    /// <param name="gaugePressure">
    /// The pressure, as a <see cref="Dimension.Pressure"/> quantity — which is gauge by definition in
    /// <c>13</c>, and is what the whole model stores. Adding the atmosphere is the implementation's
    /// job, not the caller's.
    /// </param>
    /// <param name="temperature">The temperature, as a <see cref="Dimension.Temperature"/> quantity.</param>
    /// <returns>The state, or why the pair does not describe one.</returns>
    Result<FluidState> FromPressureTemperature(Quantity gaugePressure, Quantity temperature);

    /// <summary>Fixes a state from pressure and specific enthalpy.</summary>
    /// <param name="gaugePressure">The pressure, gauge, as above.</param>
    /// <param name="enthalpy">The specific enthalpy, J per kg of fluid.</param>
    /// <returns>The state, or why the pair does not describe one.</returns>
    /// <remarks>
    /// The pair the solver uses. An energy balance produces an enthalpy, and going back through
    /// temperature would mean inverting <c>cp</c> — which is what a property backend does correctly
    /// and a hand-rolled inversion does not.
    /// </remarks>
    Result<FluidState> FromPressureEnthalpy(Quantity gaugePressure, Quantity enthalpy);

    /// <summary>Finds the freezing point at a pressure.</summary>
    /// <param name="gaugePressure">The pressure, gauge.</param>
    /// <returns>The freezing temperature, in K, or why it is unknown.</returns>
    Result<Quantity> FreezingPoint(Quantity gaugePressure);

    /// <summary>Finds the saturation pressure at a temperature.</summary>
    /// <param name="temperature">The temperature.</param>
    /// <returns>
    /// The saturation pressure as a gauge quantity, or why it is unknown. Backs the boiling and
    /// cavitation checks, which compare it against a gauge pressure the model holds.
    /// </returns>
    Result<Quantity> SaturationPressure(Quantity temperature);
}

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

/// <summary>Resolves the name a script writes to the substance behind it.</summary>
public interface ISubstanceRegistry
{
    /// <summary>Gets every registered name, in order, for a diagnostic that lists them.</summary>
    System.Collections.Immutable.ImmutableArray<string> Names { get; }

    /// <summary>Resolves a script name.</summary>
    /// <param name="name">The name as written, such as <c>water</c>.</param>
    /// <returns>The substance, or <c>FS2001</c> listing what is available.</returns>
    Result<ISubstance> Resolve(string name);
}
