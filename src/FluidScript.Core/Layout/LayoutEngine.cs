using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Binding;
using FluidScript.Core.Components;
using FluidScript.Core.Language;
using FluidScript.Core.Model;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Layout;

/// <summary>A symbol's orientation: which arrangement of its anchors, whether it is mirrored left-to-right first, and its clockwise quarter turn.</summary>
/// <param name="Arrangement"><c>default</c> or one of the symbol's alternatives (<c>D-102</c>).</param>
/// <param name="Mirrored">Whether x is negated before the turn.</param>
/// <param name="Rotation">Clockwise degrees: 0, 90, 180 or 270.</param>
internal readonly record struct Transform(string Arrangement, bool Mirrored, int Rotation)
{
    /// <summary>Gets the untransformed default.</summary>
    public static Transform Identity => new("default", false, 0);

    /// <summary>Every transform a symbol admits, identity first so ties keep the drawn default.</summary>
    /// <param name="symbol">The symbol.</param>
    /// <returns>Arrangements × turns × mirrors, unmirrored first within each turn.</returns>
    public static IEnumerable<Transform> All(SymbolWire symbol)
    {
        var arrangements = new List<string> { "default" };
        arrangements.AddRange((symbol.Alternatives ?? new Dictionary<string, IReadOnlyDictionary<string, AnchorWire>>()).Keys.Order(StringComparer.Ordinal));

        foreach (var arrangement in arrangements)
        {
            foreach (var rotation in new[] { 0, 90, 180, 270 })
            {
                yield return new Transform(arrangement, false, rotation);
                yield return new Transform(arrangement, true, rotation);
            }
        }
    }

    /// <summary>The anchor set this transform's arrangement uses.</summary>
    /// <param name="symbol">The symbol.</param>
    /// <returns>Port name to anchor.</returns>
    public IReadOnlyDictionary<string, AnchorWire> Anchors(SymbolWire symbol) =>
        Arrangement == "default" || symbol.Alternatives is null ? symbol.PortAnchors : symbol.Alternatives[Arrangement];

    /// <summary>A symbol-space point after mirroring and turning.</summary>
    /// <param name="x">Symbol x.</param>
    /// <param name="y">Symbol y, up.</param>
    /// <returns>The point relative to the centre.</returns>
    public Point Apply(double x, double y)
    {
        if (Mirrored)
        {
            x = -x;
        }

        return Rotation switch
        {
            90 => new Point(y, -x),
            180 => new Point(-x, -y),
            270 => new Point(-y, x),
            _ => new Point(x, y),
        };
    }

    /// <summary>A symbol-space direction after mirroring and turning.</summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The turned direction.</returns>
    public Direction Apply(Direction direction) => Direction.Of(Apply(direction.X, direction.Y)) ?? direction;

    /// <summary>The box size after the turn.</summary>
    /// <param name="symbol">The symbol.</param>
    /// <returns>Width and height.</returns>
    public (double Width, double Height) Size(SymbolWire symbol) =>
        Rotation is 90 or 270 ? (symbol.ViewBox[3], symbol.ViewBox[2]) : (symbol.ViewBox[2], symbol.ViewBox[3]);
}

/// <summary>The rule-based layout engine (<c>28</c>, <c>D-106</c>), built one rule at a time against the ladder in <c>29-layout-ladder</c>.</summary>
/// <remarks>
/// Every rule is numbered by the ladder step that established it and is marked in the code with that
/// number. What no rule covers yet is not guessed: the component is put in the fallback column below
/// everything placed, and its connections go to the router, so a picture shows exactly how far the
/// rules reach. <c>y</c> grows upward.
/// </remarks>
internal sealed class LayoutEngine
{
    private const double Eps = 1e-9;

    private readonly CircuitGraph _graph;
    private readonly SemanticModel _model;
    private readonly LayoutHints _hints;
    private readonly double _margin;
    private readonly int _n;
    private readonly Dictionary<string, int> _index;
    private readonly List<Link> _links;
    private readonly SymbolWire[] _symbol;

    private readonly bool[] _placed;
    private readonly bool[] _fallback;
    private readonly bool[] _loop;
    private readonly bool[] _inline;
    
    private readonly Point[] _centre;
    private readonly Transform[] _transform;
    private readonly Dictionary<(int Component, int Port), Direction> _side = [];
    private readonly ImmutableArray<Point>?[] _routeOf;
    private readonly List<(int Link, Route Route)> _routes = [];
    private readonly List<Placement> _instruments = [];

    public LayoutEngine(CircuitGraph graph, SemanticModel model, LayoutHints hints, double margin)
    {
        _graph = graph;
        _model = model;
        _hints = hints;
        _margin = margin;
        _n = graph.Components.Length;
        _index = Index(graph);
        _links = Links(graph, model, _index);
        _symbol = graph.Components.Select(static c => SymbolCatalog.All.First(s => s.Id == SymbolCatalog.IdFor(c.Kind))).ToArray();
        _placed = new bool[_n];
        _fallback = new bool[_n];
        _inline = new bool[_n];
        _loop = new bool[_n];
        _centre = new Point[_n];
        _transform = new Transform[_n];
        _routeOf = new ImmutableArray<Point>?[_links.Count];

        for (var i = 0; i < _n; i++)
        {
            _transform[i] = Transform.Identity;
        }
    }

    /// <summary>One model connection between two graph components, as the script wrote it.</summary>
    private readonly record struct Link(int Connection, int From, int FromPort, int To, int ToPort);

    // ---- entry ----------------------------------------------------------------------------------------------------

    public Scene Solve()
    {
        var declared = Ordered(Enumerable.Range(0, _n)).Where(i => !_hints.Inferred.Contains(_graph.Components[i].Name)).ToList();

        if (Head(declared) is { } head && !Loop(head))
        {
            // C1 (ladder step 1, corrected by D-108): the first component -- the heat source, else the head of the chain -- sits at the origin in its drawn default.
            Place(head, Transform.Identity, new Point(0, 0));
        }

        Sequential();
        AlignBoundaries();
        Fallback(Ordered(Enumerable.Range(0, _n)).ToList());
        Connect();
        ComputeHops();
        PlaceInstruments();
        return ToScene();
    }


    /// <summary>C1 (D-108): the fragment's heat source -- the largest positive stated duty, else a declared supply boundary -- else the head of the chain: the first declared component in <c>Order</c> that no other declared component feeds.</summary>
    /// <param name="declared">The declared components in <c>Order</c>.</param>
    /// <returns>The component the first process path starts from, or null for an empty model.</returns>
    private int? Head(List<int> declared)
    {
        if (declared.Count == 0)
        {
            return null;
        }

        var sources = declared.Where(i => _graph.Components[i] is HeatExchanger { Power: > 0 }).OrderByDescending(i => ((HeatExchanger)_graph.Components[i]).Power).ToList();

        if (sources.Count > 0)
        {
            return sources[0];
        }

        foreach (var i in declared)
        {
            if (_graph.Components[i] is CircuitNode { Boundary: BoundaryRole.Supply })
            {
                return i;
            }
        }

        foreach (var i in declared)
        {
            if (!Upstream(i).Any(j => !_hints.Inferred.Contains(_graph.Components[j].Name)))
            {
                return i;
            }
        }

        return declared[0];
    }

