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
    private Point _loopCentre;
    private readonly bool[] _inline;
    
    private readonly Point[] _centre;
    private readonly Transform[] _transform;
    private readonly Dictionary<(int Component, int Port), Direction> _side = [];
    private readonly ImmutableArray<Point>?[] _routeOf;
    private readonly List<(int Link, Route Route)> _routes = [];
    private readonly List<PlacementNote> _trace = [];
    private readonly List<Placement> _instruments = [];
    /// <summary>Every instrument's inner box in placement order; the box at index k is owned by <c>_n + k</c> for the signal router (C-94).</summary>
    private readonly List<Box> _instrumentBoxes = [];

    /// <summary>The links on the supply side (C16), found once when the first route asks.</summary>
    private HashSet<int>? _supply;

    // The groups laid out as one object (A8): every ring and every block, outer before inner, as (kind, members, whether it is the ring that holds the heat source).
    private readonly List<(string Kind, List<int> Members, bool Top)> _groups = [];

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
        // C17: independent circuits stack top to bottom in script order, each laid out by its own rules on its own canvas and then moved under the one before, left edges aligned.
        var left = double.NaN;
        var floor = double.NaN;

        var ordinal = 0;

        foreach (var fragment in Fragments())
        {
            var declared = Ordered(fragment).Where(i => !_hints.Inferred.Contains(_graph.Components[i].Name)).ToList();
            var before = (bool[])_placed.Clone();
            var routed = _routeOf.Select(static r => r is not null).ToArray();
            var subject = $"fragment {++ordinal}";
            Array.Clear(_placed);
            Note(subject, "members", string.Join(", ", declared.Select(i => _graph.Components[i].Name)));

            // Each form in turn, the first that applies drawing the fragment; the trace says which, and why the ones before it declined.
            if (Head(declared) is { } head)
            {
                var drawn = Tried(subject, "C2 sourced loop", () => Loop(head))
                    || Tried(subject, "C20 ring of one", () => Ring(head, fragment))
                    || Tried(subject, "C19 open supply-to-return", () => Open(head, fragment))
                    || Tried(subject, "C18 unsourced ring", () => Closed(fragment));

                if (!drawn)
                {
                    // C1 (ladder step 1, corrected by D-108): the first component -- the heat source, else the head of the chain -- sits at the origin in its drawn default.
                    Note(subject, "form", "chain (C1): every form declined");
                    Place(head, Transform.Identity, new Point(0, 0), "C1", "the fragment's head at the origin, no ring, loop or open form having applied");
                }
            }

            Sequential();
            AlignBoundaries();
            var members = fragment.Where(i => _placed[i]).ToList();
            var links = Enumerable.Range(0, _links.Count).Where(k => _routeOf[k] is not null && !routed[k]).ToList();

            for (var i = 0; i < _n; i++)
            {
                _placed[i] |= before[i];
            }

            if (members.Count == 0)
            {
                continue;
            }

            var box = Extent(members, links);

            if (double.IsNaN(left))
            {
                left = box.X;
                floor = box.Y;
                continue;
            }

            var dx = left - box.X;
            var dy = floor - _margin - box.Top;

            foreach (var i in members)
            {
                _centre[i] = _centre[i].Offset(dx, dy);
            }

            foreach (var k in links)
            {
                _routeOf[k] = [.. _routeOf[k]!.Value.Select(p => p.Offset(dx, dy))];
            }

            floor = box.Y + dy;
        }

        Fallback(Ordered(Enumerable.Range(0, _n)).ToList());
        Connect();
        PlaceInstruments();
        ComputeHops();
        return ToScene();
    }

    private string? _declined;

    /// <summary>Runs one form and records whether it drew the fragment, with the reason it declined when it did not.</summary>
    private bool Tried(string subject, string form, Func<bool> attempt)
    {
        _declined = null;
        var drawn = attempt();
        Note(subject, "form", drawn ? $"{form}: drawn" : $"{form}: declined -- {_declined ?? "not built for this fragment"}");
        return drawn;
    }

    /// <summary>Records why a form is about to decline, then declines.</summary>
    private bool Decline(string reason)
    {
        _declined = reason;
        return false;
    }

    /// <summary>The graph's connected fragments (C17), the fragments in the order the script declares their first components and each fragment's members in the engine's order; a component with no connection is a fragment of one.</summary>
    private List<List<int>> Fragments()
    {
        var root = Enumerable.Range(0, _n).ToArray();

        int Find(int i)
        {
            while (root[i] != i)
            {
                root[i] = root[root[i]];
                i = root[i];
            }

            return i;
        }

        foreach (var link in _links)
        {
            root[Find(link.From)] = Find(link.To);
        }

        var fragments = new Dictionary<int, List<int>>();
        var first = new Dictionary<int, int>();

        foreach (var i in Ordered(Enumerable.Range(0, _n)))
        {
            var r = Find(i);

            if (!fragments.TryGetValue(r, out var members))
            {
                members = [];
                fragments[r] = members;
                first[r] = int.MaxValue;
            }

            members.Add(i);
            var name = _graph.Components[i].Name;
            var declared = -1;

            for (var c = 0; c < _model.Components.Length && declared < 0; c++)
            {
                if (string.Equals(_model.Components[c].Name, name, StringComparison.Ordinal))
                {
                    declared = c;
                }
            }

            if (declared >= 0)
            {
                first[r] = Math.Min(first[r], declared);
            }
        }

        return fragments.Keys.OrderBy(r => first[r]).Select(r => fragments[r]).ToList();
    }

    /// <summary>The box a fragment occupies: its members' outer boxes and the points of its routes.</summary>
    private Box Extent(List<int> members, List<int> links)
    {
        var xs = new List<double>();
        var ys = new List<double>();

        foreach (var i in members)
        {
            var outer = InnerOf(i).Grow(_inline[i] ? 0 : _margin);
            xs.Add(outer.X);
            xs.Add(outer.Right);
            ys.Add(outer.Y);
            ys.Add(outer.Top);
        }

        foreach (var k in links)
        {
            foreach (var p in _routeOf[k]!.Value)
            {
                xs.Add(p.X);
                ys.Add(p.Y);
            }
        }

        return new Box(xs.Min(), ys.Min(), xs.Max() - xs.Min(), ys.Max() - ys.Min());
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

        var sources = declared.Where(IsSource).OrderByDescending(i => ((HeatExchanger)_graph.Components[i]).Power).ToList();

        if (sources.Count > 0)
        {
            Note(_graph.Components[sources[0]].Name, "head", $"the largest positive duty ({((HeatExchanger)_graph.Components[sources[0]]).Power / 1000:0.##} kW) among {sources.Count} source(s) (C1, D-108)");
            return sources[0];
        }

        foreach (var i in declared)
        {
            if (_graph.Components[i] is CircuitNode { Boundary: BoundaryRole.Inlet })
            {
                Note(_graph.Components[i].Name, "head", "no positive duty; the first inlet boundary (C1)");
                return i;
            }
        }

        foreach (var i in declared)
        {
            if (!Upstream(i).Any(j => !_hints.Inferred.Contains(_graph.Components[j].Name)))
            {
                Note(_graph.Components[i].Name, "head", "no positive duty and no inlet; the first member with nothing declared upstream of it (C1, A3)");
                return i;
            }
        }

        Note(_graph.Components[declared[0]].Name, "head", "no positive duty, no inlet, nothing without an upstream; the first declared member (C1)");
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

                if (Wildcard(i) && !_inline[i] && !_side.ContainsKey((i, p)))
                {
                    _side[(i, p)] = FreeSide(i);
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

    /// <summary>Whether an element is inline (A5, <c>D-114</c>): a pipe or a node with exactly two connections -- never a boundary, which is an end of the plant and keeps its box whatever meets it there.</summary>
    private bool Inline(int j) =>
        (_graph.Components[j] is Pipe || (Wildcard(j) && _graph.Components[j] is not CircuitNode { Boundary: BoundaryRole.Inlet or BoundaryRole.Outlet })) && _links.Count(l => l.From == j || l.To == j) == 2;

    /// <summary>Places an inline element at the cut of its run and turns its two ports along the run (A5).</summary>
    /// <param name="n">The inline element.</param>
    /// <param name="near">Its port on the first half.</param>
    /// <param name="far">Its port on the second half.</param>
    /// <param name="first">The run up to the cut.</param>
    /// <param name="second">The run from the cut.</param>
    private void Cut(int n, int near, int far, ImmutableArray<Point> first, ImmutableArray<Point> second)
    {
        _inline[n] = true;
        Place(n, Transform.Identity, first[^1], "A5", "an inline node cut into its run");
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
        // Two margins, not one: the loop's own rail on this side runs along the outer anchors' line, and a
        // corner there put the primary's approach and the secondary's rail on one line 0.3 apart, reading
        // as a pipe crossing the exchanger's top with a gap (C-86). One margin further out, the two corners
        // sit on different lines and the picture separates them.
        var corner = anchor.Along(2 * _margin);
        return (Anchor(corner, flank, flank), [anchor.At]);
    }

    /// <summary>C7 (step 5): an open end aligns with its supply. Where a supply and a return boundary hang level off the same component on the same side, the nearer one is moved out to the farther one's line when no placed box lies in the way, and its run is laid again over the longer pipe.</summary>
    private void AlignBoundaries()
    {
        var ends = new List<(int Node, Run Walk, Direction Approach, int Root)>();

        for (var b = 0; b < _n; b++)
        {
            // C7: an open end is any node with one connection -- a declared boundary or the terminating node the language infers for an open port.
            if (!_placed[b] || _inline[b] || !Wildcard(b))
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
                // C7: the fragment's inlets and outlets line up with one another wherever they hang (the user's correction on the tour: SB1, NB1 and NB2 on one vertical); inferred open ends pair by their root.
                var root = _graph.Components[b] is CircuitNode { Boundary: not BoundaryRole.Interior } ? -3 : Root(walk);
                ends.Add((b, walk, approach, root));
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
                Note(_graph.Components[near.Node].Name, "C7", $"an open end aligned to ({target.X:0.##}, {target.Y:0.##}) with its counterpart");
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

        // Every member of the loop is one root: open ends hanging off the same loop are paired (C7) wherever they hang from it.
        return m >= 0 && _loop[m] ? -2 : m;
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

    /// <summary>
    /// The consumer side of a ring, laid out at a provisional place: one member, or a whole inner loop as a
    /// block (A8, C11). The ring slides it into place and shifts its runs with it.
    /// </summary>
    /// <param name="Members">The boxed components the unit moves as one.</param>
    /// <param name="In">Where the ring's top rail enters, facing left (a corner) or up (from the rail above).</param>
    /// <param name="Out">Where the ring's right side leaves, facing down or right.</param>
    /// <param name="OutFrom">The member and port the leaving run walks from.</param>
    /// <param name="Bottom">The lowest inner-box edge, provisional units.</param>
    /// <param name="Runs">The unit's own runs, provisional units, assigned by the ring after the shift.</param>
    private sealed record Unit(List<int> Members, PlacedAnchor In, PlacedAnchor Out, Member OutFrom, double Bottom, List<(Member From, List<Point> Points)> Runs);

    /// <summary>One thing on a rail: a member placed by <see cref="OnRail"/>, or a block laid out as a unit (C11).</summary>
    private readonly record struct Item(Member? Member, Unit? Unit);

    /// <summary>A branch block hanging between the rails (C14): placed under its top junction, its return still to be laid to its bottom junction, which stands under the top one.</summary>
    private sealed record Hanger(PlacedAnchor Out, Member OutFrom, int Top, int Bottom, int BottomPort);

    private bool Loop(int source)
    {
        if (!IsSource(source))
        {
            return Decline("the head is not a source (a positive duty)");
        }

        if (Cycle(source) is not { } cycle)
        {
            return Decline("no cycle of boxed members returns to the source");
        }

        var s = cycle[0];
        var ts = Admitted(s.Component).Where(t => t.Arrangement == "default" && Outward(s.Component, s.OutPort, t) == Direction.Up && Outward(s.Component, s.InPort, t) == Direction.Down).ToList();

        if (ts.Count == 0)
        {
            return Decline("the source has no default arrangement with its outlet up and its inlet down");
        }

        _loopCentre = new Point(0, 0);

        foreach (var member in cycle)
        {
            _loop[member.Component] = true;
        }

        var ring = cycle.Select(static m => m.Component).ToHashSet();
        HashSet<int> avoid = [source];
        var mark = _groups.Count;

        // C11: every inner loop along the ring is a block. The last in flow order is the ring's right side with its outlet facing back; the others stand on the top rail with their outlets facing on, so a chain steps from block to block. Without any, the consumer stands on the right alone.
        var ranges = Ranges(cycle, 1, avoid);

        Unit? unit;
        int unitStart;
        int unitEnd;
        var items = new List<Item>();

        if (ranges.Count > 0)
        {
            var last = ranges[^1];
            unitStart = last.Start;
            unitEnd = last.End;
            unit = Block(last.Inner, cycle[unitStart], cycle[unitEnd], avoid, Direction.Left);
            var next = 1;

            foreach (var (start, end, inner) in ranges.SkipLast(1))
            {
                items.AddRange(cycle.GetRange(next, start - next).Select(static m => new Item(m, null)));
                var block = Block(inner, cycle[start], cycle[end], avoid, Direction.Right);

                if (block is null)
                {
                    unit = null;
                    break;
                }

                items.Add(new Item(null, block));
                next = end + 1;
            }

            if (unit is not null)
            {
                items.AddRange(cycle.GetRange(next, unitStart - next).Select(static m => new Item(m, null)));
            }
        }
        else
        {
            unitStart = unitEnd = ConsumerOf(cycle, 1);
            unit = unitStart < 0 ? null : Single(cycle[unitStart]);

            if (unit is not null)
            {
                items.AddRange(cycle.GetRange(1, unitStart - 1).Select(static m => new Item(m, null)));
            }
        }

        if (unit is null)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            return Decline("no consumer unit was found on the cycle");
        }

        // Every unit stands at a provisional place until it is slid in; none is an obstacle before that.
        foreach (var u in items.Where(static i => i.Unit is not null).Select(static i => i.Unit!).Append(unit))
        {
            foreach (var i in u.Members)
            {
                _placed[i] = false;
            }
        }

        Place(s.Component, ts[0], new Point(0, 0), "C2", "the loop's source at the origin, outlet up and inlet down");
        var sOut = AnchorOf(s.Component, s.OutPort);
        var sIn = AnchorOf(s.Component, s.InPort);
        var yTop = sOut.Along(_margin).Y;
        var bottomMembers = cycle.GetRange(unitEnd + 1, cycle.Count - unitEnd - 1);
        var runs = new List<(Member From, List<Point> Points)>();
        var hangers = new List<Hanger>();
        List<Point> topStart = [sOut.At, new Point(sOut.At.X, yTop)];

        if (Top(Anchor(topStart[^1], Direction.Right, Direction.Right), topStart, s, items, ring, bottomMembers, avoid, runs, hangers) is not { } top)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            return Decline("the top rail could not be laid");
        }

        var (_, rightJunction) = Corners(bottomMembers, unit, leftFirst: false);
        var (drop, yBottom) = Bottom(unit, top.End.At.Y, sIn.Along(_margin).Y, rightJunction?.Component ?? -1);
        yBottom = Math.Min(yBottom, Under(hangers));

        // C12: a member on a side with slack sits at the side's middle. The source moves down by half the excess of the rails' span over its own.
        var slack = sIn.Along(_margin).Y - yBottom;
        Place(s.Component, ts[0], new Point(0, -slack / 2), "C12", $"the source at the middle of its side, half the rails' slack ({slack:0.##}) down");
        sOut = AnchorOf(s.Component, s.OutPort);
        sIn = AnchorOf(s.Component, s.InPort);
        topStart[0] = sOut.At;

        if (!Close(top, unit, drop, yBottom, bottomMembers, [sIn.At, sIn.Along(_margin), new Point(sIn.At.X, yBottom)], null, rightJunction, hangers, runs))
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            return Decline("the ring could not be closed along the bottom rail");
        }

        // The ring is a group (A8), listed before the blocks it holds.
        _groups.Insert(mark, ("loop", cycle.Select(static m => m.Component).ToList(), true));

        foreach (var (from, points) in runs)
        {
            Assign(Walk(from.Component, from.OutPort), Normalise(points));
        }

        return true;
    }

    /// <summary>The member that takes a ring's right side: the standing consumer of the largest duty from <paramref name="from"/> on, else the first member the flow leaves the ring by.</summary>
    private int ConsumerOf(List<Member> cycle, int from)
    {
        var consumerAt = -1;

        for (var k = from; k < cycle.Count; k++)
        {
            if (Duty(cycle[k].Component) is { } duty && (consumerAt < 0 || duty < Duty(cycle[consumerAt].Component)))
            {
                consumerAt = k;
            }
        }

        for (var k = from; k < cycle.Count && consumerAt < 0; k++)
        {
            // No standing consumer: the member the loop's flow leaves by -- a valve or a junction with an off-loop outlet -- takes the right side.
            if (LeavesLoop(cycle[k]))
            {
                consumerAt = k;
            }
        }

        return consumerAt;
    }

    /// <summary>A consumer's duty, negative (W): its stated power, or a nominal amount for an exchanger whose power is sized but which is written as a load or states an inlet above its outlet; <see langword="null"/> for anything else.</summary>
    private double? Duty(int component) => _graph.Components[component] switch
    {
        HeatExchanger { Power: < 0 } h => h.Power,
        HeatExchanger { Power: 0 } when Written(component) is "load" or "radiator" or "cooler" or "chiller" => -double.Epsilon,
        HeatExchanger { Power: 0 } h when h.StatedParameters.TryGetValue("in", out var inlet) && h.StatedParameters.TryGetValue("out", out var outlet) && inlet.SiValue > outlet.SiValue => -double.Epsilon,
        _ => null,
    };

    /// <summary>Whether an exchanger is a heat source: its stated power positive, or its power sized but written as a heater or boiler, or stating an outlet above its inlet.</summary>
    private bool IsSource(int component) => _graph.Components[component] switch
    {
        HeatExchanger { Power: > 0 } => true,
        HeatExchanger { Power: 0 } when Written(component) is "heater" or "boiler" => true,
        HeatExchanger { Power: 0 } h when h.StatedParameters.TryGetValue("in", out var inlet) && h.StatedParameters.TryGetValue("out", out var outlet) && outlet.SiValue > inlet.SiValue => true,
        _ => false,
    };

    /// <summary>The kind the script wrote for a component (<c>load</c>, <c>heater</c>, …), lower-cased; <see langword="null"/> for one the language inferred.</summary>
    private string? Written(int component)
    {
        var name = _graph.Components[component].Name;

        foreach (var c in _model.Components)
        {
            if (string.Equals(c.Name, name, StringComparison.Ordinal))
            {
                return c.WrittenKind.ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// C18: a closed loop with no heat source is still a ring. Its consumer of the largest duty takes the right
    /// side; the top-left corner is the first boxed node after it in flow order -- in from below, out to the
    /// right -- or, where the loop's nodes are points on the line, a bare bend before the first member the flow
    /// reaches past such a point; the members between consumer and corner lie on the bottom rail, the rest on
    /// the top, and the left side is the bare vertical up into the corner. A loop none of whose members has a
    /// known duty -- an exchanger neither stated nor solved, which a script under editing is most of the time --
    /// is still a ring (<c>C-100</c>): its first exchanger takes the consumer's seat, so the loop reads as a
    /// loop before it solves.
    /// </summary>
    private bool Closed(List<int> fragment)
    {
        var consumer = fragment.Where(i => Duty(i) is not null).OrderBy(i => Duty(i)).ThenBy(static i => i).FirstOrDefault(-1);

        if (consumer < 0)
        {
            consumer = fragment.Where(i => _graph.Components[i] is HeatExchanger).Order().FirstOrDefault(-1);
        }

        if (consumer < 0)
        {
            return Decline("no exchanger to take the consumer's seat");
        }

        if (Cycle(consumer) is not { } cycle)
        {
            return Decline("no cycle of boxed members returns to the consumer");
        }

        var cornerAt = -1;
        var split = -1;

        for (var k = 1; k < cycle.Count && cornerAt < 0; k++)
        {
            if (Wildcard(cycle[k].Component))
            {
                cornerAt = k;
            }
            else if (split < 0 && PassesNode(cycle[k - 1]))
            {
                split = k;
            }
        }

        if (cornerAt < 0 && split < 0)
        {
            split = 1;
        }

        var corner = cornerAt >= 0 ? cycle[cornerAt] : (Member?)null;
        var wasInline = corner is { } c0 && _inline[c0.Component];
        var mark = _groups.Count;
        var ring = cycle.Select(static m => m.Component).ToHashSet();
        // The unit search must not find the ring itself: it avoids the corner, or with a bare bend the first member on the top rail.
        HashSet<int> avoid = corner is { } c1 ? [c1.Component] : [cycle[split].Component];

        bool Fail(string reason)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);

            if (corner is { } c2)
            {
                _inline[c2.Component] = wasInline;
            }

            return Decline(reason);
        }

        if (corner is { } c3)
        {
            _inline[c3.Component] = false;
        }

        var (_, innerEnd, unit) = UnitOf(cycle, 0, avoid, Direction.Left);
        var end = cornerAt >= 0 ? cornerAt : split;

        if (unit is null || innerEnd >= end)
        {
            return Fail(unit is null ? "no unit was found past the consumer" : "the unit reaches the corner and leaves nothing for the rails");
        }

        foreach (var i in unit.Members)
        {
            _placed[i] = false;
        }

        foreach (var member in cycle)
        {
            _loop[member.Component] = true;
        }

        var bottomMembers = cycle.GetRange(innerEnd + 1, end - innerEnd - 1);

        // C9, mirrored to the ring's bottom-left corner (C-105): the last bottom member that can turn the flow
        // from leftward to upward takes the corner, and the left side rises out of it instead of a bare bend
        // beside it -- the case C18 had recorded as not built. A junction there is C10's (Corners), not this.
        (Member Member, Transform Transform)? leftTurner = null;

        if (corner is not null && bottomMembers.Count > 0 && !Wildcard(bottomMembers[^1].Component) && !_inline[bottomMembers[^1].Component])
        {
            var last = bottomMembers[^1];
            var turning = Admitted(last.Component)
                .Where(t => Outward(last.Component, last.InPort, t) == Direction.Right && Outward(last.Component, last.OutPort, t) == Direction.Up)
                .ToList();

            if (turning.Count > 0)
            {
                leftTurner = (last, turning[0]);
                bottomMembers = bottomMembers.GetRange(0, bottomMembers.Count - 1);
            }
        }

        var items = cycle.GetRange(end + (cornerAt >= 0 ? 1 : 0), cycle.Count - end - (cornerAt >= 0 ? 1 : 0)).Select(static m => new Item(m, null)).ToList();
        PlacedAnchor cOut;
        PlacedAnchor cIn;
        Member previous;

        if (corner is { } c4)
        {
            Place(c4.Component, Transform.Identity, new Point(0, 0), "C18", "the unsourced ring's corner node at the origin");
            _side[(c4.Component, c4.InPort)] = Direction.Down;
            _side[(c4.Component, c4.OutPort)] = Direction.Right;
            cOut = AnchorOf(c4.Component, c4.OutPort);
            cIn = AnchorOf(c4.Component, c4.InPort);
            previous = c4;
        }
        else
        {
            // A bare bend at the origin: the run that turns it belongs to the last bottom member (or the consumer), and its two halves are joined below.
            cOut = Anchor(new Point(0, 0), Direction.Right, Direction.Right);
            cIn = Anchor(new Point(0, 0), Direction.Down, Direction.Up);
            previous = cycle[end - 1];
        }

        var (_, right) = Corners(bottomMembers, unit, leftFirst: false);
        var runs = new List<(Member From, List<Point> Points)>();
        var hangers = new List<Hanger>();

        if (Top(cOut, [cOut.At], previous, items, ring, bottomMembers, avoid, runs, hangers) is not { } top)
        {
            return Fail("the top rail could not be laid");
        }

        var (drop, yBottom) = Bottom(unit, top.End.At.Y, cIn.Along(_margin).Y, right?.Component ?? -1);
        yBottom = Math.Min(yBottom, Under(hangers));

        if (leftTurner is { } lt)
        {
            // The rail is where the turner's inlet lies; its outlet's tip must sit a margin under the corner's inlet.
            var dIn = AnchorOffset(lt.Member.Component, lt.Member.InPort, lt.Transform)!.Value.Offset;
            var dOut = AnchorOffset(lt.Member.Component, lt.Member.OutPort, lt.Transform)!.Value.Offset;
            yBottom = Math.Min(yBottom, cIn.Along(_margin).Y - (dOut.Y - dIn.Y));
        }

        if (!Close(top, unit, drop, yBottom, bottomMembers, [cIn.At, cIn.Along(_margin), new Point(cIn.At.X, yBottom)], null, right, hangers, runs, leftTurner))
        {
            return Fail("the ring could not be closed along the bottom rail");
        }

        // C8 reads a junction's free side against the loop's centre. This ring is built from its corner at the
        // origin, so the centre had stayed there and the corner's own free port -- at zero distance from it --
        // fell to the default order and went up; an open end there stands off the ring's side, level (C-105).
        _loopCentre = new Point(cycle.Average(m => _centre[m.Component].X), cycle.Average(m => _centre[m.Component].Y));

        if (corner is null)
        {
            // The bare corner's two halves are one walk: the bottom rail's last run ends at the origin, the top rail's first starts there.
            var halves = runs.Where(r => r.From.Equals(previous)).ToList();
            var into = halves.FirstOrDefault(r => r.Points[^1].ManhattanTo(cOut.At) < Eps);
            var outOf = halves.FirstOrDefault(r => r.Points[0].ManhattanTo(cOut.At) < Eps);

            if (into.Points is not null && outOf.Points is not null && !ReferenceEquals(into.Points, outOf.Points))
            {
                runs.Remove(into);
                runs.Remove(outOf);
                runs.Add((previous, [.. into.Points, .. outOf.Points.Skip(1)]));
            }
        }

        _groups.Insert(mark, ("loop", cycle.Select(static m => m.Component).ToList(), true));

        foreach (var (from, points) in runs)
        {
            Assign(Walk(from.Component, from.OutPort), Normalise(points));
        }

        return true;
    }

    /// <summary>
    /// C20 (<c>C-101</c>): a component connected to itself is a ring of one. Neither C2 nor C18 can seat it -- a pump is
    /// no source and no consumer -- and the chain rule would put its inferred node one row under it and route the
    /// outlet back through the member's own box. The member stands at the origin in its drawn default; the pipe
    /// leaves the outlet by its outward margin, which puts it on the outer box's edge, walks that edge clockwise (H9)
    /// round to the inlet's stub and enters by its margin, so a pump's return runs under it and an exchanger's, out at
    /// the bottom and in at the top, runs up its left side; the inline elements between -- the inferred node the
    /// syntax gives <c>PU1 - PU1</c> -- are cut into the return (A5). Only a fragment whose one boxed member is the
    /// head is a ring of one; anything larger is another rule's, and a member whose stubs do not end on its outer
    /// box is left to them too.
    /// </summary>
    private bool Ring(int head, List<int> fragment)
    {
        if (fragment.Any(i => i != head && !Inline(i)))
        {
            return Decline("the fragment has a boxed member besides the head");
        }

        var ports = _graph.Components[head].Ports;

        for (var p = 0; p < ports.Length; p++)
        {
            if (Enters(head, p, ports[p].Role))
            {
                continue;
            }

            var walk = Walk(head, p);

            if (walk.Far != head || walk.FarPort == p)
            {
                continue;
            }

            Place(head, Transform.Identity, new Point(0, 0), "C20", "a ring of one: the head at the origin, the walk returning to it");
            var outlet = AnchorOf(head, p);
            var inlet = AnchorOf(head, walk.FarPort);
            var (width, height) = SizeOf(head);

            if (Clockwise(outlet.Along(_margin), inlet.Along(_margin), (width / 2) + _margin, (height / 2) + _margin) is not { } around)
            {
                _placed[head] = false;
                continue;
            }

            List<Point> points = [outlet.At, .. around, inlet.At];
            var members = new List<int> { head };
            members.AddRange(walk.Inline.Select(static e => e.Element));

            foreach (var i in members)
            {
                _loop[i] = true;
            }

            _groups.Add(("loop", members, true));
            Assign(walk, Normalise(points));
            return true;
        }

        return Decline("no walk from the head's outlet returns to another of its ports");
    }

    /// <summary>The clockwise walk along the edge of the box <c>[-x, x] × [-y, y]</c> about the origin from <paramref name="from"/> to <paramref name="to"/>, both on that edge, through the corners between.</summary>
    /// <param name="from">Where the walk starts, on the edge.</param>
    /// <param name="to">Where it ends, on the edge.</param>
    /// <param name="x">The box's half width.</param>
    /// <param name="y">The box's half height.</param>
    /// <returns>The points from <paramref name="from"/> to <paramref name="to"/> inclusive, or null where either is off the edge or they coincide.</returns>
    private static List<Point>? Clockwise(Point from, Point to, double x, double y)
    {
        // Edges clockwise with y up: top (rightwards), right (downwards), bottom (leftwards), left (upwards); the corner each edge ends at.
        Point[] corners = [new(x, y), new(x, -y), new(-x, -y), new(-x, y)];

        static int EdgeOf(Point p, double x, double y) =>
            Math.Abs(p.Y - y) < Eps ? 0 : Math.Abs(p.X - x) < Eps ? 1 : Math.Abs(p.Y + y) < Eps ? 2 : Math.Abs(p.X + x) < Eps ? 3 : -1;

        static bool Ahead(int edge, Point at, Point p) => edge switch
        {
            0 => p.X >= at.X - Eps,
            1 => p.Y <= at.Y + Eps,
            2 => p.X <= at.X + Eps,
            _ => p.Y >= at.Y - Eps,
        };

        var edge = EdgeOf(from, x, y);

        if (edge < 0 || EdgeOf(to, x, y) < 0 || from.ManhattanTo(to) < Eps)
        {
            return null;
        }

        List<Point> points = [from];
        var at = from;

        for (var step = 0; step <= 4; step++)
        {
            if (EdgeOf(to, x, y) == edge && Ahead(edge, at, to))
            {
                points.Add(to);
                return points;
            }

            at = corners[edge];
            points.Add(at);
            edge = (edge + 1) % 4;
        }

        return null;
    }

    /// <summary>C19: a supply boundary feeding two paths to one return boundary is the open supply-to-return form. The supply is the left end of the top rail and the return, directly under it, the left end of the bottom rail: the first path with no inner loop hangs straight down between them, and the other path is the ring's right side, fed level from the supply's right and returning along the bottom into the return's right.</summary>
    /// <param name="supply">The fragment's head, which must be a supply boundary.</param>
    /// <param name="fragment">The fragment's members.</param>
    /// <returns>Whether the form was laid out.</returns>
    /// <remarks>Not built: more than two paths, a chain path that turns level, a path to a second return (that one is left to the chain rule off the supply's remaining sides, up then left).</remarks>
    private bool Open(int supply, List<int> fragment)
    {
        if (_graph.Components[supply] is not CircuitNode { Boundary: BoundaryRole.Inlet } || !Wildcard(supply))
        {
            return Decline("the head is not a supply boundary");
        }

        // D-115: a boundary has one connection, so the rail's left end is the junction after the inlet and the inlet hangs off that junction's left side. An inlet wired to several paths (FS2205) is still drawn, as the junction itself.
        var inlet = -1;
        var inletPort = -1;
        var ports = _graph.Components[supply].Ports;
        var linked = Enumerable.Range(0, ports.Length).Where(p => _links.Any(l => (l.From == supply && l.FromPort == p) || (l.To == supply && l.ToPort == p))).ToList();

        if (linked.Count == 1)
        {
            var (junction, back) = Follow(supply, linked[0]);

            if (junction < 0 || !Wildcard(junction) || _inline[junction] || _graph.Components[junction] is not CircuitNode { Boundary: BoundaryRole.Interior })
            {
                return Decline("the supply does not feed an interior junction node");
            }

            inlet = supply;
            inletPort = back;
            supply = junction;
            ports = _graph.Components[supply].Ports;
            linked = Enumerable.Range(0, ports.Length).Where(p => p != back && _links.Any(l => (l.From == supply && l.FromPort == p) || (l.To == supply && l.ToPort == p))).ToList();
        }

        List<List<Member>>? paths = null;
        var ret = -1;
        var outletPort = -1;

        foreach (var candidate in Ordered(fragment).Where(i => i != supply && i != inlet && _graph.Components[i] is CircuitNode { Boundary: BoundaryRole.Outlet }))
        {
            var found = new List<List<Member>>();

            foreach (var p in linked)
            {
                var path = new List<Member>();
                HashSet<int> visited = inlet < 0 ? [supply] : [supply, inlet];

                if (Extend(supply, -1, p, candidate, path, visited))
                {
                    found.Add(path);
                }
            }

            if (found.Count is not (1 or 2))
            {
                continue;
            }

            paths = found;
            ret = candidate;

            // Both paths ending on one junction: it is the bottom rail's left end, and the outlet hangs off its left side.
            if (found.Count == 2 && found[0].Count > 1 && found[1].Count > 1 && found[0][^1].Component == found[1][^1].Component && Wildcard(found[0][^1].Component) && !_inline[found[0][^1].Component])
            {
                ret = found[0][^1].Component;
                outletPort = found[0][^1].OutPort;

                foreach (var path in found)
                {
                    path[0] = path[0] with { InPort = path[^1].InPort };
                    path.RemoveAt(path.Count - 1);
                }
            }

            break;
        }

        if (paths is null)
        {
            return Decline("the junction's paths do not both reach one return boundary");
        }

        HashSet<int> avoid = [supply];
        List<Member> CycleOf(List<Member> path) => [new Member(supply, -1, path[0].OutPort), .. path.Skip(1)];
        var chainAt = paths.FindIndex(path => Ranges(CycleOf(path), 1, avoid).Count == 0);
        var ringAt = paths.FindIndex(path => Ranges(CycleOf(path), 1, avoid).Count > 0);

        // One path with no inner loop is the chain alone: the outlet stands at its foot and there are no rails (C19, step 11b).
        var chainOnly = ringAt < 0 && paths.Count == 1;

        if (ringAt < 0 && !chainOnly)
        {
            ringAt = chainAt == 0 ? 1 : 0;
        }

        var chain = chainAt >= 0 && chainAt != ringAt ? paths[chainAt] : null;
        var cycle = chainOnly ? [new Member(supply, -1, chain![0].OutPort)] : CycleOf(paths[ringAt]);
        var ring = cycle.Select(static m => m.Component).ToHashSet();
        var placed = (bool[])_placed.Clone();
        var sides = new Dictionary<(int Component, int Port), Direction>(_side);
        var mark = _groups.Count;

        bool Fail(string reason)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            placed.CopyTo(_placed, 0);
            _side.Clear();

            foreach (var (key, value) in sides)
            {
                _side[key] = value;
            }

            return Decline(reason);
        }

        // The ring path's unit and top-rail items, as a ring's (C11).
        var ranges = Ranges(cycle, 1, avoid);
        Unit? unit;
        int unitEnd;
        var items = new List<Item>();
        (int Start, int End, List<Member> Inner)? unitRange = null;
        var unitGroups = 0;

        if (ranges.Count > 0)
        {
            var last = ranges[^1];
            unitEnd = last.End;
            unit = Block(last.Inner, cycle[last.Start], cycle[last.End], avoid, Direction.Left);
            unitRange = last;
            unitGroups = _groups.Count - mark;
            var next = 1;

            foreach (var (start, end, inner) in ranges.SkipLast(1))
            {
                items.AddRange(cycle.GetRange(next, start - next).Select(static m => new Item(m, null)));
                var block = Block(inner, cycle[start], cycle[end], avoid, Direction.Right);

                if (block is null)
                {
                    unit = null;
                    break;
                }

                items.Add(new Item(null, block));
                next = end + 1;
            }

            if (unit is not null)
            {
                items.AddRange(cycle.GetRange(next, last.Start - next).Select(static m => new Item(m, null)));
            }
        }
        else
        {
            var consumerAt = ConsumerOf(cycle, 1);
            unitEnd = consumerAt;
            unit = consumerAt < 0 ? null : Single(cycle[consumerAt]);

            if (unit is not null)
            {
                items.AddRange(cycle.GetRange(1, consumerAt - 1).Select(static m => new Item(m, null)));
            }
        }

        if (unit is null && !chainOnly)
        {
            return Fail("no unit was found on the ring path");
        }

        foreach (var u in items.Where(static i => i.Unit is not null).Select(static i => i.Unit!).Concat(unit is null ? [] : [unit]))
        {
            foreach (var i in u.Members)
            {
                _placed[i] = false;
            }
        }

        _loopCentre = new Point(0, 0);
        Place(supply, Transform.Identity, new Point(0, 0), "C19", "the supply boundary at the left end of the top rail");
        _side[(supply, cycle[0].OutPort)] = Direction.Right;
        var half = Transform.Identity.Size(_symbol[ret]).Height / 2;
        var natural = InnerOf(supply).Y - (2 * _margin) - half;
        var runs = new List<(Member From, List<Point> Points)>();
        Member chainEnd = default;
        PlacedAnchor chainCursor = default;

        if (chain is not null)
        {
            // The chain hangs straight down under the supply, each member placed from the one before.
            _side[(supply, chain[0].OutPort)] = Direction.Down;
            var cursor = AnchorOf(supply, chain[0].OutPort);
            var previous = new Member(supply, -1, chain[0].OutPort);
            var pending = new List<Point> { cursor.At };

            foreach (var m in chain.Skip(1))
            {
                if (OnRail(cursor, m, m.InPort, m.OutPort) is not { } points)
                {
                    return Fail("a chain member could not be laid on the rail");
                }

                pending.AddRange(points.Skip(1));
                runs.Add((previous, pending));
                cursor = AnchorOf(m.Component, m.OutPort);
                previous = m;
                pending = [cursor.At];
            }

            if (cursor.Outward != Direction.Down || Math.Abs(cursor.At.X) > Eps)
            {
                return Fail("the chain does not end pointing down on the supply's axis");
            }

            chainEnd = previous;
            chainCursor = cursor;
            natural = Math.Min(natural, cursor.At.Y - _margin - half);
        }

        if (chainOnly)
        {
            // No ring path: the outlet stands at the chain's foot, directly under the junction.
            Place(ret, Transform.Identity, new Point(0, natural), "C19", "no ring path: the return at the chain's foot, directly under the junction");
            _side[(ret, chain![0].InPort)] = Direction.Up;
            runs.Add((chainEnd, [chainCursor.At, AnchorOf(ret, chain[0].InPort).At]));
            Finish();
            return true;
        }

        if (unit is null)
        {
            return Fail("no unit was found on the ring path");
        }

        // C12 for the open form: a block standing on both rails whose outlet sits above the chain's level would put a step in the return, so the block is rebuilt deeper and its outlet meets the rail.
        if (unitRange is { } range && unit.In.Outward == Direction.Left && unit.Out.Outward == Direction.Left)
        {
            var outletAt = AnchorOf(supply, cycle[0].OutPort).At.Y + unit.Out.At.Y - unit.In.At.Y;

            if (outletAt > natural + Eps)
            {
                _groups.RemoveRange(mark, unitGroups);
                var at = _groups.Count;
                unit = Block(range.Inner, cycle[range.Start], cycle[range.End], avoid, Direction.Left, outletAt - natural);

                if (unit is null)
                {
                    return Fail("the ring's block could not be laid as a unit");
                }

                var created = _groups.GetRange(at, _groups.Count - at);
                _groups.RemoveRange(at, created.Count);
                _groups.InsertRange(mark, created);

                foreach (var i in unit.Members)
                {
                    _placed[i] = false;
                }
            }
        }

        var sOut = AnchorOf(supply, cycle[0].OutPort);
        var bottomMembers = cycle.GetRange(unitEnd + 1, cycle.Count - unitEnd - 1);
        var hangers = new List<Hanger>();

        if (Top(sOut, [sOut.At], cycle[0], items, ring, bottomMembers, avoid, runs, hangers) is not { } top)
        {
            return Fail("the top rail could not be laid");
        }

        var (_, rightJunction) = Corners(bottomMembers, unit, leftFirst: false);
        var (drop, yBottom) = Bottom(unit, top.End.At.Y, natural, rightJunction?.Component ?? -1);
        yBottom = Math.Min(yBottom, Under(hangers));

        // The return stands directly under the supply at the bottom rail's level, fed by the chain from above and by the rail from the right.
        Place(ret, Transform.Identity, new Point(0, yBottom), "C19", "the return directly under the supply at the bottom rail's level");
        _side[(ret, paths[ringAt][0].InPort)] = Direction.Right;

        if (chain is not null)
        {
            _side[(ret, chain[0].InPort)] = Direction.Up;
            runs.Add((chainEnd, [chainCursor.At, AnchorOf(ret, chain[0].InPort).At]));
        }

        var rIn = AnchorOf(ret, paths[ringAt][0].InPort);

        if (!Close(top, unit, drop, yBottom, bottomMembers, [rIn.At, rIn.Along(_margin)], null, rightJunction, hangers, runs))
        {
            return Fail("the ring could not be closed along the bottom rail");
        }

        foreach (var m in cycle)
        {
            _loop[m.Component] = true;
        }

        Finish();
        return true;

        void Finish()
        {
            // The inlet and the outlet hang off their junctions' left sides (D-115); the supply's other connections leave by the sides the form leaves free, up first.
            if (inlet >= 0)
            {
                _side[(supply, inletPort)] = Direction.Left;
            }

            if (outletPort >= 0)
            {
                _side[(ret, outletPort)] = Direction.Left;
            }

            var free = new Queue<Direction>([Direction.Up, Direction.Left]);

            foreach (var p in linked.Where(p => !_side.ContainsKey((supply, p))))
            {
                while (free.Count > 0)
                {
                    var side = free.Dequeue();

                    if (!_side.Any(kv => kv.Key.Component == supply && kv.Value == side))
                    {
                        _side[(supply, p)] = side;
                        break;
                    }
                }
            }

            foreach (var (from, points) in runs)
            {
                Assign(Walk(from.Component, from.OutPort), Normalise(points));
            }
        }
    }

    /// <summary>Whether the walk leaving a member passes a node that is a point on the line -- an inline node the cycle search steps over (C18).</summary>
    private bool PassesNode(Member from)
    {
        foreach (var (k, _) in Walk(from.Component, from.OutPort).Links)
        {
            var link = _links[k];

            if ((Wildcard(link.From) && _inline[link.From]) || (Wildcard(link.To) && _inline[link.To]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The unit around a ring's consumer (C11): the consumer alone, or the block of the inner loop through it that avoids <paramref name="avoid"/>; with the range of ring members the unit replaces.</summary>
    private (int Start, int End, Unit? Unit) UnitOf(List<Member> cycle, int consumerAt, HashSet<int> avoid, Direction outlet)
    {
        var (start, end, inner) = Column(cycle, consumerAt, avoid);
        return (start, end, inner is null ? Single(cycle[consumerAt]) : Block(inner, cycle[start], cycle[end], avoid, outlet));
    }

    /// <summary>
    /// The junctions that take a ring's bottom corners (C10): on the right, the one next to the unit under an outlet
    /// that faces down or right; on the left, when the ring is a block whose outlet faces left, the one nearest the
    /// left side -- so the block's outlet faces its parent beside its inlet. One junction takes one corner.
    /// </summary>
    private (Member? Left, Member? Right) Corners(List<Member> bottomMembers, Unit unit, bool leftFirst)
    {
        var left = leftFirst && bottomMembers.Count > 0 && Wildcard(bottomMembers[^1].Component) ? bottomMembers[^1] : (Member?)null;
        var under = unit.Out.Outward == Direction.Down || unit.Out.Outward == Direction.Right;
        var right = under && bottomMembers.Count > (left is null ? 0 : 1) && Wildcard(bottomMembers[0].Component) ? bottomMembers[0] : (Member?)null;
        return (left, right);
    }

    /// <summary>One member as a ring's right side, placed with its inlet at the local origin: at the corner if it can turn the flow from leftward to downward (C9), else taking the flow from above.</summary>
    private Unit? Single(Member m)
    {
        List<Transform> candidates;

        if (Wildcard(m.Component))
        {
            candidates = [Transform.Identity];
        }
        else
        {
            bool Faces(Transform t, Direction inward) => Outward(m.Component, m.InPort, t) == inward && Outward(m.Component, m.OutPort, t) == Direction.Down;

            // Any arrangement the symbol offers may turn the corner, the default first (A9, D-112).
            var turning = Admitted(m.Component).Where(t => Faces(t, Direction.Left)).ToList();
            candidates = turning.Count > 0 ? turning : Admitted(m.Component).Where(t => Faces(t, Direction.Up)).ToList();
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var (_, h) = candidates[0].Size(_symbol[m.Component]);
        var inOffset = Wildcard(m.Component) ? new Point(0, h / 2) : AnchorOffset(m.Component, m.InPort, candidates[0])!.Value.Offset;
        Place(m.Component, candidates[0], new Point(-inOffset.X, -inOffset.Y), "C11", "a single member as the unit, its inlet at the origin, turned to face the top rail (A9)");

        if (Wildcard(m.Component))
        {
            _side[(m.Component, m.InPort)] = Direction.Up;
            _side[(m.Component, m.OutPort)] = Direction.Down;
        }

        return new Unit([m.Component], AnchorOf(m.Component, m.InPort), AnchorOf(m.Component, m.OutPort), m, InnerOf(m.Component).Y, []);
    }

    /// <summary>
    /// An inner loop laid out as its own clockwise ring at the local origin (C11): its consumer's unit on the right,
    /// the first member after it that turns the flow from upward to rightward at the top-left corner with the block's
    /// inlet facing out to the left, the members between them on the bottom rail and the rest on the top. The
    /// block's outlet -- its junction's free port -- faces <paramref name="outlet"/>: left, beside the inlet, when the
    /// parent's return goes back that way; right when the parent continues on to the next member.
    /// </summary>
    /// <param name="path">The inner loop in flow order from its consumer.</param>
    /// <param name="entry">The member and port the outer loop enters the block by.</param>
    /// <param name="exit">The member and port the outer loop leaves the block by.</param>
    /// <param name="avoid">The enclosing rings' sources and corner members, which a nested unit's search must not pass.</param>
    /// <param name="outlet">Which way the block's outlet faces.</param>
    /// <param name="deeper">How much lower than its own need the block's bottom rail lies, so that its outlet meets a taller neighbour's rail level (C12); zero for its own need.</param>
    /// <returns>The block as a unit, or <see langword="null"/> when no member can take the corner or the entry and exit do not face the ring.</returns>
    private Unit? Block(List<Member> path, Member entry, Member exit, HashSet<int> avoid, Direction outlet, double deeper = 0)
    {
        var cornerAt = -1;
        var tc = Transform.Identity;

        for (var k = 1; k < path.Count && cornerAt < 0; k++)
        {
            var m = path[k];

            if (Wildcard(m.Component))
            {
                continue;
            }

            var external = m.Component == entry.Component ? entry.InPort : -1;
            var turning = Admitted(m.Component).Where(t => Outward(m.Component, m.InPort, t) == Direction.Down && Outward(m.Component, m.OutPort, t) == Direction.Right && (external < 0 || Outward(m.Component, external, t) == Direction.Left)).ToList();

            if (turning.Count > 0)
            {
                cornerAt = k;
                tc = turning[0];
            }
        }

        if (cornerAt < 0)
        {
            return null;
        }

        var corner = path[cornerAt];
        var mark = _groups.Count;

        // The block is laid out on its own (C11): nothing placed so far is an obstacle to it, and it comes back unplaced, to be slid in by its parent.
        var placed = (bool[])_placed.Clone();
        void Restore() => placed.CopyTo(_placed, 0);
        Array.Clear(_placed);
        var (_, innerEnd, unit) = UnitOf(path, 0, [.. avoid, corner.Component], Direction.Left);

        if (unit is null || innerEnd >= cornerAt)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            Restore();
            return null;
        }

        foreach (var i in unit.Members)
        {
            _placed[i] = false;
        }

        var bottomMembers = path.GetRange(innerEnd + 1, cornerAt - innerEnd - 1);
        var items = path.GetRange(cornerAt + 1, path.Count - cornerAt - 1).Select(static m => new Item(m, null)).ToList();
        Place(corner.Component, tc, new Point(0, 0), "C9", "the corner member at the origin, in the arrangement that turns the pipe");
        var cOut = AnchorOf(corner.Component, corner.OutPort);
        var cIn = AnchorOf(corner.Component, corner.InPort);
        var (left, right) = Corners(bottomMembers, unit, leftFirst: outlet == Direction.Left);
        var runs = new List<(Member From, List<Point> Points)>();
        var hangers = new List<Hanger>();

        if (Top(cOut, [cOut.At], corner, items, path.Select(static m => m.Component).ToHashSet(), null, avoid, runs, hangers) is not { } top)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            Restore();
            return null;
        }

        var natural = cIn.Along(_margin).Y - (left is { } l ? Transform.Identity.Size(_symbol[l.Component]).Height / 2 : 0);
        var (drop, yBottom) = Bottom(unit, top.End.At.Y, natural, right?.Component ?? -1);

        if (deeper > Eps)
        {
            // C12: the bottom rail goes down by the caller's need, whatever set it; a unit hung from above keeps its outlet mid-side.
            yBottom -= deeper;
            drop += unit.In.Outward == Direction.Up ? deeper / 2 : 0;
        }

        if (!Close(top, unit, drop, yBottom, bottomMembers, [cIn.At, cIn.Along(_margin), new Point(cIn.At.X, yBottom)], left, right, hangers, runs))
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            Restore();
            return null;
        }

        if (Wildcard(exit.Component))
        {
            // The outlet leaves by a side the junction's other ports leave free, the parent's way first, then down.
            var used = new HashSet<Direction>();

            for (var p = 0; p < _graph.Components[exit.Component].Ports.Length; p++)
            {
                if (p != exit.OutPort && _side.TryGetValue((exit.Component, p), out var side))
                {
                    used.Add(side);
                }
            }

            Direction[] order = outlet == Direction.Left ? [Direction.Left, Direction.Down, Direction.Right] : [Direction.Right, Direction.Down, Direction.Left];
            _side[(exit.Component, exit.OutPort)] = order.First(d => !used.Contains(d));
        }

        var members = path.Select(static m => m.Component).ToList();
        var entering = AnchorOf(entry.Component, entry.InPort);
        var leaving = AnchorOf(exit.Component, exit.OutPort);

        if (entering.Outward != Direction.Left || leaving.Outward == Direction.Up)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            Restore();
            return null;
        }

        // The block is a group (A8), listed before the blocks it holds.
        _groups.Insert(mark, ("loop", members, false));
        Restore();

        return new Unit(members, entering, leaving, new Member(exit.Component, -1, exit.OutPort), members.Min(i => InnerOf(i).Y), runs);
    }

    /// <summary>
    /// How far the unit's inlet drops below the rail it is fed from and where the bottom rail lies: level with an
    /// outlet that faces left; else low enough for the unit, for the stub out of it, and for a corner junction under
    /// its outlet (C10). A unit entered from above with the other side the taller is centred on its side (C12).
    /// </summary>
    private (double Drop, double YBottom) Bottom(Unit unit, double yTop, double natural, int junction)
    {
        var drop = unit.In.Outward == Direction.Up ? _margin : 0;
        var half = junction < 0 ? 0 : Transform.Identity.Size(_symbol[junction]).Height / 2;
        var stub = junction >= 0 && unit.Out.Outward == Direction.Right ? _margin : 0;
        double Low(double dy) => unit.Out.Outward == Direction.Left
            ? unit.Out.At.Y + dy
            : Math.Min(unit.Bottom + dy - _margin, unit.Out.Along(_margin).Y + dy - half - stub);
        var low = Low(yTop - drop - unit.In.At.Y);

        if (unit.In.Outward == Direction.Up && low > natural)
        {
            drop += (low - natural) / 2;
            low = Low(yTop - drop - unit.In.At.Y);
        }

        return (drop, Math.Min(natural, low));
    }

    /// <summary>The bottom rail's highest admissible height under the hanging branches: a margin under the lowest block, and room for the junction each returns to.</summary>
    private double Under(List<Hanger> hangers) =>
        hangers.Count == 0 ? double.MaxValue : hangers.Min(h => h.Out.At.Y - _margin - Transform.Identity.Size(_symbol[h.Bottom]).Height / 2 - _margin / 5);

    /// <summary>
    /// Slides a unit from its provisional place into the layout: its inlet level with <paramref name="yIn"/>, one
    /// margin right of <paramref name="originX"/> and further right until every member clears what is placed (H2)
    /// -- jumping past each obstacle in whole tenths -- and, when <paramref name="clear"/> is given, by tenths
    /// until it holds for the offset. Its runs move with it.
    /// </summary>
    /// <returns>The unit's inlet and outlet where they landed.</returns>
    private (PlacedAnchor In, PlacedAnchor Out) Slide(Unit unit, double originX, double yIn, List<(Member From, List<Point> Points)> runs, Func<double, double, bool>? clear = null)
    {
        var dx = originX + _margin - Math.Min(unit.In.At.X, unit.Out.At.X);
        var dy = yIn - unit.In.At.Y;

        foreach (var i in unit.Members)
        {
            _placed[i] = false;
        }

        while (true)
        {
            var shift = unit.Members.Max(i => Shift(i, _transform[i], _centre[i].Offset(dx, dy)));

            if (shift > 0)
            {
                // The same lattice as stepping by tenths from the start, in one jump.
                dx += Math.Ceiling(shift * 10 - 1e-6) / 10;
                continue;
            }

            if (clear is not null && !clear(dx, dy))
            {
                dx += 0.1;
                continue;
            }

            break;
        }

        foreach (var i in unit.Members)
        {
            _centre[i] = _centre[i].Offset(dx, dy);
            _placed[i] = true;
            Note(_graph.Components[i].Name, "C11", $"slid in as a unit member by ({dx:0.##}, {dy:0.##}) to ({_centre[i].X:0.##}, {_centre[i].Y:0.##}), a margin right of its origin and past every obstacle (H2)");
        }

        foreach (var (walkFrom, points) in unit.Runs)
        {
            runs.Add((walkFrom, points.Select(p => p.Offset(dx, dy)).ToList()));
        }

        return (unit.In with { At = unit.In.At.Offset(dx, dy) }, unit.Out with { At = unit.Out.At.Offset(dx, dy) });
    }

    /// <summary>How far right a member at <paramref name="centre"/> must move to clear every placed member (H2): its largest overlap with any of their margins, 0 when it is clear.</summary>
    private double Shift(int j, Transform t, Point centre)
    {
        var (w, h) = t.Size(_symbol[j]);
        var inner = Box.Around(centre, w, h);
        var outer = inner.Grow(_margin);
        var shift = 0.0;

        for (var i = 0; i < _n; i++)
        {
            if (!_placed[i] || i == j)
            {
                continue;
            }

            var other = InnerOf(i);

            if (other.Intersects(outer) || inner.Intersects(other.Grow(_margin)))
            {
                shift = Math.Max(shift, other.Grow(_margin).Right - inner.X);
            }
        }

        return shift;
    }

    /// <summary>Whether a vertical pipe at <paramref name="x"/> between two heights stays out of every placed member's outer box (B: a pipe never enters a box), the members in <paramref name="except"/> aside.</summary>
    private bool Free(double x, double y0, double y1, IReadOnlyList<int> except)
    {
        for (var i = 0; i < _n; i++)
        {
            if (!_placed[i] || except.Contains(i))
            {
                continue;
            }

            var box = InnerOf(i).Grow(_margin);

            if (box.X < x && x < box.Right && box.Y < y1 && y0 < box.Top)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Lays a ring's top rail rightwards from its start: each member by <see cref="OnRail"/>, each block by
    /// <see cref="Slide"/> with the rail continuing from its outlet, and under each junction whose free port leads
    /// to a bottom-rail member, the branch's block hanging between the rails (C14). Appends the runs so far.
    /// </summary>
    /// <returns>Where the rail ends, its pending points and the member it leaves; <see langword="null"/> when a member cannot be placed.</returns>
    private (PlacedAnchor End, List<Point> Pending, Member Previous)? Top(PlacedAnchor cursor, List<Point> pending, Member previous, List<Item> items, IReadOnlySet<int> ring, List<Member>? bottomMembers, HashSet<int> avoid, List<(Member From, List<Point> Points)> runs, List<Hanger> hangers)
    {
        foreach (var item in items)
        {
            if (item.Unit is { } u)
            {
                var (uIn, uOut) = Slide(u, cursor.At.X, cursor.At.Y, runs);
                pending.Add(uIn.At);
                runs.Add((previous, pending));
                cursor = uOut;
                pending = [uOut.At];
                previous = u.OutFrom;
                continue;
            }

            var m = item.Member!.Value;

            if (OnRail(cursor, m, m.InPort, m.OutPort) is not { } points)
            {
                return null;
            }

            pending.AddRange(points.Skip(1));
            runs.Add((previous, pending));
            cursor = AnchorOf(m.Component, m.OutPort);
            previous = m;

            if (bottomMembers is not null && Wildcard(m.Component) && Hang(m, cursor.At.Y, ring, bottomMembers, avoid, runs, hangers) is { } moved)
            {
                cursor = moved;
            }

            pending = [cursor.At];
        }

        return (cursor, pending, previous);
    }

    /// <summary>
    /// C14: a branch off a top-rail junction that reaches a bottom-rail member hangs between the rails. The branch
    /// is a chain of blocks like a rail (C11): its first block hangs one margin under the junction with its inlet
    /// one margin right of it, the junction moved along its rail to stand over the inlet so the drop from its free
    /// port is one bend; each further block steps on from the previous block's outlet, and the last faces back to
    /// the left. The return is laid once the bottom rail exists (<see cref="Close"/>).
    /// </summary>
    /// <returns>The junction's outlet where the rail continues from, or <see langword="null"/> when nothing hangs.</returns>
    private PlacedAnchor? Hang(Member j, double yTop, IReadOnlySet<int> ring, List<Member> bottomMembers, HashSet<int> avoid, List<(Member From, List<Point> Points)> runs, List<Hanger> hangers)
    {
        var ports = _graph.Components[j.Component].Ports;

        for (var p = 0; p < ports.Length; p++)
        {
            if (p == j.InPort || p == j.OutPort)
            {
                continue;
            }

            foreach (var b in bottomMembers)
            {
                var path = new List<Member>();
                var visited = new HashSet<int>(ring) { j.Component };
                visited.Remove(b.Component);

                if (!Extend(j.Component, -1, p, b.Component, path, visited) || path.Count < 2)
                {
                    continue;
                }

                var branch = path.GetRange(1, path.Count - 1);
                var bottomPort = path[0].InPort;
                HashSet<int> branchAvoid = [.. avoid, .. ring];
                var ranges = Ranges(branch, 0, branchAvoid);

                if (ranges.Count == 0)
                {
                    continue;
                }

                var mark = _groups.Count;
                var units = new List<Unit>();
                var items = new List<Item>();
                var next = 0;

                foreach (var (start, end, inner) in ranges)
                {
                    var block = Block(inner, branch[start], branch[end], branchAvoid, end == ranges[^1].End ? Direction.Left : Direction.Right);

                    if (block is null)
                    {
                        break;
                    }

                    if (units.Count > 0)
                    {
                        items.AddRange(branch.GetRange(next, start - next).Select(static m => new Item(m, null)));
                        items.Add(new Item(null, block));
                    }

                    units.Add(block);
                    next = end + 1;
                }

                if (units.Count < ranges.Count)
                {
                    _groups.RemoveRange(mark, _groups.Count - mark);
                    continue;
                }

                foreach (var i in units.SelectMany(static u => u.Members))
                {
                    _loop[i] = true;
                }

                var first = units[0];
                var feed = runs.Count - 1;
                var rise = first.Members.Max(i => InnerOf(i).Top) - first.In.At.Y;
                var half = Transform.Identity.Size(_symbol[j.Component]).Height / 2;
                var (uIn, uOut) = Slide(first, _centre[j.Component].X, yTop - half - _margin - rise, runs);
                var jx = uIn.At.X - _margin;

                if (jx > _centre[j.Component].X)
                {
                    _centre[j.Component] = new Point(jx, _centre[j.Component].Y);
                    Note(_graph.Components[j.Component].Name, "C14", $"the junction moved right to ({jx:0.##}, {_centre[j.Component].Y:0.##}) so its hanger's inlet clears it by a margin");
                    runs[feed].Points[^1] = AnchorOf(j.Component, j.InPort).At;
                }

                _side[(j.Component, p)] = Direction.Down;
                var free = AnchorOf(j.Component, p);
                runs.Add((new Member(j.Component, -1, p), [free.At, new Point(free.At.X, uIn.At.Y), uIn.At]));

                // The rest of the chain steps on from the first block's outlet, as a rail does.
                if (Top(uOut, [uOut.At], first.OutFrom, items, ring, null, branchAvoid, runs, hangers) is not { } chain)
                {
                    return null;
                }

                hangers.Add(new Hanger(chain.End, chain.Previous, j.Component, b.Component, bottomPort));
                return AnchorOf(j.Component, j.OutPort);
            }
        }

        return null;
    }

    /// <summary>The runs of consecutive members that the inner loops along a ring or branch take (C11), in flow order from <paramref name="from"/>, each with its loop.</summary>
    private List<(int Start, int End, List<Member> Inner)> Ranges(List<Member> cycle, int from, HashSet<int> avoid)
    {
        var ranges = new List<(int Start, int End, List<Member> Inner)>();

        for (var k = from; k < cycle.Count; k++)
        {
            var (start, end, inner) = Column(cycle, k, avoid);

            if (inner is not null)
            {
                ranges.Add((start, end, inner));
                k = end;
            }
        }

        return ranges;
    }

    /// <summary>
    /// Closes a ring whose top rail is laid: the bottom rail rightwards from its start against the flow, the corner
    /// junctions (C10), the unit slid into the right side at the top rail's end, and the hanging branches' returns
    /// down into their bottom junctions. Appends the ring's remaining runs.
    /// </summary>
    private bool Close((PlacedAnchor End, List<Point> Pending, Member Previous) top, Unit unit, double drop, double yBottom, List<Member> bottomMembers, List<Point> bottomStart, Member? leftJunction, Member? rightJunction, List<Hanger> hangers, List<(Member From, List<Point> Points)> runs, (Member Member, Transform Transform)? leftTurner = null)
    {
        var (topEnd, topPending, topPrevious) = top;

        // The bottom rail, built left to right against the flow, each member placed by its outlet facing the left side.
        var cursor = Anchor(bottomStart[^1], Direction.Right, Direction.Right);
        var pending = bottomStart;
        var bottomRuns = new List<(Member From, List<Point> Points)>();
        var first = bottomMembers.Count - 1;

        if (leftJunction is { } lj)
        {
            // The junction nearest the left side takes the bottom-left corner: in from the rail, out up the left side; its free port is the block's outlet, facing out beside the inlet.
            Place(lj.Component, Transform.Identity, bottomStart[^1], "C14", "the junction nearest the left side takes the bottom-left corner");
            _side[(lj.Component, lj.OutPort)] = Direction.Up;
            _side[(lj.Component, lj.InPort)] = Direction.Right;
            pending.RemoveAt(pending.Count - 1);
            pending.Add(AnchorOf(lj.Component, lj.OutPort).At);
            bottomRuns.Add((lj, pending));
            cursor = AnchorOf(lj.Component, lj.InPort);
            pending = [cursor.At];
            first--;
        }
        else if (leftTurner is { } lt)
        {
            // A member that turns the flow from leftward to upward takes the bottom-left corner (C9 mirrored): its
            // inlet on the rail facing right, its outlet on the left side's line facing up, so the side's run ends on
            // it and the rail starts from it.
            var dIn = AnchorOffset(lt.Member.Component, lt.Member.InPort, lt.Transform)!.Value.Offset;
            var dOut = AnchorOffset(lt.Member.Component, lt.Member.OutPort, lt.Transform)!.Value.Offset;
            var at = bottomStart[^1];
            Place(lt.Member.Component, lt.Transform, new Point(at.X - dOut.X, at.Y - dIn.Y), "C9", "the left turner takes the bottom-left corner, mirrored (C-105)");
            pending.RemoveAt(pending.Count - 1);
            pending.Add(AnchorOf(lt.Member.Component, lt.Member.OutPort).At);
            bottomRuns.Add((lt.Member, pending));
            cursor = AnchorOf(lt.Member.Component, lt.Member.InPort);
            pending = [cursor.At];
        }

        for (var k = first; k >= 0; k--)
        {
            var m = bottomMembers[k];

            if (hangers.Find(h => h.Bottom == m.Component) is { } hanging)
            {
                // C14: the junction a branch returns to stands directly under the one that feeds it -- as a loop's supply and return nodes align -- and never nearer the outlet than a margin, so the return is one bend; the rail runs on to it.
                var half = Transform.Identity.Size(_symbol[m.Component]).Width / 2;
                var x = Math.Min(_centre[hanging.Top].X, hanging.Out.At.X - _margin) - half - _margin;

                if (x > cursor.At.X)
                {
                    cursor = Anchor(new Point(x, cursor.At.Y), Direction.Right, Direction.Right);
                }
            }

            if (OnRail(cursor, m, m.OutPort, m.InPort) is not { } points)
            {
                return false;
            }

            pending.AddRange(points.Skip(1));
            bottomRuns.Add((m, pending));
            cursor = AnchorOf(m.Component, m.InPort);
            pending = [cursor.At];
        }

        // The unit: as far right as the longer rail needs, its inlet level with the top rail's end (or a drop under it).
        var origin = Math.Max(topEnd.At.X, cursor.At.X);

        if (rightJunction is { } j0)
        {
            // The junction moves under the unit's outlet (C10), so its rail box is not in the way: the unit needs only to put that outlet at or beyond the junction's packed rail position.
            _placed[j0.Component] = false;
            origin = Math.Max(topEnd.At.X, _centre[j0.Component].X - (unit.Out.Along(_margin).X - unit.In.At.X) - _margin);
        }

        // A left-facing outlet descends to the bottom rail one margin out; the descent must clear the boxes too.
        Func<double, double, bool>? clear = rightJunction is null && unit.Out.Outward == Direction.Left
            ? (dx, dy) => Free(unit.Out.Along(_margin).X + dx, yBottom, unit.Out.At.Y + dy, unit.Members)
            : null;
        var (uIn, uOut) = Slide(unit, origin, topEnd.At.Y - drop, runs, clear);

        if (rightJunction is { } j1)
        {
            _placed[j1.Component] = true;
        }

        if (uIn.Outward == Direction.Up)
        {
            topPending.Add(new Point(uIn.At.X, topEnd.At.Y));
        }

        topPending.Add(uIn.At);
        runs.Add((topPrevious, topPending));
        var outOuter = uOut.Along(_margin);

        if (rightJunction is { } j)
        {
            // The junction slides right along its rail until it clears the unit, under the outlet stub; the descent lands on it from above.
            var jx = outOuter.X;
            var jy = _centre[j.Component].Y;

            while (Clashes(j.Component, Transform.Identity, new Point(jx, jy)))
            {
                jx += 0.1;
            }

            _centre[j.Component] = new Point(jx, jy);
            Note(_graph.Components[j.Component].Name, "C14", $"the hanger's bottom junction slid right along its rail to ({jx:0.##}, {jy:0.##}), under the unit's outlet stub and clear of it");
            _side[(j.Component, j.InPort)] = Direction.Up;
            var reaching = bottomRuns.FindIndex(r => r.From.Component == j.Component);
            bottomRuns[reaching].Points[^1] = AnchorOf(j.Component, j.OutPort).At;
            pending = [AnchorOf(j.Component, j.InPort).At, new Point(jx, uOut.At.Y), uOut.At];
        }
        else
        {
            pending.AddRange([new Point(outOuter.X, yBottom), outOuter, uOut.At]);
        }

        bottomRuns.Add((unit.OutFrom, pending));

        foreach (var (walkFrom, points) in bottomRuns)
        {
            points.Reverse();
            runs.Add((walkFrom, points));
        }

        // C14: each hanging branch returns from its outlet, level to above its bottom junction and down into it.
        foreach (var h in hangers)
        {
            _side[(h.Bottom, h.BottomPort)] = Direction.Up;
            var up = AnchorOf(h.Bottom, h.BottomPort);
            runs.Add((h.OutFrom, [h.Out.At, new Point(up.At.X, h.Out.At.Y), up.At]));
        }

        return true;
    }

    /// <summary>
    /// The run of consecutive ring members around <paramref name="consumerAt"/> -- any member of the ring -- that an
    /// inner loop through it avoiding <paramref name="avoid"/> passes through (C11), and that inner loop in flow
    /// order from its consumer.
    /// </summary>
    /// <remarks>
    /// With no such inner loop, or one that leaves the ring through a boxed member -- a second branch, not a
    /// recirculation -- the run is the consumer alone and the path is <see langword="null"/>.
    /// </remarks>
    private (int Start, int End, List<Member>? Inner) Column(List<Member> cycle, int consumerAt, HashSet<int> avoid)
    {
        var c = cycle[consumerAt];
        var path = new List<Member>();
        var visited = new HashSet<int>(avoid) { c.Component };

        if (!Extend(c.Component, -1, c.OutPort, c.Component, path, visited))
        {
            return (consumerAt, consumerAt, null);
        }

        // The search may start anywhere on the inner loop; the block wants it in flow order from its consumer.
        var at = ConsumerOf(path, 0);

        if (at < 0)
        {
            return (consumerAt, consumerAt, null);
        }

        path = [.. path.Skip(at), .. path.Take(at)];
        var inner = path.Select(static m => m.Component).ToHashSet();
        var start = consumerAt;
        var end = consumerAt;

        while (start > 0 && !avoid.Contains(cycle[start - 1].Component) && inner.Contains(cycle[start - 1].Component))
        {
            start--;
        }

        while (end + 1 < cycle.Count && !avoid.Contains(cycle[end + 1].Component) && inner.Contains(cycle[end + 1].Component))
        {
            end++;
        }

        foreach (var m in path)
        {
            var index = cycle.FindIndex(x => x.Component == m.Component);

            if (index < start || index > end)
            {
                return (consumerAt, consumerAt, null);
            }
        }

        return (start, end, path);
    }

    /// <summary>A loop member placed on a rail from the cursor: a component by <see cref="PlaceFrom"/> through the port facing the cursor, a junction by <see cref="PlaceNode"/> with its other loop port turned on along the rail.</summary>
    /// <param name="cursor">Where the rail has reached.</param>
    /// <param name="m">The member.</param>
    /// <param name="facing">Its port towards the cursor.</param>
    /// <param name="onward">Its other loop port.</param>
    /// <returns>The pipe from the cursor to the facing port, or null when nothing admitted fits.</returns>
    private ImmutableArray<Point>? OnRail(PlacedAnchor cursor, Member m, int facing, int onward)
    {
        if (!Wildcard(m.Component))
        {
            return PlaceFrom(cursor, m.Component, facing);
        }

        var points = PlaceNode(cursor, m.Component, facing);
        _side[(m.Component, onward)] = cursor.Outward;
        return points;
    }

    /// <summary>Whether a loop member has a connection off the loop the fluid leaves by.</summary>
    private bool LeavesLoop(Member m)
    {
        var ports = _graph.Components[m.Component].Ports;

        for (var p = 0; p < ports.Length; p++)
        {
            if (p != m.InPort && p != m.OutPort && !Enters(m.Component, p, ports[p].Role) && _links.Any(l => (l.From == m.Component && l.FromPort == p) || (l.To == m.Component && l.ToPort == p)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A junction's side for a port no rule has placed: one no other port uses, the side facing away from the loop's centre first for a loop member -- vertical where the rail is level, level where it is vertical -- else the first free from right, up, left, down.</summary>
    private Direction FreeSide(int i)
    {
        var used = new HashSet<Direction>();

        for (var p = 0; p < _graph.Components[i].Ports.Length; p++)
        {
            if (_side.TryGetValue((i, p), out var s))
            {
                used.Add(s);
            }
        }

        var preferred = new List<Direction>();

        if (_loop[i])
        {
            var away = _centre[i].Offset(-_loopCentre.X, -_loopCentre.Y);
            var level = used.Contains(Direction.Left) || used.Contains(Direction.Right);
            var vertical = used.Contains(Direction.Up) || used.Contains(Direction.Down);
            var levelAway = away.X >= 0 ? Direction.Right : Direction.Left;
            var verticalAway = away.Y >= 0 ? Direction.Up : Direction.Down;

            // On a level rail the free port leaves vertically; on a vertical side, level. At a corner (C10) it leaves level, so an open end there lines up with the loop's other open ends (C7).
            preferred.AddRange(level && !vertical ? [verticalAway, levelAway] : [levelAway, verticalAway]);
        }

        preferred.AddRange([Direction.Right, Direction.Up, Direction.Left, Direction.Down]);
        return preferred.First(d => !used.Contains(d));
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

    /// <summary>The simple flow loop that leaves <paramref name="source"/> by <paramref name="leaving"/>, if that port is on one: a depth-first walk over the ports the fluid leaves by, through inline elements and junctions alike, back to the source.</summary>
    /// <param name="source">Where the walk starts.</param>
    /// <param name="leaving">The source's port to leave by.</param>
    /// <returns>The members in flow order, the source first, or null.</returns>
    private List<Member>? Cycle(int source, int leaving)
    {
        var path = new List<Member>();
        var visited = new HashSet<int> { source };
        return Extend(source, -1, leaving, source, path, visited) ? path : null;
    }

    private bool Extend(int component, int inPort, int outPort, int source, List<Member> path, HashSet<int> visited)
    {
        var (next, nextPort) = Follow(component, outPort);

        if (next < 0)
        {
            return false;
        }

        path.Add(new Member(component, inPort, outPort));

        if (next == source)
        {
            path[0] = path[0] with { InPort = nextPort };
            return true;
        }

        if (visited.Add(next))
        {
            var ports = _graph.Components[next].Ports;

            for (var p = 0; p < ports.Length; p++)
            {
                if (p != nextPort && !Enters(next, p, ports[p].Role) && _links.Any(l => (l.From == next && l.FromPort == p) || (l.To == next && l.ToPort == p)) && Extend(next, nextPort, p, source, path, visited))
                {
                    return true;
                }
            }

            visited.Remove(next);
        }

        path.RemoveAt(path.Count - 1);
        return false;
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
        Place(j, Transform.Identity, at.Offset(delta.X, delta.Y), "C4", $"a node one clearance out on the placed port's axis, {Name(d)}");
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
        // A level kind (D-113) faces a vertical pipe only as a last resort: the pipe turns level into it first.
        var level = _symbol[j].TransformClass == "level";
        // H10: among the transforms that face the pipe, the one that sends the member's outlet on to the right comes first, mirrored where that is what it takes (step 11b).
        var facing = admitted.Where(t => t.Arrangement == "default" && AnchorOffset(j, q, t) is { } a && a.Outward == d.Opposite && (!level || t.Rotation is 0 or 180)).OrderBy(t => Onward(j, q, t)).ToList();

        if (facing.Count == 0)
        {
            // C3: the pipe turns into a member that cannot face it -- down into a standing kind from a level pipe, rightwards into a level kind from a vertical one.
        var along = d == Direction.Left || d == Direction.Right ? Direction.Down : Direction.Right;
            var turned = admitted.Where(t => t.Arrangement == "default" && AnchorOffset(j, q, t) is { } a && a.Outward == along.Opposite).ToList();

            if (turned.Count > 0)
            {
                var turn = AnchorOffset(j, q, turned[0])!.Value.Offset;
                var corner = anchor.At.Towards(d, _margin);
                var inner = Clear(j, turned[0], corner, along, new Point(-turn.X, -turn.Y));
                Place(j, turned[0], inner.Offset(-turn.X, -turn.Y), "C3", $"the pipe turns {Name(along)} into a member that cannot face it");
                return [anchor.At, corner, inner];
            }

            facing = admitted.Where(t => AnchorOffset(j, q, t) is { } a && a.Outward == d.Opposite).OrderBy(t => Onward(j, q, t)).ToList();
        }

        if (facing.Count == 0)
        {
            return null;
        }

        var offset = AnchorOffset(j, q, facing[0])!.Value.Offset;
        var end = Clear(j, facing[0], anchor.At, d, new Point(-offset.X, -offset.Y));
        Place(j, facing[0], end.Offset(-offset.X, -offset.Y), "C5", $"facing the pipe arriving {Name(d)}, straight along the axis, the arrangement sending its outlet on to the right first (H10)");
        return [anchor.At, end];
    }

    /// <summary>H10 as a sort key for a member placed from a pipe at port <paramref name="q"/>: 0 when a port the flow leaves by faces right under <paramref name="t"/>, so the flow goes on left to right, else 1; the admitted order decides between equals.</summary>
    private int Onward(int j, int q, Transform t)
    {
        var ports = _graph.Components[j].Ports;

        for (var p = 0; p < ports.Length; p++)
        {
            if (p != q && !Enters(j, p, ports[p].Role) && Outward(j, p, t) == Direction.Right)
            {
                return 0;
            }
        }

        return 1;
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

        var admitted = symbol.TransformClass switch
        {
            "standing" => Transform.All(symbol).Where(static t => t.Rotation is 0 or 180),
            "upright" => Transform.All(symbol).Where(static t => t.Rotation == 0),
            _ => Transform.All(symbol),
        };

        // A9 (D-109): the default arrangement first, then the smaller turn, then unmirrored before mirrored -- so a symbol
        // reversing on a line is mirrored rather than half-turned and its top (a valve's stem, a pump's badge) stays up.
        // A `level` kind (a pump, D-113) stands vertical only when nothing level fits: its quarter turns come last.
        var level = symbol.TransformClass == "level";
        return admitted.OrderBy(static t => t.Arrangement != "default").ThenBy(t => level && t.Rotation is 90 or 270).ThenBy(static t => t.Rotation).ThenBy(static t => t.Mirrored);
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
            Place(i, Transform.Identity, new Point(0, bottom - _margin - (h / 2)), "fallback", "placed by no rule; stacked under the drawing");
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

        if (flow is CircuitNode { Boundary: BoundaryRole.Inlet })
        {
            return false;
        }

        if (flow is CircuitNode { Boundary: BoundaryRole.Outlet })
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
                        CircuitNode { Boundary: BoundaryRole.Inlet } => true,
                        CircuitNode { Boundary: BoundaryRole.Outlet } => false,
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

    private void Place(int i, Transform t, Point centre, string rule, string reason)
    {
        _transform[i] = t;
        _centre[i] = centre;
        _placed[i] = true;
        Note(_graph.Components[i].Name, rule, $"{reason}; centre ({centre.X:0.##}, {centre.Y:0.##}), {Describe(t)}");
    }

    /// <summary>Records one decision for the layout report (<c>C-107</c>).</summary>
    private void Note(string subject, string rule, string reason) => _trace.Add(new PlacementNote(subject, rule, reason));

    private static string Name(Direction d) =>
        d == Direction.Right ? "rightwards" : d == Direction.Left ? "leftwards" : d == Direction.Up ? "upwards" : d == Direction.Down ? "downwards" : d.ToString();

    private static string Describe(Transform t) =>
        $"{t.Arrangement} rot {t.Rotation}{(t.Mirrored ? " mirrored" : string.Empty)}";

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

            _routes.Add((k, new Route($"c{link.Connection}", "pipe", LayerOf(k), points, [])));
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

    private void ComputeHops()
    {
        // C16: where two routes cross, the one drawn behind takes the hop -- a signal behind a return behind a supply -- and between equals the later one.
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

    /// <summary>A layer's place in the draw order (C16): the supply in front, the return behind it, signals behind everything.</summary>
    private static int Rank(string layer) => layer switch { "supply" => 2, "return" => 1, _ => 0 };

    /// <summary>A pipe's layer (C16): <c>supply</c> while the flow from a heat source has not passed a losing side, else <c>return</c>.</summary>
    private string LayerOf(int link) => (_supply ??= SupplyLinks()).Contains(link) ? "supply" : "return";

    /// <summary>
    /// The links the flow reaches from every heat source -- a supply boundary, or an exchanger's gaining outlet
    /// -- before it passes a losing side, a consumer's first side or a source's second (C16). Every other pipe
    /// carries return.
    /// </summary>
    private HashSet<int> SupplyLinks()
    {
        var supply = new HashSet<int>();
        var queue = new Queue<(int Component, int Port)>();

        for (var i = 0; i < _n; i++)
        {
            if (IsSource(i) || _graph.Components[i] is CircuitNode { Boundary: BoundaryRole.Inlet })
            {
                Leaving(i, -1, queue);
            }
        }

        while (queue.Count > 0)
        {
            var (i, p) = queue.Dequeue();

            for (var k = 0; k < _links.Count; k++)
            {
                var link = _links[k];
                var (peer, peerPort) = link.From == i && link.FromPort == p ? (link.To, link.ToPort)
                    : link.To == i && link.ToPort == p ? (link.From, link.FromPort)
                    : (-1, -1);

                if (peer >= 0 && supply.Add(k) && !Loses(peer, peerPort))
                {
                    Leaving(peer, peerPort, queue);
                }
            }
        }

        return supply;
    }

    /// <summary>Whether the stream entering <paramref name="i"/> at <paramref name="port"/> leaves its heat there: a consumer's first side, or a source's second.</summary>
    private bool Loses(int i, int port)
    {
        if (_graph.Components[i] is not HeatExchanger h)
        {
            return false;
        }

        return h.Ports[port].Name.EndsWith('2') ? IsSource(i) : Duty(i) is not null;
    }

    /// <summary>Queues the ports the stream leaves <paramref name="i"/> by, having entered at <paramref name="except"/> (-1 at a source): an exchanger's outlet on the same side, any other component's outlets.</summary>
    private void Leaving(int i, int except, Queue<(int Component, int Port)> queue)
    {
        var ports = _graph.Components[i].Ports;
        var exchanger = _graph.Components[i] is HeatExchanger;
        var second = except >= 0 && ports[except].Name.EndsWith('2');

        for (var q = 0; q < ports.Length; q++)
        {
            if (q == except || Enters(i, q, ports[q].Role) || (exchanger && ports[q].Name.EndsWith('2') != second))
            {
                continue;
            }

            queue.Enqueue((i, q));
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

    private void PlaceInstruments()
    {
        var boxes = new Dictionary<string, Box>(StringComparer.Ordinal);
        var placed = new List<(NonFlowElementHint Element, int Anchor, string SymbolId)>();

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
                .FirstOrDefault(box => !Collides(box, boxes.Values), Box.Around(candidates[0], size[2], size[3]));

            while (Collides(inner, boxes.Values))
            {
                inner = inner.Offset(size[2] + _margin, 0);
            }

            boxes[element.ComponentId] = inner;
            placed.Add((element, anchor, symbol.Id));
        }

        // C15: a controller reads its node through the sensor standing on it. The signal leaves the sensor level, by the side facing the controller, and turns once into it; when that level stub would be shorter than a margin, the sensor and its inline node slide along the rail to make room.
        foreach (var (element, _, _) in placed)
        {
            if (element.ActuationTargetId is null || SensorOf(placed, element) is not { } sensor)
            {
                continue;
            }

            var s = boxes[sensor.Element.ComponentId];
            var c = boxes[element.ComponentId];
            var toward = c.Centre.X >= s.Centre.X ? 1 : -1;
            var stub = toward > 0 ? c.Centre.X - s.Right : s.X - c.Centre.X;
            var shift = -toward * (_margin - stub);

            if (stub < _margin - Eps && _inline[sensor.Anchor] && Nudge(sensor.Anchor, shift))
            {
                boxes[sensor.Element.ComponentId] = s.Offset(shift, 0);
            }
        }

        var owners = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (element, _, _) in placed)
        {
            owners[element.ComponentId] = _n + _instrumentBoxes.Count;
            _instrumentBoxes.Add(boxes[element.ComponentId]);
        }

        foreach (var (element, _, symbolId) in placed)
        {
            var inner = boxes[element.ComponentId];
            var owner = owners[element.ComponentId];

            _instruments.Add(new Placement
            {
                ComponentId = element.ComponentId,
                SymbolId = symbolId,
                Inner = inner,
                Outer = inner.Grow(_margin),
                Rotation = 0,
                Mirrored = false,
                Arrangement = "default",
                Anchors = ImmutableSortedDictionary<string, PlacedAnchor>.Empty.Add("*", Anchor(inner.Centre, Direction.Down, Direction.Down)),
                LabelAt = inner.Centre,
                Source = "computed",
            });

            if (element.ActuationTargetId is { } actuated)
            {
                if (SensorOf(placed, element) is { } sensor)
                {
                    Signal(element.ComponentId + ":measures", boxes[sensor.Element.ComponentId], owners[sensor.Element.ComponentId], inner, owner, toCentre: false);
                }
                else
                {
                    Signal(element.ComponentId + ":measures", inner, owner, element.MeasurementTargetId);
                }

                if (actuated != element.MeasurementTargetId)
                {
                    Signal(element.ComponentId + ":actuates", inner, owner, actuated);
                }
            }
            else
            {
                Signal(element.ComponentId + ":measures", inner, owner, element.MeasurementTargetId);
            }
        }
    }

    /// <summary>The sensor standing on the node a controller reads, when the script placed one (C15).</summary>
    private static (NonFlowElementHint Element, int Anchor, string SymbolId)? SensorOf(List<(NonFlowElementHint Element, int Anchor, string SymbolId)> placed, NonFlowElementHint controller)
    {
        foreach (var candidate in placed)
        {
            if (candidate.Element.ActuationTargetId is null && candidate.Element.MeasurementTargetId == controller.MeasurementTargetId && candidate.Element.ComponentId != controller.ComponentId)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Moves an inline node along its level run by <paramref name="dx"/> (C15) when every route ending on it stays level and keeps a margin to its next point; the routes follow.</summary>
    private bool Nudge(int node, double dx)
    {
        var from = _centre[node];
        var to = from.Offset(dx, 0);
        var ends = new List<(int Index, bool AtStart)>();

        for (var k = 0; k < _routes.Count; k++)
        {
            var points = _routes[k].Route.Points;

            if (points.Length < 2)
            {
                continue;
            }

            foreach (var atStart in new[] { true, false })
            {
                var end = atStart ? points[0] : points[^1];
                var next = atStart ? points[1] : points[^2];

                if (end.ManhattanTo(from) > Eps)
                {
                    continue;
                }

                if (Math.Abs(next.Y - from.Y) > Eps || Math.Sign(next.X - to.X) != Math.Sign(next.X - from.X) || Math.Abs(next.X - to.X) < _margin - Eps)
                {
                    return false;
                }

                ends.Add((k, atStart));
            }
        }

        if (ends.Count == 0)
        {
            return false;
        }

        foreach (var (k, atStart) in ends)
        {
            var points = _routes[k].Route.Points;
            var moved = points.SetItem(atStart ? 0 : points.Length - 1, to);
            _routes[k] = (_routes[k].Link, _routes[k].Route with { Points = moved });

            if (_routes[k].Link >= 0)
            {
                _routeOf[_routes[k].Link] = moved;
            }
        }

        _centre[node] = to;
        Note(_graph.Components[node].Name, "C15", $"an inline node nudged along its level run by {dx:0.##} to ({to.X:0.##}, {to.Y:0.##})");
        return true;
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

    /// <summary>A signal line from an instrument's box to a component: to the point of an inline one, to the facing edge of a boxed one.</summary>
    private void Signal(string id, Box from, int fromOwner, string targetId)
    {
        if (!_index.TryGetValue(targetId, out var target) || !_placed[target])
        {
            return;
        }

        Signal(id, from, fromOwner, InnerOf(target), target, toCentre: _inline[target]);
    }

    /// <summary>A signal line between two boxes: one bend, level first, into the facing edge (C15) -- unless that path crosses a box, when the router takes it round every placed box instead, pipes free to cross (C-94).</summary>
    /// <param name="id">The route's id.</param>
    /// <param name="from">The instrument's box the line leaves.</param>
    /// <param name="fromOwner">That box's owner: an instrument's is <c>_n</c> plus its index.</param>
    /// <param name="to">The box the line reaches.</param>
    /// <param name="toOwner">That box's owner, a component index or an instrument's.</param>
    /// <param name="toCentre">Whether the line ends at the box's centre, as it does on an inline node's point.</param>
    private void Signal(string id, Box from, int fromOwner, Box to, int toOwner, bool toCentre)
    {
        var plumb = Math.Abs(from.Centre.X - to.Centre.X) < Eps;
        var level = Math.Abs(from.Centre.Y - to.Centre.Y) < Eps;
        var right = from.Centre.X < to.Centre.X;
        var down = from.Centre.Y > to.Centre.Y;
        var points = new List<Point>();

        if (plumb)
        {
            points.Add(new Point(from.Centre.X, down ? from.Y : from.Top));
            points.Add(toCentre ? to.Centre : new Point(to.Centre.X, down ? to.Top : to.Y));
        }
        else if (level)
        {
            points.Add(new Point(right ? from.Right : from.X, from.Centre.Y));
            points.Add(toCentre ? to.Centre : new Point(right ? to.X : to.Right, to.Centre.Y));
        }
        else
        {
            points.Add(new Point(right ? from.Right : from.X, from.Centre.Y));
            points.Add(new Point(to.Centre.X, from.Centre.Y));
            points.Add(toCentre ? to.Centre : new Point(to.Centre.X, down ? to.Top : to.Y));
        }

        if (SignalCrosses(points, fromOwner, toOwner))
        {
            var router = new OrthogonalRouter(_margin);

            for (var i = 0; i < _n; i++)
            {
                if (_placed[i] && !_inline[i])
                {
                    router.AddBox(InnerOf(i), i);
                }
            }

            for (var k = 0; k < _instrumentBoxes.Count; k++)
            {
                router.AddBox(_instrumentBoxes[k], _n + k);
            }

            // Every line drawn so far keeps the signal a margin off it along its length; crossing is free (C16).
            foreach (var (link, route) in _routes)
            {
                var (ownerA, ownerB) = link >= 0 ? (_links[link].From, _links[link].To) : (-1, -1);

                for (var s = 1; s < route.Points.Length; s++)
                {
                    router.AddPipe(route.Points[s - 1], route.Points[s], link, ownerA, ownerB);
                }
            }

            if (router.Route(Edges(from, false), fromOwner, Edges(to, toCentre), toOwner) is { } routed)
            {
                points = [.. routed.Points];
            }
        }

        _routes.Add((-1, new Route(id, "signal", "signal", Normalise(points), [])));
    }

    /// <summary>Whether a signal path passes through the inside of any placed box other than the two it joins, or runs along a line already drawn (C-94).</summary>
    private bool SignalCrosses(List<Point> points, int fromOwner, int toOwner)
    {
        for (var s = 1; s < points.Count; s++)
        {
            var (a, b) = (points[s - 1], points[s]);
            var vertical = Math.Abs(a.X - b.X) < Eps;

            foreach (var (_, route) in _routes)
            {
                for (var t = 1; t < route.Points.Length; t++)
                {
                    var (c, d) = (route.Points[t - 1], route.Points[t]);

                    if (Math.Abs(c.X - d.X) < Eps != vertical)
                    {
                        continue;
                    }

                    var (line, other) = vertical ? (a.X, c.X) : (a.Y, c.Y);
                    var (lo, hi) = vertical ? (Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)) : (Math.Min(a.X, b.X), Math.Max(a.X, b.X));
                    var (lo2, hi2) = vertical ? (Math.Min(c.Y, d.Y), Math.Max(c.Y, d.Y)) : (Math.Min(c.X, d.X), Math.Max(c.X, d.X));

                    if (Math.Abs(line - other) < Eps && Math.Min(hi, hi2) - Math.Max(lo, lo2) > Eps)
                    {
                        return true;
                    }
                }
            }

            for (var i = 0; i < _n; i++)
            {
                if (i != fromOwner && i != toOwner && _placed[i] && !_inline[i] && Passes(InnerOf(i), points[s - 1], points[s]))
                {
                    return true;
                }
            }

            for (var k = 0; k < _instrumentBoxes.Count; k++)
            {
                if (_n + k != fromOwner && _n + k != toOwner && Passes(_instrumentBoxes[k], points[s - 1], points[s]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The four anchors a signal may leave or reach a box by: the middle of each edge facing out, or the centre point facing every way for an inline node.</summary>
    private static List<PlacedAnchor> Edges(Box box, bool centre) =>
        [.. Direction.All.Select(d => Anchor(centre ? box.Centre : box.Centre.Towards(d, d.Horizontal ? box.Width / 2 : box.Height / 2), d, d))];

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

        // The groups (A8): the rings and blocks that are one component to the rest of the system -- exactly one connection enters and one leaves. A closed circuit crosses nothing and is the drawing itself; a header with several taps is not one thing either. Outer before inner; bounds are the members' inner boxes and the routes between them.
        var groups = new List<LayoutGroup>();
        var kept = new List<List<int>>();

        foreach (var (kind, members, top) in _groups)
        {
            var set = members.ToHashSet();
            var ins = 0;
            var outs = 0;
            var inside = new HashSet<int>();

            foreach (var i in members)
            {
                var ports = _graph.Components[i].Ports;

                for (var p = 0; p < ports.Length; p++)
                {
                    var walk = Walk(i, p);

                    if (walk.Far < 0)
                    {
                        continue;
                    }

                    if (set.Contains(walk.Far))
                    {
                        // A route belongs to the group when its walk runs from one member to another; a stub out to something else does not.
                        inside.UnionWith(walk.Links.Select(static l => l.Link));
                    }
                    else if (top && Returns(walk.Far, set, walk.Links.Select(static l => l.Link).ToHashSet()))
                    {
                        // The ring that holds the source: a tap whose flow comes back to the ring is a branch of the closed circuit, not an inlet or an outlet.
                    }
                    else if (Enters(i, p, ports[p].Role))
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

            Box? bounds = null;

            foreach (var i in members)
            {
                bounds = bounds is { } b ? b.Union(InnerOf(i)) : InnerOf(i);
            }

            foreach (var (link, route) in _routes)
            {
                if (inside.Contains(link))
                {
                    foreach (var point in route.Points)
                    {
                        var dot = new Box(point.X, point.Y, 0, 0);
                        bounds = bounds is { } b ? b.Union(dot) : dot;
                    }
                }
            }

            groups.Add(new LayoutGroup($"loop-{groups.Count + 1}", kind, "cw", [.. members.Select(i => _graph.Components[i].Name)], Rounded(bounds ?? new Box(0, 0, 0, 0))));
            kept.Add(members);
        }

        // The innermost group that placed a component: groups are listed outer before inner.
        string? GroupOf(int i)
        {
            var k = kept.FindLastIndex(m => m.Contains(i));
            return k < 0 ? null : $"loop-{k + 1}";
        }

        // Whether the flow that left a set of members and arrived here by the given links can reach the set again by some other path.
        bool Returns(int from, HashSet<int> set, HashSet<int> arrived)
        {
            var seen = new HashSet<int> { from };
            var queue = new Queue<int>();
            queue.Enqueue(from);

            while (queue.Count > 0)
            {
                var n = queue.Dequeue();

                for (var k = 0; k < _links.Count; k++)
                {
                    if (arrived.Contains(k))
                    {
                        continue;
                    }

                    var link = _links[k];
                    var other = link.From == n ? link.To : link.To == n ? link.From : -1;

                    if (other < 0)
                    {
                        continue;
                    }

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
                Group = _fallback[i] ? "fallback" : GroupOf(i),
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
            Groups = [.. groups],
            Provenance = [.. _trace],
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
