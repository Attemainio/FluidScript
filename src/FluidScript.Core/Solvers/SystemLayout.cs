using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Solvers;

/// <summary>
/// Which unknown each position in the state vector is, and who owns it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Grouped by kind, not interleaved per node</strong> (<c>31</c>). All branch flows, then all
/// node pressures, then all node enthalpies, each in the graph's own deterministic order. That gives
/// the Jacobian a block structure — the flow/pressure block is the hydraulic problem and the enthalpy
/// block is the thermal one, coupled only weakly through density — which is what a later
/// block-decomposed solver would exploit, and it costs nothing to establish now.
/// </para>
/// <para>
/// <strong>Node ordering is a contract, not an implementation detail.</strong> <c>23</c>'s invariant 6
/// ties both the renderer's placement memory and the solver's variable ordering to lowering's order, so
/// a solver that re-sorted variables for its own reasons — by degree, for fill-in — would move the
/// diagram.
/// </para>
/// <para>
/// <strong>It is built against <see cref="CountingTable"/> rather than beside it.</strong> The table is
/// the same two numbers computed a second way, and the only thing that makes their agreement mean
/// anything is that this layout checks itself against it (<c>S-9</c>). Doing that found <c>S-11</c>
/// before a single residual was evaluated.
/// </para>
/// </remarks>
public sealed class SystemLayout
{
    private SystemLayout(
        ImmutableArray<UnknownDeclaration> unknowns,
        int branchFlowOffset,
        int nodePressureOffset,
        int nodeEnthalpyOffset,
        int componentUnknownOffset,
        int externalFluxOffset,
        ImmutableArray<GraphNode> fluxNodes)
    {
        Unknowns = unknowns;
        BranchFlowOffset = branchFlowOffset;
        NodePressureOffset = nodePressureOffset;
        NodeEnthalpyOffset = nodeEnthalpyOffset;
        ComponentUnknownOffset = componentUnknownOffset;
        ExternalFluxOffset = externalFluxOffset;
        FluxNodes = fluxNodes;
    }

    /// <summary>Gets every unknown, in solve order.</summary>
    public ImmutableArray<UnknownDeclaration> Unknowns { get; }

    /// <summary>Gets the index of the first branch flow.</summary>
    public int BranchFlowOffset { get; }

    /// <summary>Gets the index of the first node pressure.</summary>
    public int NodePressureOffset { get; }

    /// <summary>Gets the index of the first node enthalpy.</summary>
    public int NodeEnthalpyOffset { get; }

    /// <summary>Gets the index of the first unknown a component declared as its own.</summary>
    /// <value>
    /// Placed immediately after the node enthalpies rather than at the end, because the only one v1 has
    /// <em>is</em> an enthalpy — a tank's mixed state — and putting it there keeps the energy block
    /// contiguous, which is the whole reason the unknowns are grouped by kind (<c>D-74</c>).
    /// </value>
    public int ComponentUnknownOffset { get; }

    /// <summary>Gets the index of the first external mass flux.</summary>
    public int ExternalFluxOffset { get; }

    /// <summary>Gets the nodes carrying an external mass flux, in the order their unknowns appear.</summary>
    public ImmutableArray<GraphNode> FluxNodes { get; }

    /// <summary>Gets how many unknowns the system has.</summary>
    public int Count => Unknowns.Length;

    /// <summary>Lays out the unknowns of a graph.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="counting">The counting table, which names the nodes carrying a flux.</param>
    /// <returns>The layout.</returns>
    /// <remarks>
    /// <para>
    /// <strong>An enthalpy level is counted and never allocated, and the difference is the whole of
    /// <c>D-65</c>.</strong> <see cref="CountingTable.Unknowns"/> includes one per closed, steady,
    /// thermally isolated component, so that a script stating no temperature anywhere comes out
    /// under-specified by exactly one and gets <c>FS2211</c>. There is no column for it here, because
    /// the deficiency is in the energy block's <em>rank</em> and not in its width: adding one offset to
    /// every enthalpy satisfies every energy relation at once. A column would be a variable nothing
    /// constrains; the honest object is a rank the assembler expects to be short by one until the
    /// script fixes a temperature.
    /// </para>
    /// </remarks>
    public static SystemLayout Build(CircuitGraph graph, CountingTable counting)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(counting);

        var unknowns = ImmutableArray.CreateBuilder<UnknownDeclaration>(
            graph.Branches.Length + (2 * graph.Nodes.Length) + counting.ComponentUnknowns.Length
            + counting.ExternalFluxes + counting.Promotions.Length);

        var branchFlows = 0;

        foreach (var branch in graph.Branches)
        {
            unknowns.Add(new UnknownDeclaration(
                unknowns.Count,
                UnknownKind.BranchFlow,
                $"{branch.From.Label}->{branch.To.Label}",
                $"branch {branch.Index} flow",
                "kg/s"));
        }

        var pressures = unknowns.Count;

        foreach (var node in graph.Nodes)
        {
            unknowns.Add(new UnknownDeclaration(
                unknowns.Count, UnknownKind.NodePressure, node.Name, $"{node.Name}.p", "Pa"));
        }

        var enthalpies = unknowns.Count;

        foreach (var node in graph.Nodes)
        {
            unknowns.Add(new UnknownDeclaration(
                unknowns.Count, UnknownKind.NodeEnthalpy, node.Name, $"{node.Name}.h", "J/kg"));
        }

        // A control volume's own state. It has to be allocated by whoever allocates the rest, and only
        // the component can say what it is -- which is why the table names these rather than counting
        // them, and why nothing worked until it did (`S-16`, `D-74`).
        var owned = unknowns.Count;

        foreach (var declaration in counting.ComponentUnknowns)
        {
            unknowns.Add(declaration with { Index = unknowns.Count });
        }

        var fluxes = unknowns.Count;

        foreach (var node in counting.FluxNodes)
        {
            unknowns.Add(new UnknownDeclaration(
                unknowns.Count, UnknownKind.ExternalMassFlux, node.Name, $"{node.Name} external flow", "kg/s"));
        }

        foreach (var promotion in counting.Promotions)
        {
            unknowns.Add(new UnknownDeclaration(
                unknowns.Count,
                UnknownKind.Parameter,
                promotion.Component,
                promotion.Label,

                // A promoted parameter's dimension is the registry's, and the registry is tier 10's.
                // The layout does not reach for it: this string is what a report prints beside a
                // number, and an empty one is honest where a wrong unit would not be. P3.7 promotes
                // these for real and fills it in.
                string.Empty));
        }

        return new SystemLayout(
            unknowns.ToImmutable(), branchFlows, pressures, enthalpies, owned, fluxes, counting.FluxNodes);
    }

    /// <summary>Finds the state-vector index of a branch's flow.</summary>
    /// <param name="branch">The branch's index in the graph.</param>
    /// <returns>Its position in the state vector.</returns>
    public int BranchFlow(int branch) => BranchFlowOffset + branch;

    /// <summary>Finds the state-vector index of a node's pressure.</summary>
    /// <param name="node">The node's index in the graph.</param>
    /// <returns>Its position in the state vector.</returns>
    public int NodePressure(int node) => NodePressureOffset + node;

    /// <summary>Finds the state-vector index of a node's enthalpy.</summary>
    /// <param name="node">The node's index in the graph.</param>
    /// <returns>Its position in the state vector.</returns>
    public int NodeEnthalpy(int node) => NodeEnthalpyOffset + node;
}
