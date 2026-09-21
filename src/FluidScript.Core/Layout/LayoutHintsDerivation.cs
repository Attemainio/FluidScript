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
                InletAnchorId = supply,
                OutletAnchorId = returned,
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
                    && candidate.InletAnchorId is not null
                    && candidate.OutletAnchorId is not null)
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
        if (graph.Components.Length == 0)
        {
            return [];
        }

        var stages = CollapseVertices(graph, index);
        ClassifyPivots(graph, stages);
        LinkTransport(graph, stages);
        ClassifyByReach(graph, model, stages, diagnostics);
        RankClassified(stages);
        FillNeutral(stages);
        BandLoops(index, loops, stages);
        return Stages(graph, stages);
    }

    /// <summary>The working state of <see cref="ThermalStages"/>: the collapsed vertices and what each phase decided about them.</summary>
    /// <param name="vertexOf">The vertex of each component, by component index.</param>
    /// <param name="vertices">Each vertex's members, in graph order.</param>
    private sealed class StageGraph(int[] vertexOf, List<List<int>> vertices)
    {
        /// <summary>Gets the vertex of each component, by component index.</summary>
        public int[] VertexOf { get; } = vertexOf;

        /// <summary>Gets each vertex's members, in graph order.</summary>
        public List<List<int>> Vertices { get; } = vertices;

        /// <summary>Gets the number of vertices.</summary>
        public int Count => Vertices.Count;

        /// <summary>Gets each vertex's role; <see cref="ThermalStageRole.Neutral"/> until a phase decides it.</summary>
        public ThermalStageRole[] Role { get; } = new ThermalStageRole[vertices.Count];

        /// <summary>Gets, per pivot, the vertices its losing side peers into.</summary>
        public List<int>[] Hot { get; } = Lists(vertices.Count);

        /// <summary>Gets, per pivot, the vertices its gaining side peers into.</summary>
        public List<int>[] Cold { get; } = Lists(vertices.Count);

        /// <summary>Gets the transport adjacency between vertices, in both directions.</summary>
        public HashSet<int>[] Adjacent { get; } = Sets(vertices.Count);

        /// <summary>Gets the pivots: the vertices classified as conversion or storage before the rest.</summary>
        public List<int> Pivots { get; set; } = [];

        /// <summary>Gets what each pivot's hot side reaches without crossing another pivot.</summary>
        public HashSet<int>[] HotReach { get; } = new HashSet<int>[vertices.Count];

        /// <summary>Gets what each pivot's cold side reaches without crossing another pivot.</summary>
        public HashSet<int>[] ColdReach { get; } = new HashSet<int>[vertices.Count];

        /// <summary>Gets the vertices ranked in the last classification, in vertex order.</summary>
        public List<int> Classified { get; set; } = [];

        /// <summary>Gets each vertex's rank; <c>-1</c> until assigned.</summary>
        public int[] Rank { get; } = Unranked(vertices.Count);

        /// <summary>The vertices whose role is decided, in vertex order.</summary>
        /// <returns>The classified vertices.</returns>
        public List<int> Decided() => Enumerable.Range(0, Count).Where(v => Role[v] != ThermalStageRole.Neutral).ToList();

        private static List<int>[] Lists(int count)
        {
            var lists = new List<int>[count];

            for (var v = 0; v < count; v++)
            {
                lists[v] = [];
            }

            return lists;
        }

        private static HashSet<int>[] Sets(int count)
        {
            var sets = new HashSet<int>[count];

            for (var v = 0; v < count; v++)
            {
                sets[v] = [];
            }

            return sets;
        }

        private static int[] Unranked(int count)
        {
            var rank = new int[count];
            Array.Fill(rank, -1);
            return rank;
        }
    }

    /// <summary>Phase 1 of <see cref="ThermalStages"/>: every group is one vertex and every other component its own.</summary>
    /// <remarks>A loop is not collapsed: a cycle-basis loop can run through two circuits' headers, and one vertex spanning two circuits could carry neither's role. Loop members are banded in <see cref="BandLoops"/>.</remarks>
    private static StageGraph CollapseVertices(CircuitGraph graph, ImmutableDictionary<string, int> index)
    {
        var count = graph.Components.Length;
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

        return new StageGraph(vertexOf, vertices);
    }

    /// <summary>Phase 2 of <see cref="ThermalStages"/>: pivots and their sides.</summary>
    /// <remarks>For an exchanger, side 2 (ports 2, 3) loses heat when the duty is positive; for a tank, <c>in{n}</c> ports charge it. Each pivot's hot and cold neighbours are the vertices its sides peer into.</remarks>
    private static void ClassifyPivots(CircuitGraph graph, StageGraph stages)
    {
        for (var i = 0; i < graph.Components.Length; i++)
        {
            var component = graph.Components[i];
            var v = stages.VertexOf[i];

            switch (component)
            {
                case HeatExchanger exchanger when exchanger.SecondarySideConnected || exchanger.Rating is { CanRate: true }:
                    stages.Role[v] = ThermalStageRole.Conversion;

                    for (var port = 0; port < exchanger.Ports.Length; port++)
                    {
                        var peer = graph.Adjacency.Peer(i, port);

                        if (!peer.Exists || stages.VertexOf[peer.Component] == v)
                        {
                            continue;
                        }

                        var losing = (port >= 2) == (exchanger.Power >= 0);
                        (losing ? stages.Hot[v] : stages.Cold[v]).Add(stages.VertexOf[peer.Component]);
                    }

                    break;

                case Tank tank:
                    stages.Role[v] = ThermalStageRole.Storage;

                    for (var port = 0; port < tank.Ports.Length; port++)
                    {
                        var peer = graph.Adjacency.Peer(i, port);

                        if (!peer.Exists || stages.VertexOf[peer.Component] == v)
                        {
                            continue;
                        }

                        var charging = tank.Ports[port].Name.StartsWith("in", StringComparison.Ordinal);
                        (charging ? stages.Hot[v] : stages.Cold[v]).Add(stages.VertexOf[peer.Component]);
                    }

                    break;
            }
        }
    }

    /// <summary>Phase 3 of <see cref="ThermalStages"/>: transport adjacency between vertices, and what each pivot's sides reach without crossing another pivot.</summary>
    private static void LinkTransport(CircuitGraph graph, StageGraph stages)
    {
        for (var i = 0; i < graph.Components.Length; i++)
        {
            foreach (var next in Neighbours(graph, i))
            {
                if (stages.VertexOf[next] != stages.VertexOf[i])
                {
                    stages.Adjacent[stages.VertexOf[i]].Add(stages.VertexOf[next]);
                }
            }
        }

        stages.Pivots = stages.Decided();

        foreach (var pivot in stages.Pivots)
        {
            stages.HotReach[pivot] = Reach(stages.Hot[pivot], stages.Role, stages.Adjacent);
            stages.ColdReach[pivot] = Reach(stages.Cold[pivot], stages.Role, stages.Adjacent);
        }
    }

    /// <summary>Phase 4 of <see cref="ThermalStages"/>: classify the rest, relative to pivots first, then by circuit role with the duty as a check.</summary>
    private static void ClassifyByReach(CircuitGraph graph, SemanticModel model, StageGraph stages, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        for (var v = 0; v < stages.Count; v++)
        {
            if (stages.Role[v] != ThermalStageRole.Neutral)
            {
                continue;
            }

            var feeds = stages.Pivots.Any(pivot => stages.HotReach[pivot].Contains(v));
            var fed = stages.Pivots.Any(pivot => stages.ColdReach[pivot].Contains(v));

            if (feeds != fed)
            {
                stages.Role[v] = feeds ? ThermalStageRole.Source : ThermalStageRole.Consumer;
                continue;
            }

            var circuit = CircuitOf(graph, stages.Vertices[v]);

            if (circuit is null)
            {
                continue;
            }

            var evidence = model.Circuits.FirstOrDefault(candidate => string.Equals(candidate.Name, circuit, StringComparison.Ordinal))?.Role;

            if (evidence is null || evidence.Stage == ThermalStageRole.Neutral)
            {
                continue;
            }

            var duty = stages.Vertices[v]
                .Select(i => graph.Components[i])
                .OfType<HeatExchanger>()
                .Sum(static exchanger => exchanger.Power);

            var physics = duty > 0 ? ThermalStageRole.Source : duty < 0 ? ThermalStageRole.Consumer : evidence.Stage;

            if (evidence.Stage is ThermalStageRole.Source or ThermalStageRole.Consumer && physics != evidence.Stage)
            {
                stages.Role[v] = physics;

                diagnostics.Add(Diagnostic.Create(
                    LayoutDiagnostics.RoleContradictsDuty,
                    span: null,
                    new DiagnosticArgument("circuit", circuit),
                    new DiagnosticArgument("role", evidence.CanonicalName),
                    new DiagnosticArgument("stage", physics.ToString().ToLowerInvariant())));
            }
            else
            {
                stages.Role[v] = evidence.Stage;
            }
        }
    }

    /// <summary>Phase 5 of <see cref="ThermalStages"/>: rank the classified vertices by longest path over heat progression.</summary>
    /// <remarks>Across each pivot from its hot side to its cold side, and from one pivot's cold side to the next pivot's hot side. Where no pivot orders them, class order does: a source before a consumer in one circuit.</remarks>
    private static void RankClassified(StageGraph stages)
    {
        var successors = new HashSet<int>[stages.Count];

        for (var v = 0; v < stages.Count; v++)
        {
            successors[v] = [];
        }

        foreach (var pivot in stages.Pivots)
        {
            foreach (var upstream in stages.HotReach[pivot].Where(u => stages.Role[u] != ThermalStageRole.Neutral))
            {
                successors[upstream].Add(pivot);
            }

            foreach (var downstream in stages.ColdReach[pivot].Where(d => stages.Role[d] != ThermalStageRole.Neutral))
            {
                successors[pivot].Add(downstream);
            }
        }

        stages.Classified = stages.Decided();

        foreach (var a in stages.Classified)
        {
            foreach (var b in stages.Classified)
            {
                if (a != b && ClassOrder.IndexOf(stages.Role[a]) < ClassOrder.IndexOf(stages.Role[b]) && Connected(a, b, stages.Adjacent))
                {
                    successors[a].Add(b);
                }
            }
        }

        foreach (var v in stages.Classified.OrderBy(v => v))
        {
            LongestPath(v, successors, stages.Rank, []);
        }
    }

    /// <summary>Phase 6 of <see cref="ThermalStages"/>: neutral vertices take the rank of the nearest classified vertex, breadth-first; with nothing classified, every rank is zero.</summary>
    private static void FillNeutral(StageGraph stages)
    {
        var pending = Enumerable.Range(0, stages.Count).Where(v => stages.Rank[v] < 0).ToList();

        if (stages.Classified.Count == 0)
        {
            foreach (var v in pending)
            {
                stages.Rank[v] = 0;
            }

            return;
        }

        var frontier = new Queue<int>(stages.Classified.OrderBy(v => v));
        var assigned = new HashSet<int>(stages.Classified);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();

            foreach (var next in stages.Adjacent[current].OrderBy(static n => n))
            {
                if (assigned.Add(next))
                {
                    stages.Rank[next] = stages.Rank[current];
                    frontier.Enqueue(next);
                }
            }
        }

        foreach (var v in pending.Where(v => stages.Rank[v] < 0))
        {
            stages.Rank[v] = 0;
        }
    }

    /// <summary>Phase 7 of <see cref="ThermalStages"/>: a loop is one band (<c>25</c>): its members that are not pivots share the highest rank among them, so a recirculation loop hanging off an exchanger sits wholly on the exchanger's cold side.</summary>
    private static void BandLoops(ImmutableDictionary<string, int> index, ImmutableArray<ImmutableArray<string>> loops, StageGraph stages)
    {
        foreach (var loop in loops)
        {
            var members = loop
                .Select(name => index.TryGetValue(name, out var i) ? stages.VertexOf[i] : -1)
                .Where(v => v >= 0 && !stages.Pivots.Contains(v))
                .Distinct()
                .ToList();

            if (members.Count > 1)
            {
                var band = members.Max(v => stages.Rank[v]);

                foreach (var member in members)
                {
                    stages.Rank[member] = band;
                }
            }
        }
    }

    /// <summary>Phase 8 of <see cref="ThermalStages"/>: one stage per (rank, role), members in graph order.</summary>
    private static ImmutableArray<ThermalStage> Stages(CircuitGraph graph, StageGraph stages) =>
    [
        .. Enumerable.Range(0, stages.Count)
            .GroupBy(v => (stages.Rank[v], Role: stages.Role[v]))
            .OrderBy(static group => group.Key.Item1)
            .ThenBy(static group => ClassOrder.IndexOf(group.Key.Role) is var at && at < 0 ? ClassOrder.Length : at)
            .Select(group => new ThermalStage
            {
                Rank = group.Key.Item1,
                Role = group.Key.Role,
                Components = [.. group.SelectMany(v => stages.Vertices[v]).OrderBy(static i => i).Select(i => graph.Components[i].Name)],
            }),
    ];

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
