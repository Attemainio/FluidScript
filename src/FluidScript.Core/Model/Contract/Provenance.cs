namespace FluidScript.Core.Model.Contract;

/// <summary>What produced the payload.</summary>
public sealed record Provenance
{
    /// <summary>SHA-256 of the source text, as <c>sha256:</c> and 64 hex digits.</summary>
    public required string SourceHash { get; init; }

    /// <summary>The language major version the script declared.</summary>
    public required int LanguageMajor { get; init; }

    /// <summary>The pipe catalogue sizes were drawn from.</summary>
    public required VersionedId Catalog { get; init; }

    /// <summary>The fluid property package.</summary>
    public required VersionedId PropertyBackend { get; init; }

    /// <summary>The atmosphere gauge pressures are relative to, kPa absolute (<c>D-26</c>).</summary>
    public required double AtmosphereKPaAbsolute { get; init; }
}
