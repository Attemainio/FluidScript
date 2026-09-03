using System.Collections.Immutable;

using FluidScript.Core.Components;

namespace FluidScript.Core.Topology;

/// <summary>One end of a branch: the junction element it meets, and the port it meets it at.</summary>
/// <remarks>
/// <strong>Not a <see cref="GraphNode"/>.</strong> A branch ends at a junction <em>element</em>, and a
/// multi-port component is a junction element without being a node — the cooling loop's branches end
/// at <c>3WV.a</c>, <c>3WV.b</c> and <c>3WV.c</c>, which no node type can name. Typing both ends as a
/// node makes the branch table <c>23</c> tabulates unrepresentable.
/// </remarks>
public sealed record BranchEnd
{
    /// <summary>Gets the junction element this end meets.</summary>
    public required IFlowComponent Element { get; init; }

    /// <summary>Gets the index of the port it meets, in <see cref="IFlowComponent.Ports"/> order.</summary>
    public required int Port { get; init; }

    /// <summary>Gets the port's name, or <see langword="null"/> when the element is a node.</summary>
    /// <value>
    /// Null for a node, whose ports are unnamed and interchangeable — there is nothing to report but
    /// the node itself. <c>a</c>, <c>b</c> or <c>c</c> for a three-way valve, where which port a
    /// branch meets is the whole content of the row.
    /// </value>
    public string? PortName { get; init; }

    /// <summary>Gets a reportable form: the element's name, and its port when the port has one.</summary>
    public string Label => PortName is null ? Element.Name : $"{Element.Name}.{PortName}";
}

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

/// <summary>One independent cycle of the branch graph.</summary>
/// <remarks>
/// <para>
/// Named <c>CircuitLoop</c> rather than <c>Loop</c>: the bare word is a reserved keyword in other
/// .NET languages and <c>CA1716</c> refuses it. Every message still calls it a loop.
/// </para>
/// <para>
/// <strong>A loop contributes no equations.</strong> The formulation is nodal, so every component
/// writes <c>p_in − p_out = Δp</c> and walking any cycle telescopes to zero identically, for any
/// iterate, because pressure is single-valued on the nodes. Adding one pressure equation per loop
/// over-determines the system by exactly the loop count — on the cooling loop that is 21 equations
/// against 20 unknowns.
/// </para>
/// <para>
/// It exists for layout and for reporting: the renderer partitions a diagram by loops, and
/// <c>FS2214</c> names the one nothing drives.
/// </para>
/// </remarks>
public sealed record CircuitLoop
{
    /// <summary>Gets the branches the cycle runs through, in walk order.</summary>
    public required ImmutableArray<Branch> Branches { get; init; }

    /// <summary>Gets a reportable form: each branch's starting element and what lies along it.</summary>
    /// <remarks>
    /// Enough to recognise the circuit, which is what <c>FS2214</c> needs of it — that message's exact
    /// wording belongs to well-posedness rather than here.
    /// </remarks>
    public string Label => string.Join(
        " → ",
        Branches.SelectMany(static branch =>
            new[] { branch.From.Label }.Concat(branch.Path.Select(static part => part.Name))));
}

/// <summary>The expansion of one written component into the several the graph holds.</summary>
/// <remarks>
/// A pipe with <c>nodes=4</c> becomes five sub-pipes and four internal nodes, and nothing downstream
/// can recover that they were one line of script unless the graph says so. The canvas draws one pipe,
/// write-back edits one parameter, and a diagnostic about the third sub-pipe has to name the pipe the
/// user wrote.
/// </remarks>
public sealed record ComponentGroup
{
    /// <summary>Gets the name of the component the script wrote.</summary>
    public required string Source { get; init; }

    /// <summary>Gets every graph element the expansion produced, in order.</summary>
    /// <value>
    /// Sub-pipes and internal nodes alike. For <c>nodes=4</c> this holds nine names — five sub-pipes
    /// and four nodes — and the source pipe itself is not among them, because it is not in the graph.
    /// </value>
    public required ImmutableArray<string> Members { get; init; }
}
