using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Layout.Hints;

public static partial class LayoutHintsDerivation
{
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
}
