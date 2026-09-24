using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Layout.Engine.Structures;

namespace FluidScript.Core.Layout.Engine;

/// <summary>
/// Reads one fragment as structures before any geometry (<c>28</c> E2): its head (C1), the form it takes, the body
/// between its two terminals read as series and parallel parts, and what hangs off the body.
/// </summary>
/// <remarks>
/// <para>
/// The forms are tried in the order the ladder engine tried them: a ring through the head when it is a source (C2),
/// a member joined to itself (C20), the open form from an inlet (C19), a ring cut at the consumer (C18), and the chain
/// (C1). A ring is cut at its member: the member's outlet side and inlet side become two virtual terminals, and the
/// body is everything on the paths between them.
/// </para>
/// <para>
/// The body is the biconnected block that holds a virtual link between the two terminals -- exactly the elements on
/// some path from one to the other. Inside it, a vertex every path passes splits a <b>series</b>; the pieces left
/// between the two terminals when both are removed are in <b>parallel</b>, a header when their flows agree and a loop
/// when they oppose. What is neither is <b>loose</b>. Everything outside the body hangs off it at one port.
/// </para>
/// </remarks>
internal sealed class Decomposition
{
    private readonly CircuitView _view;
    private readonly ImmutableArray<int> _fragment;
    private readonly List<Run> _runs;
    private readonly int _plus;
    private readonly int _minus;

    private Decomposition(CircuitView view, ImmutableArray<int> fragment)
    {
        _view = view;
        _fragment = fragment;
        var members = fragment.ToHashSet();
        _runs = [.. view.Runs.Where(r => members.Contains(r.Start.Component))];
        _plus = view.Count;
        _minus = view.Count + 1;
    }

    /// <summary>One run as an edge between two vertices, in the run's flow direction.</summary>
    private readonly record struct Edge(int Run, int A, int B);

    /// <summary>Reads a fragment.</summary>
    /// <param name="view">The circuit view.</param>
    /// <param name="fragment">The fragment's members.</param>
    /// <returns>Its plan.</returns>
    public static FragmentPlan Plan(CircuitView view, ImmutableArray<int> fragment) => new Decomposition(view, fragment).Plan();

    private FragmentPlan Plan()
    {
        var head = Head();

        if (_runs.Count == 0)
        {
            return new FragmentPlan(FragmentKind.Chain, head, -1, null, [], -1, -1);
        }

        if (_view.IsSource(head) && Ring(head, head, FragmentKind.Sourced) is { } sourced)
        {
            return sourced with { Rings = Attached(sourced) };
        }

        if (_runs.Any(r => r.Start.Component == head && r.End.Component == head))
        {
            var own = _runs.First(r => r.Start.Component == head && r.End.Component == head);
            return new FragmentPlan(FragmentKind.SelfLoop, head, head, new RunLeaf(head, head, own.Index, true), Pendants([own.Index], [head]), -1, -1);
        }

        if (_view.IsInlet(head) && Open(head) is { } open)
        {
            return open;
        }

        if (Consumer(head) is { } consumer && Ring(head, consumer, FragmentKind.Unsourced) is { } ring)
        {
            return ring;
        }

        return new FragmentPlan(FragmentKind.Chain, head, -1, null, Pendants([], [head]), -1, -1);
    }

    // ---- C1: the head ---------------------------------------------------------------------------------------------

    /// <summary>C1 (<c>D-108</c>): the largest positive duty, else the first inlet, else the first declared member with nothing declared upstream, else the first declared.</summary>
    private int Head()
    {
        var declared = _view.Ordered(_fragment).Where(c => !_view.IsInferred(c)).ToList();

        if (declared.Count == 0)
        {
            return _view.Ordered(_fragment).First();
        }

        var sources = declared.Where(_view.IsSource).OrderByDescending(c => ((HeatExchangerComponent)_view.Graph.Components[c]).Power).ToList();

        if (sources.Count > 0)
        {
            return sources[0];
        }

        foreach (var c in declared.Where(_view.IsInlet))
        {
            return c;
        }

        foreach (var c in declared)
        {
            if (!Upstream(c).Any(u => !_view.IsInferred(u)))
            {
                return c;
            }
        }

        return declared[0];
    }

