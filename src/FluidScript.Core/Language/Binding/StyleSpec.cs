namespace FluidScript.Core.Language.Binding;

/// <summary>What a <c>style</c> directive stated, category by category; a field is <see langword="null"/> where it said nothing.</summary>
/// <param name="Stroke">The stroke colour as <c>#rrggbb</c>.</param>
/// <param name="StrokeWidth">The stroke width in CSS pixels at scale 1.</param>
/// <param name="Corner"><c>fillet</c>, <c>round</c> or <c>sharp</c>.</param>
/// <param name="Pattern"><c>solid</c>, <c>dashed</c>, <c>dotted</c> or <c>dash-dot</c>.</param>
/// <param name="Fill">The static fill colour as <c>#rrggbb</c>; the colour scale paints over it while <c>show</c> is active (<c>D-104</c>).</param>
public sealed record StyleSpec(string? Stroke, double? StrokeWidth, string? Corner, string? Pattern, string? Fill)
{
    /// <summary>The spec that states nothing.</summary>
    public static StyleSpec Empty { get; } = new(null, null, null, null, null);

    /// <summary>Gets whether every category is unstated.</summary>
    public bool IsEmpty => Stroke is null && StrokeWidth is null && Corner is null && Pattern is null && Fill is null;

    /// <summary>Layers <paramref name="over"/> on this: a category the later one states replaces the earlier.</summary>
    /// <param name="over">The later spec.</param>
    /// <returns>The merged spec.</returns>
    public StyleSpec Merge(StyleSpec over) => new(
        over.Stroke ?? Stroke,
        over.StrokeWidth ?? StrokeWidth,
        over.Corner ?? Corner,
        over.Pattern ?? Pattern,
        over.Fill ?? Fill);
}
