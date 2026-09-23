using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Physics.Fluids;

/// <summary>A humid-air state, adding the psychrometric properties to the common set.</summary>
/// <remarks>
/// <strong>Its enthalpy is per kg of dry air</strong>, which is the psychrometric convention and is
/// unlike every other substance in the model. An air-side energy balance written as though the two
/// bases were the same is wrong by the humidity ratio — a few percent, small enough to look like a
/// modelling choice rather than a defect. The name says so, and <see cref="IThermodynamicState"/>
/// deliberately has no enthalpy member for a caller to reach it through.
/// </remarks>
public sealed record HumidAirState : IThermodynamicState
{
    /// <summary>Gets the substance this state belongs to.</summary>
    public required ISubstance Substance { get; init; }

    /// <summary>Gets the gauge pressure.</summary>
    /// <value>Pa relative to the standard atmosphere, positive above it.</value>
    public required Quantity Pressure { get; init; }

    /// <summary>Gets the dry-bulb temperature.</summary>
    /// <value>K absolute.</value>
    public required Quantity Temperature { get; init; }

    /// <summary>Gets the density of the moist air.</summary>
    /// <value>
    /// kg of moist air per m³, always positive. <strong>Not dry air at the same state</strong>, which
    /// is what a reference table is far more likely to hand you: at 25 °C and 50 % RH that is
    /// 1.184 kg/m³ against this figure's 1.177, and moist air is the lighter of the two because water
    /// vapour is lighter than the air it displaces.
    /// </value>
    public required Quantity Density { get; init; }

    /// <summary>Gets the dynamic viscosity.</summary>
    /// <value>Pa·s, always positive.</value>
    public required Quantity DynamicViscosity { get; init; }

    /// <summary>Gets the specific heat at constant pressure.</summary>
    /// <value>J/(kg·K), always positive.</value>
    public required Quantity SpecificHeat { get; init; }

    /// <summary>Gets the thermal conductivity.</summary>
    /// <value>W/(m·K), always positive.</value>
    public required Quantity ThermalConductivity { get; init; }

    /// <summary>Gets the phase at this state.</summary>
    public Phase Phase { get; init; } = Phase.Gas;

    /// <summary>Gets the humidity ratio.</summary>
    /// <value>kg of water per kg of dry air, never negative.</value>
    public required Quantity HumidityRatio { get; init; }

    /// <summary>Gets the relative humidity.</summary>
    /// <value>A fraction from 0 to 1, not a percentage.</value>
    public required Quantity RelativeHumidity { get; init; }

    /// <summary>Gets the wet-bulb temperature.</summary>
    /// <value>K absolute.</value>
    public required Quantity WetBulb { get; init; }

    /// <summary>Gets the dew-point temperature.</summary>
    /// <value>K absolute. Backs the condensation warnings.</value>
    public required Quantity DewPoint { get; init; }

    /// <summary>Gets the specific enthalpy, per kg of <strong>dry air</strong>.</summary>
    /// <value>
    /// J per kg of dry air, never per kg of mixture. At 25 °C and 50 % RH the two differ by 0.3 %,
    /// which is inside the tolerance of the right answer — so a basis error here cannot be caught by
    /// eye at all, only by a test that asserts the basis rather than inspecting a number.
    /// </value>
    public required Quantity DryAirBasisEnthalpy { get; init; }

    /// <summary>Gets the specific entropy, per kg of dry air.</summary>
    /// <value>J/(kg dry air·K). The same basis as <see cref="DryAirBasisEnthalpy"/>, and named for it.</value>
    public required Quantity DryAirBasisEntropy { get; init; }
}
