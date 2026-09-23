namespace FluidScript.Api.Contracts;

/// <summary>One fixed port.</summary>
/// <param name="Name">The port name as written after a dot.</param>
/// <param name="Role"><c>inlet</c>, <c>outlet</c> or <c>bidirectional</c>.</param>
/// <param name="Optional">Whether inference rule I3 leaves it unconnected without a boundary node.</param>
public sealed record PortWire(string Name, string Role, bool Optional);
