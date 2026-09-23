namespace FluidScript.Core.Physics.Fluids;

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
