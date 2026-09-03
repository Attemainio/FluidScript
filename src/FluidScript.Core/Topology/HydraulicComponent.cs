using System.Collections.Immutable;

using FluidScript.Core.Components;

namespace FluidScript.Core.Topology;

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
    /// <see langword="true"/> when some node that carries a mass balance is a <c>return</c> or states a
    /// pressure, and does not state the flow crossing it. This is what decides the mass-balance
    /// redundancy: with every flux known, summing the balances gives an identity and one of them is
    /// implied by the rest — and a storage header whose every boundary states a flow is that case,
    /// however many boundaries it has.
    /// </value>
    public required bool HasUnknownFlux { get; init; }
}

/// <summary>Splits a graph into the sets of elements fluid can flow between.</summary>
public static class HydraulicPartition
{
    /// <summary>The parameter a node states to fix its pressure.</summary>
    public const string Pressure = "p";

    /// <summary>The parameter a node states to inject or extract a known mass flow.</summary>
    public const string Flow = "flow";

    /// <summary>The parameter a node or an exchanger port states to fix a temperature.</summary>
    public const string Temperature = "t";

    /// <summary>Partitions a graph by what flow can reach.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <returns>One entry per hydraulic connected component, in a stable order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    public static ImmutableArray<HydraulicComponent> Of(CircuitGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var vertices = graph.JunctionElements;
        var owner = new int[vertices.Length];
        var index = new Dictionary<IFlowComponent, int>(vertices.Length);

        for (var i = 0; i < vertices.Length; i++)
        {
            owner[i] = i;
            index[vertices[i]] = i;
        }

        foreach (var branch in graph.Branches)
        {
            if (index.TryGetValue(branch.From.Element, out var from)
                && index.TryGetValue(branch.To.Element, out var to))
            {
                Union(owner, from, to);
            }
        }

        // Numbered by the order their lowest-indexed vertex appears, so the partition is as stable as
        // the element order lowering guarantees -- which the renderer and the solver both key off.
        var numbers = new Dictionary<int, int>(vertices.Length);
        for (var i = 0; i < vertices.Length; i++)
        {
            var root = Find(owner, i);

            if (!numbers.ContainsKey(root))
            {
                numbers[root] = numbers.Count;
            }
        }

        var elements = new List<HashSet<IFlowComponent>>();
        var branches = new List<List<Branch>>();

        for (var i = 0; i < numbers.Count; i++)
        {
            elements.Add([]);
            branches.Add([]);
        }

        for (var i = 0; i < vertices.Length; i++)
        {
            elements[numbers[Find(owner, i)]].Add(vertices[i]);
        }

        foreach (var branch in graph.Branches)
        {
            if (!index.TryGetValue(branch.From.Element, out var from))
            {
                continue;
            }

            var number = numbers[Find(owner, from)];
            branches[number].Add(branch);

            foreach (var element in branch.Path)
            {
                elements[number].Add(element);
            }
        }

        var partition = ImmutableArray.CreateBuilder<HydraulicComponent>(numbers.Count);

        for (var i = 0; i < numbers.Count; i++)
        {
            partition.Add(Assemble(graph, i, elements[i], branches[i]));
        }

        return partition.ToImmutable();
    }

    /// <summary>Reads a component's stated parameter, in SI.</summary>
    /// <param name="component">The component to read.</param>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <returns>The value, or <see langword="null"/> when the script did not state it.</returns>
    /// <remarks>
    /// Absence rather than a sentinel (<c>D-02</c>). A default the registry supplies is deliberately
    /// not consulted here: well-posedness counts what the user asserted, and a decided default asserts
    /// nothing.
    /// </remarks>
    public static double? Stated(IFlowComponent component, string parameter)
    {
        ArgumentNullException.ThrowIfNull(component);

        return component.StatedParameters.TryGetValue(parameter, out var quantity)
            ? quantity.SiValue
            : null;
    }

    /// <summary>Builds one component, choosing its datum.</summary>
    private static HydraulicComponent Assemble(
        CircuitGraph graph, int number, HashSet<IFlowComponent> elements, List<Branch> branches)
    {
        var nodes = ImmutableArray.CreateBuilder<GraphNode>();
        var stated = ImmutableArray.CreateBuilder<GraphNode>();

        foreach (var node in graph.Nodes)
        {
            if (!elements.Contains(node.Component))
            {
                continue;
            }

            nodes.Add(node);

            if (Stated(node.Component, Pressure) is not null)
            {
                stated.Add(node);
            }
        }

        // The first stated pressure is the datum as well as a boundary condition, so a circuit with
        // one needs no auto-pick. Otherwise the node with the most connections, ties broken by
        // declaration order: arbitrary, but deterministic and stable across edits, which is what stops
        // an unrelated keystroke renumbering every pressure in the result.
        var datum = stated.Count > 0
            ? stated[0]
            : nodes.OrderByDescending(static node => node.Component.Ports.Length).FirstOrDefault();

        return new HydraulicComponent
        {
            Index = number,
            Elements = [.. graph.Components.Where(elements.Contains)],
            Branches = [.. branches],
            Nodes = nodes.ToImmutable(),
            Datum = datum?.Name ?? string.Empty,
            DatumWasStated = stated.Count > 0,
            StatedPressures = stated.ToImmutable(),
            Boundaries = [.. nodes.Where(static node =>
                node.Component.Boundary is not BoundaryRole.Interior)],

            // Closed means no external mass at all, which is stronger than having no datum. A stated
            // `flow` injects mass as surely as a `supply` does, and a stated `p` lets mass in to hold
            // the pressure -- so any of the three is enough to make the circuit open.
            IsClosed = !nodes.Any(static node =>
                node.Component.Boundary is not BoundaryRole.Interior
                || Stated(node.Component, Pressure) is not null
                || Stated(node.Component, Flow) is not null),

            // An unknown flux needs somewhere to enter: a node with no mass balance is interior to a
            // branch, and a branch carries one flow from end to end. A stated `flow` is the flux itself,
            // so a boundary that states one admits mass without leaving anything to solve for.
            HasUnknownFlux = nodes.Any(static node =>
                node.Component.CarriesMassBalance
                && Stated(node.Component, Flow) is null
                && (node.Component.Boundary is BoundaryRole.Return
                    || Stated(node.Component, Pressure) is not null)),
        };
    }

    private static int Find(int[] owner, int item)
    {
        while (owner[item] != item)
        {
            owner[item] = owner[owner[item]];
            item = owner[item];
        }

        return item;
    }

    private static void Union(int[] owner, int left, int right)
    {
        var a = Find(owner, left);
        var b = Find(owner, right);

        if (a != b)
        {
            owner[Math.Max(a, b)] = Math.Min(a, b);
        }
    }
}