    /// <summary>The components whose nominal flow reaches <paramref name="i"/>, directly or through nodes (<c>28</c> A3).</summary>
    /// <param name="i">The component.</param>
    /// <returns>Every non-node component upstream of it.</returns>
    private IEnumerable<int> Upstream(int i)
    {
        var seen = new HashSet<int> { i };
        var queue = new Queue<int>();
        queue.Enqueue(i);

        while (queue.Count > 0)
        {
            var j = queue.Dequeue();

            foreach (var link in _links)
            {
                var (peer, port) = link.To == j ? (link.From, link.ToPort) : link.From == j ? (link.To, link.FromPort) : (-1, -1);

                if (peer < 0 || !Enters(j, port, _graph.Components[j].Ports[port].Role) || !seen.Add(peer))
                {
                    continue;
                }

                if (Wildcard(peer))
                {
                    queue.Enqueue(peer);
                }
                else
                {
                    yield return peer;
                }
            }
        }
    }

    /// <summary>C4, C5 and A5 (steps 2 and 5): from every placed port, the run through any inline elements is followed to the next element with a box; that element is placed along the port's axis -- a node by C4, a component by C5, after C6's lead where the port is a loop member's flank -- and the inline elements are spread evenly along the run's longest segment.</summary>
    private void Sequential()
    {
        for (var changed = true; changed;)
        {
            changed = false;

            for (var k = 0; k < _links.Count; k++)
            {
                var link = _links[k];
                var (i, p) = _placed[link.From] && !_placed[link.To] ? (link.From, link.FromPort)
                    : _placed[link.To] && !_placed[link.From] ? (link.To, link.ToPort)
                    : (-1, -1);

                if (i < 0)
                {
                    continue;
                }

                var walk = Walk(i, p);

                if (walk.Far < 0 || _placed[walk.Far])
                {
                    // The run closes on two placed elements: no rule yet; the router draws it.
                    continue;
                }

                var anchor = AnchorOf(i, p);
                var (start, lead) = Lead(i, anchor, walk.Far);
                var run = Wildcard(walk.Far) ? PlaceNode(start, walk.Far, walk.FarPort) : PlaceFrom(start, walk.Far, walk.FarPort);

                if (run is not { } points)
                {
                    continue;
                }

                Assign(walk, [.. lead, .. points]);
                changed = true;
            }
        }
    }

    /// <summary>A run from a port: the links it covers in order, the inline elements between them with their near and far ports, and the element with a box it reaches (or -1 at a dead end).</summary>
    private readonly record struct Run(List<(int Link, bool Forward)> Links, List<(int Element, int Near, int Far)> Inline, int Far, int FarPort);

    /// <summary>Follows the connections from a port through inline elements (A5) to the next element with a box.</summary>
    /// <param name="component">The component the run leaves.</param>
    /// <param name="port">Its port.</param>
    /// <returns>The run.</returns>
    private Run Walk(int component, int port)
    {
        var links = new List<(int, bool)>();
        var inline = new List<(int, int, int)>();
        var (n, q) = (component, port);

        for (var guard = 0; guard <= _n; guard++)
        {
            var k = Enumerable.Range(0, _links.Count).FirstOrDefault(l => (_links[l].From == n && _links[l].FromPort == q) || (_links[l].To == n && _links[l].ToPort == q), -1);

            if (k < 0)
            {
                return new Run(links, inline, -1, -1);
            }

            var link = _links[k];
            var forward = link.From == n && link.FromPort == q;
            links.Add((k, forward));
            (n, q) = forward ? (link.To, link.ToPort) : (link.From, link.FromPort);

            if (!Inline(n))
            {
                return new Run(links, inline, n, q);
            }

            var other = _links.First(l => (l.From == n && l.FromPort != q) || (l.To == n && l.ToPort != q));
            var far = other.From == n ? other.FromPort : other.ToPort;
            inline.Add((n, q, far));
            q = far;
        }

        return new Run(links, inline, -1, -1);
    }

    /// <summary>Gives a run its polyline: each link its piece, each inline element its cut, spread evenly along the run's longest segment (A5).</summary>
    /// <param name="walk">The run.</param>
    /// <param name="points">Its polyline, from the first link's placed end to the boxed element it reaches.</param>
    private void Assign(Run walk, ImmutableArray<Point> points)
    {
        var pieces = Pieces(Normalise(points), walk.Inline.Count);

        for (var t = 0; t < walk.Links.Count; t++)
        {
            var (k, forward) = walk.Links[t];
            _routeOf[k] = forward ? pieces[t] : Reversed(pieces[t]);
        }

        for (var t = 0; t < walk.Inline.Count; t++)
        {
            var (element, near, far) = walk.Inline[t];
            Cut(element, near, far, pieces[t], pieces[t + 1]);
        }
    }

    /// <summary>A run cut into one more piece than <paramref name="count"/>, at points spread evenly along its longest segment.</summary>
    /// <param name="points">The run.</param>
    /// <param name="count">How many cuts.</param>
    /// <returns>The pieces in order, consecutive ones sharing their cut.</returns>
    private static List<ImmutableArray<Point>> Pieces(ImmutableArray<Point> points, int count)
    {
        var longest = 1;

        for (var s = 2; s < points.Length; s++)
        {
            if (points[s - 1].ManhattanTo(points[s]) > points[longest - 1].ManhattanTo(points[longest]) + Eps)
            {
                longest = s;
            }
        }

        var a = points[longest - 1];
        var b = points[longest];
        var pieces = new List<ImmutableArray<Point>>();
        List<Point> head = [.. points.Take(longest)];

        for (var c = 1; c <= count; c++)
        {
            var f = (double)c / (count + 1);
            var cut = new Point(a.X + ((b.X - a.X) * f), a.Y + ((b.Y - a.Y) * f));
            pieces.Add([.. head, cut]);
            head = [cut];
        }

        pieces.Add([.. head, .. points.Skip(longest)]);
        return pieces;
    }

    /// <summary>Whether an element is inline (A5): a declared pipe, or a node inferred by the language, with exactly two connections.</summary>
    private bool Inline(int j) =>
        (_graph.Components[j] is Pipe || (Wildcard(j) && _hints.Inferred.Contains(_graph.Components[j].Name))) && _links.Count(l => l.From == j || l.To == j) == 2;

    /// <summary>Places an inline element at the cut of its run and turns its two ports along the run (A5).</summary>
    /// <param name="n">The inline element.</param>
    /// <param name="near">Its port on the first half.</param>
    /// <param name="far">Its port on the second half.</param>
    /// <param name="first">The run up to the cut.</param>
    /// <param name="second">The run from the cut.</param>
    private void Cut(int n, int near, int far, ImmutableArray<Point> first, ImmutableArray<Point> second)
    {
        _inline[n] = true;
        Place(n, Transform.Identity, first[^1]);
        var back = first[^2].Offset(-first[^1].X, -first[^1].Y);
        var on = second[1].Offset(-second[0].X, -second[0].Y);
        _side[(n, near)] = Direction.Of(back) ?? Direction.Left;
        _side[(n, far)] = Direction.Of(on) ?? Direction.Right;
    }