    /// <summary>The components whose nominal flow reaches a component, directly or through nodes (A3).</summary>
    private IEnumerable<int> Upstream(int c)
    {
        var seen = new HashSet<int> { c };
        var queue = new Queue<int>([c]);

        while (queue.Count > 0)
        {
            var j = queue.Dequeue();

            foreach (var p in _view.Connected(j).Where(p => _view.Enters(j, p)))
            {
                if (_view.Peer(j, p) is not { } peer || !seen.Add(peer.Component))
                {
                    continue;
                }

                if (_view.Wildcard(peer.Component))
                {
                    queue.Enqueue(peer.Component);
                }
                else
                {
                    yield return peer.Component;
                }
            }
        }
    }

    /// <summary>C18's seat: the consumer of the largest duty, else the first exchanger, else the first boxed member but the head that is not a pump.</summary>
    private int? Consumer(int head)
    {
        var boxed = _fragment.Where(c => !_view.IsInline(c)).ToList();
        var consumers = boxed.Where(c => _view.Duty(c) is not null).OrderBy(c => _view.Duty(c)).ThenBy(static c => c).ToList();

        if (consumers.Count > 0)
        {
            return consumers[0];
        }

        var exchangers = boxed.Where(c => _view.Graph.Components[c] is HeatExchangerComponent).Order().ToList();

        if (exchangers.Count > 0)
        {
            return exchangers[0];
        }

        var seats = boxed.Where(c => c != head && !_view.Wildcard(c)).Order().ToList();
        return seats.Count == 0 ? null : seats.FirstOrDefault(c => _view.Graph.Components[c] is not PumpComponent, seats[0]);
    }

    // ---- the forms ------------------------------------------------------------------------------------------------

    /// <summary>A ring cut at <paramref name="cut"/>: the first of its leaving ports and entering ports, in port order, joined by a path that avoids it.</summary>
    private FragmentPlan? Ring(int head, int cut, FragmentKind kind)
    {
        var connected = _view.Connected(cut).ToList();

        foreach (var outPort in connected.Where(p => !_view.Enters(cut, p)))
        {
            foreach (var inPort in connected.Where(p => _view.Enters(cut, p)))
            {
                int Vertex(RunEnd end) => end.Component != cut ? end.Component : end.Port == outPort ? _plus : end.Port == inPort ? _minus : -1;

                var edges = _runs
                    .Select(r => new Edge(r.Index, Vertex(r.Start), Vertex(r.End)))
                    .Where(static e => e.A >= 0 && e.B >= 0)
                    .ToList();

                var body = Body(edges, _plus, _minus);

                if (body.Count == 0)
                {
                    continue;
                }

                var structure = Decompose(body, _plus, _minus);
                return new FragmentPlan(kind, head, cut, structure, Pendants([.. body.Select(static e => e.Run)], [cut]), _plus, _minus);
            }
        }

        return null;
    }

    /// <summary>C19: from an inlet to the outlet whose paths from it take in the most runs, the earlier in the engine's order between equals.</summary>
    private FragmentPlan? Open(int inlet)
    {
        var edges = _runs.Select(static r => new Edge(r.Index, r.Start.Component, r.End.Component)).ToList();
        List<Edge>? best = null;
        var outlet = -1;

        foreach (var t in _view.Ordered(_fragment).Where(c => c != inlet && _view.IsBoundary(c) && !_view.IsInlet(c)))
        {
            var body = Body(edges, inlet, t);

            if (body.Count > (best?.Count ?? 0))
            {
                best = body;
                outlet = t;
            }
        }

        if (best is null)
        {
            return null;
        }

        return new FragmentPlan(FragmentKind.Open, inlet, -1, Decompose(best, inlet, outlet), Pendants([.. best.Select(static e => e.Run)], []), -1, -1);
    }

    // ---- the body -------------------------------------------------------------------------------------------------

