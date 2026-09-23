using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Topology.Construction;
using FluidScript.Core.Topology.Counting;

namespace FluidScript.Core.Topology.Graph;

/// <summary>
/// The solvable form of a model: nodes carrying state, components imposing equations, and the branch
/// decomposition the solver assigns flow unknowns to.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing here holds a reference to a syntax or semantic-model type</strong> (invariant 7),
/// and an architecture test asserts it. That is what makes the solver testable from a hand-built graph
/// with no script behind it, and it is the reason names are strings: the graph must be reportable, and
/// carrying a symbol to achieve that would drag the whole binder in behind it.
/// </para>
/// <para>
/// <strong>Several circuits are one graph, not several</strong> (<c>D-33</c>). A rated exchanger
/// already produces more than one hydraulic connected component inside a single graph, and energy
/// spans what pressure does not; circuits are the same shape of thing one level up. Membership is a
/// per-component field rather than a partition, because a circuit may span hydraulic components and a
/// hydraulic component may span circuits.
/// </para>
/// </remarks>
public sealed record CircuitGraph
{
    /// <summary>Gets the model's name, for reporting.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the fluid every node's state is evaluated against.</summary>
    public required ISubstance Substance { get; init; }

    /// <summary>Gets whether the graph is solved as an equilibrium or in time.</summary>
    public required SolveMode Mode { get; init; }

    /// <summary>Gets every node, including inferred and pipe-internal ones, in a stable order.</summary>
    public required ImmutableArray<GraphNode> Nodes { get; init; }

    /// <summary>Gets every component, in a stable order.</summary>
    /// <value>
    /// Nodes appear here too — a node is a component, and it writes equations like any other. What is
    /// <em>not</em> here is an observer: an instrument has no ports, no residuals and no place in the
    /// graph, and adding or removing one leaves this list byte-identical (<c>D-61</c>, invariant 9).
    /// </value>
    public required ImmutableArray<IFlowComponent> Components { get; init; }

    /// <summary>Gets the branch decomposition: maximal paths between junction elements.</summary>
    public required ImmutableArray<Branch> Branches { get; init; }

    /// <summary>Gets the vertices of the branch graph.</summary>
    /// <value>
    /// Components with a flow group that does not hold exactly two ports: a group of three or more is
    /// a split, and a group of one is a terminal. <strong>Port count is never the test</strong> — a
    /// coupled exchanger has four ports in two groups of two and is interior to a branch on each side,
    /// while a three-way valve has three ports in one group and is a vertex (<c>D-63</c>).
    /// </value>
    public required ImmutableArray<IFlowComponent> JunctionElements { get; init; }

    /// <summary>Gets one entry per element of the cycle basis.</summary>
    /// <value>
    /// Sized <c>Branches.Length − JunctionElements.Length + 1</c> per connected component of the
    /// branch graph. The second term counts junction elements and terminals — the vertices of the
    /// <em>branch</em> graph — and not every node, because a node interior to a branch is not one.
    /// </value>
    public required ImmutableArray<CircuitLoop> Loops { get; init; }

    /// <summary>Gets the expansions lowering performed, so a diagnostic can name what the user wrote.</summary>
    /// <value>Empty unless a pipe carried <c>nodes=n</c>.</value>
    public required ImmutableArray<ComponentGroup> Groups { get; init; }

    /// <summary>Gets each component's circuit, keyed by component name (<c>D-33</c>).</summary>
    /// <remarks>
    /// A field rather than a partition of <see cref="Components"/>: neither containment holds between
    /// circuits and hydraulic components, in either direction.
    /// </remarks>
    public required ImmutableDictionary<string, string> CircuitOf { get; init; }

    /// <summary>Gets which port each port is joined to.</summary>
    /// <value>
    /// Indexed by position in <see cref="Components"/> and then by the component's own port order.
    /// <para>
    /// <strong>This is what a branch decomposition alone cannot say.</strong> <see cref="Branch.Path"/>
    /// gives the order a walk crosses the elements, and the assembler needs the port: a component's
    /// residual reads <c>Ports[i]</c> as the state at the node port <c>i</c> touches, and a two-port
    /// pass-through walked from the other end is entered at its outlet. Lowering computes this to
    /// decompose the branches at all, and used to discard it (<c>S-10</c>).
    /// </para>
    /// </value>
    public required PortAdjacency Adjacency { get; init; }

