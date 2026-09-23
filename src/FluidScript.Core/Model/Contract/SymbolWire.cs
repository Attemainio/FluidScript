using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>A symbol definition in a normalized box (<c>D-20</c>, <c>D-102</c>).</summary>
public sealed record SymbolWire
{
    /// <summary>The id components reference, <c>kind.variant</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The bounding box as <c>[x, y, width, height]</c> in symbol units; what layout reasons on.</summary>
    public required ImmutableArray<double> ViewBox { get; init; }

    /// <summary>What the canvas draws inside the box. Never executable.</summary>
    public required ImmutableArray<PrimitiveWire> Primitives { get; init; }

    /// <summary>The default arrangement: each named port's anchor on the box edge and its outward direction.</summary>
    public required IReadOnlyDictionary<string, AnchorWire> PortAnchors { get; init; }

    /// <summary>
    /// Other complete arrangements of the same ports on the same box, by name; the renderer may pick one
    /// per instance, with a rotation, to shorten the connections it has to draw. Absent when there is one.
    /// </summary>
    [AbsentWhenNull]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, AnchorWire>>? Alternatives { get; init; }

    /// <summary>Rules for indexed ports such as a tank's <c>in{n}</c>; absent for a fixed-port symbol.</summary>
    [AbsentWhenNull]
    public ImmutableArray<IndexedAnchorWire>? IndexedPortAnchors { get; init; }

    /// <summary>Where the label sits, <c>[x, y]</c>.</summary>
    public required ImmutableArray<double> LabelAnchor { get; init; }

    /// <summary>
    /// Which transforms the kind admits (<c>28</c> A4, <c>D-108</c>): <c>free</c> turns by any quarter, mirrored or not;
    /// <c>standing</c> is never turned, only mirrored left-right, up-down or both (every exchanger); <c>upright</c>
    /// admits only the left-right mirror (a tank, whose layers are a vertical order); <c>level</c> admits every transform
    /// but stands vertical only where nothing level fits (a pump, <c>D-113</c>). A fact about the kind, never a preference.
    /// </summary>
    public string TransformClass { get; init; } = "free";
}
