namespace FluidScript.Api.Endpoints;

/// <summary>The health body.</summary>
/// <param name="Status">Always <c>ok</c>; a host that cannot answer does not answer.</param>
/// <param name="Core">The Core assembly version.</param>
public sealed record HealthWire(string Status, string? Core);
