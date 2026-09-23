using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Model;
using FluidScript.Core.Model.Contract;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Layout.Engine;

/// <summary>
/// The circuit as the layout reads it (<c>28</c> E1), built once per scene: every port's peer in one lookup, every
/// port's flow, the runs between boxed elements with the inline elements collapsed onto them, and the fragments.
/// </summary>
/// <remarks>
/// Everything after this speaks of runs and boxed elements, never of links. A cycle made only of inline elements --
/// two bare nodes joined twice -- has no boxed element to end a run on; its first element in the engine's order keeps
/// its box, so every run ends on two boxed elements.
/// </remarks>
internal sealed class CircuitView
{
    private readonly Dictionary<(int Component, int Port), int> _linkAt = [];
    private readonly bool[][] _enters;
    private readonly bool[] _inline;
    private readonly Dictionary<(int Component, int Port), int> _runAt = [];
    private readonly int[] _order;
    private readonly int[] _declared;

    /// <summary>Builds the view.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="model">The bound model.</param>
    /// <param name="hints">The hints derived from the same graph, for the engine's order and what was inferred.</param>
    public CircuitView(CircuitGraph graph, SemanticModel model, LayoutHints hints)
    {
        Graph = graph;
        Model = model;
        Hints = hints;
        Count = graph.Components.Length;
        Index = GraphLinks.Index(graph);
        Links = GraphLinks.Of(graph, model, Index);
        Symbols = [.. graph.Components.Select(static c => SymbolCatalog.All.First(s => s.Id == SymbolCatalog.IdFor(c.Kind)))];

        for (var k = 0; k < Links.Length; k++)
        {
            _linkAt[(Links[k].From, Links[k].FromPort)] = k;
            _linkAt[(Links[k].To, Links[k].ToPort)] = k;
        }

        _order = new int[Count];
        Array.Fill(_order, int.MaxValue);

        for (var i = 0; i < hints.Order.Length; i++)
        {
            if (Index.TryGetValue(hints.Order[i], out var c))
            {
                _order[c] = i;
            }
        }

        _declared = new int[Count];
        Array.Fill(_declared, -1);

        for (var i = 0; i < model.Components.Length; i++)
        {
            if (Index.TryGetValue(model.Components[i].Name, out var c))
            {
                _declared[c] = i;
            }
        }

        _enters = [.. Enumerable.Range(0, Count).Select(c => Enumerable.Range(0, graph.Components[c].Ports.Length).Select(p => ComputeEnters(c, p)).ToArray())];
        _inline = [.. Enumerable.Range(0, Count).Select(IsInlineKind)];
        Runs = BuildRuns();
        Fragments = BuildFragments();
    }

    /// <summary>Gets the lowered graph.</summary>
    public CircuitGraph Graph { get; }

    /// <summary>Gets the bound model.</summary>
    public SemanticModel Model { get; }

    /// <summary>Gets the layout hints.</summary>
    public LayoutHints Hints { get; }

    /// <summary>Gets how many graph components there are.</summary>
    public int Count { get; }

    /// <summary>Gets each component's graph index by name.</summary>
    public IReadOnlyDictionary<string, int> Index { get; }

    /// <summary>Gets the links, written connections first.</summary>
    public ImmutableArray<GraphLink> Links { get; }

    /// <summary>Gets each component's symbol.</summary>
    public ImmutableArray<SymbolWire> Symbols { get; }

    /// <summary>Gets the runs between boxed elements (A5).</summary>
    public ImmutableArray<Run> Runs { get; }

    /// <summary>Gets the connected fragments (C17), in the order the script declares their first members, each fragment's members in the engine's order.</summary>
    public ImmutableArray<ImmutableArray<int>> Fragments { get; }

    /// <summary>A component's name.</summary>
    public string Name(int c) => Graph.Components[c].Name;

    /// <summary>The link at a port, or null where the port is open.</summary>
    public int? LinkAt(int c, int port) => _linkAt.TryGetValue((c, port), out var k) ? k : null;

    /// <summary>The component and port a port is joined to, or null where it is open.</summary>
    public (int Component, int Port)? Peer(int c, int port)
    {
        if (LinkAt(c, port) is not { } k)
        {
            return null;
        }

        var link = Links[k];
        return link.From == c && link.FromPort == port ? (link.To, link.ToPort) : (link.From, link.FromPort);
    }