    /// <summary>C6 (step 5): whatever hangs from a loop member's flank port -- a standing exchanger's second side -- runs level, away from the loop on that flank's side, after the port's straight margin: a primary arrives from the left and drops into <c>in2</c>, and its return leaves <c>out2</c> downward and turns back left. Off a chain's exchanger (step 2) a port's continuation still hangs straight.</summary>
    /// <param name="i">The placed component.</param>
    /// <param name="anchor">Its placed port.</param>
    /// <param name="next">The element about to be placed from it.</param>
    /// <returns>The anchor to place from, and the pipe up to it.</returns>
    private (PlacedAnchor Start, ImmutableArray<Point> Lead) Lead(int i, PlacedAnchor anchor, int next)
    {
        if (!_loop[i] || _graph.Components[i] is not HeatExchanger || anchor.Outward.Horizontal)
        {
            return (anchor, []);
        }

        var flank = anchor.At.X < _centre[i].X - Eps ? Direction.Left : Direction.Right;
        var corner = anchor.Along(_margin);
        return (Anchor(corner, flank, flank), [anchor.At]);
    }

    /// <summary>C7 (step 5): an open end aligns with its supply. Where a supply and a return boundary hang level off the same component on the same side, the nearer one is moved out to the farther one's line when no placed box lies in the way, and its run is laid again over the longer pipe.</summary>
    private void AlignBoundaries()
    {
        var ends = new List<(int Node, Run Walk, Direction Approach, int Root)>();

        for (var b = 0; b < _n; b++)
        {
            if (!_placed[b] || _inline[b] || _graph.Components[b] is not CircuitNode { Boundary: BoundaryRole.Supply or BoundaryRole.Return })
            {
                continue;
            }

            var at = Enumerable.Range(0, _links.Count).Where(l => _links[l].From == b || _links[l].To == b).ToList();

            if (at.Count != 1 || _routeOf[at[0]] is not { } route || route.Length < 2)
            {
                continue;
            }

            var link = _links[at[0]];
            var walk = Walk(b, link.From == b ? link.FromPort : link.ToPort);
            var (end, before) = link.From == b ? (route[0], route[1]) : (route[^1], route[^2]);

            if (walk.Far >= 0 && Direction.Of(end.Offset(-before.X, -before.Y)) is { Horizontal: true } approach)
            {
                ends.Add((b, walk, approach, Root(walk)));
            }
        }

        for (var x = 0; x < ends.Count; x++)
        {
            for (var y = x + 1; y < ends.Count; y++)
            {
                var (a, b) = (ends[x], ends[y]);

                if (a.Root != b.Root || a.Approach != b.Approach)
                {
                    continue;
                }

                var d = a.Approach;
                var (near, far) = (_centre[a.Node].X * d.X) < (_centre[b.Node].X * d.X) ? (a, b) : (b, a);
                var target = new Point(_centre[far.Node].X, _centre[near.Node].Y);

                if (Math.Abs(target.X - _centre[near.Node].X) < Eps || Clashes(near.Node, Transform.Identity, target) || Obstructed(_centre[near.Node], target, near.Node))
                {
                    continue;
                }

                _centre[near.Node] = target;
                var host = Walk(near.Walk.Far, near.Walk.FarPort);
                var points = new List<Point>();

                foreach (var (k, forward) in host.Links)
                {
                    var piece = forward ? _routeOf[k]!.Value : Reversed(_routeOf[k]!.Value);
                    points.AddRange(points.Count > 0 ? piece.Skip(1) : piece);
                }

                points[^1] = target.Towards(d.Opposite, InnerOf(near.Node).Width / 2);
                Assign(host, Normalise(points));
            }
        }
    }

    /// <summary>The component a boundary's chain hangs from: its run followed through two-port components to a loop member, a junction, or the chain's far end.</summary>
    /// <param name="walk">The run from the boundary.</param>
    /// <returns>The root component, or -1 for a dead end.</returns>
    private int Root(Run walk)
    {
        var (m, q) = (walk.Far, walk.FarPort);

        for (var guard = 0; guard <= _n && m >= 0 && !_loop[m] && !Wildcard(m); guard++)
        {
            var at = _links.Where(l => l.From == m || l.To == m).ToList();

            if (at.Count != 2)
            {
                break;
            }

            var other = at.First(l => !((l.From == m && l.FromPort == q) || (l.To == m && l.ToPort == q)));
            var next = Walk(m, other.From == m ? other.FromPort : other.ToPort);

            if (next.Far < 0)
            {
                break;
            }

            (m, q) = (next.Far, next.FarPort);
        }

        return m;
    }

    /// <summary>Whether a level or vertical move from <paramref name="from"/> to <paramref name="to"/> would cross a placed box or its margin, other than <paramref name="self"/>'s.</summary>
    private bool Obstructed(Point from, Point to, int self)
    {
        for (var i = 0; i < _n; i++)
        {
            if (i != self && _placed[i] && !_inline[i] && Passes(InnerOf(i).Grow(_margin), from, to))
            {
                return true;
            }
        }

        return false;
    }

    // ---- loops ----------------------------------------------------------------------------------------------------

    /// <summary>One member of a flow loop: the component, the port the loop enters it by, and the port it leaves by.</summary>
    private readonly record struct Member(int Component, int InPort, int OutPort);

