using System.Collections.Immutable;

namespace FluidScript.Core.Layout.Drawing;

/// <summary>The whole drawing: what the renderer draws and the exporter writes (<c>D-103</c>).</summary>
public sealed record Scene
{
    /// <summary>Every component's placement, in <c>hints.Order</c> then non-flow elements.</summary>
    public required ImmutableArray<Placement> Placements { get; init; }

    /// <summary>Every connection's route, in connection order, then signal lines.</summary>
    public required ImmutableArray<Route> Routes { get; init; }

    /// <summary>The bounds of everything, outer boxes and routes included.</summary>
    public required Box Extent { get; init; }

    /// <summary>The margin every outer box was grown by, world units.</summary>
    public required double Margin { get; init; }

    /// <summary>The groups the solver laid out as objects, outermost first; empty for a scene with none.</summary>
    public ImmutableArray<LayoutGroup> Groups { get; init; } = [];

    /// <summary>Every decision the layout made, in the order it made them (<c>C-107</c>).</summary>
    public ImmutableArray<PlacementNote> Provenance { get; init; } = [];
}
