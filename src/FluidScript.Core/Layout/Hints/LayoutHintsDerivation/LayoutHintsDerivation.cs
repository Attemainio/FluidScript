using System.Collections.Immutable;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Solvers;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Layout.Hints;

/// <summary>Derives <see cref="LayoutHints"/> from a lowered graph, its model, and the solved branch flows (<c>25</c>).</summary>
/// <remarks>
/// <para>
/// <strong>Deterministic by construction.</strong> Every traversal walks components in graph order and
/// ports in declaration order; every tie breaks on the component name, ordinally; no dictionary is
/// iterated for output. <c>25</c>'s <em>ordering determinism</em> section names these three as the
/// sources of a diagram that jumps on every keystroke, and this file has no fourth.
/// </para>
/// <para>
/// <strong>It reports and never throws.</strong> A hint that cannot be computed is omitted; the three
/// codes here are informational, because the user can see the consequence of each and would otherwise
/// wonder. A graph lowered from a half-written script reaches this on every keystroke.
/// </para>
/// </remarks>
public static partial class LayoutHintsDerivation
{
    /// <summary>The classes on the heat-progression axis, in the order stages are ranked.</summary>
    private static readonly ImmutableArray<ThermalStageRole> ClassOrder =
        [ThermalStageRole.Source, ThermalStageRole.Conversion, ThermalStageRole.Storage, ThermalStageRole.Consumer];

    /// <summary>Derives the hints.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="model">The bound model the graph was lowered from.</param>
    /// <param name="branchFlows">The solved branch flows, kg/s, indexed by <see cref="Branch.Index"/>, or <see langword="null"/> when nothing is solved.</param>
    /// <returns>The hints and the informational diagnostics raised deriving them.</returns>
    public static (LayoutHints Hints, ImmutableArray<Diagnostic> Diagnostics) Derive(
        CircuitGraph graph, SemanticModel model, ImmutableArray<double>? branchFlows)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(model);

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var hydraulics = HydraulicPartition.Of(graph);
        var index = Index(graph);
        var order = Order(graph, hydraulics, index, graph.Loops.Length > 0, diagnostics);
        var loops = Loops(graph, order);

        var circuits = Circuits(graph, model, index);
        var stages = ThermalStages(graph, model, index, loops, diagnostics);

