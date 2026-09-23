namespace FluidScript.Core.Model.Contract;

/// <summary>One tank layer.</summary>
/// <param name="Index">One-based, from the bottom.</param>
/// <param name="Elevation">The layer's top as a fraction of the tank height, <c>0…1</c>.</param>
/// <param name="T">The layer temperature.</param>
public sealed record LayerWire(int Index, double Elevation, QuantityWire T);