    /// <summary>The edges on some path from <paramref name="s"/> to <paramref name="t"/>: the biconnected block of the graph with a virtual edge between them that holds that edge.</summary>
    private List<Edge> Body(List<Edge> edges, int s, int t)
    {
        var size = _view.Count + 2;
        var adjacency = Enumerable.Range(0, size).Select(static _ => new List<(int Edge, int To)>()).ToArray();
        var all = new List<Edge>(edges) { new(-1, s, t) };

        for (var e = 0; e < all.Count; e++)
        {
            if (all[e].A == all[e].B)
            {
                continue;
            }

            adjacency[all[e].A].Add((e, all[e].B));
            adjacency[all[e].B].Add((e, all[e].A));
        }

        var discovered = new int[size];
        var low = new int[size];
        var stack = new Stack<int>();
        var timer = 0;
        List<Edge>? found = null;

        void Visit(int u, int parent)
        {
            discovered[u] = low[u] = ++timer;

            foreach (var (e, w) in adjacency[u])
            {
                if (e == parent)
                {
                    continue;
                }

                if (discovered[w] == 0)
                {
                    stack.Push(e);
                    Visit(w, e);
                    low[u] = Math.Min(low[u], low[w]);

                    if (low[w] >= discovered[u])
                    {
                        var block = new List<int>();
                        int popped;

                        do
                        {
                            popped = stack.Pop();
                            block.Add(popped);
                        }
                        while (popped != e);

                        if (block.Contains(all.Count - 1))
                        {
                            found = [.. block.Where(b => b != all.Count - 1).Order().Select(b => all[b])];
                        }
                    }
                }
                else if (discovered[w] < discovered[u])
                {
                    stack.Push(e);
                    low[u] = Math.Min(low[u], discovered[w]);
                }
            }
        }

        Visit(s, -1);

        // The virtual edge alone is no body: the two terminals are not joined.
        return discovered[t] == 0 ? [] : found ?? [];
    }

    /// <summary>A two-terminal body read as a series of parts, a header, a loop, one run, or loose.</summary>
    private Structure Decompose(List<Edge> edges, int s, int t)
    {
        if (edges.Count == 1)
        {
            return new RunLeaf(s, t, edges[0].Run, edges[0].A == s);
        }

        var cuts = Cuts(edges, s, t);

        if (cuts.Count > 0)
        {
            List<int> points = [s, .. cuts, t];
            var segments = Split(edges, points);
            return new SeriesStructure(s, t, [.. segments.Select((segment, i) => Decompose(segment, points[i], points[i + 1]))]);
        }

        var children = Parallel(edges, s, t);

        if (children.Count < 2)
        {
            return new LooseStructure(s, t, [.. edges.Select(static e => e.Run)]);
        }

        var forward = children.Where(c => Flows(c, s)).ToList();
        var back = children.Where(c => !Flows(c, s)).ToList();

        if (back.Count == 0)
        {
            return Header(forward, s, t);
        }

        if (forward.Count == 0)
        {
            return Header(back, t, s);
        }

        var there = forward.Count == 1 ? Decompose(forward[0], s, t) : Header(forward, s, t);
        var home = back.Count == 1 ? Decompose(back[0], t, s) : Header(back, t, s);
        return new LoopStructure(s, t, there, home);
    }

    /// <summary>A header's branches in script order -- the order of their first declared members -- and the spine, the one declared last.</summary>
    private HeaderStructure Header(List<List<Edge>> children, int split, int merge)
    {
        var ordered = children.Select(c => (Edges: c, Key: Key(c, split, merge))).OrderBy(static c => c.Key).ToList();
        var spine = ordered.Count - 1;
        return new HeaderStructure(split, merge, [.. ordered.Select(c => Decompose(c.Edges, split, merge))], spine);
    }

    /// <summary>The position among the model's declarations of a part's first declared boxed member, its terminals aside; -1 for a part with none.</summary>
    private int Key(List<Edge> edges, int s, int t)
    {
        var declared = edges
            .SelectMany(static e => new[] { e.A, e.B })
            .Where(v => v != s && v != t && v < _view.Count)
            .Select(_view.Declared)
            .Where(static d => d >= 0)
            .ToList();

        return declared.Count == 0 ? -1 : declared.Min();
    }

    /// <summary>Whether a parallel part carries the flow away from <paramref name="s"/>: read on its first edge at <paramref name="s"/>.</summary>
    private static bool Flows(List<Edge> part, int s) => part.First(e => e.A == s || e.B == s).A == s;

    /// <summary>The vertices between <paramref name="s"/> and <paramref name="t"/> that every path from one to the other passes, in the order a path meets them.</summary>
    private static List<int> Cuts(List<Edge> edges, int s, int t)
    {
        var path = Path(edges, s, t, -1);

        if (path is null)
        {
            return [];
        }

        return [.. path.Skip(1).SkipLast(1).Where(v => Path(edges, s, t, v) is null)];
    }

