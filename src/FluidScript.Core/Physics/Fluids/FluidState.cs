using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Physics.Fluids;

/// <summary>A fully determined thermodynamic point of a fluid.</summary>
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
/// <para>
/// <strong>Two-phase and supercritical points are states like any other</strong> (<c>D-78</c>). The
/// pair that fixes one is <c>(p, h)</c>: inside the dome a pressure and a temperature are the same
/// constraint rather than two, so <see cref="Temperature"/> there is the saturation temperature and
/// says nothing further. That is the reason the solver's node unknown is enthalpy — a formulation
/// carrying node temperature could not represent an evaporator inlet at all.
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

    /// <summary>Gets the specific entropy.</summary>
    /// <value>
    /// J/(kg·K), on the substance's own reference datum exactly as <see cref="Enthalpy"/> is, so only
    /// differences carry meaning. Its dimension is <see cref="Dimension.SpecificHeat"/>, which is the
    /// same J/(kg·K) — dimensionally exact, and why there is no separate entropy dimension.
    /// </value>
    /// <remarks>
    /// Carried for one job: fixing a compressor's isentropic discharge state (<c>D-78</c>). It is also
    /// what a T–s or p–h plot is drawn from, which is the form an engineer reads a refrigeration
    /// circuit in.
    /// </remarks>
    public required Quantity Entropy { get; init; }

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
