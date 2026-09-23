namespace FluidScript.Core.Language.Compatibility;

/// <summary>A catalogue reference, optionally pinned to an exact version.</summary>
/// <param name="Id">The catalogue's ASCII id, such as <c>steel_en10255</c>.</param>
/// <param name="Version">
/// The <c>@major.minor</c> the script pinned, or <see langword="null"/> for the application's shipped
/// version — which is then recorded in provenance, so a solved result still says what it used.
/// </param>
public sealed record CatalogPin(string Id, string? Version);
