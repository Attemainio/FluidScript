using System.Collections.Immutable;

using FluidScript.Core.Binding;

namespace FluidScript.Core.Layout;

/// <summary>Structural advice for a renderer. Contains no geometry (<c>D-03</c>).</summary>
/// <remarks>
/// <para>
/// <strong>Everything here is a fact about the graph, and nothing here is a fact about a drawing.</strong>
/// <c>25</c>'s test for what belongs: only Core knows it, it is structural, and it carries no
/// coordinate, dimension, pixel, tag, spacing or layout-mode name. The renderer (<c>53</c>) turns these
/// into placements; the hints never say where anything goes, only what is next to what, which way
/// heat nominally runs, and which things are the same kind of thing.
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

    /// <summary>Rank for non-loop components: hops from the nearest loop member, or from the datum when the part has no loop.</summary>
    /// <remarks>The local column in a layered layout. Loop members are deliberately absent; <see cref="Loops"/> places them.</remarks>
    public required ImmutableDictionary<string, int> Rank { get; init; }

    /// <summary>Nominal heat-progression stages, ordered left to right (<c>D-31</c>).</summary>
    /// <remarks>Fixed at the design point; a transient reversal changes <see cref="Flow"/> and never this.</remarks>
    public required ImmutableArray<ThermalStage> ThermalStages { get; init; }

    /// <summary>Per-connection flow direction at the solved operating point, keyed by connection id.</summary>
    /// <value>
    /// <see cref="FlowDirection.Forward"/> as written, <see cref="FlowDirection.Reverse"/> when the solved
    /// flow runs against the written direction, <see cref="FlowDirection.None"/> inside the zero-flow
    /// tolerance or when nothing is solved.
    /// </value>
    public required ImmutableDictionary<string, FlowDirection> Flow { get; init; }

    /// <summary>Suggested side for each port, keyed <c>component.port</c>, as though every component sat on a horizontal run.</summary>
    /// <value>Inlets west, outlets east, a three-way valve's legs north and south, an exchanger's second side north and south.</value>
    public required ImmutableDictionary<string, PortSide> PortSides { get; init; }

    /// <summary>Components forming each independent loop, in traversal order.</summary>
    /// <remarks>An order and not positions: <c>D-44</c> forbids a component at a corner, and only the renderer knows a symbol's extent.</remarks>
    public required ImmutableArray<ImmutableArray<string>> Loops { get; init; }

    /// <summary>Orientation per loop, aligned by index with <see cref="Loops"/>.</summary>
    public required ImmutableArray<LoopOrientation> LoopOrientations { get; init; }

    /// <summary>Semantic groupings: every graph element one written component expanded into.</summary>
    public required ImmutableArray<ComponentGroupHint> Groups { get; init; }

    /// <summary>Placement and navigation anchors for rendered non-flow elements: controllers and placed instruments.</summary>
    public required ImmutableArray<NonFlowElementHint> NonFlowElements { get; init; }

    /// <summary>Which circuit each component belongs to (<c>D-33</c>; the enthalpy-losing side for a two-sided one, <c>D-36</c>).</summary>
    public required ImmutableDictionary<string, string> CircuitOf { get; init; }

    /// <summary>Every circuit, in declaration order, with the structure a renderer needs.</summary>
    public required ImmutableArray<CircuitHint> Circuits { get; init; }

    /// <summary>Sets of circuits sharing one supply/return pair (<c>D-38</c>); never fewer than two members.</summary>
    public required ImmutableArray<DistributionGroup> DistributionGroups { get; init; }

    /// <summary>Components created by inference rather than written.</summary>
    public required ImmutableHashSet<string> Inferred { get; init; }

    /// <summary>Each attached circuit's branch shape: the kinds along its path from supply anchor to return anchor (<c>D-100</c>).</summary>
    /// <remarks>Equal shapes are the same assembly and are drawn congruently. Observers and nodes are excluded; keyed by circuit name.</remarks>
    public required ImmutableDictionary<string, ImmutableArray<string>> BranchShapes { get; init; }
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

/// <summary>The side of a component a port is suggested to sit on.</summary>
public enum PortSide
{
    /// <summary>The left side, where inlets go on a horizontal run.</summary>
    West = 1,

    /// <summary>The right side, where outlets go.</summary>
    East,

    /// <summary>The top.</summary>
    North,

    /// <summary>The bottom.</summary>
    South,
}

/// <summary>Which way round a loop is drawn, with supply on top (<c>D-30</c>).</summary>
public enum LoopOrientation
{
    /// <summary>Flow leaves the loop's first driver rightward along the top.</summary>
    Clockwise = 1,

    /// <summary>Flow leaves the loop's first driver leftward along the top.</summary>
    Counterclockwise,
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
    public string? SupplyAnchorId { get; init; }

    /// <summary>The parent's component this circuit returns flow to, when attached.</summary>
    public string? ReturnAnchorId { get; init; }
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
