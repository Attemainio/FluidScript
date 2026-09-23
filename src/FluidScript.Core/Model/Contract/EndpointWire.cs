namespace FluidScript.Core.Model.Contract;

/// <summary>One end of a connection.</summary>
/// <param name="Component">The component id.</param>
/// <param name="Port">The port name, or <see langword="null"/> for a node.</param>
public sealed record EndpointWire(string Component, string? Port);
