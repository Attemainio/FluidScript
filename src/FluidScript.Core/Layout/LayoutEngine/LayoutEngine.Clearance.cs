using System.Collections.Immutable;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout;

internal sealed partial class LayoutEngine
{
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
        var at = Clear(j, Transform.Identity, anchor.At, d, delta, Reserve(j, q));
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
                var inner = Clear(j, turned[0], corner, along, new Point(-turn.X, -turn.Y), Reserve(j, q));
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
        var end = Clear(j, facing[0], anchor.At, d, new Point(-offset.X, -offset.Y), Reserve(j, q));
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
    /// <param name="minimum">The shortest pipe the run needs for the instruments on its inline points (<see cref="Reserve"/>).</param>
    /// <returns>The point the pipe ends at: the component's inner anchor.</returns>
    private Point Clear(int j, Transform t, Point origin, Direction along, Point delta, double minimum = 0)
    {
        var gap = Math.Max(_margin, minimum);
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
        var mine = new List<Box> { Box.Around(centre, w, h) };
        mine.AddRange(Provisional(j, t, centre));

        for (var i = 0; i < _n; i++)
        {
            if (!_placed[i] || i == j)
            {
                continue;
            }

            // A device's instruments are part of its footprint (C15, D-151): its bubbles keep the clearance as its box does.
            var theirs = new List<Box> { InnerOf(i) };

            if (!_inline[i])
            {
                theirs.AddRange(Provisional(i, _transform[i], _centre[i]));
            }

            foreach (var a in mine)
            {
                foreach (var b in theirs)
                {
                    if (b.Intersects(a.Grow(_margin)) || a.Intersects(b.Grow(_margin)))
                    {
                        return true;
                    }
                }
            }

            // A stub is the first margin of a pipe, and a pipe keeps out of a bubble's clearance (C15, D-151): the syntax
            // tour's TV2 otherwise stood with its outlet aimed at PID3 on TV3's stem, 1.3 off where a stub and a
            // clearance need 1.5, and the pipe had to climb over the bubble with three bends.
            if (!_inline[i] && (Crosses(Stubs(j, t, centre), theirs.Skip(1)) || Crosses(Stubs(i, _transform[i], _centre[i]), mine.Skip(1))))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The first margin of pipe out of every connected port of a component under a transform at a centre.</summary>
    private IEnumerable<(Point From, Point To)> Stubs(int j, Transform t, Point centre)
    {
        foreach (var link in _links)
        {
            foreach (var (c, p) in new[] { (link.From, link.FromPort), (link.To, link.ToPort) })
            {
                if (c == j && AnchorOffset(j, p, t) is { } anchor)
                {
                    var at = centre.Offset(anchor.Offset.X, anchor.Offset.Y);
                    yield return (at, at.Towards(anchor.Outward, _margin));
                }
            }
        }
    }

    /// <summary>Whether any segment runs through the clearance of any of the bubbles.</summary>
    private bool Crosses(IEnumerable<(Point From, Point To)> segments, IEnumerable<Box> bubbles)
    {
        var outers = bubbles.Select(b => b.Grow(_margin)).ToList();
        return outers.Count > 0 && segments.Any(s => outers.Any(o => Passes(o, s.From, s.To)));
    }
}