    /// <summary>The ports of a component that are joined to something.</summary>
    public IEnumerable<int> Connected(int c) => Enumerable.Range(0, Graph.Components[c].Ports.Length).Where(p => _linkAt.ContainsKey((c, p)));

    /// <summary>How many of a component's ports are joined.</summary>
    public int Degree(int c) => Connected(c).Count();

    /// <summary>Whether a component's symbol takes its connections on any side (a node).</summary>
    public bool Wildcard(int c) => Symbols[c].PortAnchors.ContainsKey("*");

    /// <summary>Whether a component is an inlet or an outlet boundary: an end of the plant, never inline (<c>D-114</c>).</summary>
    public bool IsBoundary(int c) => Graph.Components[c] is NodeComponent { Boundary: BoundaryRole.Inlet or BoundaryRole.Outlet };

    /// <summary>Whether a component is an inlet boundary.</summary>
    public bool IsInlet(int c) => Graph.Components[c] is NodeComponent { Boundary: BoundaryRole.Inlet };

    /// <summary>Whether a component is drawn as a point on its run (A5).</summary>
    public bool IsInline(int c) => _inline[c];

    /// <summary>Whether the nominal flow enters a component at a port (A3).</summary>
    public bool Enters(int c, int port) => _enters[c][port];

    /// <summary>Whether a component was inferred by the language rather than declared.</summary>
    public bool IsInferred(int c) => Hints.Inferred.Contains(Name(c));

    /// <summary>The run at a boxed element's port, or null where the port is open.</summary>
    public Run? RunAt(int c, int port) => _runAt.TryGetValue((c, port), out var r) ? Runs[r] : null;

    /// <summary>The components in the engine's order: the hints' order, then graph order.</summary>
    public IEnumerable<int> Ordered(IEnumerable<int> components) => components.OrderBy(c => _order[c]).ThenBy(static c => c);

    /// <summary>A component's position among the model's declarations, or -1 for one the model does not declare.</summary>
    public int Declared(int c) => _declared[c];

    /// <summary>Whether an exchanger is a heat source: its stated power positive, or sized but written as a heater or boiler, or stating an outlet above its inlet.</summary>
    public bool IsSource(int c) => Graph.Components[c] switch
    {
        HeatExchangerComponent { Power: > 0 } => true,
        HeatExchangerComponent { Power: 0 } when Written(c) is "heater" or "boiler" => true,
        HeatExchangerComponent { Power: 0 } h when h.StatedParameters.TryGetValue("in", out var inlet) && h.StatedParameters.TryGetValue("out", out var outlet) && outlet.SiValue > inlet.SiValue => true,
        _ => false,
    };

    /// <summary>A consumer's duty, negative (W): its stated power, or a nominal amount for an exchanger sized but written as a load or stating an inlet above its outlet; null for anything else.</summary>
    public double? Duty(int c) => Graph.Components[c] switch
    {
        HeatExchangerComponent { Power: < 0 } h => h.Power,
        HeatExchangerComponent { Power: 0 } when Written(c) is "load" or "radiator" or "cooler" or "chiller" => -double.Epsilon,
        HeatExchangerComponent { Power: 0 } h when h.StatedParameters.TryGetValue("in", out var inlet) && h.StatedParameters.TryGetValue("out", out var outlet) && inlet.SiValue > outlet.SiValue => -double.Epsilon,
        _ => null,
    };

    /// <summary>The kind the script wrote for a component, lower-cased; null for one the language inferred.</summary>
    public string? Written(int c) => _declared[c] < 0 ? null : Model.Components[_declared[c]].WrittenKind.ToLowerInvariant();

    /// <summary>A3: a return boundary receives and a supply sends; else the port's role; else the joined port's; else the way the connection was written.</summary>
    private bool ComputeEnters(int c, int port)
    {
        var flow = Graph.Components[c];

        if (flow is NodeComponent { Boundary: BoundaryRole.Inlet })
        {
            return false;
        }

        if (flow is NodeComponent { Boundary: BoundaryRole.Outlet })
        {
            return true;
        }

        switch (flow.Ports[port].Role)
        {
            case PortRole.Inlet:
                return true;
            case PortRole.Outlet:
                return false;
        }

        if (LinkAt(c, port) is not { } k)
        {
            return false;
        }

        var link = Links[k];
        var (peer, peerPort, towards) = link.From == c && link.FromPort == port ? (link.To, link.ToPort, false) : (link.From, link.FromPort, true);

        return Graph.Components[peer] switch
        {
            NodeComponent { Boundary: BoundaryRole.Inlet } => true,
            NodeComponent { Boundary: BoundaryRole.Outlet } => false,
            _ => Graph.Components[peer].Ports[peerPort].Role switch
            {
                PortRole.Inlet => false,
                PortRole.Outlet => true,
                _ => towards,
            },
        };
    }

