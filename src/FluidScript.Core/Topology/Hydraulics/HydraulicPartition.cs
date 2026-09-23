using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Topology.Hydraulics;

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

    /// <summary>The node at the first pump's inlet, in declaration order, or <see langword="null"/> when the part has no pump.</summary>
    /// <remarks>
    /// The inlet is port 0 by the pump's own declaration, and rule I2 puts a node on it. A pump whose
    /// inlet is wired to something that is not a node -- an expansion, a junction element -- yields
    /// nothing rather than a guess, and the pick falls back to <c>23</c>'s rule.
    /// </remarks>
    private static GraphNode? Suction(CircuitGraph graph, HashSet<IFlowComponent> elements, ImmutableArray<GraphNode>.Builder nodes)
    {
        for (var index = 0; index < graph.Components.Length; index++)
        {
            var component = graph.Components[index];

            if (component is not Pump || !elements.Contains(component) || index >= graph.Adjacency.ComponentCount)
            {
                continue;
            }

            var peer = graph.Adjacency.Peer(index, 0);

            if (!peer.Exists)
            {
                continue;
            }

            var inlet = graph.Components[peer.Component];

            foreach (var node in nodes)
            {
                if (ReferenceEquals(node.Component, inlet))
                {
                    return node;
                }
            }
        }

        return null;
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
        // one needs no auto-pick. Otherwise the first pump's suction (`D-98`): the picked datum sits
        // at 0 Pa gauge, the fluid's validated floor is a bar absolute, and in a pumped loop the
        // suction is the low point -- so every other node comes out at or above the datum. Picking the
        // most-connected node instead put the substation's pump suction 21 kPa below water's floor,
        // unsolvable however the solver was seeded. Without a pump, the node with the most connections,
        // ties broken by declaration order: arbitrary, but deterministic and stable across edits.
        var datum = stated.Count > 0
            ? stated[0]
            : Suction(graph, elements, nodes)
            ?? nodes.OrderByDescending(static node => node.Component.Ports.Length).FirstOrDefault();

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
            // `flow` injects mass as surely as a `supply` does, so either is enough to make the circuit
            // open.
            //
            // `D-86` again, and this was its fourth implementation site rather than its third: a stated
            // pressure does *not* let mass in. On a `supply` or a `return` the first clause has already
            // fired; on an interior node the pressure is a datum -- an expansion vessel connection passes
            // no water. Reading it as an opening left `NeedsEnthalpyLevel` returning false on a closed
            // circuit, so no energy balance was dropped, and the uniform-enthalpy-offset redundancy that
            // every closed circuit has stayed in the system as a dependent row (`S-41`).
            IsClosed = !nodes.Any(static node =>
                node.Component.Boundary is not BoundaryRole.Interior
                || Stated(node.Component, Flow) is not null),
            // An unknown flux needs somewhere to enter: a node with no mass balance is interior to a
            // branch, and a branch carries one flow from end to end. A stated `flow` is the flux itself,
            // so a boundary that states one admits mass without leaving anything to solve for.
            //
            // `D-86`: the test is the node's *kind*, not its annotations. A stated pressure on a
            // `supply` or a `return` is a boundary condition and mass crosses at whatever rate holds it;
            // on an interior node it is a datum and nothing enters. Reading a datum as a boundary makes a
            // closed circuit look open, drops no redundant mass balance, and leaves the global
            // conservation identity in the system as a dependent row (`S-39`).
            HasUnknownFlux = nodes.Any(static node =>
                node.Component.CarriesMassBalance
                && Stated(node.Component, Flow) is null
                && node.Component.Boundary is not BoundaryRole.Interior),
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
