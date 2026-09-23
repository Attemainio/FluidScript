using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout.Engine;

/// <summary>
/// The draw stage (<c>28</c> E4): every run no rule laid is drawn through the router from its anchors with both stubs
/// kept, a node's side decided by the route; then the instruments (C15), the crossings (C16), the groups (A8), the
/// labels (A11) and the scene.
/// </summary>
internal sealed partial class Painter
{
    private const double Eps = 1e-9;

    private readonly Sheet _sheet;
    private readonly CircuitView _view;
    private readonly double _margin;
    private readonly List<(int Link, Route Route)> _routes = [];
    private readonly List<Placement> _instruments = [];
    private readonly List<Box> _instrumentBoxes = [];
    private HashSet<int>? _supply;

    /// <summary>Creates a painter over a composed sheet.</summary>
    /// <param name="sheet">The sheet.</param>
    public Painter(Sheet sheet)
    {
        _sheet = sheet;
        _view = sheet.View;
        _margin = sheet.Margin;
    }

    /// <summary>Draws the scene.</summary>
    /// <returns>The scene.</returns>
    public Scene Paint()
    {
        Connect();
        PlaceInstruments();
        ComputeHops();
        return ToScene();
    }

    // ---- runs -----------------------------------------------------------------------------------------------------

    /// <summary>Every run no rule laid: through the router from its start's anchors to its end's, each stub kept (H5), the sides of a node's ports set by where the route leaves it.</summary>
    private void Connect()
    {
        var router = new OrthogonalRouter(_margin);

        for (var i = 0; i < _view.Count; i++)
        {
            if (!_view.IsInline(i))
            {
                router.AddBox(_sheet.InnerOf(i), i);
            }
        }

        var bubbles = _sheet.HatBoxes(_sheet.Hats().Keys).ToList();

        for (var b = 0; b < bubbles.Count; b++)
        {
            router.AddBox(bubbles[b], _view.Count + b);
        }

        foreach (var run in _view.Runs)
        {
            if (_sheet.RunPoints(run.Index) is { } laid)
            {
                AddPipe(router, run, laid);
                continue;
            }

            foreach (var end in new[] { run.Start, run.End })
            {
                if (!_view.Wildcard(end.Component) || _sheet.Side.ContainsKey((end.Component, end.Port)))
                {
                    router.AddStub(_sheet.AnchorOf(end.Component, end.Port), _margin, end.Component, end.Port);
                }
            }
        }

        foreach (var run in _view.Runs.OrderBy(static r => r.Links.Min(static l => l.Link)))
        {
            if (_sheet.RunPoints(run.Index) is not null)
            {
                continue;
            }

            var (a, b) = (run.Start, run.End);
            router.ReleaseStub(a.Component, a.Port);
            router.ReleaseStub(b.Component, b.Port);
            var towardsB = _view.Wildcard(b.Component) && !_sheet.Side.ContainsKey((b.Component, b.Port)) ? _sheet.Centre[b.Component] : _sheet.AnchorOf(b.Component, b.Port).At;
            var towardsA = _view.Wildcard(a.Component) && !_sheet.Side.ContainsKey((a.Component, a.Port)) ? _sheet.Centre[a.Component] : _sheet.AnchorOf(a.Component, a.Port).At;
            var result = router.Route([.. Ends(a.Component, a.Port, towardsB)], a.Component, [.. Ends(b.Component, b.Port, towardsA)], b.Component)!;

            if (_view.Wildcard(a.Component))
            {
                _sheet.Side[(a.Component, a.Port)] = result.From.Outward;
            }

            if (_view.Wildcard(b.Component))
            {
                _sheet.Side[(b.Component, b.Port)] = result.To.Outward;
            }

            _sheet.Lay(run, result.Points);
            _sheet.Note(StructureText.Run(_view, run), "E4", result.Clean ? "routed, both stubs kept" : "routed, no clean path: the straight join, drawn beneath what it crosses");
            AddPipe(router, run, _sheet.RunPoints(run.Index)!.Value);
        }

        foreach (var run in _view.Runs)
        {
            var pieces = _sheet.Pieces(run.Index);

            for (var t = 0; t < run.Links.Length; t++)
            {
                var (k, forward) = run.Links[t];
                var points = forward ? pieces[t] : [.. pieces[t].Reverse()];
                _routes.Add((k, new Route(_view.Links[k].Id, "pipe", LayerOf(k), Sheet.Normalise(points), [])));
            }
        }
    }

