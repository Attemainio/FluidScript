using FluidScript.Core.Components;

namespace FluidScript.Core.Solvers.Equations;

/// <summary>One node whose enthalpy can reach a node port, and the port it arrives through.</summary>
/// <param name="Node">The node the enthalpy is read from.</param>
/// <param name="Component">The component carrying it across, in graph order.</param>
/// <param name="Port">The port of that component the flow enters by.</param>
/// <param name="Lift">
/// J/kg the enthalpy loses on the way: <c>g·(z_here − z_source)</c> across a bare connection between
/// two nodes at different heights (<c>D-70</c>), 0 everywhere else. A pipe carries its own rise
/// through <see cref="IFlowComponent.EvaluateEnergyInjection"/>; a bare link has no component to do
/// it, so the node reads the arriving enthalpy already lifted.
/// </param>
internal readonly record struct ArrivingSource(int Node, int Component, int Port, double Lift = 0);
