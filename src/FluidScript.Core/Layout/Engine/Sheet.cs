using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout.Engine;

/// <summary>
/// The drawing as it is composed (<c>28</c> E3): where each element stands and in which transform, the side each of a
/// node's ports takes, and each run's polyline with its inline points cut into it.
/// </summary>
/// <remarks>
/// One sheet per scene. <see cref="Placed"/> is the canvas: what a clearance test sees. A structure laid out on a clean
/// canvas of its own -- a fragment (C17), a block (C11) -- clears it, lays itself out, and restores it, and its members
/// come back placed at a provisional origin for the parent to move as one.
/// </remarks>
internal sealed partial class Sheet
{
    private const double Eps = 1e-9;

    private readonly ImmutableArray<Point>?[] _runs;

    /// <summary>Creates an empty sheet.</summary>
    /// <param name="view">The circuit view.</param>
    /// <param name="margin">The clearance, world units.</param>
    public Sheet(CircuitView view, double margin)
    {
        View = view;
        Margin = margin;
        Centre = new Point[view.Count];
        Transform = new Transform[view.Count];
        Array.Fill(Transform, Layout.Transform.Identity);
        Placed = new bool[view.Count];
        Stood = new bool[view.Count];
        _runs = new ImmutableArray<Point>?[view.Runs.Length];
    }

    /// <summary>Gets the circuit view.</summary>
    public CircuitView View { get; }

    /// <summary>Gets the clearance, world units.</summary>
    public double Margin { get; }

    /// <summary>Gets each element's centre, world units, y up.</summary>
    public Point[] Centre { get; }

    /// <summary>Gets each element's transform.</summary>
    public Transform[] Transform { get; }

    /// <summary>Gets which elements the current canvas holds: what a clearance test sees.</summary>
    public bool[] Placed { get; }

    /// <summary>Gets which elements have been given a place at all, on any canvas.</summary>
    public bool[] Stood { get; }

    /// <summary>Gets the side each port of a node takes, keyed by element and port.</summary>
    public Dictionary<(int Component, int Port), Direction> Side { get; } = [];

    /// <summary>Gets the decisions, in the order they were made (<c>C-107</c>).</summary>
    public List<PlacementNote> Trace { get; } = [];

    /// <summary>Gets the groups laid out as one object (A8): each ring and block, outer before inner, with whether it is the ring that holds the source.</summary>
    public List<(List<int> Members, bool Top)> Groups { get; } = [];

    /// <summary>A run's polyline from its start to its end, or null before it is laid.</summary>
    public ImmutableArray<Point>? RunPoints(int run) => _runs[run];

    // ---- geometry -------------------------------------------------------------------------------------------------

    /// <summary>An element's box size under its transform; an inline point has none.</summary>
    public (double Width, double Height) SizeOf(int c) => View.IsInline(c) ? (0, 0) : Transform[c].Size(View.Symbols[c]);

    /// <summary>An element's inner box where it stands.</summary>
    public Box InnerOf(int c)
    {
        var (w, h) = SizeOf(c);
        return Box.Around(Centre[c], w, h);
    }

    /// <summary>A named or indexed port's anchor relative to the centre under a transform, and its outward direction; null for a node's port.</summary>
    public (Point Offset, Direction Outward)? AnchorOffset(int c, int port, Transform t)
    {
        var flow = View.Graph.Components[c];
        var symbol = View.Symbols[c];
        var name = flow.Ports[port].Name;

        if (t.Anchors(symbol).TryGetValue(name, out var anchor) && anchor.Direction is { } direction)
        {
            return (t.Apply(anchor.At[0], anchor.At[1]), t.Apply(Direction.Of(new Point(direction[0], direction[1])) ?? Direction.Right));
        }

        foreach (var rule in symbol.IndexedPortAnchors ?? [])
        {
            if (name.StartsWith(rule.Prefix, StringComparison.Ordinal) && flow is TankComponent tank)
            {
                var box = symbol.ViewBox;
                var x = rule.Side == "west" ? box[0] : box[0] + box[2];
                var y = box[1] + (tank.PortLevels[port] * box[3]);
                return (t.Apply(x, y), t.Apply(Direction.Of(new Point(rule.Direction[0], rule.Direction[1])) ?? Direction.Right));
            }
        }

        return null;
    }

    /// <summary>A port's placed anchor: on an inline point, the point, facing along its run; on a node, the middle of the side its port takes; else the symbol's anchor.</summary>
    /// <remarks>A node's port with no side yet faces right; the run builder assigns every side before a run is drawn (E4).</remarks>
    public PlacedAnchor AnchorOf(int c, int port)
    {
        if (View.IsInline(c))
        {
            var along = Side.GetValueOrDefault((c, port), Direction.Right);
            return Anchor(Centre[c], along, FlowOf(c, port, along));
        }

        if (AnchorOffset(c, port, Transform[c]) is { } named)
        {
            return Anchor(Centre[c].Offset(named.Offset.X, named.Offset.Y), named.Outward, FlowOf(c, port, named.Outward));
        }

        var side = Side.GetValueOrDefault((c, port), Direction.Right);
        var (w, h) = SizeOf(c);
        return Anchor(Centre[c].Towards(side, side.Horizontal ? w / 2 : h / 2), side, FlowOf(c, port, side));
    }

    /// <summary>A port's outward direction under a transform, or null for a node's port.</summary>
    public Direction? Outward(int c, int port, Transform t) => AnchorOffset(c, port, t)?.Outward;