    /// <summary>A shortest path of vertices from <paramref name="s"/> to <paramref name="t"/> avoiding <paramref name="avoid"/>, or null.</summary>
    private static List<int>? Path(List<Edge> edges, int s, int t, int avoid)
    {
        var previous = new Dictionary<int, int> { [s] = s };
        var queue = new Queue<int>([s]);

        while (queue.Count > 0)
        {
            var u = queue.Dequeue();

            if (u == t)
            {
                var path = new List<int> { t };

                while (path[^1] != s)
                {
                    path.Add(previous[path[^1]]);
                }

                path.Reverse();
                return path;
            }

            foreach (var e in edges)
            {
                var w = e.A == u ? e.B : e.B == u ? e.A : -1;

                if (w >= 0 && w != avoid && previous.TryAdd(w, u))
                {
                    queue.Enqueue(w);
                }
            }
        }

        return null;
    }

    /// <summary>The edges between each pair of consecutive points of a series.</summary>
    private static List<List<Edge>> Split(List<Edge> edges, List<int> points)
    {
        var at = points.Select((v, i) => (v, i)).ToDictionary(static p => p.v, static p => p.i);
        var root = new Dictionary<int, int>();

        int Find(int v)
        {
            root.TryAdd(v, v);

            while (root[v] != v)
            {
                v = root[v] = root[root[v]];
            }

            return v;
        }

        foreach (var e in edges.Where(e => !at.ContainsKey(e.A) && !at.ContainsKey(e.B)))
        {
            root[Find(e.A)] = Find(e.B);
        }

        // Each inner vertex's component touches two consecutive points; the lower one names its segment.
        var touches = new Dictionary<int, int>();

        foreach (var e in edges)
        {
            foreach (var (point, other) in new[] { (e.A, e.B), (e.B, e.A) })
            {
                if (at.TryGetValue(point, out var i) && !at.ContainsKey(other))
                {
                    var r = Find(other);
                    touches[r] = touches.TryGetValue(r, out var j) ? Math.Min(i, j) : i;
                }
            }
        }

        var segments = points.Skip(1).Select(static _ => new List<Edge>()).ToList();

        foreach (var e in edges)
        {
            var i = at.TryGetValue(e.A, out var a) && at.TryGetValue(e.B, out var b) ? Math.Min(a, b)
                : touches[Find(at.ContainsKey(e.A) ? e.B : e.A)];
            segments[i].Add(e);
        }

        return segments;
    }

    /// <summary>The parts between two terminals once both are removed: each edge between them alone, and each component of what is left.</summary>
    private static List<List<Edge>> Parallel(List<Edge> edges, int s, int t)
    {
        var root = new Dictionary<int, int>();

        int Find(int v)
        {
            root.TryAdd(v, v);

            while (root[v] != v)
            {
                v = root[v] = root[root[v]];
            }

            return v;
        }

        bool Terminal(int v) => v == s || v == t;

        foreach (var e in edges.Where(e => !Terminal(e.A) && !Terminal(e.B)))
        {
            root[Find(e.A)] = Find(e.B);
        }

        var children = new List<List<Edge>>();
        var byRoot = new Dictionary<int, List<Edge>>();

        foreach (var e in edges)
        {
            if (Terminal(e.A) && Terminal(e.B))
            {
                children.Add([e]);
                continue;
            }

            var r = Find(Terminal(e.A) ? e.B : e.A);

            if (!byRoot.TryGetValue(r, out var list))
            {
                byRoot[r] = list = [];
                children.Add(list);
            }

            list.Add(e);
        }

        return children;
    }

    // ---- what hangs off the body ----------------------------------------------------------------------------------