    /// <summary>C2 (step 3): a simple flow loop from a heat source is a clockwise rectangle -- the source on the left side flowing up, the consumer (the most negative stated duty) on the right side flowing down, the members between them in flow order along the top rail from the source and along the bottom rail back to it; the rails sit one margin outside the source's ports and the consumer's column is as far right as the longer rail needs (H9, H10).</summary>
    /// <param name="source">The heat source (C1).</param>
    /// <returns>True when the loop was laid out; false when the source is on no simple loop with a standing consumer, in which case nothing was placed.</returns>
    private bool Loop(int source)
    {
        if (_graph.Components[source] is not HeatExchanger { Power: > 0 } || Cycle(source) is not { } cycle)
        {
            return false;
        }

        var consumerAt = -1;

        for (var k = 1; k < cycle.Count; k++)
        {
            if (_graph.Components[cycle[k].Component] is HeatExchanger { Power: < 0 } candidate && (consumerAt < 0 || candidate.Power < ((HeatExchanger)_graph.Components[cycle[consumerAt].Component]).Power))
            {
                consumerAt = k;
            }
        }

        if (consumerAt < 0)
        {
            return false;
        }

        var s = cycle[0];
        var c = cycle[consumerAt];
        var ts = Admitted(s.Component).Where(t => t.Arrangement == "default" && Outward(s.Component, s.OutPort, t) == Direction.Up && Outward(s.Component, s.InPort, t) == Direction.Down).ToList();
        var tc = Admitted(c.Component).Where(t => t.Arrangement == "default" && Outward(c.Component, c.InPort, t) == Direction.Up && Outward(c.Component, c.OutPort, t) == Direction.Down).ToList();

        if (ts.Count == 0 || tc.Count == 0)
        {
            return false;
        }

        foreach (var member in cycle)
        {
            _loop[member.Component] = true;
        }

        Place(s.Component, ts[0], new Point(0, 0));
        var sOut = AnchorOf(s.Component, s.OutPort);
        var sIn = AnchorOf(s.Component, s.InPort);
        var top = sOut.Along(_margin);
        var bottom = sIn.Along(_margin);
        var (_, consumerHeight) = tc[0].Size(_symbol[c.Component]);
        var yBottom = Math.Min(bottom.Y, top.Y - consumerHeight - (2 * _margin));
        var bottomStart = new Point(bottom.X, yBottom);
        var runs = new List<(Member From, Member To, List<Point> Points)>();

        // The top rail, in flow order from the source.
        var cursor = Anchor(top, Direction.Right, Direction.Right);
        var pending = new List<Point> { sOut.At, top };
        var previous = s;

        for (var k = 1; k < consumerAt; k++)
        {
            var m = cycle[k];

            if (PlaceFrom(cursor, m.Component, m.InPort) is not { } points)
            {
                return false;
            }

            pending.AddRange(points.Skip(1));
            runs.Add((previous, m, pending));
            cursor = AnchorOf(m.Component, m.OutPort);
            pending = [cursor.At];
            previous = m;
        }

        var topEnd = cursor;
        var topPending = pending;
        var topPrevious = previous;

        // The bottom rail, built left to right against the flow, each member placed by its outlet facing the source.
        cursor = Anchor(bottomStart, Direction.Right, Direction.Right);
        pending = [sIn.At, bottom, bottomStart];
        previous = s;
        var bottomRuns = new List<(Member Far, Member Near, List<Point> Points)>();

        for (var k = cycle.Count - 1; k > consumerAt; k--)
        {
            var m = cycle[k];

            if (PlaceFrom(cursor, m.Component, m.OutPort) is not { } points)
            {
                return false;
            }

            pending.AddRange(points.Skip(1));
            bottomRuns.Add((m, previous, pending));
            cursor = AnchorOf(m.Component, m.InPort);
            pending = [cursor.At];
            previous = m;
        }

        // The consumer's column: as far right as the longer rail needs, its inlet corner on the top rail.
        var inOffset = AnchorOffset(c.Component, c.InPort, tc[0])!.Value.Offset;
        var origin = new Point(Math.Max(topEnd.At.X, cursor.At.X), top.Y);
        var corner = Clear(c.Component, tc[0], origin, Direction.Right, new Point(-inOffset.X, -inOffset.Y - _margin));
        Place(c.Component, tc[0], corner.Offset(-inOffset.X, -inOffset.Y - _margin));
        var cIn = AnchorOf(c.Component, c.InPort);
        var cOut = AnchorOf(c.Component, c.OutPort);
        topPending.AddRange([corner, cIn.At]);
        runs.Add((topPrevious, c, topPending));
        var cOutOuter = cOut.Along(_margin);
        pending.AddRange([new Point(cOutOuter.X, yBottom), cOutOuter, cOut.At]);
        bottomRuns.Add((c, previous, pending));

        foreach (var (far, near, points) in bottomRuns)
        {
            points.Reverse();
            runs.Add((far, near, points));
        }

        foreach (var (from, to, points) in runs)
        {
            Assign(Walk(from.Component, from.OutPort), Normalise(points));
        }

        return true;
    }

    /// <summary>The simple flow loop through <paramref name="source"/>: from each component the first connected port the fluid leaves by, through inline nodes, until the walk returns; null where it reaches a junction, a boundary or a dead end first.</summary>
    /// <param name="source">Where the walk starts.</param>
    /// <returns>The members in flow order, the source first, or null.</returns>
    private List<Member>? Cycle(int source)
    {
        var ports = _graph.Components[source].Ports;

        for (var p = 0; p < ports.Length; p++)
        {
            if (!Enters(source, p, ports[p].Role) && Cycle(source, p) is { } members)
            {
                return members;
            }
        }

        return null;
    }

    /// <summary>The simple flow loop that leaves <paramref name="source"/> by <paramref name="leaving"/>, if that port is on one.</summary>
    /// <param name="source">Where the walk starts.</param>
    /// <param name="leaving">The source's port to leave by.</param>
    /// <returns>The members in flow order, the source first, or null.</returns>
    private List<Member>? Cycle(int source, int leaving)
    {
        var members = new List<Member>();
        var component = source;
        var inPort = -1;

        for (var guard = 0; guard <= _n; guard++)
        {
            var ports = _graph.Components[component].Ports;
            var outPort = component == source ? leaving : -1;

            for (var p = 0; p < ports.Length && outPort < 0; p++)
            {
                if (p != inPort && !Enters(component, p, ports[p].Role) && _links.Any(l => (l.From == component && l.FromPort == p) || (l.To == component && l.ToPort == p)))
                {
                    outPort = p;
                }
            }

            if (outPort < 0 || !_links.Any(l => (l.From == component && l.FromPort == outPort) || (l.To == component && l.ToPort == outPort)))
            {
                return null;
            }

            var (next, nextPort) = Follow(component, outPort);
            members.Add(new Member(component, inPort, outPort));

            if (next == source)
            {
                members[0] = members[0] with { InPort = nextPort };
                return members;
            }

            if (next < 0 || Wildcard(next))
            {
                return null;
            }

            component = next;
            inPort = nextPort;
        }

        return null;
    }

    /// <summary>The component and port a connection reaches from a port, passing through inline nodes.</summary>
    /// <param name="component">The component.</param>
    /// <param name="port">Its port.</param>
    /// <returns>The far end, or (-1, -1) where the port has no connection.</returns>
    private (int Component, int Port) Follow(int component, int port)
    {
        var (n, q) = (component, port);

        for (var guard = 0; guard <= _n; guard++)
        {
            var link = _links.FirstOrDefault(l => (l.From == n && l.FromPort == q) || (l.To == n && l.ToPort == q), new Link(-1, -1, -1, -1, -1));

            if (link.Connection < 0)
            {
                return (-1, -1);
            }

            (n, q) = link.From == n && link.FromPort == q ? (link.To, link.ToPort) : (link.From, link.FromPort);

            if (!Inline(n))
            {
                return (n, q);
            }

            var other = _links.First(l => (l.From == n && l.FromPort != q) || (l.To == n && l.ToPort != q));
            q = other.From == n ? other.FromPort : other.ToPort;
        }

        return (-1, -1);
    }



    /// <summary>A port's outward direction under a transform, or null for a wildcard.</summary>
    private Direction? Outward(int component, int port, Transform t) => AnchorOffset(component, port, t)?.Outward;

