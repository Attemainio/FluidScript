using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout;

internal sealed partial class LayoutEngine
{
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

            _routes.Add((k, new Route(link.Id, "pipe", LayerOf(k), points, [])));
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
            if (IsSource(i) || _graph.Components[i] is NodeComponent { Boundary: BoundaryRole.Inlet })
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
        if (_graph.Components[i] is not HeatExchangerComponent h)
        {
            return false;
        }

        return h.Ports[port].Name.EndsWith('2') ? IsSource(i) : Duty(i) is not null;
    }

    /// <summary>Queues the ports the stream leaves <paramref name="i"/> by, having entered at <paramref name="except"/> (-1 at a source): an exchanger's outlet on the same side, any other component's outlets.</summary>
    private void Leaving(int i, int except, Queue<(int Component, int Port)> queue)
    {
        var ports = _graph.Components[i].Ports;
        var exchanger = _graph.Components[i] is HeatExchangerComponent;
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
                if (Segments.Crossing(a[i - 1], a[i], b[j - 1], b[j], Eps) is { } crossing)
                {
                    result.Add(crossing);
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
}
