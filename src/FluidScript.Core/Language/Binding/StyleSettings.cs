namespace FluidScript.Core.Language.Binding;

/// <summary>Presentation values: the project's <c>style:</c> block classified (<c>D-104</c>, <c>D-171</c>) and the <c>spacing</c>.</summary>
/// <param name="Spacing">
/// The <c>spacing</c> setting's value in world units, or <see langword="null"/> when the script
/// states none, in which case the layout's default margin applies (<c>D-103</c>).
/// </param>
/// <param name="Default">The project-level style, which a circuit's own <c>style:</c> block overrides for its components.</param>
public sealed record StyleSettings(double? Spacing, StyleSpec Default);