    /// <summary>C4: a node with a box on the placed port's axis, one clearance out or as far as H2 needs.</summary>
    /// <param name="anchor">The placed port.</param>
    /// <param name="j">The node.</param>
    /// <param name="q">Its port towards the placed one.</param>
    /// <returns>The pipe from the placed port to the node.</returns>
    private ImmutableArray<Point> PlaceNode(PlacedAnchor anchor, int j, int q)
    {
        var d = anchor.Outward;
        var (w, h) = Transform.Identity.Size(_symbol[j]);
        var half = d.Horizontal ? w / 2 : h / 2;
        var delta = new Point(d.X * half, d.Y * half);
        var at = Clear(j, Transform.Identity, anchor.At, d, delta);
        Place(j, Transform.Identity, at.Offset(delta.X, delta.Y));
        _side[(j, q)] = d.Opposite;
        return [anchor.At, at];
    }

    /// <summary>C5: a component placed off a placed port -- in the first admitted transform of its default arrangement whose port faces the pipe, straight along the axis; else, for a standing kind that cannot face a level pipe, below a single turn at the placed port's outer anchor, entered from above (C3); else in an alternative arrangement that faces the pipe. One clearance out or as far as H2 needs.</summary>
    /// <param name="anchor">The placed port.</param>
    /// <param name="j">The component.</param>
    /// <param name="q">Its port on the connection.</param>
    /// <returns>The pipe from the placed port to the component's port, or null when no admitted transform fits.</returns>
    private ImmutableArray<Point>? PlaceFrom(PlacedAnchor anchor, int j, int q)
    {
        var d = anchor.Outward;
        var admitted = Admitted(j).ToList();
        var facing = admitted.Where(t => t.Arrangement == "default" && AnchorOffset(j, q, t) is { } a && a.Outward == d.Opposite).ToList();

        if (facing.Count == 0)
        {
            var turned = admitted.Where(t => t.Arrangement == "default" && AnchorOffset(j, q, t) is { } a && a.Outward == Direction.Up).ToList();

            if (turned.Count > 0)
            {
                var turn = AnchorOffset(j, q, turned[0])!.Value.Offset;
                var corner = anchor.At.Towards(d, _margin);
                var inner = Clear(j, turned[0], corner, Direction.Down, new Point(-turn.X, -turn.Y));
                Place(j, turned[0], inner.Offset(-turn.X, -turn.Y));
                return [anchor.At, corner, inner];
            }

            facing = admitted.Where(t => AnchorOffset(j, q, t) is { } a && a.Outward == d.Opposite).ToList();
        }

        if (facing.Count == 0)
        {
            return null;
        }

        var offset = AnchorOffset(j, q, facing[0])!.Value.Offset;
        var end = Clear(j, facing[0], anchor.At, d, new Point(-offset.X, -offset.Y));
        Place(j, facing[0], end.Offset(-offset.X, -offset.Y));
        return [anchor.At, end];
    }



    private static ImmutableArray<Point> Reversed(ImmutableArray<Point> points) => [.. points.Reverse()];

    /// <summary>The nearest point at least one clearance from <paramref name="origin"/> along <paramref name="along"/> at which a box for <paramref name="j"/>, its centre <paramref name="delta"/> from that point, keeps the clearance (<c>28</c> H2) against everything placed; stepped by a tenth of a unit, so slack goes into the pipe.</summary>
    /// <param name="j">The component.</param>
    /// <param name="t">Its transform.</param>
    /// <param name="origin">Where the pipe starts from.</param>
    /// <param name="along">The axis it runs along.</param>
    /// <param name="delta">The centre relative to the returned point.</param>
    /// <returns>The point the pipe ends at: the component's inner anchor.</returns>
    private Point Clear(int j, Transform t, Point origin, Direction along, Point delta)
    {
        var gap = _margin;
        var at = origin.Towards(along, gap);

        while (Clashes(j, t, at.Offset(delta.X, delta.Y)))
        {
            gap += 0.1;
            at = origin.Towards(along, gap);
        }

        return at;
    }

    /// <summary>The transforms a kind admits (<c>28</c> A4, D-108): a standing kind (an exchanger) has no quarter turn, an upright one (a tank) only the left-right mirror, the rest turn freely. Identity first, so a tie keeps the drawn default (A9).</summary>
    /// <param name="j">The component.</param>
    /// <returns>The admitted transforms in tie-break order.</returns>
    private IEnumerable<Transform> Admitted(int j)
    {
        var symbol = _symbol[j];

        var admitted = _graph.Components[j] switch
        {
            HeatExchanger => Transform.All(symbol).Where(static t => t.Rotation is 0 or 180),
            Tank => Transform.All(symbol).Where(static t => t.Rotation == 0),
            _ => Transform.All(symbol),
        };

        // A9 (D-109): the default arrangement first, then the smaller turn, then unmirrored before mirrored -- so a symbol
        // reversing on a line is mirrored rather than half-turned and its top (a valve's stem, a pump's badge) stays up.
        return admitted.OrderBy(static t => t.Arrangement != "default").ThenBy(static t => t.Rotation).ThenBy(static t => t.Mirrored);
    }

    /// <summary>Whether a box for <paramref name="j"/> at <paramref name="centre"/> would break the clearance (<c>28</c> H2) against anything placed.</summary>
    /// <param name="j">The component.</param>
    /// <param name="t">Its transform.</param>
    /// <param name="centre">Its centre.</param>
    /// <returns>True when its inner box enters a placed outer box or its outer box is entered by a placed inner box.</returns>
    private bool Clashes(int j, Transform t, Point centre)
    {
        var (w, h) = t.Size(_symbol[j]);
        var inner = Box.Around(centre, w, h);
        var outer = inner.Grow(_margin);

        for (var i = 0; i < _n; i++)
        {
            if (!_placed[i] || i == j)
            {
                continue;
            }

            var other = InnerOf(i);

            if (other.Intersects(outer) || inner.Intersects(other.Grow(_margin)))
            {
                return true;
            }
        }

        return false;
    }

    // ---- fallback -------------------------------------------------------------------------------------------------

    /// <summary>What no rule has placed goes in a column below everything placed, in order, at the clearance, in its drawn default.</summary>
    private void Fallback(List<int> order)
    {
        var bottom = Enumerable.Range(0, _n).Where(i => _placed[i]).Select(i => InnerOf(i).Y).DefaultIfEmpty(0).Min();

        foreach (var i in order)
        {
            if (_placed[i])
            {
                continue;
            }

            var (_, h) = Transform.Identity.Size(_symbol[i]);
            Place(i, Transform.Identity, new Point(0, bottom - _margin - (h / 2)));
            _fallback[i] = true;
            bottom = InnerOf(i).Y;
        }
    }

    // ---- geometry -------------------------------------------------------------------------------------------------

    private bool Wildcard(int i) => _symbol[i].PortAnchors.ContainsKey("*");

    private (double Width, double Height) SizeOf(int i) => _inline[i] ? (0, 0) : _transform[i].Size(_symbol[i]);

    private Box InnerOf(int i)
    {
        var (w, h) = SizeOf(i);
        return Box.Around(_centre[i], w, h);
    }

