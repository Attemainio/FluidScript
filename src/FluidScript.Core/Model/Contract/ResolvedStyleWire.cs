namespace FluidScript.Core.Model.Contract;

/// <summary>A style with every name resolved (<c>D-104</c>). A <see langword="null"/> colour or width is the theme's default.</summary>
/// <param name="Stroke">The stroke colour, <c>#rrggbb</c>, or <see langword="null"/> for the theme's.</param>
/// <param name="StrokeWidth">The stroke width in CSS pixels at scale 1, or <see langword="null"/> for the theme's.</param>
/// <param name="Pattern"><c>solid</c>, <c>dashed</c>, <c>dotted</c> or <c>dash-dot</c>.</param>
/// <param name="Fill">The static fill colour, or <see langword="null"/> for none; the colour scale paints over it while <c>show</c> is active.</param>
/// <param name="Corner"><c>fillet</c>, <c>round</c>, <c>sharp</c> or <see langword="null"/> for the theme's.</param>
public sealed record ResolvedStyleWire(string? Stroke, double? StrokeWidth, string Pattern, string? Fill, string? Corner);
