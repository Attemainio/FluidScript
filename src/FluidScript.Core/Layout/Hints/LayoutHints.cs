using System.Collections.Immutable;

namespace FluidScript.Core.Layout.Hints;

/// <summary>The classification of the graph the layout engine (<c>28</c>) starts from: an order, the circuits and how they attach, the distribution groups, the non-flow elements, what was inferred, and the solved flow direction per connection for the arrows.</summary>
/// <remarks>
/// <para>
/// <strong>Everything here is a fact about the graph, and nothing here is a fact about a drawing.</strong>
/// <c>25</c>'s test for what belongs: only Core knows it, it is structural, and it carries no
/// coordinate. The engine places and routes from these; the hints never say where anything goes.
/// </para>
/// <para>
/// A pure function of the graph, the model and the solved branch flows: byte-identical across builds
/// for one input (<c>25</c> invariant 4), because an unstable hint makes the diagram jump on every
/// keystroke.
/// </para>
/// </remarks>
public sealed record LayoutHints
{
    /// <summary>Components in a stable topological order, sources first.</summary>
    /// <remarks>
    /// A closed circuit has no true topological order, so this is a depth-first walk from each
    /// hydraulic component's pressure datum, ports in declaration order, back edges deferred
    /// (<c>FS2401</c>). Every graph component appears exactly once.
    /// </remarks>
    public required ImmutableArray<string> Order { get; init; }

    /// <summary>Nominal heat-progression stages, ordered left to right.</summary>
    /// <remarks>Not a placement input: the classification carries <c>FS2403</c> (a circuit's name against its duties). Fixed at the design point; a transient reversal changes <see cref="Flow"/> and never this.</remarks>
    public required ImmutableArray<ThermalStage> ThermalStages { get; init; }

    /// <summary>Per-connection flow direction at the solved operating point, keyed by connection id: what the arrows draw.</summary>
    /// <value>
    /// <see cref="FlowDirection.Forward"/> as written, <see cref="FlowDirection.Reverse"/> when the solved
    /// flow runs against the written direction, <see cref="FlowDirection.None"/> inside the zero-flow
    /// tolerance or when nothing is solved.
    /// </value>
    public required ImmutableDictionary<string, FlowDirection> Flow { get; init; }

    /// <summary>Semantic groupings: every graph element one written component expanded into.</summary>
    public required ImmutableArray<ComponentGroupHint> Groups { get; init; }

    /// <summary>Placement and navigation anchors for rendered non-flow elements: controllers and placed instruments.</summary>
    public required ImmutableArray<NonFlowElementHint> NonFlowElements { get; init; }

    /// <summary>Which circuit each component belongs to (<c>D-33</c>; the enthalpy-losing side for a two-sided one, <c>D-36</c>).</summary>
    public required ImmutableDictionary<string, string> CircuitOf { get; init; }

    /// <summary>Every circuit, in declaration order, with the structure the engine needs.</summary>
    public required ImmutableArray<CircuitHint> Circuits { get; init; }

    /// <summary>Sets of circuits sharing one supply/return pair (<c>D-33</c>); never fewer than two members.</summary>
    public required ImmutableArray<DistributionGroup> DistributionGroups { get; init; }

    /// <summary>Components created by inference rather than written.</summary>
    public required ImmutableHashSet<string> Inferred { get; init; }
}