    /// <summary>A named or indexed port's anchor relative to the centre, and its outward direction; null for a wildcard.</summary>
    private (Point Offset, Direction Outward)? AnchorOffset(int i, int port, Transform t)
    {
        var flow = _graph.Components[i];
        var symbol = _symbol[i];
        var name = flow.Ports[port].Name;

        if (t.Anchors(symbol).TryGetValue(name, out var anchor) && anchor.Direction is { } direction)
        {
            return (t.Apply(anchor.At[0], anchor.At[1]), t.Apply(Direction.Of(new Point(direction[0], direction[1])) ?? Direction.Right));
        }

        foreach (var rule in symbol.IndexedPortAnchors ?? [])
        {
            if (name.StartsWith(rule.Prefix, StringComparison.Ordinal) && flow is Tank tank)
            {
                var box = symbol.ViewBox;
                var x = rule.Side == "west" ? box[0] : box[0] + box[2];
                var level = tank.PortLevels[port];
                var y = box[1] + (level * box[3]);
                return (t.Apply(x, y), t.Apply(Direction.Of(new Point(rule.Direction[0], rule.Direction[1])) ?? Direction.Right));
            }
        }

        return null;
    }

    /// <summary>The placed anchor of a port: for a wildcard, on the side assigned to it, or the side facing right until one is.</summary>
    private PlacedAnchor AnchorOf(int i, int port)
    {
        var role = _graph.Components[i].Ports[port].Role;

        if (_inline[i])
        {
            var along = _side.GetValueOrDefault((i, port), Direction.Right);
            return Anchor(_centre[i], along, FlowOf(i, port, along, role));
        }

        if (AnchorOffset(i, port, _transform[i]) is { } named)
        {
            return Anchor(_centre[i].Offset(named.Offset.X, named.Offset.Y), named.Outward, FlowOf(i, port, named.Outward, role));
        }

        var side = _side.GetValueOrDefault((i, port), Direction.Right);
        var (w, h) = SizeOf(i);
        var at = _centre[i].Towards(side, side.Horizontal ? w / 2 : h / 2);
        return Anchor(at, side, FlowOf(i, port, side, role));
    }

    /// <summary>The flow vector at a port (<c>28</c> A3): inward where the fluid enters, outward where it leaves.</summary>
    private Direction FlowOf(int i, int port, Direction outward, PortRole role) => Enters(i, port, role) ? outward.Opposite : outward;

    /// <summary>Whether the nominal flow enters component <paramref name="i"/> at <paramref name="port"/> (<c>28</c> A3): a return boundary receives and a supply sends; else by the port's role; else by the joined port's; else the way the connection was written.</summary>
    /// <param name="i">The component.</param>
    /// <param name="port">Its port index.</param>
    /// <param name="role">The port's role.</param>
    /// <returns>True where fluid flows into the component at that port.</returns>
    private bool Enters(int i, int port, PortRole role)
    {
        var flow = _graph.Components[i];

        if (flow is CircuitNode { Boundary: BoundaryRole.Supply })
        {
            return false;
        }

        if (flow is CircuitNode { Boundary: BoundaryRole.Return })
        {
            return true;
        }

        switch (role)
        {
            case PortRole.Inlet:
                return true;
            case PortRole.Outlet:
                return false;
            default:
                foreach (var link in _links)
                {
                    var (peer, peerPort, towards) = link.From == i && link.FromPort == port ? (link.To, link.ToPort, false)
                        : link.To == i && link.ToPort == port ? (link.From, link.FromPort, true)
                        : (-1, -1, false);

                    if (peer < 0)
                    {
                        continue;
                    }

                    return _graph.Components[peer] switch
                    {
                        CircuitNode { Boundary: BoundaryRole.Supply } => true,
                        CircuitNode { Boundary: BoundaryRole.Return } => false,
                        _ => _graph.Components[peer].Ports[peerPort].Role switch
                        {
                            PortRole.Inlet => false,
                            PortRole.Outlet => true,
                            _ => towards,
                        },
                    };
                }

                return false;
        }
    }

    private static PlacedAnchor Anchor(Point at, Direction outward, Direction flow) => new(at, outward.AsPoint, flow);

    private void Place(int i, Transform t, Point centre)
    {
        _transform[i] = t;
        _centre[i] = centre;
        _placed[i] = true;
    }

    private IEnumerable<int> Ordered(IEnumerable<int> components)
    {
        var position = new Dictionary<int, int>();
        for (var i = 0; i < _hints.Order.Length; i++)
        {
            if (_index.TryGetValue(_hints.Order[i], out var index))
            {
                position[index] = i;
            }
        }

        return components.OrderBy(i => position.GetValueOrDefault(i, _hints.Order.Length + i));
    }

    // ---- connections ----------------------------------------------------------------------------------------------

    /// <summary>Every connection: a straight line where the two anchors face each other on one axis, else the router's path.</summary>
    private void Connect()
    {
        var router = new OrthogonalRouter(_margin);

        for (var i = 0; i < _n; i++)
        {
            if (!_inline[i])
            {
                router.AddBox(InnerOf(i), i);
            }
        }

        for (var k = 0; k < _links.Count; k++)
        {
            var link = _links[k];

            if (link.From == link.To)
            {
                continue;
            }

            foreach (var (c, p) in new[] { (link.From, link.FromPort), (link.To, link.ToPort) })
            {
                if (!Wildcard(c))
                {
                    router.AddStub(AnchorOf(c, p), _margin, c, p);
                }
            }
        }

        for (var k = 0; k < _links.Count; k++)
        {
            var link = _links[k];

            if (link.From == link.To)
            {
                continue;
            }
            // A fallback component's connections are not worth the router's time: an L between the anchors says all there is to say.
            if (_fallback[link.From] || _fallback[link.To])
            {
                var a = AnchorOf(link.From, link.FromPort).At;
                var b = AnchorOf(link.To, link.ToPort).At;
                _routeOf[k] ??= Normalise([a, new Point(b.X, a.Y), b]);
            }

            router.ReleaseStub(link.From, link.FromPort);
            router.ReleaseStub(link.To, link.ToPort);
            router.ReleaseStub(link.To, link.ToPort);
            var towardsB = Wildcard(link.To) ? _centre[link.To] : AnchorOf(link.To, link.ToPort).At;
            var towardsA = Wildcard(link.From) ? _centre[link.From] : AnchorOf(link.From, link.FromPort).At;
            var result = _routeOf[k] is not null ? null : router.Route(Ends(link.From, link.FromPort, towardsB).ToList(), link.From, Ends(link.To, link.ToPort, towardsA).ToList(), link.To);

            if (_routeOf[k] is not null)
            {
                // Drawn by a rule already.
            }
            else if (result is null)
            {
                var a = AnchorOf(link.From, link.FromPort).At;
                var b = AnchorOf(link.To, link.ToPort).At;
                _routeOf[k] = Normalise([a, new Point(b.X, a.Y), b]);
            }
            else
            {
                if (Wildcard(link.From))
                {
                    _side[(link.From, link.FromPort)] = result.From.Outward;
                }

                if (Wildcard(link.To))
                {
                    _side[(link.To, link.ToPort)] = result.To.Outward;
                }

                _routeOf[k] = Normalise(result.Points);
            }

            var points = _routeOf[k]!.Value;
            for (var s = 1; s < points.Length; s++)
            {
                router.AddPipe(points[s - 1], points[s], k, link.From, link.To);
            }

            _routes.Add((k, new Route($"c{link.Connection}", "pipe", points, [])));
        }
    }

