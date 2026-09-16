using System.Collections.Immutable;

using FluidScript.Core.Binding;
using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Solvers;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Layout;

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
public static class LayoutHintsDerivation
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

        for (var i = 0; i < model.Connections.Length; i++)
        {
            var connection = model.Connections[i];
            var direction = FlowDirection.None;

            if (branchFlows is { } flows
                && index.ContainsKey(connection.From.Component)
                && index.ContainsKey(connection.To.Component)
                && along.TryGetValue((connection.From.Component, connection.To.Component), out var carried)
                && carried.Branch < flows.Length)
            {
                var signed = flows[carried.Branch] * carried.Sign;

                direction = Math.Abs(signed) <= Tolerances.FlowZero ? FlowDirection.None
                    : signed > 0 ? FlowDirection.Forward
                    : FlowDirection.Reverse;
            }

            flow[$"c{i}"] = direction;
        }

        return flow.ToImmutable();
    }


    // ---- circuits ---------------------------------------------------------------------------------------

    /// <summary>One hint per circuit, with its attachment read from the graph when the script wrote none.</summary>
    /// <remarks>
    /// <c>supply</c>/<c>return</c> lines bind a parent and two anchors (<c>D-33</c>); a subcircuit written
    /// as connections -- which is how a mixing branch has to be written (<c>F-16</c>) -- binds nothing,
    /// and the distribution header would have no group. So a circuit with no stated parent takes the
    /// one it touches: its components connect to another circuit's <em>nodes</em>, and to no other
    /// circuit's. Touching through a node and not through a component is what separates hanging off a
    /// header from being coupled across an exchanger (<c>D-36</c>), and touching one circuit, not two,
    /// is what separates a branch from a bridge. The supply anchor is the parent node feeding one of
    /// the circuit's inlets and the return anchor the one fed by one of its outlets; with no port role
    /// to say, connection order does.
    /// </remarks>
    private static ImmutableArray<CircuitHint> Circuits(
        CircuitGraph graph, SemanticModel model, ImmutableDictionary<string, int> index)
    {
        // Every contact a circuit makes with another circuit's node, in connection order: the parent's
        // node, and whether the port on this circuit's side takes flow in, gives it out, or neither.
        var contacts = model.Circuits.ToDictionary(
            static circuit => circuit.Name,
            static _ => new List<(string Parent, string Node, PortRole Role)>(),
            StringComparer.Ordinal);

        foreach (var connection in model.Connections)
        {
            Contact(connection.From.Component, connection.To.Component);
            Contact(connection.To.Component, connection.From.Component);
        }

        void Contact(string member, string other)
        {
            if (!index.TryGetValue(member, out var at)
                || !index.TryGetValue(other, out var peer)
                || graph.Components[peer] is not CircuitNode
                || !graph.CircuitOf.TryGetValue(member, out var circuit)
                || !graph.CircuitOf.TryGetValue(other, out var parent)
                || string.Equals(circuit, parent, StringComparison.Ordinal)
                || !contacts.TryGetValue(circuit, out var list))
            {
                return;
            }

            var role = PortRole.Bidirectional;

            for (var port = 0; port < graph.Adjacency.PortCount(at); port++)
            {
                var reached = graph.Adjacency.Peer(at, port);

                if (reached.Exists && reached.Component == peer)
                {
                    role = graph.Components[at].Ports[port].Role;
                    break;
                }
            }

            list.Add((parent, other, role));
        }

        string? TouchedParent(string circuit) =>
            contacts.TryGetValue(circuit, out var list)
            && list.Select(static contact => contact.Parent).Distinct(StringComparer.Ordinal).Take(2).ToArray() is [var only]
                ? only
                : null;

        var hints = ImmutableArray.CreateBuilder<CircuitHint>(model.Circuits.Length);

        foreach (var circuit in model.Circuits)
        {
            var parent = circuit.ParentCircuit;
            var supply = circuit.Supply?.ParentComponentName;
            var returned = circuit.Return?.ParentComponentName;

            if (parent is null && supply is null && returned is null
                && TouchedParent(circuit.Name) is { } touched
                && !string.Equals(TouchedParent(touched), circuit.Name, StringComparison.Ordinal))
            {
                var list = contacts[circuit.Name];
                parent = touched;
                supply = (list.FirstOrDefault(static contact => contact.Role == PortRole.Inlet).Node ?? list[0].Node);
                returned = list.LastOrDefault(contact => contact.Role == PortRole.Outlet && !string.Equals(contact.Node, supply, StringComparison.Ordinal)).Node
                    ?? list.LastOrDefault(contact => !string.Equals(contact.Node, supply, StringComparison.Ordinal)).Node
                    ?? supply;
            }

            hints.Add(new CircuitHint
            {
                Name = circuit.Name,
                Number = circuit.Number,
                Role = circuit.Role.Stage == ThermalStageRole.Neutral && circuit.Role.CanonicalName == "neutral"
                    ? null
                    : new CircuitRoleHint(circuit.Role.CanonicalName, circuit.Role.Stage),
                ParentCircuit = parent,
                SupplyAnchorId = supply,
                ReturnAnchorId = returned,
            });
        }

        return hints.ToImmutable();
    }

    /// <summary>Attached circuits grouped by parent, kept only where a parent has two or more (<c>25</c> invariant 11).</summary>
    private static ImmutableArray<DistributionGroup> DistributionGroups(ImmutableArray<CircuitHint> circuits)
    {
        var groups = ImmutableArray.CreateBuilder<DistributionGroup>();

        foreach (var parent in circuits)
        {
            var members = circuits
                .Where(candidate =>
                    string.Equals(candidate.ParentCircuit, parent.Name, StringComparison.Ordinal)
                    && candidate.SupplyAnchorId is not null
                    && candidate.ReturnAnchorId is not null)
                .Select(static candidate => candidate.Name)
                .ToImmutableArray();

            if (members.Length >= 2)
            {
                groups.Add(new DistributionGroup { ParentCircuit = parent.Name, Members = members });
            }
        }

        return groups.ToImmutable();
    }


    // ---- non-flow elements ------------------------------------------------------------------------------

    private static ImmutableArray<NonFlowElementHint> NonFlowElements(SemanticModel model, ImmutableArray<string> order)
    {
        var position = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < order.Length; i++)
        {
            position[order[i]] = i;
        }

        var observers = ModelObservers.Collect(model);
        var elements = new List<(int Anchor, int Declared, NonFlowElementHint Hint)>();
        var declaredAt = 0;

        foreach (var component in model.Components)
        {
            declaredAt++;

            if (component.Kind is { IsObserver: true } && component.AttachedTo is { } node && position.TryGetValue(node, out var at))
            {
                elements.Add((at, declaredAt, new NonFlowElementHint
                {
                    ComponentId = component.Name,
                    PlacementAnchorId = node,
                    MeasurementTargetId = node,
                    ActuationTargetId = null,
                    NavigationOrder = 0,
                }));
            }
        }

        foreach (var binding in model.ControlBindings)
        {
            var actuator = binding.Actuator.Component;
            var measured = ModelObservers.Resolve(binding.Measurement, observers).Component;

            if (!position.TryGetValue(actuator, out var anchor))
            {
                continue;
            }

            elements.Add((anchor, int.MaxValue, new NonFlowElementHint
            {
                ComponentId = binding.Controller.Name,
                PlacementAnchorId = actuator,
                MeasurementTargetId = position.ContainsKey(measured) ? measured : actuator,
                ActuationTargetId = actuator,
                NavigationOrder = 0,
            }));
        }
        // One tab order over flow components and instruments together: each element sits immediately
        // after its anchor, so a position is unique across the whole scene, and a flow component's own
        // position is its Order index plus the elements anchored before it.
        var hints = ImmutableArray.CreateBuilder<NonFlowElementHint>(elements.Count);
        var sorted = elements
            .OrderBy(static element => element.Anchor)
            .ThenBy(static element => element.Declared)
            .ThenBy(static element => element.Hint.ComponentId, StringComparer.Ordinal)
            .ToList();
        var slot = 0;

        foreach (var (anchor, _, hint) in sorted)
        {
            slot = Math.Max(slot + 1, anchor + sorted.Count(element => element.Anchor < anchor) + 1);
            hints.Add(hint with { NavigationOrder = slot });
        }

        return hints.ToImmutable();
    }

    // ---- thermal stages ----------------------------------------------------------------------------------

    /// <summary>The heat-progression stages (<c>D-31</c>), on the thermal group graph <c>25</c> describes.</summary>
    /// <remarks>
    /// <para>
    /// Vertices: each loop collapses to one, each pipe expansion to one, every other component is its
    /// own. Pivots: an extended exchanger is <c>Conversion</c>, a tank <c>Storage</c>. A boundary or a
    /// duty-carrying vertex is classified <em>relative to a pivot</em>: what feeds a pivot's losing or
    /// charging side is <c>Source</c>, what its gaining or discharging side feeds is <c>Consumer</c>.
    /// With no pivot nothing is classified that way, and the circuit's role is the evidence left
    /// (<c>D-35</c>) -- which is why the cooling loop, a chilled-water circuit with no exchanger between
    /// it and anything, is one <c>Neutral</c> stage rather than a source and a consumer either side of
    /// a mixing loop.
    /// </para>
    /// <para>
    /// A role is evidence, not an override. A circuit whose role says <c>Consumer</c> while every duty
    /// it states is positive is a source however it is named, and <c>FS2403</c> says so.
    /// </para>
    /// <para>
    /// Rank: the classified vertices form a directed graph whose edges are heat transfer across a pivot
    /// and transport from one pivot's gaining side to the next pivot's losing side; rank is the longest
    /// path from a vertex with no predecessor. A <c>Neutral</c> vertex takes the rank of its nearest
    /// classified vertex, upstream first.
    /// </para>
    /// </remarks>
    private static ImmutableArray<ThermalStage> ThermalStages(
        CircuitGraph graph,
        SemanticModel model,
        ImmutableDictionary<string, int> index,
        ImmutableArray<ImmutableArray<string>> loops,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var count = graph.Components.Length;

        if (count == 0)
        {
            return [];
        }

        // 1. Vertices. `vertexOf[component]` is the vertex id; members listed in graph order.
        var vertexOf = new int[count];
        Array.Fill(vertexOf, -1);
        var vertices = new List<List<int>>();

        void Collapse(IEnumerable<string> names)
        {
            var members = names
                .Select(name => index.TryGetValue(name, out var i) ? i : -1)
                .Where(i => i >= 0 && vertexOf[i] < 0)
                .OrderBy(static i => i)
                .ToList();

            if (members.Count == 0)
            {
                return;
            }

            foreach (var member in members)
            {
                vertexOf[member] = vertices.Count;
            }

            vertices.Add(members);
        }

        // A loop is not collapsed: a cycle-basis loop can run through two circuits' headers, and one
        // vertex spanning two circuits could carry neither's role. Loop members are banded in step 7.
        foreach (var group in graph.Groups)
        {
            Collapse(group.Members);
        }

        for (var i = 0; i < count; i++)
        {
            if (vertexOf[i] < 0)
            {
                Collapse([graph.Components[i].Name]);
            }
        }

        // 2. Pivots and sides. For an exchanger, side 2 (ports 2, 3) loses heat when the duty is positive;
        // for a tank, in{n} ports charge it. Each pivot's hot and cold neighbours are the vertices its
        // sides peer into.
        var role = new ThermalStageRole[vertices.Count];
        var hot = new List<int>[vertices.Count];
        var cold = new List<int>[vertices.Count];

        for (var v = 0; v < vertices.Count; v++)
        {
            hot[v] = [];
            cold[v] = [];
        }

        for (var i = 0; i < count; i++)
        {
            var component = graph.Components[i];
            var v = vertexOf[i];

            switch (component)
            {
                case HeatExchanger exchanger when exchanger.SecondarySideConnected || exchanger.Rating is { CanRate: true }:
                    role[v] = ThermalStageRole.Conversion;

                    for (var port = 0; port < exchanger.Ports.Length; port++)
                    {
                        var peer = graph.Adjacency.Peer(i, port);

                        if (!peer.Exists || vertexOf[peer.Component] == v)
                        {
                            continue;
                        }

                        var losing = (port >= 2) == (exchanger.Power >= 0);
                        (losing ? hot[v] : cold[v]).Add(vertexOf[peer.Component]);
                    }

                    break;

                case Tank tank:
                    role[v] = ThermalStageRole.Storage;

                    for (var port = 0; port < tank.Ports.Length; port++)
                    {
                        var peer = graph.Adjacency.Peer(i, port);

                        if (!peer.Exists || vertexOf[peer.Component] == v)
                        {
                            continue;
                        }

                        var charging = tank.Ports[port].Name.StartsWith("in", StringComparison.Ordinal);
                        (charging ? hot[v] : cold[v]).Add(vertexOf[peer.Component]);
                    }

                    break;
            }
        }

        // 3. Transport adjacency between vertices, in both directions, for reachability.
        var adjacent = new HashSet<int>[vertices.Count];

        for (var v = 0; v < vertices.Count; v++)
        {
            adjacent[v] = [];
        }

        for (var i = 0; i < count; i++)
        {
            foreach (var next in Neighbours(graph, i))
            {
                if (vertexOf[next] != vertexOf[i])
                {
                    adjacent[vertexOf[i]].Add(vertexOf[next]);
                }
            }
        }

        // What each pivot's hot side and cold side reach without crossing another pivot.
        var pivots = Enumerable.Range(0, vertices.Count).Where(v => role[v] != ThermalStageRole.Neutral).ToList();
        var hotReach = new HashSet<int>[vertices.Count];
        var coldReach = new HashSet<int>[vertices.Count];

        foreach (var pivot in pivots)
        {
            hotReach[pivot] = Reach(hot[pivot], role, adjacent);
            coldReach[pivot] = Reach(cold[pivot], role, adjacent);
        }

        // 4. Classify the rest: relative to pivots first, then by circuit role with the duty as a check.
        for (var v = 0; v < vertices.Count; v++)
        {
            if (role[v] != ThermalStageRole.Neutral)
            {
                continue;
            }

            var feeds = pivots.Any(pivot => hotReach[pivot].Contains(v));
            var fed = pivots.Any(pivot => coldReach[pivot].Contains(v));

            if (feeds != fed)
            {
                role[v] = feeds ? ThermalStageRole.Source : ThermalStageRole.Consumer;
                continue;
            }

            var circuit = CircuitOf(graph, vertices[v]);

            if (circuit is null)
            {
                continue;
            }

            var evidence = model.Circuits.FirstOrDefault(candidate => string.Equals(candidate.Name, circuit, StringComparison.Ordinal))?.Role;

            if (evidence is null || evidence.Stage == ThermalStageRole.Neutral)
            {
                continue;
            }

            var duty = vertices[v]
                .Select(i => graph.Components[i])
                .OfType<HeatExchanger>()
                .Sum(static exchanger => exchanger.Power);

            var physics = duty > 0 ? ThermalStageRole.Source : duty < 0 ? ThermalStageRole.Consumer : evidence.Stage;

            if (evidence.Stage is ThermalStageRole.Source or ThermalStageRole.Consumer && physics != evidence.Stage)
            {
                role[v] = physics;

                diagnostics.Add(Diagnostic.Create(
                    LayoutDiagnostics.RoleContradictsDuty,
                    span: null,
                    new DiagnosticArgument("circuit", circuit),
                    new DiagnosticArgument("role", evidence.CanonicalName),
                    new DiagnosticArgument("stage", physics.ToString().ToLowerInvariant())));
            }
            else
            {
                role[v] = evidence.Stage;
            }
        }

        // 5. Rank the classified vertices by longest path over heat progression: across each pivot from
        // its hot side to its cold side, and from one pivot's cold side to the next pivot's hot side.
        var successors = new HashSet<int>[vertices.Count];

        for (var v = 0; v < vertices.Count; v++)
        {
            successors[v] = [];
        }

        foreach (var pivot in pivots)
        {
            foreach (var upstream in hotReach[pivot].Where(u => role[u] != ThermalStageRole.Neutral))
            {
                successors[upstream].Add(pivot);
            }

            foreach (var downstream in coldReach[pivot].Where(d => role[d] != ThermalStageRole.Neutral))
            {
                successors[pivot].Add(downstream);
            }
        }

        // Where no pivot orders them, class order does: a source before a consumer in one circuit.
        var classified = Enumerable.Range(0, vertices.Count).Where(v => role[v] != ThermalStageRole.Neutral).ToList();

        foreach (var a in classified)
        {
            foreach (var b in classified)
            {
                if (a != b && ClassOrder.IndexOf(role[a]) < ClassOrder.IndexOf(role[b]) && Connected(a, b, adjacent))
                {
                    successors[a].Add(b);
                }
            }
        }

        var rank = new int[vertices.Count];
        Array.Fill(rank, -1);

        foreach (var v in classified.OrderBy(v => v))
        {
            LongestPath(v, successors, rank, []);
        }

        // 6. Neutral vertices take the rank of the nearest classified vertex, breadth-first.
        var pending = Enumerable.Range(0, vertices.Count).Where(v => rank[v] < 0).ToList();

        if (classified.Count == 0)
        {
            foreach (var v in pending)
            {
                rank[v] = 0;
            }
        }
        else
        {
            var frontier = new Queue<int>(classified.OrderBy(v => v));
            var assigned = new HashSet<int>(classified);

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();

                foreach (var next in adjacent[current].OrderBy(static n => n))
                {
                    if (assigned.Add(next))
                    {
                        rank[next] = rank[current];
                        frontier.Enqueue(next);
                    }
                }
            }

            foreach (var v in pending.Where(v => rank[v] < 0))
            {
                rank[v] = 0;
            }
        }

        // 7. A loop is one band (25): its members that are not pivots share the highest rank among
        // them, so a recirculation loop hanging off an exchanger sits wholly on the exchanger's cold side.
        foreach (var loop in loops)
        {
            var members = loop
                .Select(name => index.TryGetValue(name, out var i) ? vertexOf[i] : -1)
                .Where(v => v >= 0 && !pivots.Contains(v))
                .Distinct()
                .ToList();

            if (members.Count > 1)
            {
                var band = members.Max(v => rank[v]);

                foreach (var member in members)
                {
                    rank[member] = band;
                }
            }
        }

        // 8. Stages: one per (rank, role), members in graph order.
        return
        [
            .. Enumerable.Range(0, vertices.Count)
                .GroupBy(v => (rank[v], Role: role[v]))
                .OrderBy(static group => group.Key.Item1)
                .ThenBy(static group => ClassOrder.IndexOf(group.Key.Role) is var at && at < 0 ? ClassOrder.Length : at)
                .Select(group => new ThermalStage
                {
                    Rank = group.Key.Item1,
                    Role = group.Key.Role,
                    Components = [.. group.SelectMany(v => vertices[v]).OrderBy(static i => i).Select(i => graph.Components[i].Name)],
                }),
        ];
    }

    /// <summary>The vertices reachable from a set of starting vertices without entering a pivot.</summary>
    private static HashSet<int> Reach(List<int> starts, ThermalStageRole[] role, HashSet<int>[] adjacent)
    {
        var reached = new HashSet<int>();
        var queue = new Queue<int>();

        foreach (var start in starts.Where(start => role[start] == ThermalStageRole.Neutral))
        {
            if (reached.Add(start))
            {
                queue.Enqueue(start);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var next in adjacent[current])
            {
                if (role[next] == ThermalStageRole.Neutral && reached.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return reached;
    }

    private static bool Connected(int from, int to, HashSet<int>[] adjacent)
    {
        var seen = new HashSet<int> { from };
        var queue = new Queue<int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current == to)
            {
                return true;
            }

            foreach (var next in adjacent[current])
            {
                if (seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return false;
    }

    private static int LongestPath(int vertex, HashSet<int>[] successors, int[] rank, HashSet<int> onPath)
    {
        if (rank[vertex] >= 0)
        {
            return rank[vertex];
        }

        // A cycle among classified vertices -- two exchangers feeding each other -- is cut where it is
        // met, so the walk terminates; the rank it produces is then a choice, and a deterministic one.
        if (!onPath.Add(vertex))
        {
            return 0;
        }

        var depth = 0;

        foreach (var predecessor in Enumerable.Range(0, successors.Length).Where(p => successors[p].Contains(vertex)).OrderBy(static p => p))
        {
            depth = Math.Max(depth, LongestPath(predecessor, successors, rank, onPath) + 1);
        }

        onPath.Remove(vertex);
        rank[vertex] = depth;

        return depth;
    }

    /// <summary>The one circuit every member of a vertex belongs to, or <see langword="null"/> when they differ.</summary>
    private static string? CircuitOf(CircuitGraph graph, List<int> members)
    {
        string? circuit = null;

        foreach (var member in members)
        {
            if (!graph.CircuitOf.TryGetValue(graph.Components[member].Name, out var owner))
            {
                return null;
            }

            if (circuit is null)
            {
                circuit = owner;
            }
            else if (!string.Equals(circuit, owner, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return circuit;
    }
}