    /// <summary>A5: a pipe, or a node with exactly two connections that is not a boundary.</summary>
    private bool IsInlineKind(int c) =>
        (Graph.Components[c] is PipeComponent || (Wildcard(c) && !IsBoundary(c))) && Degree(c) == 2;

    /// <summary>Walks every boxed port's connection through the inline elements to the next boxed element; a cycle of inline elements alone gives its first element a box and is walked again.</summary>
    private ImmutableArray<Run> BuildRuns()
    {
        while (true)
        {
            var runs = new List<Run>();
            _runAt.Clear();
            var covered = new bool[Count];

            foreach (var c in Ordered(Enumerable.Range(0, Count)))
            {
                if (_inline[c])
                {
                    continue;
                }

                foreach (var p in Connected(c))
                {
                    if (_runAt.ContainsKey((c, p)))
                    {
                        continue;
                    }

                    var run = Walk(runs.Count, c, p);

                    foreach (var point in run.Inline)
                    {
                        covered[point.Element] = true;
                    }

                    _runAt[(run.Start.Component, run.Start.Port)] = run.Index;
                    _runAt[(run.End.Component, run.End.Port)] = run.Index;
                    runs.Add(run);
                }
            }

            var stranded = Ordered(Enumerable.Range(0, Count)).Where(c => _inline[c] && !covered[c]).Take(1).ToList();

            if (stranded.Count == 0)
            {
                return [.. runs];
            }

            _inline[stranded[0]] = false;
        }
    }

    /// <summary>The run from a boxed port, oriented with the flow where its ends say which way it runs.</summary>
    private Run Walk(int index, int component, int port)
    {
        var links = new List<(int Link, bool Forward)>();
        var inline = new List<InlinePoint>();
        var (n, q) = (component, port);

        while (true)
        {
            var k = LinkAt(n, q)!.Value;
            var link = Links[k];
            var forward = link.From == n && link.FromPort == q;
            links.Add((k, forward));
            (n, q) = forward ? (link.To, link.ToPort) : (link.From, link.FromPort);

            if (!_inline[n])
            {
                break;
            }

            var far = Connected(n).First(p => p != q);
            inline.Add(new InlinePoint(n, q, far));
            q = far;
        }

        var start = new RunEnd(component, port);
        var end = new RunEnd(n, q);

        // Against the flow when the start receives and the end sends: turn it round.
        if (Enters(start.Component, start.Port) && !Enters(end.Component, end.Port))
        {
            links.Reverse();
            inline.Reverse();
            return new Run(index, end, start, [.. links.Select(static l => (l.Link, !l.Forward))], [.. inline.Select(static i => new InlinePoint(i.Element, i.Far, i.Near))]);
        }

        return new Run(index, start, end, [.. links], [.. inline]);
    }

    /// <summary>C17: the connected fragments, in the order the script declares their first members; a component joined to nothing is a fragment of one.</summary>
    private ImmutableArray<ImmutableArray<int>> BuildFragments()
    {
        var root = Enumerable.Range(0, Count).ToArray();

        int Find(int i)
        {
            while (root[i] != i)
            {
                root[i] = root[root[i]];
                i = root[i];
            }

            return i;
        }

        foreach (var link in Links)
        {
            root[Find(link.From)] = Find(link.To);
        }

        var members = new Dictionary<int, List<int>>();
        var first = new Dictionary<int, int>();

        foreach (var c in Ordered(Enumerable.Range(0, Count)))
        {
            var r = Find(c);

            if (!members.TryGetValue(r, out var list))
            {
                members[r] = list = [];
                first[r] = int.MaxValue;
            }

            list.Add(c);

            if (_declared[c] >= 0)
            {
                first[r] = Math.Min(first[r], _declared[c]);
            }
        }

        return [.. members.Keys.OrderBy(r => first[r]).Select(r => members[r].ToImmutableArray())];
    }
}