    /// <summary>
    /// The rings that share one element with the body (<c>D-157</c>): each pendant that is no tree and touches the body
    /// at exactly two ports of one element -- one it leaves by, one it returns by -- read as a ring cut at that element
    /// between those two ports, over the pendant's own runs.
    /// </summary>
    private ImmutableArray<AttachedRing> Attached(FragmentPlan plan)
    {
        var rings = ImmutableArray.CreateBuilder<AttachedRing>();
        var onBody = new HashSet<int>();

        foreach (var r in plan.Body is null ? [] : Runs(plan.Body))
        {
            onBody.Add(_view.Runs[r].Start.Component);
            onBody.Add(_view.Runs[r].End.Component);
        }

        foreach (var pendant in plan.Pendants.Where(static p => !p.Tree))
        {
            var runs = pendant.Runs.Select(r => _view.Runs[r]).ToList();
            var touches = runs.SelectMany(static r => new[] { r.Start, r.End }).Where(e => onBody.Contains(e.Component)).Distinct().ToList();

            if (touches.Count != 2 || touches[0].Component != touches[1].Component)
            {
                continue;
            }

            var at = touches[0].Component;
            var leaving = touches.FirstOrDefault(e => !_view.Enters(at, e.Port));
            var entering = touches.FirstOrDefault(e => _view.Enters(at, e.Port));

            if (leaving == default || entering == default)
            {
                continue;
            }

            var own = pendant.Runs.ToHashSet();
            int Vertex(RunEnd end) => end.Component != at ? end.Component : end.Port == leaving.Port ? _plus : end.Port == entering.Port ? _minus : -1;
            var edges = _runs.Where(r => own.Contains(r.Index)).Select(r => new Edge(r.Index, Vertex(r.Start), Vertex(r.End))).Where(static e => e.A >= 0 && e.B >= 0).ToList();
            var body = Body(edges, _plus, _minus);

            if (body.Count > 0)
            {
                rings.Add(new AttachedRing(at, leaving.Port, entering.Port, Decompose(body, _plus, _minus)));
            }
        }

        return rings.ToImmutable();
    }

    /// <summary>Every run a structure holds.</summary>
    private static IEnumerable<int> Runs(Structure structure) => structure switch
    {
        RunLeaf leaf => [leaf.Run],
        SeriesStructure series => series.Parts.SelectMany(Runs),
        HeaderStructure header => header.Branches.SelectMany(Runs),
        LoopStructure loop => Runs(loop.Forward).Concat(Runs(loop.Back)),
        _ => [],
    };

    /// <summary>
    /// Every run outside the body, grouped into the pieces that hang off it: a body element's port is where a piece is
    /// attached, and two pieces at different ports of one element are two pieces.
    /// </summary>
    /// <param name="body">The body's runs.</param>
    /// <param name="also">Elements that count as the body's though no body run ends on them: a ring's cut, a chain's head.</param>
    private ImmutableArray<Pendant> Pendants(HashSet<int> body, IEnumerable<int> also)
    {
        var onBody = new HashSet<int>(also);

        foreach (var r in _runs.Where(r => body.Contains(r.Index)))
        {
            onBody.Add(r.Start.Component);
            onBody.Add(r.End.Component);
        }

        // Nodes: a body element's port is (-1 - component, port); any other element is (component, -1).
        (int, int) Node(RunEnd end) => onBody.Contains(end.Component) ? (-1 - end.Component, end.Port) : (end.Component, -1);

        var root = new Dictionary<(int, int), (int, int)>();

        (int, int) Find((int, int) v)
        {
            root.TryAdd(v, v);

            while (root[v] != v)
            {
                v = root[v] = root[root[v]];
            }

            return v;
        }

        var rest = _runs.Where(r => !body.Contains(r.Index)).ToList();

        foreach (var r in rest)
        {
            root[Find(Node(r.Start))] = Find(Node(r.End));
        }

        var pendants = ImmutableArray.CreateBuilder<Pendant>();

        foreach (var group in rest.GroupBy(r => Find(Node(r.Start))).OrderBy(static g => g.Min(static r => r.Index)))
        {
            var runs = group.ToList();
            var nodes = runs.SelectMany(r => new[] { Node(r.Start), Node(r.End) }).Distinct().ToList();
            var attachments = nodes.Where(static n => n.Item1 < 0).OrderBy(static n => n.Item1).ThenBy(static n => n.Item2).ToList();
            var members = _view.Ordered(nodes.Where(static n => n.Item1 >= 0).Select(static n => n.Item1)).ToImmutableArray();
            var (at, port) = attachments.Count > 0 ? (-1 - attachments[0].Item1, attachments[0].Item2) : (members[0], -1);
            pendants.Add(new Pendant(at, port, members, [.. runs.Select(static r => r.Index)], attachments.Count <= 1 && runs.Count == nodes.Count - 1));
        }

        return pendants.ToImmutable();
    }
}