    /// <summary>The flow vector at a port (A3): inward where the fluid enters, outward where it leaves.</summary>
    public Direction FlowOf(int c, int port, Direction outward) => View.Enters(c, port) ? outward.Opposite : outward;

    /// <summary>A placed anchor.</summary>
    public static PlacedAnchor Anchor(Point at, Direction outward, Direction flow) => new(at, outward.AsPoint, flow);

    /// <summary>
    /// The transforms a kind admits (A4, <c>D-108</c>) in tie-break order (A9, <c>D-109</c>): the default arrangement
    /// first, a level kind's quarter turns last, then the smaller turn, then unmirrored before mirrored.
    /// </summary>
    public IEnumerable<Transform> Admitted(int c)
    {
        var symbol = View.Symbols[c];
        var admitted = symbol.TransformClass switch
        {
            "standing" => Layout.Transform.All(symbol).Where(static t => t.Rotation is 0 or 180),
            "upright" => Layout.Transform.All(symbol).Where(static t => t.Rotation == 0),
            _ => Layout.Transform.All(symbol),
        };

        var level = symbol.TransformClass == "level";
        return admitted.OrderBy(static t => t.Arrangement != "default").ThenBy(t => level && t.Rotation is 90 or 270).ThenBy(static t => t.Rotation).ThenBy(static t => t.Mirrored);
    }

    /// <summary>H10 as a sort key for a member placed from a pipe at port <paramref name="q"/>: 0 when a port the flow leaves by faces right under <paramref name="t"/>, else 1.</summary>
    public int Onward(int c, int q, Transform t)
    {
        foreach (var p in View.Connected(c))
        {
            if (p != q && !View.Enters(c, p) && Outward(c, p, t) == Direction.Right)
            {
                return 0;
            }
        }

        return 1;
    }

    // ---- placing --------------------------------------------------------------------------------------------------

    /// <summary>Stands an element at a centre in a transform on the current canvas, and notes the rule that put it there.</summary>
    public void Place(int c, Transform t, Point centre, string rule, string reason)
    {
        Transform[c] = t;
        Centre[c] = centre;
        Placed[c] = true;
        Stood[c] = true;
        Note(View.Name(c), rule, $"{reason}; centre ({centre.X:0.##}, {centre.Y:0.##}), {Describe(t)}");
    }

    /// <summary>Records one decision for the layout report (<c>C-107</c>).</summary>
    public void Note(string subject, string rule, string reason) => Trace.Add(new PlacementNote(subject, rule, reason));

    /// <summary>Moves elements and their laid runs as one (A8).</summary>
    public void Move(IEnumerable<int> members, IEnumerable<int> runs, double dx, double dy)
    {
        foreach (var c in members)
        {
            Centre[c] = Centre[c].Offset(dx, dy);
        }

        foreach (var r in runs)
        {
            if (_runs[r] is { } points)
            {
                _runs[r] = [.. points.Select(p => p.Offset(dx, dy))];
            }
        }
    }

    /// <summary>A clean canvas: nothing placed so far is an obstacle until the returned scope is disposed, when the canvas is put back as it was and what was placed meanwhile is added to it.</summary>
    public IDisposable Canvas()
    {
        var saved = (bool[])Placed.Clone();
        Array.Clear(Placed);
        return new Restore(() =>
        {
            for (var c = 0; c < Placed.Length; c++)
            {
                Placed[c] |= saved[c];
            }
        });
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    public static string Name(Direction d) =>
        d == Direction.Right ? "rightwards" : d == Direction.Left ? "leftwards" : d == Direction.Up ? "upwards" : d == Direction.Down ? "downwards" : d.ToString();

    private static string Describe(Transform t) => $"{t.Arrangement} rot {t.Rotation}{(t.Mirrored ? " mirrored" : string.Empty)}";

    // ---- runs (A5) ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Lays a run: its polyline from start to end, normalised, with its inline points cut into it at even fractions
    /// of its longest segment (A5), each turned along the run.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="points">Its polyline, from the run's start to its end.</param>
    public void Lay(Run run, IReadOnlyList<Point> points)
    {
        var line = Normalise(points);
        _runs[run.Index] = line;
        var pieces = Pieces(line, run.Inline.Length);

        for (var t = 0; t < run.Inline.Length; t++)
        {
            var (element, near, far) = run.Inline[t];
            var first = pieces[t];
            var second = pieces[t + 1];
            Centre[element] = first[^1];
            Placed[element] = true;
            Stood[element] = true;
            Side[(element, near)] = Direction.Of(first[^2].Offset(-first[^1].X, -first[^1].Y)) ?? Direction.Left;
            Side[(element, far)] = Direction.Of(second[1].Offset(-second[0].X, -second[0].Y)) ?? Direction.Right;
        }
    }

    /// <summary>Lays a run given from its end back to its start.</summary>
    public void LayBackwards(Run run, IReadOnlyList<Point> points) => Lay(run, [.. points.Reverse()]);

    /// <summary>The pieces of a laid run, one per link from start to end, consecutive ones sharing their cut.</summary>
    public List<ImmutableArray<Point>> Pieces(int run) => Pieces(_runs[run]!.Value, View.Runs[run].Inline.Length);

    /// <summary>A polyline cut into one more piece than <paramref name="count"/>, at even fractions of its longest segment (A5).</summary>
    public static List<ImmutableArray<Point>> Pieces(ImmutableArray<Point> points, int count)
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

    /// <summary>A polyline without duplicate, collinear or backtracking points (A7).</summary>
    public static ImmutableArray<Point> Normalise(IReadOnlyList<Point> points)
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