    /// <summary>Gets the ports the script itself named, as <c>component.port</c> (<c>D-88</c>).</summary>
    /// <value>
    /// <para>
    /// Empty by default, which is right for a hand-built graph: nothing named anything, so nothing may
    /// be read as the user's word.
    /// </para>
    /// <para>
    /// <strong>A port name in <see cref="BranchEnd.PortName"/> is always present and only sometimes
    /// means something.</strong> Lowering resolves an unqualified endpoint to a real port and records
    /// its name like any other, so the graph alone cannot say whether <c>3WV.a</c> is what the user
    /// wrote or what connection order produced. This set is that difference, and the only thing that
    /// currently needs it is <see cref="FluidScript.Core.Solvers.Results.ValveLegs"/> — <c>a</c> is a three-way valve's control
    /// path and <c>b</c> its bypass, so a script naming them has said which leg the valve modulates,
    /// while positional binding has said nothing at all.
    /// </para>
    /// </value>
    public ImmutableHashSet<string> StatedPorts { get; init; } = [];

    /// <summary>Gets the sized parameters, as <c>component.parameter</c>, whose value is a bootstrap provisional (<c>D-96</c>).</summary>
    /// <value>
    /// <para>
    /// Empty by default, which is right for a hand-built graph: every value on it was put there on purpose.
    /// </para>
    /// <para>
    /// <strong>A provisional is absence with a number in it.</strong> A valve has no component without a
    /// Kv, so the outer loop's bootstrap writes one into <see cref="IComponent.SizedParameters"/> before
    /// there is a graph to size against — and from the maps alone that value is indistinguishable from a
    /// rule's choice. This set is that difference. Well-posedness reads it so that a provisional counts
    /// as free: a stated constraint the loop cannot otherwise absorb may promote it, which is the
    /// balancing-valve case <c>23</c> describes and <c>C-75</c> found dead.
    /// </para>
    /// </value>
    public ImmutableHashSet<string> ProvisionalParameters { get; init; } = [];

    /// <summary>Gets every <c>control</c> line's setpoint, applied to the design solve or not (<c>D-141</c>).</summary>
    /// <value>
    /// Empty by default, which is right for a hand-built graph and for a static circuit with no control
    /// line. An applied entry says the measured component's stated parameter of that name is the
    /// setpoint rather than the script's own word, and that the constraint it raises is answered by
    /// the named actuator alone (<see cref="WellPosedness"/>).
    /// </value>
    public ImmutableArray<Setpoint> Setpoints { get; init; } = [];

    /// <summary>Gets the schedule: every disturbance the script wrote, in SI, in source order (<c>33</c>).</summary>
    /// <value>Empty by default, which is right for a hand-built graph and for a static script.</value>
    public ImmutableArray<ScheduledChange> Schedule { get; init; } = [];

    /// <summary>Tells whether a component's ports carry more than one flow between them.</summary>
    /// <param name="component">The component to classify.</param>
    /// <returns>
    /// <see langword="true"/> when some flow group does not hold exactly two ports, which makes the
    /// component a vertex of the branch graph rather than something a branch passes through.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>The rule is "not exactly two", not "more than two", and both ends matter.</strong> A
    /// group of three or more is a split and a branch cannot cross it; a group of <em>one</em> is a
    /// terminal, and a branch has to end somewhere. Counting only the splits gives the cooling loop
    /// <c>4 − 2 + 1 = 3</c> loops where it has one.
    /// </para>
    /// <para>
    /// A component with no ports at all is not a junction element — it is not in the graph. Nothing
    /// reaches here in that state, since observers and controllers are dropped before lowering builds
    /// anything.
    /// </para>
    /// </remarks>
    public static bool IsJunctionElement(IFlowComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component.FlowGroups.IsEmpty)
        {
            return false;
        }

        var sizes = new Dictionary<int, int>(component.FlowGroups.Length);

        foreach (var group in component.FlowGroups)
        {
            sizes[group] = sizes.GetValueOrDefault(group) + 1;
        }

        foreach (var size in sizes.Values)
        {
            if (size != 2)
            {
                return true;
            }
        }

        return false;
    }
}
