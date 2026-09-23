namespace FluidScript.Core.Model.Contract;

/// <summary>A connection's solved state.</summary>
/// <param name="Flow">The mass flow in the written direction.</param>
public sealed record ConnectionStateWire(QuantityWire Flow);
