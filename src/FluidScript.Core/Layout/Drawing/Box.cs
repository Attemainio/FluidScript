namespace FluidScript.Core.Layout.Drawing;

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
