namespace FluidScript.Core.Model.Contract;

/// <summary>The <c>project</c> line.</summary>
/// <param name="Name">The project name.</param>
/// <param name="DefaultMode">The default solve mode, <c>steady</c>, <c>transient</c> or <see langword="null"/>.</param>
public sealed record ProjectWire(string? Name, string? DefaultMode);
