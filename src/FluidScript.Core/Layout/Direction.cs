using System.Collections.Immutable;

namespace FluidScript.Core.Layout;

/// <summary>One of the four axis directions of the layout plane (<c>28</c> §1): <c>y</c> grows upward.</summary>
/// <param name="X">−1, 0 or 1.</param>
/// <param name="Y">−1, 0 or 1; exactly one of <paramref name="X"/> and <paramref name="Y"/> is non-zero.</param>
public readonly record struct Direction(int X, int Y)
{
    /// <summary><c>(1, 0)</c>, the layout's native direction (<c>28</c> §7).</summary>
    public static readonly Direction Right = new(1, 0);

    /// <summary><c>(0, 1)</c>.</summary>
    public static readonly Direction Up = new(0, 1);

    /// <summary><c>(−1, 0)</c>.</summary>
    public static readonly Direction Left = new(-1, 0);

    /// <summary><c>(0, −1)</c>.</summary>
    public static readonly Direction Down = new(0, -1);

    /// <summary>The four, counter-clockwise from <see cref="Right"/>.</summary>
    public static readonly ImmutableArray<Direction> All = [Right, Up, Left, Down];

    /// <summary>Gets the reverse direction.</summary>
    public Direction Opposite => new(-X, -Y);

    /// <summary>Gets this direction turned a quarter counter-clockwise.</summary>
    public Direction TurnLeft => new(-Y, X);

    /// <summary>Gets this direction turned a quarter clockwise.</summary>
    public Direction TurnRight => new(Y, -X);

    /// <summary>Gets whether the direction is along <c>x</c>.</summary>
    public bool Horizontal => Y == 0;

    /// <summary>Gets the index in <see cref="All"/>.</summary>
    public int Index => X == 1 ? 0 : Y == 1 ? 1 : X == -1 ? 2 : 3;

    /// <summary>Gets the direction as a point.</summary>
    public Point AsPoint => new(X, Y);

    /// <summary>The direction a vector points in.</summary>
    /// <param name="vector">Any vector; the dominant axis decides, <c>x</c> on a tie.</param>
    /// <returns>The direction, or <see langword="null"/> for the zero vector.</returns>
    public static Direction? Of(Point vector)
    {
        if (Math.Abs(vector.X) < 1e-9 && Math.Abs(vector.Y) < 1e-9)
        {
            return null;
        }

        return Math.Abs(vector.X) >= Math.Abs(vector.Y)
            ? (vector.X > 0 ? Right : Left)
            : (vector.Y > 0 ? Up : Down);
    }

    /// <summary>This direction rotated clockwise by quarter turns.</summary>
    /// <param name="quarterTurns">Any integer; negative turns counter-clockwise.</param>
    /// <returns>The rotated direction.</returns>
    public Direction Rotated(int quarterTurns)
    {
        var d = this;
        var turns = ((quarterTurns % 4) + 4) % 4;

        for (var k = 0; k < turns; k++)
        {
            d = d.TurnRight;
        }

        return d;
    }

    /// <summary>The dot product with another direction: 1 when equal, −1 when opposite, 0 when perpendicular.</summary>
    /// <param name="other">The other direction.</param>
    /// <returns>The dot product.</returns>
    public int Dot(Direction other) => (X * other.X) + (Y * other.Y);

    /// <summary>The coordinate of a point along this direction.</summary>
    /// <param name="point">The point.</param>
    /// <returns><c>p · d</c>.</returns>
    public double Along(Point point) => (X * point.X) + (Y * point.Y);

    /// <inheritdoc/>
    public override string ToString() => $"({X}, {Y})";
}