    private static void AddPipe(OrthogonalRouter router, Run run, ImmutableArray<Point> points)
    {
        for (var s = 1; s < points.Length; s++)
        {
            router.AddPipe(points[s - 1], points[s], run.Index, run.Start.Component, run.End.Component);
        }
    }

    /// <summary>The anchors a run may leave an element from: one for a named port or a side already set; for a node's port with none, the free sides that do not face away from the peer (<c>D-105</c>).</summary>
    private IEnumerable<PlacedAnchor> Ends(int component, int port, Point towards)
    {
        if (!_view.Wildcard(component) || _sheet.Side.ContainsKey((component, port)))
        {
            yield return _sheet.AnchorOf(component, port);
            yield break;
        }

        var inner = _sheet.InnerOf(component);
        var d = towards.Offset(-inner.Centre.X, -inner.Centre.Y);
        var used = new HashSet<Direction>();

        foreach (var p in _view.Connected(component))
        {
            if (_sheet.Side.TryGetValue((component, p), out var s))
            {
                used.Add(s);
            }
        }

        var any = false;

        for (var pass = 0; pass < 2 && !any; pass++)
        {
            foreach (var direction in Direction.All)
            {
                if ((direction.X * d.X) + (direction.Y * d.Y) < -Eps || (pass == 0 && used.Contains(direction)))
                {
                    continue;
                }

                any = true;
                yield return Sheet.Anchor(inner.Centre.Towards(direction, direction.Horizontal ? inner.Width / 2 : inner.Height / 2), direction, _sheet.FlowOf(component, port, direction));
            }
        }
    }

    // ---- crossings and layers (C16) -------------------------------------------------------------------------------

    private void ComputeHops()
    {
        var hops = _routes.Select(static _ => new List<Point>()).ToList();

        for (var b = 0; b < _routes.Count; b++)
        {
            for (var a = 0; a < b; a++)
            {
                var crossings = Crossings(_routes[a].Route.Points, _routes[b].Route.Points);

                if (crossings.Count > 0)
                {
                    hops[Rank(_routes[a].Route.Layer) < Rank(_routes[b].Route.Layer) ? a : b].AddRange(crossings);
                }
            }
        }

        for (var k = 0; k < _routes.Count; k++)
        {
            if (hops[k].Count > 0)
            {
                _routes[k] = (_routes[k].Link, _routes[k].Route with { Hops = [.. hops[k]] });
            }
        }
    }

    private static int Rank(string layer) => layer switch { "supply" => 2, "return" => 1, _ => 0 };

    /// <summary>C16: <c>supply</c> while the flow from a heat source has not passed a losing side, else <c>return</c>.</summary>
    private string LayerOf(int link) => (_supply ??= SupplyLinks()).Contains(link) ? "supply" : "return";

    private HashSet<int> SupplyLinks()
    {
        var supply = new HashSet<int>();
        var queue = new Queue<(int Component, int Port)>();

        for (var i = 0; i < _view.Count; i++)
        {
            if (_view.IsSource(i) || _view.IsInlet(i))
            {
                Leaving(i, -1, queue);
            }
        }

        while (queue.Count > 0)
        {
            var (i, p) = queue.Dequeue();

            if (_view.LinkAt(i, p) is not { } k || _view.Peer(i, p) is not { } peer)
            {
                continue;
            }

            if (supply.Add(k) && !Loses(peer.Component, peer.Port))
            {
                Leaving(peer.Component, peer.Port, queue);
            }
        }

        return supply;
    }

    private bool Loses(int i, int port)
    {
        if (_view.Graph.Components[i] is not HeatExchangerComponent h)
        {
            return false;
        }

        return h.Ports[port].Name.EndsWith('2') ? _view.IsSource(i) : _view.Duty(i) is not null;
    }

