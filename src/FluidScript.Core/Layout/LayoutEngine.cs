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
    private readonly List<Placement> _instruments = [];

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

    /// <summary>Whether an element is inline (A5): a declared pipe, or a node inferred by the language, with exactly two connections.</summary>
    private bool Inline(int j) =>
        (_graph.Components[j] is Pipe || Wildcard(j)) && _links.Count(l => l.From == j || l.To == j) == 2;

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

    private bool Loop(int source)
    {
        if (_graph.Components[source] is not HeatExchanger { Power: > 0 } || Cycle(source) is not { } cycle)
        {
            return false;
        }

        var consumerAt = ConsumerOf(cycle);

        if (consumerAt < 0)
        {
            return false;
        }

        var s = cycle[0];
        var ts = Admitted(s.Component).Where(t => t.Arrangement == "default" && Outward(s.Component, s.OutPort, t) == Direction.Up && Outward(s.Component, s.InPort, t) == Direction.Down).ToList();

        if (ts.Count == 0)
        {
            return false;
        }

        _loopCentre = new Point(0, 0);

        foreach (var member in cycle)
        {
            _loop[member.Component] = true;
        }

        // C11: the right side is a unit -- the consumer alone, or an inner loop laid out first as a block (A8). Blocks nest: each is found by the same search, with the enclosing rings' sources avoided.
        var mark = _groups.Count;
        var (unitStart, unitEnd, unit) = UnitOf(cycle, consumerAt, [source]);

        if (unit is null)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            return false;
        }

        Place(s.Component, ts[0], new Point(0, 0));
        var sOut = AnchorOf(s.Component, s.OutPort);
        var sIn = AnchorOf(s.Component, s.InPort);
        var yTop = sOut.Along(_margin).Y;
        var bottomMembers = cycle.GetRange(unitEnd + 1, cycle.Count - unitEnd - 1);
        var (_, rightJunction) = Corners(bottomMembers, unit, leftFirst: false);
        var (drop, yBottom) = Bottom(unit, yTop, sIn.Along(_margin).Y, rightJunction?.Component ?? -1);

        // C12: a member on a side with slack sits at the side's middle. The source moves down by half the excess of the rails' span over its own.
        var slack = sIn.Along(_margin).Y - yBottom;
        Place(s.Component, ts[0], new Point(0, -slack / 2));
        sOut = AnchorOf(s.Component, s.OutPort);
        sIn = AnchorOf(s.Component, s.InPort);

        var runs = new List<(Member From, List<Point> Points)>();
        var topStart = new Point(sOut.At.X, yTop);

        if (!Rails(Anchor(topStart, Direction.Right, Direction.Right), [sOut.At, topStart], s, cycle.GetRange(1, unitStart - 1), unit, drop, yBottom, bottomMembers, [sIn.At, sIn.Along(_margin), new Point(sIn.At.X, yBottom)], null, rightJunction, runs))
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            return false;
        }

        // The ring is a group (A8), listed before the blocks it holds.
        _groups.Insert(mark, ("loop", cycle.Select(static m => m.Component).ToList(), true));

        foreach (var (from, points) in runs)
        {
            Assign(Walk(from.Component, from.OutPort), Normalise(points));
        }

        return true;
    }

    /// <summary>The loop member that takes the right side: the standing consumer of the largest duty, else the first member the flow leaves the loop by.</summary>
    private int ConsumerOf(List<Member> cycle)
    {
        var consumerAt = -1;

        for (var k = 1; k < cycle.Count; k++)
        {
            if (_graph.Components[cycle[k].Component] is HeatExchanger { Power: < 0 } candidate && (consumerAt < 0 || candidate.Power < ((HeatExchanger)_graph.Components[cycle[consumerAt].Component]).Power))
            {
                consumerAt = k;
            }
        }

        for (var k = 1; k < cycle.Count && consumerAt < 0; k++)
        {
            // No standing consumer: the member the loop's flow leaves by -- a valve or a junction with an off-loop outlet -- takes the right side.
            if (LeavesLoop(cycle[k]))
            {
                consumerAt = k;
            }
        }

        return consumerAt;
    }

    /// <summary>The unit around a ring's consumer (C11): the consumer alone, or the block of the inner loop through it that avoids <paramref name="avoid"/>; with the range of ring members the unit replaces.</summary>
    private (int Start, int End, Unit? Unit) UnitOf(List<Member> cycle, int consumerAt, HashSet<int> avoid)
    {
        var (start, end, inner) = Column(cycle, consumerAt, avoid);
        return (start, end, inner is null ? Single(cycle[consumerAt]) : Block(inner, cycle[start], cycle[end], avoid));
    }

    /// <summary>
    /// The junctions that take a ring's bottom corners (C10): on the right, the one next to the unit under an outlet
    /// that faces down or right; on the left, when the ring is a block, the one nearest the left side -- so a block's
    /// outlet faces its parent beside its inlet. One junction takes one corner.
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
        Place(m.Component, candidates[0], new Point(-inOffset.X, -inOffset.Y));

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
    /// inlet facing out to the left, the members between them on the bottom rail -- the last of them, a junction,
    /// at the bottom-left corner with the block's outlet facing out beside the inlet -- and the rest on the top.
    /// </summary>
    /// <param name="path">The inner loop in flow order from its consumer.</param>
    /// <param name="entry">The member and port the outer loop enters the block by.</param>
    /// <param name="exit">The member and port the outer loop leaves the block by.</param>
    /// <param name="avoid">The enclosing rings' sources and corner members, which a nested unit's search must not pass.</param>
    /// <returns>The block as a unit, or <see langword="null"/> when no member can take the corner or the entry and exit do not face the ring.</returns>
    private Unit? Block(List<Member> path, Member entry, Member exit, HashSet<int> avoid)
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
        var (_, innerEnd, unit) = UnitOf(path, 0, [.. avoid, corner.Component]);

        if (unit is null || innerEnd >= cornerAt)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            return null;
        }

        var bottomMembers = path.GetRange(innerEnd + 1, cornerAt - innerEnd - 1);
        var topMembers = path.GetRange(cornerAt + 1, path.Count - cornerAt - 1);
        Place(corner.Component, tc, new Point(0, 0));
        var cOut = AnchorOf(corner.Component, corner.OutPort);
        var cIn = AnchorOf(corner.Component, corner.InPort);
        var (left, right) = Corners(bottomMembers, unit, leftFirst: true);
        var natural = cIn.Along(_margin).Y - (left is { } l ? Transform.Identity.Size(_symbol[l.Component]).Height / 2 : 0);
        var (drop, yBottom) = Bottom(unit, cOut.At.Y, natural, right?.Component ?? -1);
        var runs = new List<(Member From, List<Point> Points)>();

        if (!Rails(cOut, [cOut.At], corner, topMembers, unit, drop, yBottom, bottomMembers, [cIn.At, cIn.Along(_margin), new Point(cIn.At.X, yBottom)], left, right, runs))
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            return null;
        }

        if (Wildcard(exit.Component))
        {
            // The outlet leaves by a side the junction's other ports leave free, level and towards the inlet first, so the block presents its inlet and outlet to the parent together.
            var used = new HashSet<Direction>();

            for (var p = 0; p < _graph.Components[exit.Component].Ports.Length; p++)
            {
                if (p != exit.OutPort && _side.TryGetValue((exit.Component, p), out var side))
                {
                    used.Add(side);
                }
            }

            _side[(exit.Component, exit.OutPort)] = new[] { Direction.Left, Direction.Down, Direction.Right }.First(d => !used.Contains(d));
        }

        var members = path.Select(static m => m.Component).ToList();
        var entering = AnchorOf(entry.Component, entry.InPort);
        var leaving = AnchorOf(exit.Component, exit.OutPort);

        if (entering.Outward != Direction.Left || leaving.Outward == Direction.Up)
        {
            _groups.RemoveRange(mark, _groups.Count - mark);
            return null;
        }

        // The block is a group (A8), listed before the blocks it holds.
        _groups.Insert(mark, ("loop", members, false));
        return new Unit(members, entering, leaving, new Member(exit.Component, -1, exit.OutPort), members.Min(i => InnerOf(i).Y), runs);
    }

    /// <summary>
    /// How far the unit's inlet drops below the top rail and where the bottom rail lies: level with an outlet that
    /// faces left; else low enough for the unit, for the stub out of it, and for a corner junction under its outlet
    /// (C10). A unit entered from above with the other side the taller is centred on its side (C12).
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

    /// <summary>
    /// Lays the two rails of a ring and slides the unit into its right side: the top members rightwards from the
    /// top start, the bottom members rightwards from the bottom start against the flow, the unit at the longer
    /// rail's end, the corner junctions (C10) at the bottom corners. Appends the ring's runs.
    /// </summary>
    private bool Rails(PlacedAnchor topCursor, List<Point> topStart, Member topFirst, List<Member> topMembers, Unit unit, double drop, double yBottom, List<Member> bottomMembers, List<Point> bottomStart, Member? leftJunction, Member? rightJunction, List<(Member From, List<Point> Points)> runs)
    {
        var yTop = topCursor.At.Y;

        // The unit stands at a provisional place until the slide below; it is no obstacle to the rails' members.
        foreach (var i in unit.Members)
        {
            _placed[i] = false;
        }

        var cursor = topCursor;
        var pending = topStart;
        var previous = topFirst;

        foreach (var m in topMembers)
        {
            if (OnRail(cursor, m, m.InPort, m.OutPort) is not { } points)
            {
                return false;
            }

            pending.AddRange(points.Skip(1));
            runs.Add((previous, pending));
            cursor = AnchorOf(m.Component, m.OutPort);
            pending = [cursor.At];
            previous = m;
        }

        var topEnd = cursor;
        var topPending = pending;
        var topPrevious = previous;

        // The bottom rail, built left to right against the flow, each member placed by its outlet facing the left side.
        cursor = Anchor(bottomStart[^1], Direction.Right, Direction.Right);
        pending = bottomStart;
        var bottomRuns = new List<(Member From, List<Point> Points)>();
        var from = bottomMembers.Count - 1;

        if (leftJunction is { } lj)
        {
            // The junction nearest the left side takes the bottom-left corner: in from the rail, out up the left side; its free port is the block's outlet, facing out beside the inlet.
            Place(lj.Component, Transform.Identity, bottomStart[^1]);
            _side[(lj.Component, lj.OutPort)] = Direction.Up;
            _side[(lj.Component, lj.InPort)] = Direction.Right;
            pending.RemoveAt(pending.Count - 1);
            pending.Add(AnchorOf(lj.Component, lj.OutPort).At);
            bottomRuns.Add((lj, pending));
            cursor = AnchorOf(lj.Component, lj.InPort);
            pending = [cursor.At];
            from--;
        }

        for (var k = from; k >= 0; k--)
        {
            var m = bottomMembers[k];

            if (OnRail(cursor, m, m.OutPort, m.InPort) is not { } points)
            {
                return false;
            }

            pending.AddRange(points.Skip(1));
            bottomRuns.Add((m, pending));
            cursor = AnchorOf(m.Component, m.InPort);
            pending = [cursor.At];
        }

        // The unit: as far right as the longer rail needs, sliding right by tenths until every member clears what is placed (H2).
        var origin = Math.Max(topEnd.At.X, cursor.At.X);

        if (rightJunction is { } j0)
        {
            // The junction moves under the unit's outlet (C10), so its rail box is not in the way: the unit needs only to put that outlet at or beyond the junction's packed rail position.
            _placed[j0.Component] = false;
            origin = Math.Max(topEnd.At.X, _centre[j0.Component].X - (unit.Out.Along(_margin).X - unit.In.At.X) - _margin);
        }

        var dx = origin + _margin - Math.Min(unit.In.At.X, unit.Out.At.X);
        var dy = yTop - drop - unit.In.At.Y;

        while (unit.Members.Any(i => Clashes(i, _transform[i], _centre[i].Offset(dx, dy))))
        {
            dx += 0.1;
        }

        foreach (var i in unit.Members)
        {
            _centre[i] = _centre[i].Offset(dx, dy);
            _placed[i] = true;
        }

        if (rightJunction is { } j1)
        {
            _placed[j1.Component] = true;
        }

        var uIn = unit.In with { At = unit.In.At.Offset(dx, dy) };
        var uOut = unit.Out with { At = unit.Out.At.Offset(dx, dy) };

        foreach (var (walkFrom, points) in unit.Runs)
        {
            runs.Add((walkFrom, points.Select(p => p.Offset(dx, dy)).ToList()));
        }

        if (uIn.Outward == Direction.Up)
        {
            topPending.Add(new Point(uIn.At.X, yTop));
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

        return true;
    }

    /// <summary>
    /// The run of consecutive ring members around the consumer that an inner loop avoiding <paramref name="avoid"/>
    /// passes through (C11), and that inner loop in flow order from the consumer.
    /// </summary>
    /// <remarks>
    /// With no such inner loop the run is the consumer alone and the path is <see langword="null"/>. An inner
    /// member that is neither on the ring nor inline is left for sequential placement.
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
        // A level kind (D-113) faces a vertical pipe only as a last resort: the pipe turns level into it first.
        var level = _symbol[j].TransformClass == "level";
        var facing = admitted.Where(t => t.Arrangement == "default" && AnchorOffset(j, q, t) is { } a && a.Outward == d.Opposite && (!level || t.Rotation is 0 or 180)).ToList();

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
