using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>One component's place in the drawing (<c>D-103</c>). World units: a pump is 1×1, <c>y</c> grows upward and a box's <c>y</c> is its bottom edge (<c>28</c> A1).</summary>
public sealed record PlacementWire
{
    /// <summary>The component.</summary>
    public required string ComponentId { get; init; }

    /// <summary>The symbol drawn inside <see cref="Inner"/>.</summary>
    public required string SymbolId { get; init; }

    /// <summary>The symbol's box as placed, <c>[x, y, width, height]</c>; the renderer draws the strokes inside it.</summary>
    public required ImmutableArray<double> Inner { get; init; }

    /// <summary>The inner box grown by the margin; no other component's inner box enters it.</summary>
    public required ImmutableArray<double> Outer { get; init; }

    /// <summary>The quarter turn applied, clockwise degrees: 0, 90, 180 or 270.</summary>
    public required int Rotation { get; init; }

    /// <summary>Whether the symbol is mirrored left-to-right before the turn.</summary>
    public required bool Mirrored { get; init; }

    /// <summary><c>default</c> or one of the symbol's alternative arrangements (<c>D-102</c>).</summary>
    public required string Arrangement { get; init; }

    /// <summary>Every port's anchor in world coordinates with its outward direction; a node's ports are <c>#0</c>, <c>#1</c>, …</summary>
    public required IReadOnlyDictionary<string, AnchorWire> Anchors { get; init; }

    /// <summary>Where the label sits, <c>[x, y]</c>: the centre of <see cref="LabelBox"/>.</summary>
    public required ImmutableArray<double> LabelAt { get; init; }

    /// <summary>The box the label reserves, <c>[x, y, width, height]</c>, from the declared metric (<c>D-73</c>): height is the label size, width the advance times the characters. The renderer draws the text centred in it.</summary>
    public required ImmutableArray<double> LabelBox { get; init; }

    /// <summary>Whether the label sits clear of every symbol, label and line; when <see langword="false"/> the renderer draws a leader from the label to its owner (<c>53</c>).</summary>
    public required bool LabelClear { get; init; }

    /// <summary><c>computed</c>; <c>pinned</c> is reserved for a placement the script states.</summary>
    public required string Source { get; init; }

    /// <summary>The resolved style: the script's named or anonymous style (<c>D-104</c>); absent when the theme's defaults apply throughout.</summary>
    [AbsentWhenNull]
    public ResolvedStyleWire? Style { get; init; }

    /// <summary>Where the component's representative value sits on the active colour scale, 0 to 1; <see langword="null"/> when not computed. The same as <c>Scales[visualization.active].At</c>.</summary>
    public required double? Scale { get; init; }

    /// <summary>The component's position on every available scale, keyed by property (<c>D-117</c>): the switcher needs no request.</summary>
    public required IReadOnlyDictionary<string, ScalePositionWire> Scales { get; init; }
}
