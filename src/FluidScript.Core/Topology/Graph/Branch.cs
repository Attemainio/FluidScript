using System.Collections.Immutable;

using FluidScript.Core.Components;

namespace FluidScript.Core.Topology.Graph;

/// <summary>A maximal path between two junction elements, carrying one flow unknown.</summary>
/// <remarks>
/// <para>
/// <strong>Every component along a branch sees the same mass flow, which is why the branch — not the
/// component — owns the unknown.</strong> A branch with three pipes and a valve in series contributes
/// one flow unknown and four pressure-drop equations. Giving each component its own would add
/// equations saying only "these are equal": more unknowns, a larger Jacobian, worse conditioning and
/// no more information. <c>23</c> calls this the single most consequential structural decision in
/// tier 20 for solver performance.
/// </para>
/// <para>
/// <strong><see cref="Path"/> is not a partition of the component set.</strong> A coupled heat
/// exchanger appears in two branches, one per side, because its four ports are two flow groups. The
/// natural implementation — walk every component once, assign it to a branch — silently drops one
/// side.
/// </para>
/// </remarks>
public sealed record Branch
{
    /// <summary>Gets the junction element the branch leaves.</summary>
    public required BranchEnd From { get; init; }

    /// <summary>Gets the junction element the branch reaches.</summary>
    public required BranchEnd To { get; init; }

    /// <summary>Gets the components between the two ends, in the order the walk crosses them.</summary>
    /// <value>
    /// Empty for a bare connection, which <c>D-25</c> makes an ideal zero-drop link. Interior nodes
    /// appear here alongside the flow components: they carry pressure and enthalpy unknowns, and only
    /// their mass balance is subsumed by the branch's single flow.
    /// </value>
    public required ImmutableArray<IFlowComponent> Path { get; init; }

    /// <summary>Gets this branch's position in the graph's branch list.</summary>
    /// <value>Assigned by lowering, and the index of its flow unknown.</value>
    public required int Index { get; init; }
}
