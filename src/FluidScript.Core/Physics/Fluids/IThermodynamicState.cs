using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Physics.Fluids;

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
