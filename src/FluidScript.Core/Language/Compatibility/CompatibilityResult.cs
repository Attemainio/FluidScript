using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Language.Compatibility;

/// <summary>What inspecting a file's version directive established.</summary>
/// <param name="DetectedMajor">
/// The major the file states, or <see langword="null"/> when it states none.
/// </param>
/// <param name="Catalog">The catalogue the file pinned, or <see langword="null"/>.</param>
/// <param name="Disposition">What that means for this application.</param>
/// <param name="Diagnostics">What to report, in source order.</param>
/// <param name="AllowedActions">
/// Everything the application may do with this file. A file is never acted on beyond this set: it is
/// the gate itself, not advice about one.
/// </param>
public sealed record CompatibilityResult(
    LanguageMajor? DetectedMajor,
    CatalogPin? Catalog,
    CompatibilityDisposition Disposition,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<CompatibilityAction> AllowedActions);
