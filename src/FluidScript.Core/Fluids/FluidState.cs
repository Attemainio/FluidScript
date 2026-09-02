using FluidScript.Core.Units;

namespace FluidScript.Core.Fluids;

/// <summary>What phase a substance is in at a state.</summary>
public enum Phase
{
    /// <summary>The backend did not say, or the substance has no phase behaviour worth naming.</summary>
    Unknown = 0,

    /// <summary>Liquid — the only phase a v1 hydronic circuit is validated over.</summary>
    Liquid,

    /// <summary>Gas or vapour.</summary>
    Gas,

    /// <summary>Two phases at once, which no v1 component models.</summary>
    TwoPhase,

    /// <summary>Above the critical point.</summary>
    Supercritical,

    /// <summary>Solid.</summary>
    Solid,
}

/// <summary>The domain a substance's data is valid over (<c>07</c>'s engineering validity matrix).</summary>
/// <param name="MinimumTemperature">The coldest validated temperature, in K.</param>
/// <param name="MaximumTemperature">The warmest validated temperature, in K.</param>
/// <param name="MinimumAbsolutePressure">The lowest validated pressure, in Pa <strong>absolute</strong>.</param>
/// <param name="MaximumAbsolutePressure">The highest validated pressure, in Pa absolute.</param>
/// <remarks>
/// <para>
/// Pressures here are absolute, unlike everywhere else in the model, because that is how <c>07</c>
/// states the domain — "100–1000 kPa absolute" — and converting the bound instead of the value would
/// put the atmosphere in two places.
/// </para>
/// <para>
/// <strong>The range has to be checked here rather than left to the backend.</strong> The M0 spike
/// measured what CoolProp does at the edges: below the melting line it throws, and above the upper
/// bound it returns a number. Water at 5000 °C comes back with a density and no complaint, and a
/// silently extrapolated property is indistinguishable from a good one at the call site.
/// </para>
/// </remarks>
public readonly record struct StateRange(
    double MinimumTemperature,
    double MaximumTemperature,
    double MinimumAbsolutePressure,
    double MaximumAbsolutePressure)
{
    /// <summary>Determines whether a state lies inside the validated domain.</summary>
    /// <param name="temperature">The temperature, in K.</param>
    /// <param name="absolutePressure">The pressure, in Pa absolute.</param>
    /// <returns><see langword="true"/> when both lie within their bounds, inclusive.</returns>
    public bool Contains(double temperature, double absolutePressure) =>
        temperature >= MinimumTemperature
        && temperature <= MaximumTemperature
        && absolutePressure >= MinimumAbsolutePressure
        && absolutePressure <= MaximumAbsolutePressure;
}

/// <summary>The properties every state has, whatever the substance and whatever its enthalpy basis.</summary>
/// <remarks>
/// Deliberately excludes enthalpy. Humid-air enthalpy is per kg of <em>dry air</em> and every other
/// substance's is per kg of fluid, so a caller holding a base-class reference must not be able to read
/// "the enthalpy" and get one of two incompatible things. Every member below is basis-independent.
/// </remarks>
public interface IThermodynamicState
{
    /// <summary>Gets the substance this state belongs to.</summary>
    ISubstance Substance { get; }

    /// <summary>Gets the gauge pressure.</summary>
    /// <value>Pa relative to the standard atmosphere, positive above it (<c>D-26</c>).</value>
    Quantity Pressure { get; }

    /// <summary>Gets the temperature.</summary>
    /// <value>K absolute.</value>
    Quantity Temperature { get; }

    /// <summary>Gets the density.</summary>
    /// <value>kg/m³, always positive.</value>
    Quantity Density { get; }

    /// <summary>Gets the dynamic viscosity.</summary>
    /// <value>Pa·s, always positive.</value>
    Quantity DynamicViscosity { get; }

    /// <summary>Gets the specific heat at constant pressure.</summary>
    /// <value>J/(kg·K), always positive.</value>
    Quantity SpecificHeat { get; }

    /// <summary>Gets the thermal conductivity.</summary>
    /// <value>W/(m·K), always positive.</value>
    Quantity ThermalConductivity { get; }

    /// <summary>Gets the phase at this state.</summary>
    Phase Phase { get; }
}

/// <summary>A fully determined thermodynamic point of a single-phase fluid.</summary>
/// <remarks>
/// <para>
/// Immutable, and fixed by exactly two independent properties. <c>21</c> asks for derived properties
/// "computed on demand and cached"; what ships instead computes every one when the state is built,
/// which is strictly stronger — there is no first access to be slower than the rest, and no cache to
/// be wrong. It is also what the backend naturally gives: one <c>WithState</c> call settles the point,
/// and reading seven properties off it is one call each whenever they are read.
/// </para>
/// <para>
/// <strong>Two states are equal when their substance and their two fixing properties are equal.</strong>
/// Never compare derived properties for equality — enthalpy in particular carries the substance's own
/// reference datum, so only differences are meaningful and an absolute value asserted against a
/// textbook will fail for a correct implementation.
/// </para>
/// </remarks>
public sealed record FluidState : IThermodynamicState
{
    /// <summary>Gets the substance this state belongs to.</summary>
    public required ISubstance Substance { get; init; }

    /// <summary>Gets the gauge pressure.</summary>
    /// <value>Pa relative to the standard atmosphere, positive above it (<c>D-26</c>).</value>
    public required Quantity Pressure { get; init; }

    /// <summary>Gets the temperature.</summary>
    /// <value>K absolute.</value>
    public required Quantity Temperature { get; init; }

    /// <summary>Gets the specific enthalpy.</summary>
    /// <value>
    /// J per kg of <strong>fluid</strong>. The datum is the substance's own reference state, so only
    /// differences carry meaning — see <see cref="HumidAirState.DryAirBasisEnthalpy"/> for the one
    /// substance whose basis is different.
    /// </value>
    public required Quantity Enthalpy { get; init; }

    /// <summary>Gets the density.</summary>
    /// <value>kg/m³, always positive.</value>
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
    public Phase Phase { get; init; } = Phase.Unknown;
}

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
}