    /// <summary>The anchors a connection may leave a component from: one for a named port; for a junction's wildcard, its assigned side, else the free sides that do not face away from the peer (<c>D-105</c>).</summary>
    private IEnumerable<PlacedAnchor> Ends(int component, int port, Point towards)
    {
        if (!Wildcard(component) || _side.ContainsKey((component, port)))
        {
            yield return AnchorOf(component, port);
            yield break;
        }

        var inner = InnerOf(component);
        var d = towards.Offset(-inner.Centre.X, -inner.Centre.Y);
        var used = new HashSet<Direction>();
        for (var p = 0; p < _graph.Components[component].Ports.Length; p++)
        {
            if (_side.TryGetValue((component, p), out var s))
            {
                used.Add(s);
            }
        }

        var any = false;

        for (var pass = 0; pass < 2 && !any; pass++)
        {
            foreach (var direction in Direction.All)
            {
                if ((direction.X * d.X) + (direction.Y * d.Y) < -Eps)
                {
                    continue;
                }

                if (pass == 0 && used.Contains(direction))
                {
                    continue;
                }

                any = true;
                var role = _graph.Components[component].Ports[port].Role;
                yield return Anchor(inner.Centre.Towards(direction, direction.Horizontal ? inner.Width / 2 : inner.Height / 2), direction, FlowOf(component, port, direction, role));
            }
        }
    }

    /// <summary>Marks where a later pipe crosses an earlier one, on the later one.</summary>
    private void ComputeHops()
    {
        for (var b = 0; b < _routes.Count; b++)
        {
            var hops = ImmutableArray.CreateBuilder<Point>();

            for (var a = 0; a < b; a++)
            {
                if (_routes[a].Route.Kind != "pipe" || _routes[b].Route.Kind != "pipe")
                {
                    continue;
                }

                hops.AddRange(Crossings(_routes[a].Route.Points, _routes[b].Route.Points));
            }

            if (hops.Count > 0)
            {
                _routes[b] = (_routes[b].Link, _routes[b].Route with { Hops = hops.ToImmutable() });
            }
        }
    }

    /// <summary>Where two orthogonal polylines cross, strictly inside both segments.</summary>
    private static List<Point> Crossings(ImmutableArray<Point> a, ImmutableArray<Point> b)
    {
        var result = new List<Point>();

        for (var i = 1; i < a.Length; i++)
        {
            for (var j = 1; j < b.Length; j++)
            {
                var p = a[i - 1];
                var q = a[i];
                var s = b[j - 1];
                var t = b[j];
                var aVertical = Math.Abs(p.X - q.X) < Eps;
                var bVertical = Math.Abs(s.X - t.X) < Eps;

                if (aVertical == bVertical)
                {
                    continue;
                }

                var (v0, v1, h0, h1) = aVertical ? (p, q, s, t) : (s, t, p, q);
                var x = v0.X;
                var y = h0.Y;

                if (x > Math.Min(h0.X, h1.X) + Eps && x < Math.Max(h0.X, h1.X) - Eps && y > Math.Min(v0.Y, v1.Y) + Eps && y < Math.Max(v0.Y, v1.Y) - Eps)
                {
                    result.Add(new Point(x, y));
                }
            }
        }

        return result;
    }

    /// <summary>Whether an orthogonal segment runs through a box's interior.</summary>
    internal static bool Passes(Box box, Point a, Point b) =>
        Math.Abs(a.X - b.X) < Eps
            ? a.X > box.X + Eps && a.X < box.Right - Eps && Math.Max(Math.Min(a.Y, b.Y), box.Y) < Math.Min(Math.Max(a.Y, b.Y), box.Top) - Eps
            : a.Y > box.Y + Eps && a.Y < box.Top - Eps && Math.Max(Math.Min(a.X, b.X), box.X) < Math.Min(Math.Max(a.X, b.X), box.Right) - Eps;

    /// <summary>A polyline without duplicate, collinear or backtracking points (<c>28</c> §19–20).</summary>
    /// <param name="points">Orthogonal, in order.</param>
    /// <returns>The simplest equivalent polyline.</returns>
    internal static ImmutableArray<Point> Normalise(IReadOnlyList<Point> points)
    {
        var result = new List<Point>();

        foreach (var p in points)
        {
            if (result.Count > 0 && result[^1].ManhattanTo(p) < Eps)
            {
                continue;
            }

            result.Add(p);

            while (result.Count >= 3)
            {
                var a = result[^3];
                var b = result[^2];
                var c = result[^1];
                var collinear = (Math.Abs(a.X - b.X) < Eps && Math.Abs(b.X - c.X) < Eps) || (Math.Abs(a.Y - b.Y) < Eps && Math.Abs(b.Y - c.Y) < Eps);

                if (!collinear)
                {
                    break;
                }

                result.RemoveAt(result.Count - 2);

                if (result[^2].ManhattanTo(result[^1]) < Eps)
                {
                    result.RemoveAt(result.Count - 1);
                }
            }
        }

        return [.. result];
    }

    // ---- instruments and labels -----------------------------------------------------------------------------------

    /// <summary>Instruments and controllers sit beside their anchor: above it when that is free, else below, else beside, then further right.</summary>
    private void PlaceInstruments()
    {
        var instruments = new Dictionary<string, Box>(StringComparer.Ordinal);

        foreach (var element in _hints.NonFlowElements)
        {
            if (!_index.TryGetValue(element.PlacementAnchorId, out var anchor) || !_placed[anchor])
            {
                continue;
            }

            var kind = _model.Components.FirstOrDefault(c => c.Name == element.ComponentId)?.Kind?.Keyword ?? "controller";
            var symbol = SymbolCatalog.All.First(s => s.Id == SymbolCatalog.IdFor(kind));
            var size = symbol.ViewBox;
            var host = InnerOf(anchor);
            var candidates = new[]
            {
                new Point(host.Centre.X, host.Top + _margin + (size[3] / 2)),
                new Point(host.Centre.X, host.Y - _margin - (size[3] / 2)),
                new Point(host.X - _margin - (size[2] / 2), host.Centre.Y),
                new Point(host.Right + _margin + (size[2] / 2), host.Centre.Y),
            };

            var inner = candidates
                .Select(at => Box.Around(at, size[2], size[3]))
                .FirstOrDefault(box => !Collides(box, instruments.Values), Box.Around(candidates[0], size[2], size[3]));

            while (Collides(inner, instruments.Values))
            {
                inner = inner.Offset(size[2] + _margin, 0);
            }

            instruments[element.ComponentId] = inner;

            _instruments.Add(new Placement
            {
                ComponentId = element.ComponentId,
                SymbolId = symbol.Id,
                Inner = inner,
                Outer = inner.Grow(_margin),
                Rotation = 0,
                Mirrored = false,
                Arrangement = "default",
                Anchors = ImmutableSortedDictionary<string, PlacedAnchor>.Empty.Add("*", Anchor(inner.Centre, Direction.Down, Direction.Down)),
                LabelAt = inner.Centre,
                Source = "computed",
            });

            AddSignal(element.ComponentId + ":measures", inner, element.MeasurementTargetId);

            if (element.ActuationTargetId is { } actuated && actuated != element.MeasurementTargetId)
            {
                AddSignal(element.ComponentId + ":actuates", inner, actuated);
            }
        }
    }

