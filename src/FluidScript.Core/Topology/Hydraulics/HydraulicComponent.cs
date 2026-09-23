using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Topology.Hydraulics;

/// <summary>One set of graph elements that fluid can flow between.</summary>
/// <remarks>
/// <para>
/// <strong>A model may hold several of these, and that is not an error</strong> (<c>D-17</c>). A rated
/// heat exchanger joins two streams that never mix, so the substation's primary and secondary share no
/// node and no flow — only <c>HX1</c>, and only thermally. Every rule that says "one per circuit" is
/// really one per <em>hydraulic</em> component: the pressure datum, the mass-balance redundancy, and
/// the loop-driver check. The energy system is the exception and spans all of them, because that is
/// exactly what the exchanger couples.
/// </para>
/// <para>
/// Derived from the branch decomposition rather than from raw adjacency, which is what keeps a
/// two-sided component in two of these at once: a branch joins two junction elements, and an exchanger
/// is a junction element on neither side.
/// </para>
/// </remarks>
public sealed record HydraulicComponent
{
    /// <summary>Gets this component's position in the graph's partition, from zero.</summary>
    /// <value>Assigned in the order the components' lowest-indexed vertices appear, so it is stable.</value>
    public required int Index { get; init; }

    /// <summary>Gets every element flow reaches inside this component, in graph order.</summary>
    /// <value>
    /// Vertices and the elements interior to its branches alike. A coupled exchanger appears in the
    /// list of <em>both</em> the components it separates, which is what stops either looking isolated.
    /// </value>
    public required ImmutableArray<IFlowComponent> Elements { get; init; }

    /// <summary>Gets the branches whose ends are both in this component, in graph order.</summary>
    public required ImmutableArray<Branch> Branches { get; init; }

    /// <summary>Gets the nodes in this component, in graph order.</summary>
    public required ImmutableArray<GraphNode> Nodes { get; init; }

    /// <summary>Gets the node the pressure field is measured from.</summary>
    /// <value>
    /// The first node stating a pressure, or the auto-picked one. Never empty for a component holding
    /// a node; a component with none — which nothing produces today — leaves it empty rather than
    /// inventing a name.
    /// </value>
    public required string Datum { get; init; }

    /// <summary>Gets whether the datum came from a stated pressure rather than being picked.</summary>
    /// <value>
    /// <see langword="false"/> is what raises <c>FS2201</c>. It also decides the mass-balance
    /// redundancy: a component with no stated pressure has no unknown external flux, so one of its
    /// balances is implied by the others and the datum takes its place.
    /// </value>
    public required bool DatumWasStated { get; init; }

    /// <summary>Gets the nodes that state a pressure, in graph order.</summary>
    /// <value>
    /// Each one admits an unknown external mass flux, and each is a boundary condition rather than a
    /// competing datum: the cooling loop states two, and must.
    /// </value>
    public required ImmutableArray<GraphNode> StatedPressures { get; init; }

    /// <summary>Gets the nodes the script declared as a <c>supply</c> or a <c>return</c>, in graph order.</summary>
    /// <value>Empty for a closed circuit, which needs neither (<c>D-64</c>).</value>
    public required ImmutableArray<GraphNode> Boundaries { get; init; }

    /// <summary>Gets whether no mass crosses this component's boundary at all.</summary>
    /// <value>
    /// <see langword="true"/> when nothing in it is a boundary and nothing states a pressure or a flow.
    /// Stronger than having no datum: a stated <c>flow</c> injects mass as surely as a <c>supply</c>
    /// does, and a closed circuit is the one whose duties must sum to zero for a steady state to exist.
    /// </value>
    public required bool IsClosed { get; init; }

    /// <summary>Gets whether any external mass flux here is a solver unknown.</summary>
    /// <value>
    /// <see langword="true"/> when some node that carries a mass balance is a <c>supply</c> or a
    /// <c>return</c> and does not state the flow crossing it. This is what decides the mass-balance
    /// redundancy: with every flux known, summing the balances gives an identity and one of them is
    /// implied by the rest — and a storage header whose every boundary states a flow is that case,
    /// however many boundaries it has.
    /// </value>
    /// <remarks>
    /// <strong>A stated pressure does not make a node admit mass, and reading it that way cost three
    /// sessions</strong> (<c>D-86</c>, <c>S-39</c>). On a <c>supply</c> or a <c>return</c> a pressure is a
    /// boundary condition and mass crosses at whatever rate holds it; on an <em>interior</em> node it is a
    /// <em>datum</em>, the reference the circuit's pressures are measured from, and nothing enters there.
    /// An expansion vessel connection passes no water. Reading both as boundaries models a closed circuit
    /// annotated with its own datum as open, so no redundant balance is dropped and the global
    /// conservation identity stays in the system as a dependent row.
    /// </remarks>
    public required bool HasUnknownFlux { get; init; }
}
