namespace FluidScript.Core.Solvers.Results;

/// <summary>One port's solved condition, in SI.</summary>
/// <param name="Node">The graph node the port reads its state from.</param>
/// <param name="Flow">Mass flow, kg/s, positive <em>into</em> the component (<c>22</c>'s convention); zero for a port no branch reaches.</param>
/// <param name="Pressure">Absolute pressure, Pa.</param>
/// <param name="Enthalpy">Specific enthalpy, J/kg.</param>
/// <param name="Temperature">Temperature, K.</param>
/// <param name="Density">Density, kg/m³.</param>
/// <param name="SpecificHeat">Specific heat, J/(kg·K).</param>
/// <param name="DynamicViscosity">Dynamic viscosity, Pa·s; what a pipe's Reynolds number on the wire is read from (<c>A-6</c>). Zero when the state was built before it was carried.</param>
public readonly record struct SolvedPort(
    int Node, double Flow, double Pressure, double Enthalpy, double Temperature, double Density, double SpecificHeat, double DynamicViscosity = 0);
