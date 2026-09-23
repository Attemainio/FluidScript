namespace FluidScript.Api.Contracts;

/// <summary>One catalogue a script may pin (<c>27</c>).</summary>
/// <param name="Id">The id as written after <c>catalog</c>.</param>
/// <param name="Version">The exact version.</param>
/// <param name="Standard">The standard the rows are drawn from, or <see langword="null"/>.</param>
public sealed record CatalogWire(string Id, string Version, string? Standard);
