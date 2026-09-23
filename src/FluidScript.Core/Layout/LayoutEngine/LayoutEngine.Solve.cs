using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout;

internal sealed partial class LayoutEngine
{
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
                    || Tried(subject, "C18 unsourced ring", () => Closed(fragment, head));

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

        var sources = declared.Where(IsSource).OrderByDescending(i => ((HeatExchangerComponent)_graph.Components[i]).Power).ToList();

        if (sources.Count > 0)
        {
            Note(_graph.Components[sources[0]].Name, "head", $"the largest positive duty ({((HeatExchangerComponent)_graph.Components[sources[0]]).Power / 1000:0.##} kW) among {sources.Count} source(s) (C1, D-108)");
            return sources[0];
        }

        foreach (var i in declared)
        {
            if (_graph.Components[i] is NodeComponent { Boundary: BoundaryRole.Inlet })
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
        (_graph.Components[j] is PipeComponent || (Wildcard(j) && _graph.Components[j] is not NodeComponent { Boundary: BoundaryRole.Inlet or BoundaryRole.Outlet })) && _links.Count(l => l.From == j || l.To == j) == 2;

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
        if (!_loop[i] || _graph.Components[i] is not HeatExchangerComponent || anchor.Outward.Horizontal)
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
                var root = _graph.Components[b] is NodeComponent { Boundary: not BoundaryRole.Interior } ? -3 : Root(walk);
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
}
