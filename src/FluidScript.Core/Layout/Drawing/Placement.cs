using System.Collections.Immutable;

namespace FluidScript.Core.Layout.Drawing;

/// <summary>One component's place in the diagram (<c>D-103</c>).</summary>
/// <remarks>
/// <see cref="Inner"/> is the symbol's box scaled, rotated and given an arrangement; <see cref="Outer"/> is
/// the inner grown by the margin. The invariant the solver holds (<c>28</c> §4): every two inner boxes are at
/// least the clearance apart; outer boxes may overlap, and that is counted, not rejected.
/// </remarks>
public sealed record Placement
{
    /// <summary>The component's name.</summary>
    public required string ComponentId { get; init; }

    /// <summary>The symbol definition drawn inside <see cref="Inner"/>.</summary>
    public required string SymbolId { get; init; }

    /// <summary>The symbol's box after placement.</summary>
    public required Box Inner { get; init; }

    /// <summary>The inner box grown by the margin: the clearance nothing else may enter.</summary>
    public required Box Outer { get; init; }

    /// <summary>The quarter turn applied, clockwise degrees: 0, 90, 180 or 270.</summary>
    public required int Rotation { get; init; }

    /// <summary>Whether the symbol was mirrored left-to-right before rotating.</summary>
    public required bool Mirrored { get; init; }

    /// <summary><c>default</c> or one of the symbol's alternative arrangements (<c>D-102</c>).</summary>
    public required string Arrangement { get; init; }

    /// <summary>Every port's anchor after placement, by port name; a node's ports each get their own entry.</summary>
    public required ImmutableSortedDictionary<string, PlacedAnchor> Anchors { get; init; }

    /// <summary>Where the label sits: the centre of <see cref="LabelBox"/>.</summary>
    public required Point LabelAt { get; init; }

    /// <summary>The box the label reserves, from the declared metric (<c>D-73</c>, <see cref="LabelLayout"/>); no other symbol, label or line runs through it when <see cref="LabelClear"/> holds.</summary>
    public required Box LabelBox { get; init; }

    /// <summary>Whether the label was placed clear of everything; when not, the renderer draws a leader to its owner (<c>53</c>, <c>C-84</c>).</summary>
    public required bool LabelClear { get; init; }

    /// <summary><c>computed</c>; <c>pinned</c> is reserved for a placement the script states (<c>D-103</c>).</summary>
    public required string Source { get; init; }


    /// <summary>The <see cref="LayoutGroup.Id"/> of the group that placed this component, or <see langword="null"/> when it was placed on its own.</summary>
    public string? Group { get; init; }

    /// <summary>Whether this is an inline element (<c>D-105</c>): a pipe or a two-port node drawn as a point on its run, with no box and no clearance of its own.</summary>
    public bool IsInline => Inner.Width <= 0 && Inner.Height <= 0;
}
