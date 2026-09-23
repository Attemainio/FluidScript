using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>One connection's path.</summary>
public sealed record RouteWire
{
    /// <summary><c>c{n}</c> for a written connection; <c>{pipe}#c{k}</c> for the k-th link along a pipe with <c>nodes=</c>, from its inlet (<c>C-124</c>); <c>{instrument}:measures</c> or <c>{controller}:actuates</c> for a signal line.</summary>
    public required string Id { get; init; }

    /// <summary><c>pipe</c> or <c>signal</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The draw order (<c>28</c> C16): <c>supply</c> in front, <c>return</c> behind it, <c>signal</c> behind everything. A pipe is supply until the flow from a heat source has passed a losing side.</summary>
    public required string Layer { get; init; }

    /// <summary>The orthogonal polyline, flattened <c>[x0, y0, x1, y1, …]</c>; the first and last points are the anchors.</summary>
    public required ImmutableArray<double> Points { get; init; }

    /// <summary>Where this route passes behind another it crosses, flattened <c>[x0, y0, …]</c> in world units; the renderer breaks this route around each so the one in front runs through (<c>28</c> C16).</summary>
    public required ImmutableArray<double> Hops { get; init; }

    /// <summary>The resolved style, from the component the route leaves; absent when the theme's defaults apply throughout.</summary>
    [AbsentWhenNull]
    public ResolvedStyleWire? Style { get; init; }

    /// <summary>The scale position at the start, for a gradient; <see langword="null"/> when not computed. The same as <c>Scales[visualization.active].From</c>.</summary>
    public required double? ScaleFrom { get; init; }

    /// <summary>The scale position at the end.</summary>
    public required double? ScaleTo { get; init; }

    /// <summary>The route's ends on every available scale, keyed by property (<c>D-117</c>); <c>At</c> is unused for a route.</summary>
    public required IReadOnlyDictionary<string, ScalePositionWire> Scales { get; init; }
}
