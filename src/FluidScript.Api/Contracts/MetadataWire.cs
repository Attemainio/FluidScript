using System.Collections.Immutable;
using FluidScript.Core.Model.Contract;

namespace FluidScript.Api.Contracts;

/// <summary>The static description of the language, the body of <c>GET /api/v1/metadata</c> (<c>42</c>).</summary>
/// <remarks>
/// A pure function of the deployed build: the same bytes on every call until the build changes, which
/// is what the ETag says. It is what an agent reads first (<c>R-29</c>) and what drives editor
/// completion, so a gap here shows up as missing autocomplete.
/// </remarks>
public sealed record MetadataWire
{
    /// <summary>The REST major this document describes; the <c>v1</c> in the path.</summary>
    public required int RestMajor { get; init; }

    /// <summary>The model contract version <c>compile</c> returns.</summary>
    public required string ContractVersion { get; init; }

    /// <summary>The language majors this build reads.</summary>
    public required LanguageVersionsWire Language { get; init; }

    /// <summary>Every component kind, in registry order.</summary>
    public required ImmutableArray<KindWire> Kinds { get; init; }

    /// <summary>Every dimension a parameter or property can have, with its units.</summary>
    public required ImmutableArray<DimensionWire> Dimensions { get; init; }

    /// <summary>The symbol definitions the canvas draws with (<c>D-24</c>), the same records the model contract carries.</summary>
    public required ImmutableArray<SymbolWire> Symbols { get; init; }

    /// <summary>Every live diagnostic code.</summary>
    public required ImmutableArray<DiagnosticCodeWire> Diagnostics { get; init; }

    /// <summary>Codes that were allocated and are no longer emitted, so a stale reference can be told from a typo.</summary>
    public required ImmutableArray<RetiredCodeWire> RetiredDiagnostics { get; init; }

    /// <summary>The catalogues a script may pin, with their exact versions.</summary>
    public required ImmutableArray<CatalogWire> Catalogs { get; init; }

    /// <summary>The fluid property package and its version.</summary>
    public required VersionedId PropertyBackend { get; init; }

    /// <summary>The ceilings a request may reach (<c>07</c>).</summary>
    public required LimitsWire Limits { get; init; }

    /// <summary>A URI to the generated function index (<c>61</c>).</summary>
    public required string DocsIndex { get; init; }
}
