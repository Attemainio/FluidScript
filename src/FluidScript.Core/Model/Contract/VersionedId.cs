namespace FluidScript.Core.Model.Contract;

/// <summary>A named thing and its version.</summary>
/// <param name="Id">The stable identifier.</param>
/// <param name="Version">The exact version string.</param>
public sealed record VersionedId(string Id, string Version);