        // 25's collapse thresholds: a group past ten members, or a scene past five hundred, starts folded.
        foreach (var group in graph.Groups.Where(static group => group.Members.Length > 10))
        {
            diagnostics.Add(Diagnostic.Create(
                LayoutDiagnostics.RendersCollapsed,
                span: null,
                new DiagnosticArgument("group", group.Source),
                new DiagnosticArgument("count", group.Members.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        if (graph.Components.Length > 500)
        {
            diagnostics.Add(Diagnostic.Create(
                LayoutDiagnostics.RendersCollapsed,
                span: null,
                new DiagnosticArgument("group", graph.Name),
                new DiagnosticArgument("count", graph.Components.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        return (new LayoutHints
        {
            Order = order,

            ThermalStages = stages,
            Flow = Flow(graph, model, index, branchFlows),

            Groups = [.. graph.Groups.Select(static group => new ComponentGroupHint
            {
                ParentComponentId = group.Source,
                Children = group.Members,
            })],
            NonFlowElements = NonFlowElements(model, order),
            CircuitOf = graph.CircuitOf,
            Circuits = circuits,
            DistributionGroups = DistributionGroups(circuits),
            Inferred = [.. model.Components
                .Where(static component => component.Origin is Origin.Inferred && component.Kind is not null)
                .Select(static component => component.Name)
                .Where(index.ContainsKey)],

        }, diagnostics.ToImmutable());
    }

    // ---- order and rank ------------------------------------------------------------------------------

    private static ImmutableDictionary<string, int> Index(CircuitGraph graph)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < graph.Components.Length; i++)
        {
            builder[graph.Components[i].Name] = i;
        }

        return builder.ToImmutable();
    }

    /// <summary>The components a component's ports reach, in port order.</summary>
    private static IEnumerable<int> Neighbours(CircuitGraph graph, int component)
    {
        for (var port = 0; port < graph.Adjacency.PortCount(component); port++)
        {
            var peer = graph.Adjacency.Peer(component, port);

            if (peer.Exists)
            {
                yield return peer.Component;
            }
        }
    }

    private static ImmutableArray<string> Order(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ImmutableDictionary<string, int> index,
        bool hasLoops,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var order = ImmutableArray.CreateBuilder<string>(graph.Components.Length);
        var visited = new bool[graph.Components.Length];

        // Each hydraulic part from its datum, in part order; anything the parts do not reach -- a stub
        // the factory could not build past -- in graph order, so every component appears exactly once.
        var roots = hydraulics
            .Select(part => index.TryGetValue(part.Datum, out var datum) ? datum : -1)
            .Where(static datum => datum >= 0)
            .Concat(Enumerable.Range(0, graph.Components.Length));

        foreach (var root in roots)
        {
            if (visited[root])
            {
                continue;
            }

            // An explicit stack so a 900-component pipe run cannot overflow the call stack (L-48's class).
            var stack = new Stack<(int Component, IEnumerator<int> Next)>();
            visited[root] = true;
            order.Add(graph.Components[root].Name);
            stack.Push((root, Neighbours(graph, root).GetEnumerator()));

            while (stack.Count > 0)
            {
                var (_, next) = stack.Peek();

                if (!next.MoveNext())
                {
                    stack.Pop();
                    continue;
                }

                var reached = next.Current;

                if (visited[reached])
                {
                    continue;
                }

                visited[reached] = true;
                order.Add(graph.Components[reached].Name);
                stack.Push((reached, Neighbours(graph, reached).GetEnumerator()));
            }
        }

        if (hasLoops)
        {
            diagnostics.Add(Diagnostic.Create(LayoutDiagnostics.NoTopologicalOrder, span: null));
        }

        return order.ToImmutable();
    }


    // ---- loops -----------------------------------------------------------------------------------------

    /// <summary>Each cycle-basis loop as one closed walk of component names.</summary>
    /// <remarks>
    /// A basis loop's branches are not oriented consistently -- the tree path between a chord's ends
    /// runs whichever way the search built it -- so the walk follows each branch in the direction that
    /// continues from where the previous one ended.
    /// </remarks>
    private static ImmutableArray<ImmutableArray<string>> Loops(CircuitGraph graph, ImmutableArray<string> order)
    {
        var loops = ImmutableArray.CreateBuilder<ImmutableArray<string>>(graph.Loops.Length);
        var position = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < order.Length; i++)
        {
            position[order[i]] = i;
        }

        foreach (var loop in graph.Loops)
        {
            if (loop.Branches.IsEmpty)
            {
                continue;
            }

            var members = new List<string>();
            var current = loop.Branches[0].From.Element;

            foreach (var branch in loop.Branches)
            {
                var forward = ReferenceEquals(branch.From.Element, current);
                var start = forward ? branch.From.Element : branch.To.Element;
                var path = forward ? branch.Path : branch.Path.Reverse();

                members.Add(start.Name);
                members.AddRange(path.Select(static part => part.Name));
                current = forward ? branch.To.Element : branch.From.Element;
            }

            // The walk starts at the member met first in Order and runs the way Order runs, so which
            // chord the cycle basis happened to close on cannot rotate or reverse it.
            var first = members.IndexOf(members.MinBy(name => position.GetValueOrDefault(name, int.MaxValue))!);
            var rotated = members.Skip(first).Concat(members.Take(first)).ToList();

            if (rotated.Count > 2
                && position.GetValueOrDefault(rotated[1], int.MaxValue) > position.GetValueOrDefault(rotated[^1], int.MaxValue))
            {
                rotated.Reverse(1, rotated.Count - 1);
            }

            loops.Add([.. rotated]);
        }

        return loops.ToImmutable();
    }


    // ---- flow and ports -------------------------------------------------------------------------------

    /// <summary>Solved direction per written connection, keyed <c>c{n}</c> by position in the model's connection list.</summary>
    private static ImmutableDictionary<string, FlowDirection> Flow(
        CircuitGraph graph,
        SemanticModel model,
        ImmutableDictionary<string, int> index,
        ImmutableArray<double>? branchFlows)
    {
        // Every adjacent pair along a branch, in the branch's own direction, to the flow it carries.
        var along = new Dictionary<(string From, string To), (int Branch, int Sign)>();

        foreach (var branch in graph.Branches)
        {
            var sequence = new[] { branch.From.Element.Name }
                .Concat(branch.Path.Select(static part => part.Name))
                .Append(branch.To.Element.Name)
                .ToArray();

            for (var i = 0; i + 1 < sequence.Length; i++)
            {
                along.TryAdd((sequence[i], sequence[i + 1]), (branch.Index, 1));
                along.TryAdd((sequence[i + 1], sequence[i]), (branch.Index, -1));
            }
        }

        var flow = ImmutableDictionary.CreateBuilder<string, FlowDirection>(StringComparer.Ordinal);

        FlowDirection Direction(string from, string to) =>
            branchFlows is { } flows
            && along.TryGetValue((from, to), out var carried)
            && carried.Branch < flows.Length
                ? Math.Abs(flows[carried.Branch] * carried.Sign) <= Tolerances.FlowZero ? FlowDirection.None
                    : flows[carried.Branch] * carried.Sign > 0 ? FlowDirection.Forward
                    : FlowDirection.Reverse
                : FlowDirection.None;

        for (var i = 0; i < model.Connections.Length; i++)
        {
            var connection = model.Connections[i];

            // An end naming a pipe with `nodes=` is read at the cell that meets the other end (`C-124`).
            var from = ExpandedPipes.Drawn(graph, connection.From.Component, connection.To.Component);
            var to = ExpandedPipes.Drawn(graph, connection.To.Component, connection.From.Component);

            flow[$"c{i}"] = from is not null && to is not null && index.ContainsKey(from) && index.ContainsKey(to)
                ? Direction(from, to)
                : FlowDirection.None;
        }

        // The links between an expanded pipe's cells, which the diagram draws as routes of their own.
        foreach (var (id, from, _, to, _) in ExpandedPipes.Internal(graph))
        {
            flow[id] = Direction(graph.Components[from].Name, graph.Components[to].Name);
        }

        return flow.ToImmutable();
    }
}
