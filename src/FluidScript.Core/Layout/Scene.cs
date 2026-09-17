using System.Collections.Immutable;

namespace FluidScript.Core.Layout;

/// <summary>An axis-aligned box in world units, <c>y</c> up (<c>28</c> §1).</summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The bottom edge.</param>
/// <param name="Width">The width; never negative.</param>
/// <param name="Height">The height; never negative.</param>
public readonly record struct Box(double X, double Y, double Width, double Height)
{
    /// <summary>Gets the right edge.</summary>
    public double Right => X + Width;

    /// <summary>Gets the top edge.</summary>
    public double Top => Y + Height;

    /// <summary>Gets the bottom edge, the same as <see cref="Y"/>.</summary>
    public double Bottom => Y;

    /// <summary>Gets the centre.</summary>
    public Point Centre => new(X + (Width / 2), Y + (Height / 2));

    /// <summary>A box of this size centred at a point.</summary>
    /// <param name="centre">The centre.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The box.</returns>
    public static Box Around(Point centre, double width, double height) =>
        new(centre.X - (width / 2), centre.Y - (height / 2), width, height);

    /// <summary>The smallest box holding every point.</summary>
    /// <param name="points">At least one point.</param>
    /// <returns>The bounds.</returns>
    public static Box Of(IEnumerable<Point> points)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;

        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        return double.IsInfinity(minX) ? new Box(0, 0, 0, 0) : new Box(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>This box grown by <paramref name="margin"/> on every side.</summary>
    /// <param name="margin">The margin, world units.</param>
    /// <returns>The grown box.</returns>
    public Box Grow(double margin) => new(X - margin, Y - margin, Width + (2 * margin), Height + (2 * margin));

    /// <summary>This box moved.</summary>
    /// <param name="dx">East.</param>
    /// <param name="dy">North.</param>
    /// <returns>The moved box.</returns>
    public Box Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>Whether two boxes share interior area; touching edges do not count.</summary>
    /// <param name="other">The other box.</param>
    /// <returns><see langword="true"/> when the interiors intersect.</returns>
    public bool Intersects(Box other) =>
        X < other.Right - 1e-9 && other.X < Right - 1e-9 && Y < other.Top - 1e-9 && other.Y < Top - 1e-9;

    /// <summary>The clearance between two boxes (<c>28</c> §23): the largest axis gap, negative when they overlap.</summary>
    /// <param name="other">The other box.</param>
    /// <returns>Two boxes are at least <c>g</c> apart exactly when this is at least <c>g</c>.</returns>
    public double GapTo(Box other) =>
        Math.Max(Math.Max(other.X - Right, X - other.Right), Math.Max(other.Y - Top, Y - other.Top));

    /// <summary>Whether a point lies strictly inside.</summary>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> when inside, not on the edge.</returns>
    public bool ContainsInterior(Point point) =>
        point.X > X + 1e-9 && point.X < Right - 1e-9 && point.Y > Y + 1e-9 && point.Y < Top - 1e-9;

    /// <summary>The smallest box holding both.</summary>
    /// <param name="other">The other box.</param>
    /// <returns>The union's bounds.</returns>
    public Box Union(Box other)
    {
        var x = Math.Min(X, other.X);
        var y = Math.Min(Y, other.Y);
        return new Box(x, y, Math.Max(Right, other.Right) - x, Math.Max(Top, other.Top) - y);
    }
}

/// <summary>A point in world units, <c>y</c> up.</summary>
/// <param name="X">Horizontal, growing east.</param>
/// <param name="Y">Vertical, growing north.</param>
public readonly record struct Point(double X, double Y)
{
    /// <summary>This point moved.</summary>
    /// <param name="dx">East.</param>
    /// <param name="dy">North.</param>
    /// <returns>The moved point.</returns>
    public Point Offset(double dx, double dy) => new(X + dx, Y + dy);

    /// <summary>This point moved along a direction.</summary>
    /// <param name="direction">The direction.</param>
    /// <param name="distance">World units; negative moves the other way.</param>
    /// <returns>The moved point.</returns>
    public Point Towards(Direction direction, double distance) => new(X + (direction.X * distance), Y + (direction.Y * distance));

    /// <summary>The Manhattan distance to another point.</summary>
    /// <param name="other">The other point.</param>
    /// <returns><c>|Δx| + |Δy|</c>.</returns>
    public double ManhattanTo(Point other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
}

/// <summary>Where a port meets its placed symbol, in world units, which way a pipe leaves it and which way the fluid flows (<c>28</c> §2, §5).</summary>
/// <param name="At">The inner anchor: the point on the inner box's edge.</param>
/// <param name="Direction">The outward unit vector, one of the four axis directions; the stub to the outer anchor runs along it.</param>
/// <param name="Flow">The flow vector: <see cref="Direction"/> for an outlet, its opposite for an inlet.</param>
public readonly record struct PlacedAnchor(Point At, Point Direction, Direction Flow)
{

    /// <summary>The point <paramref name="distance"/> along the outward direction; at the margin it is the outer anchor.</summary>
    /// <param name="distance">World units.</param>
    /// <returns>The point.</returns>
    public Point Along(double distance) => At.Offset(Direction.X * distance, Direction.Y * distance);

    /// <summary>Gets the outward direction as a <see cref="Layout.Direction"/>.</summary>
    public Direction Outward => Layout.Direction.Of(Direction) ?? Layout.Direction.Right;
}

/// <summary>One component's place in the diagram (<c>D-103</c>).</summary>
/// <remarks>
/// <see cref="Inner"/> is the symbol's box scaled, rotated and given an arrangement; <see cref="Outer"/> is
/// the inner grown by the margin. The invariant the solver holds (<c>28</c> §4): every two inner boxes are at
/// least the clearance apart; outer boxes may overlap, and that is counted, not rejected.
/// </remarks>
public sealed record Placement
{
    /// <summary>The component's name.</summary>
    public required string ComponentId { get; init; }

    /// <summary>The symbol definition drawn inside <see cref="Inner"/>.</summary>
    public required string SymbolId { get; init; }

    /// <summary>The symbol's box after placement.</summary>
    public required Box Inner { get; init; }

    /// <summary>The inner box grown by the margin: the clearance nothing else may enter.</summary>
    public required Box Outer { get; init; }

    /// <summary>The quarter turn applied, clockwise degrees: 0, 90, 180 or 270.</summary>
    public required int Rotation { get; init; }

    /// <summary>Whether the symbol was mirrored left-to-right before rotating.</summary>
    public required bool Mirrored { get; init; }

    /// <summary><c>default</c> or one of the symbol's alternative arrangements (<c>D-102</c>).</summary>
    public required string Arrangement { get; init; }

    /// <summary>Every port's anchor after placement, by port name; a node's ports each get their own entry.</summary>
    public required ImmutableSortedDictionary<string, PlacedAnchor> Anchors { get; init; }

    /// <summary>Where the label sits.</summary>
    public required Point LabelAt { get; init; }

    /// <summary><c>computed</c>; <c>pinned</c> is reserved for a placement the script states (<c>D-103</c>).</summary>
    public required string Source { get; init; }


    /// <summary>The <see cref="LayoutGroup.Id"/> of the group that placed this component, or <see langword="null"/> when it was placed on its own.</summary>
    public string? Group { get; init; }

    /// <summary>Whether this is an inline element (<c>D-105</c>): a pipe or a two-port node drawn as a point on its run, with no box and no clearance of its own.</summary>
    public bool IsInline => Inner.Width <= 0 && Inner.Height <= 0;
}

/// <summary>One connection's path, in world units.</summary>
/// <param name="ConnectionId"><c>c{n}</c>, or a non-flow element's id for a signal line.</param>
/// <param name="Kind"><c>pipe</c> for a flow connection, <c>signal</c> for an instrument's leader line.</param>
/// <param name="Points">The polyline, orthogonal segment by segment and normalised (<c>28</c> §20); the first and last points are the inner anchors.</param>
/// <param name="Layer">The draw order (<c>28</c> C16): <c>supply</c> in front, <c>return</c> behind it, <c>signal</c> behind everything.</param>
/// <param name="Hops">Where this route passes behind another it crosses; the picture breaks this route around each.</param>
public sealed record Route(string ConnectionId, string Kind, string Layer, ImmutableArray<Point> Points, ImmutableArray<Point> Hops)
{
    /// <summary>Gets the total length, world units.</summary>
    public double Length
    {
        get
        {
            var length = 0.0;
            for (var i = 1; i < Points.Length; i++)
            {
                length += Points[i - 1].ManhattanTo(Points[i]);
            }

            return length;
        }
    }

    /// <summary>Gets the interior points where the direction changes.</summary>
    public IEnumerable<Point> Bends
    {
        get
        {
            for (var i = 1; i + 1 < Points.Length; i++)
            {
                var a = Points[i - 1];
                var b = Points[i];
                var c = Points[i + 1];
                var horizontalIn = Math.Abs(b.Y - a.Y) < 1e-9;
                var horizontalOut = Math.Abs(c.Y - b.Y) < 1e-9;

                if (horizontalIn != horizontalOut)
                {
                    yield return b;
                }
            }
        }
    }
}

/// <summary>A group the solver laid out as one object (<c>28</c> §24–25): its members were placed together and the parent only moved them.</summary>
/// <param name="Id">A stable name: <c>loop-1</c>, <c>header-1</c>, <c>chain-3</c>.</param>
/// <param name="Kind"><c>loop</c>, <c>header</c>, <c>chain</c> or <c>fragment</c>.</param>
/// <param name="Orientation"><c>cw</c> or <c>ccw</c> for a loop; the flow direction for a chain; empty otherwise.</param>
/// <param name="Members">The component ids, in the group's own order.</param>
/// <param name="Bounds">The union of the members' inner boxes and the group's own routes.</param>
public sealed record LayoutGroup(string Id, string Kind, string Orientation, ImmutableArray<string> Members, Box Bounds);

/// <summary>The whole drawing: what the renderer draws and the exporter writes (<c>D-103</c>).</summary>
public sealed record Scene
{
    /// <summary>Every component's placement, in <c>hints.Order</c> then non-flow elements.</summary>
    public required ImmutableArray<Placement> Placements { get; init; }

    /// <summary>Every connection's route, in connection order, then signal lines.</summary>
    public required ImmutableArray<Route> Routes { get; init; }

    /// <summary>The bounds of everything, outer boxes and routes included.</summary>
    public required Box Extent { get; init; }

    /// <summary>The margin every outer box was grown by, world units.</summary>
    public required double Margin { get; init; }

    /// <summary>The groups the solver laid out as objects, outermost first; empty for a scene with none.</summary>
    public ImmutableArray<LayoutGroup> Groups { get; init; } = [];
}