    private void Leaving(int i, int except, Queue<(int Component, int Port)> queue)
    {
        var ports = _view.Graph.Components[i].Ports;
        var exchanger = _view.Graph.Components[i] is HeatExchangerComponent;
        var second = except >= 0 && ports[except].Name.EndsWith('2');

        for (var q = 0; q < ports.Length; q++)
        {
            if (q == except || _view.Enters(i, q) || (exchanger && ports[q].Name.EndsWith('2') != second))
            {
                continue;
            }

            queue.Enqueue((i, q));
        }
    }

    private static List<Point> Crossings(ImmutableArray<Point> a, ImmutableArray<Point> b)
    {
        var result = new List<Point>();

        for (var i = 1; i < a.Length; i++)
        {
            for (var j = 1; j < b.Length; j++)
            {
                if (Segments.Crossing(a[i - 1], a[i], b[j - 1], b[j], Eps) is { } crossing)
                {
                    result.Add(crossing);
                }
            }
        }

        return result;
    }

    // ---- the scene --------------------------------------------------------------------------------------------------

    private Scene ToScene()
    {
        var placements = ImmutableArray.CreateBuilder<Placement>();
        var extent = (Box?)null;
        var groups = Groups(out var groupOf);

        foreach (var i in _view.Ordered(Enumerable.Range(0, _view.Count)))
        {
            var flow = _view.Graph.Components[i];
            var anchors = ImmutableSortedDictionary.CreateBuilder<string, PlacedAnchor>(StringComparer.Ordinal);

            for (var port = 0; port < flow.Ports.Length; port++)
            {
                anchors[flow.Ports[port].Name.Length == 0 ? $"#{port}" : flow.Ports[port].Name] = _sheet.AnchorOf(i, port);
            }

            var inner = _sheet.InnerOf(i);
            var placement = new Placement
            {
                ComponentId = flow.Name,
                SymbolId = _view.Symbols[i].Id,
                Inner = inner,
                Outer = _view.IsInline(i) ? inner : inner.Grow(_margin),
                Rotation = _sheet.Transform[i].Rotation,
                Mirrored = _sheet.Transform[i].Mirrored,
                Arrangement = _sheet.Transform[i].Arrangement,
                Anchors = anchors.ToImmutable(),
                LabelAt = LabelFor(i),
                LabelBox = LabelLayout.BoxFor(TextOf(flow.Name), LabelFor(i)),
                LabelClear = true,
                Source = "computed",
                Group = groupOf(i),
            };

            placements.Add(placement);
            extent = extent is { } e ? e.Union(placement.Outer) : placement.Outer;
        }

        foreach (var instrument in _instruments)
        {
            placements.Add(instrument);
            extent = extent is { } e ? e.Union(instrument.Outer) : instrument.Outer;
        }

        var routes = _routes
            .Select(static r => r.Route)
            .OrderBy(static r => r.Kind == "pipe" ? 0 : 1)
            .ThenBy(static r => r.Kind == "pipe" && int.TryParse(r.ConnectionId[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : int.MaxValue)
            .ThenBy(static r => r.ConnectionId, StringComparer.Ordinal)
            .ToImmutableArray();

        foreach (var point in routes.SelectMany(static r => r.Points))
        {
            var dot = new Box(point.X, point.Y, 0, 0);
            extent = extent is { } e ? e.Union(dot) : dot;
        }

        var labelled = LabelLayout.Place(placements, routes, p => TextOf(p.ComponentId));

        foreach (var placement in labelled.Where(static p => !p.IsInline))
        {
            extent = extent is { } e ? e.Union(placement.LabelBox) : placement.LabelBox;
        }

        return new Scene
        {
            Placements = [.. labelled.Select(Rounded)],
            Routes = [.. routes.Select(static r => r with { Points = [.. r.Points.Select(Rounded)], Hops = [.. r.Hops.Select(Rounded)] })],
            Extent = Rounded(extent ?? new Box(0, 0, 0, 0)),
            Margin = _margin,
            Groups = [.. groups],
            Provenance = [.. _sheet.Trace],
        };
    }

    /// <summary>
    /// A8: the rings and blocks that are one component to the rest of the system -- exactly one connection enters and
    /// one leaves; for the ring that holds the source, a tap whose flow comes back to the ring is a branch, not an inlet
    /// or an outlet. Outer before inner.
    /// </summary>
    private List<LayoutGroup> Groups(out Func<int, string?> groupOf)
    {
        var groups = new List<LayoutGroup>();
        var kept = new List<List<int>>();

        foreach (var (members, top) in _sheet.Groups)
        {
            var set = members.ToHashSet();
            var ins = 0;
            var outs = 0;
            var inside = new HashSet<int>();

            foreach (var i in members)
            {
                foreach (var p in _view.Connected(i))
                {
                    var run = _view.RunAt(i, p)!;
                    var far = run.From(i, p)!.Value;

                    if (set.Contains(far.Component))
                    {
                        inside.Add(run.Index);
                    }
                    else if (top && Returns(far.Component, set, run.Index))
                    {
                        // A tap of the source's ring whose flow comes back to it is a branch of the closed circuit.
                    }
                    else if (_view.Enters(i, p))
                    {
                        ins++;
                    }
                    else
                    {
                        outs++;
                    }
                }
            }

            if (ins != 1 || outs != 1)
            {
                continue;
            }

            var bounds = members.Select(_sheet.InnerOf).Aggregate(static (a, b) => a.Union(b));

            foreach (var r in inside)
            {
                bounds = _sheet.RunPoints(r)!.Value.Aggregate(bounds, static (b, p) => b.Union(new Box(p.X, p.Y, 0, 0)));
            }

            groups.Add(new LayoutGroup($"loop-{groups.Count + 1}", "loop", "cw", [.. members.Select(_view.Name)], Rounded(bounds)));
            kept.Add(members);
        }

        groupOf = i =>
        {
            var k = kept.FindLastIndex(m => m.Contains(i));
            return k < 0 ? null : $"loop-{k + 1}";
        };

        return groups;
    }

    /// <summary>Whether the flow that left a set of members by one run can reach the set again by another.</summary>
    private bool Returns(int from, HashSet<int> set, int arrived)
    {
        var seen = new HashSet<int> { from };
        var queue = new Queue<int>([from]);

        while (queue.Count > 0)
        {
            var n = queue.Dequeue();

            foreach (var p in _view.Connected(n))
            {
                var run = _view.RunAt(n, p);

                if (run is null || run.Index == arrived)
                {
                    continue;
                }

                var other = run.From(n, p)!.Value.Component;

                if (set.Contains(other))
                {
                    return true;
                }

                if (seen.Add(other))
                {
                    queue.Enqueue(other);
                }
            }
        }

        return false;
    }

    /// <summary>The text a label carries: the tag where the declaration has one, else the id (<c>D-34</c>).</summary>
    private string TextOf(string componentId) =>
        _view.Model.Components.FirstOrDefault(c => string.Equals(c.Name, componentId, StringComparison.Ordinal))?.Tag ?? componentId;

    /// <summary>A label's first position, just outside the placed box on the side the symbol's label anchor names.</summary>
    private Point LabelFor(int component)
    {
        var box = _sheet.InnerOf(component);
        var anchor = _view.Symbols[component].LabelAnchor;
        const double gap = 0.15;

        if (Math.Abs(anchor[1]) >= Math.Abs(anchor[0]))
        {
            return new Point(box.Centre.X, anchor[1] > 0 ? box.Top + gap : box.Y - gap);
        }

        return new Point(anchor[0] < 0 ? box.X - gap : box.Right + gap, box.Centre.Y);
    }

    private static double Rounded(double v) => Math.Round(v, 6);

    private static Point Rounded(Point p) => new(Rounded(p.X), Rounded(p.Y));

    private static Box Rounded(Box b) => new(Rounded(b.X), Rounded(b.Y), Rounded(b.Width), Rounded(b.Height));

    private static PlacedAnchor Rounded(PlacedAnchor a) => a with { At = Rounded(a.At) };

    private static Placement Rounded(Placement p) => p with
    {
        Inner = Rounded(p.Inner),
        Outer = Rounded(p.Outer),
        LabelAt = Rounded(p.LabelAt),
        LabelBox = Rounded(p.LabelBox),
        Anchors = p.Anchors.ToImmutableSortedDictionary(static a => a.Key, static a => Rounded(a.Value), StringComparer.Ordinal),
    };
}
