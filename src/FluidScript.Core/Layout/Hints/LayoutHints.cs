using System.Collections.Immutable;

using FluidScript.Core.Language.Binding;

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

/// <summary>Which way a connection carries flow at the operating point.</summary>
public enum FlowDirection
{
    /// <summary>Within the zero-flow tolerance, or unsolved: a dead leg draws no arrow.</summary>
    None = 0,

    /// <summary>As the connection was written.</summary>
    Forward,

    /// <summary>Against the written direction.</summary>
    Reverse,
}

/// <summary>One thermal stage: a rank on the heat-progression axis, its role, and its members.</summary>
public sealed record ThermalStage
{
    /// <summary>The stage's rank, left to right from 0. Several stages may share one.</summary>
    public required int Rank { get; init; }

    /// <summary>What the stage does with heat.</summary>
    public required ThermalStageRole Role { get; init; }

    /// <summary>The components in the stage, in source order.</summary>
    public required ImmutableArray<string> Components { get; init; }
}

/// <summary>One circuit's structural facts (<c>D-33</c>, <c>D-35</c>).</summary>
public sealed record CircuitHint
{
    /// <summary>The circuit's name.</summary>
    public required string Name { get; init; }

    /// <summary>The circuit's number, stated or resolved; the leading part of every tag it owns.</summary>
    public required int Number { get; init; }

    /// <summary>The resolved role, or <see langword="null"/> when the name matched no registry entry (<c>D-35</c>).</summary>
    public CircuitRoleHint? Role { get; init; }

    /// <summary>The parent circuit's name, or <see langword="null"/> when the circuit stands alone.</summary>
    public string? ParentCircuit { get; init; }

    /// <summary>The parent's component this circuit takes flow from, when attached.</summary>
    public string? InletAnchorId { get; init; }

    /// <summary>The parent's component this circuit returns flow to, when attached.</summary>
    public string? OutletAnchorId { get; init; }
}

/// <summary>A resolved circuit role and the stage it biases toward.</summary>
/// <param name="CanonicalName">The registry's canonical role name.</param>
/// <param name="Stage">The stage the role is evidence for.</param>
public sealed record CircuitRoleHint(string CanonicalName, ThermalStageRole Stage);

/// <summary>Circuits sharing one supply/return pair, in stacking order (<c>D-38</c>).</summary>
public sealed record DistributionGroup
{
    /// <summary>The circuit that owns the two header lines.</summary>
    public required string ParentCircuit { get; init; }

    /// <summary>The member circuits, at least two, in declaration order.</summary>
    public required ImmutableArray<string> Members { get; init; }
}

/// <summary>The expansion of one written component into the graph elements it became.</summary>
public sealed record ComponentGroupHint
{
    /// <summary>The stable id of the component the script wrote.</summary>
    public required string ParentComponentId { get; init; }

    /// <summary>Every lowered child, in deterministic local order.</summary>
    public required ImmutableArray<string> Children { get; init; }
}

/// <summary>Where a rendered non-flow element sits and what it reads and drives.</summary>
/// <remarks>
/// A controller is placed beside the component whose parameter it actuates and routes an observer line
/// to what it measures. A placed instrument (<c>D-61</c>) sits on the node it reads and actuates
/// nothing, so <see cref="ActuationTargetId"/> is <see langword="null"/> for it.
/// </remarks>
public sealed record NonFlowElementHint
{
    /// <summary>The element's stable id.</summary>
    public required string ComponentId { get; init; }

    /// <summary>The graph component the element is drawn beside.</summary>
    public required string PlacementAnchorId { get; init; }

    /// <summary>The graph component whose property the element reads.</summary>
    public required string MeasurementTargetId { get; init; }

    /// <summary>The graph component whose parameter the element drives, or <see langword="null"/> for an instrument.</summary>
    public string? ActuationTargetId { get; init; }

    /// <summary>Position in one keyboard tab order over flow components and these elements together.</summary>
    /// <remarks>
    /// A flow component's position is its <see cref="LayoutHints.Order"/> index plus the number of
    /// elements anchored before it; each element follows its anchor immediately. Unique across the scene.
    /// </remarks>
    public required int NavigationOrder { get; init; }
}