    /// <summary>Whether an instrument at a candidate box would sit within a margin of a placed symbol, another instrument, or a pipe.</summary>
    private bool Collides(Box candidate, IEnumerable<Box> instruments)
    {
        var outer = candidate.Grow(_margin);

        for (var i = 0; i < _n; i++)
        {
            if (_placed[i] && InnerOf(i).Intersects(outer))
            {
                return true;
            }
        }

        foreach (var (_, route) in _routes)
        {
            for (var k = 1; k < route.Points.Length; k++)
            {
                if (Passes(outer, route.Points[k - 1], route.Points[k]))
                {
                    return true;
                }
            }
        }

        return instruments.Any(other => other.Intersects(outer));
    }

    private void AddSignal(string id, Box from, string targetId)
    {
        if (!_index.TryGetValue(targetId, out var target) || !_placed[target])
        {
            return;
        }

        var host = InnerOf(target);
        var start = from.Centre;
        var end = host.Centre;
        var points = new List<Point> { start };

        if (Math.Abs(start.X - end.X) > Eps && Math.Abs(start.Y - end.Y) > Eps)
        {
            points.Add(new Point(start.X, end.Y));
        }

        points.Add(end);
        _routes.Add((-1, new Route(id, "signal", Normalise(points), [])));
    }

    /// <summary>The label just outside the placed box, on the side the symbol's label anchor names: text does not turn with the symbol.</summary>
    private Point LabelFor(int component)
    {
        var box = InnerOf(component);
        var anchor = _symbol[component].LabelAnchor;
        const double gap = 0.15;

        if (Math.Abs(anchor[1]) >= Math.Abs(anchor[0]))
        {
            return new Point(box.Centre.X, anchor[1] > 0 ? box.Top + gap : box.Y - gap);
        }

        return new Point(anchor[0] < 0 ? box.X - gap : box.Right + gap, box.Centre.Y);
    }

    // ---- output ---------------------------------------------------------------------------------------------------

    private Scene ToScene()
    {
        var placements = ImmutableArray.CreateBuilder<Placement>();
        var extent = (Box?)null;

        foreach (var i in Ordered(Enumerable.Range(0, _n)))
        {
            var flow = _graph.Components[i];
            var anchors = ImmutableSortedDictionary.CreateBuilder<string, PlacedAnchor>(StringComparer.Ordinal);

            for (var port = 0; port < flow.Ports.Length; port++)
            {
                anchors[flow.Ports[port].Name.Length == 0 ? $"#{port}" : flow.Ports[port].Name] = AnchorOf(i, port);
            }

            var inner = InnerOf(i);
            var placement = new Placement
            {
                ComponentId = flow.Name,
                SymbolId = _symbol[i].Id,
                Inner = inner,
                Outer = _inline[i] ? inner : inner.Grow(_margin),
                Rotation = _transform[i].Rotation,
                Mirrored = _transform[i].Mirrored,
                Arrangement = _transform[i].Arrangement,
                Anchors = anchors.ToImmutable(),
                LabelAt = LabelFor(i),
                Source = "computed",
                Group = _fallback[i] ? "fallback" : null,
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

        foreach (var route in routes)
        {
            foreach (var point in route.Points)
            {
                var dot = new Box(point.X, point.Y, 0, 0);
                extent = extent is { } e ? e.Union(dot) : dot;
            }
        }

        return new Scene
        {
            Placements = [.. placements.Select(Rounded)],
            Routes = [.. routes.Select(static r => r with { Points = [.. r.Points.Select(Rounded)], Hops = [.. r.Hops.Select(Rounded)] })],
            Extent = Rounded(extent ?? new Box(0, 0, 0, 0)),
            Margin = _margin,
        };
    }

    // Every coordinate leaves rounded to a micro-unit, so a point reached two ways is the same point to a reader and to an equality test.
    private static double Rounded(double v) => Math.Round(v, 6);

    private static Point Rounded(Point p) => new(Rounded(p.X), Rounded(p.Y));

    private static Box Rounded(Box b) => new(Rounded(b.X), Rounded(b.Y), Rounded(b.Width), Rounded(b.Height));

    private static PlacedAnchor Rounded(PlacedAnchor a) => a with { At = Rounded(a.At) };

    private static Placement Rounded(Placement p) => p with
    {
        Inner = Rounded(p.Inner),
        Outer = Rounded(p.Outer),
        LabelAt = Rounded(p.LabelAt),
        Anchors = p.Anchors.ToImmutableSortedDictionary(static a => a.Key, static a => Rounded(a.Value), StringComparer.Ordinal),
    };

    // ---- helpers --------------------------------------------------------------------------------------------------

    private static Dictionary<string, int> Index(CircuitGraph graph)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < graph.Components.Length; i++)
        {
            index[graph.Components[i].Name] = i;
        }

        return index;
    }

    /// <summary>Each model connection as component and port indices, matched on the graph's adjacency.</summary>
    private static List<Link> Links(CircuitGraph graph, SemanticModel model, Dictionary<string, int> index)
    {
        var links = new List<Link>();
        var used = new HashSet<(int, int)>();

        for (var i = 0; i < model.Connections.Length; i++)
        {
            var connection = model.Connections[i];

            if (!index.TryGetValue(connection.From.Component, out var from) || !index.TryGetValue(connection.To.Component, out var to))
            {
                continue;
            }

            var fromPort = PortTowards(graph, from, to, connection.From.Port, used);
            if (fromPort < 0)
            {
                continue;
            }

            var peer = graph.Adjacency.Peer(from, fromPort);
            used.Add((from, fromPort));
            used.Add((peer.Component, peer.Port));
            links.Add(new Link(i, from, fromPort, peer.Component, peer.Port));
        }

        return links;
    }

    private static int PortTowards(CircuitGraph graph, int from, int to, string port, HashSet<(int, int)> used)
    {
        var flow = graph.Components[from];

        for (var p = 0; p < flow.Ports.Length; p++)
        {
            var peer = graph.Adjacency.Peer(from, p);

            if (!peer.Exists || peer.Component != to || used.Contains((from, p)))
            {
                continue;
            }

            if (port.Length == 0 || string.Equals(flow.Ports[p].Name, port, StringComparison.Ordinal))
            {
                return p;
            }
        }

        return -1;
    }
}
