namespace FluidScript.Core.Solvers.Equations;

/// <summary>One state a transient integrates: a node's enthalpy, or one layer of a tank (<c>D-139</c>).</summary>
/// <param name="Column">The state's column in the vector, or <c>-1</c> for a tank layer, which has none.</param>
/// <param name="Element">The tank's index in the graph for a layer, <c>-1</c> for a node state.</param>
/// <param name="Layer">The one-based layer, bottom to top; 0 for a node state.</param>
/// <param name="Owner">The node or tank that holds it.</param>
/// <param name="Name">A human-readable name: <c>PB#n1.h</c>, <c>T1.layer[2].h</c>.</param>
/// <param name="Volume">m³ of fluid the state stands for, the cell's or the layer's; its mass at the state's density is the capacitance.</param>
public sealed record DifferentialState(int Column, int Element, int Layer, string Owner, string Name, double Volume);
