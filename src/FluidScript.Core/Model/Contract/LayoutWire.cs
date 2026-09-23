using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary><c>25</c>'s hints, field for field.</summary>
public sealed record LayoutWire
{
    /// <summary>Depth-first order from each pressure datum.</summary>
    public required ImmutableArray<string> Order { get; init; }


    /// <summary>The heat-progression bands, left to right.</summary>
    public required ImmutableArray<ThermalStageWire> ThermalStages { get; init; }

    /// <summary>Solved direction per pipe route id: every written connection's <c>c{n}</c>, then each <c>{pipe}#c{k}</c> link along a pipe with <c>nodes=</c>.</summary>
    public required IReadOnlyDictionary<string, string> Flow { get; init; }


    /// <summary>Pipe expansions.</summary>
    public required ImmutableArray<ComponentGroupWire> Groups { get; init; }

    /// <summary>Instruments and controllers.</summary>
    public required ImmutableArray<NonFlowElementWire> NonFlowElements { get; init; }

    /// <summary>Owning circuit per component.</summary>
    public required IReadOnlyDictionary<string, string> CircuitOf { get; init; }

    /// <summary>Subcircuits sharing one parent, in declaration order.</summary>
    public required ImmutableArray<DistributionGroupWire> DistributionGroups { get; init; }

    /// <summary>Components the language added.</summary>
    public required ImmutableArray<string> Inferred { get; init; }


    /// <summary>The clearance every component keeps from every other, world units (<c>D-103</c>); the <c>spacing</c> directive or 0.5.</summary>
    public required double Margin { get; init; }

    /// <summary>The metric every label box was reserved from (<c>D-73</c>): the renderer's font must fit inside it, and its own table must agree with it.</summary>
    public required LabelMetricWire LabelMetric { get; init; }

    /// <summary>The bounds of the whole drawing as <c>[x, y, width, height]</c>, world units, outer boxes and routes included.</summary>
    public required ImmutableArray<double> Extent { get; init; }

    /// <summary>Where every component sits, in <see cref="Order"/> then the non-flow elements.</summary>
    public required ImmutableArray<PlacementWire> Placements { get; init; }

    /// <summary>Every connection's path, in connection order, then the instruments' signal lines.</summary>
    public required ImmutableArray<RouteWire> Routes { get; init; }
}
