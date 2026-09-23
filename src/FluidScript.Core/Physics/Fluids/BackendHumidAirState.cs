namespace FluidScript.Core.Physics.Fluids;

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
