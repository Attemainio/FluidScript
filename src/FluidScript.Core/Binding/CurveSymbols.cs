using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Units;

namespace FluidScript.Core.Binding;

/// <summary>One driver a curve can depend on by name (<c>D-59</c>).</summary>
/// <param name="CanonicalName">The registered name, such as <c>tout</c>.</param>
/// <param name="Dimension">
/// The dimension a <c>design</c> value for this driver is read in, or <see langword="null"/> when the
/// language names none for the quantity. A design value under a dimensionful role is compared in that
/// dimension's canonical unit, which is what makes <c>design tout=-26</c> and <c>design tout=-26 C</c>
/// the same point on a curve whose own table is bare.
/// </param>
/// <remarks>
/// Registry data, not a closed set in the language: adding a driver is a registry change, never a
/// grammar change — the same trade <see cref="CircuitRole"/> makes.
/// </remarks>
public sealed record ScheduleRole(string CanonicalName, Dimension? Dimension);

/// <summary>One row of a curve: an <c>x</c> and the <c>y</c> it maps to.</summary>
/// <param name="X">
/// The driver's value. Bare, and in the driver role's canonical unit where it has one; Unix seconds
/// for a curve of <c>time</c>.
/// </param>
/// <param name="Y">The value the curve yields there. Always bare — no curve has a dimension.</param>
public readonly record struct CurvePoint(double X, double Y);

/// <summary>What a curve's second position turned out to name.</summary>
public enum CurveDriverKind
{
    /// <summary>Nothing resolved it. The header omitted a driver, or it named nothing (<c>FS1527</c>).</summary>
    Unresolved = 0,

    /// <summary>The clock. Every <c>x</c> in the table is a timestamp or a count of Unix seconds.</summary>
    Time,

    /// <summary>Another curve, whose <c>y</c> is this curve's <c>x</c>.</summary>
    Curve,

    /// <summary>A registered driver, supplied by <c>design</c> (<c>D-59</c>).</summary>
    Role,

    /// <summary>An unregistered name a <c>design</c> line gives a value to.</summary>
    /// <remarks>
    /// <c>D-59</c> is explicit that a plant is full of drivers nobody registered. A name with a design
    /// value behind it is one of those, and works; a name with nothing behind it anywhere is
    /// <c>FS1527</c>.
    /// </remarks>
    DesignOnly,
}

/// <summary>A named table of <c>x y</c> pairs, linearly interpolated (<c>D-57</c>).</summary>
/// <remarks>
/// <para>
/// <strong>Both columns are bare, and the curve has no dimension.</strong> <c>heating</c> maps −26 to
/// 50; what 50 <em>is</em> comes from the consumer, because <c>D-14</c>'s bare-number rule reinterprets
/// a bare result in the target parameter's canonical unit at assignment. That is what lets one curve
/// drive a power, a percentage and a temperature without being told which.
/// </para>
/// <para>
/// <strong>The ends clamp unless the curve says <c>extrapolated</c>.</strong> Clamping is the default
/// because it is the answer that cannot produce a nonsense number: continuing a heating curve's slope
/// to −60 °C invents a duty from two points nobody validated there.
/// </para>
/// </remarks>
public sealed record CurveSymbol
{
    /// <summary>Gets the curve's name, which is what an expression references.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the driver as written, or <see langword="null"/> when the header omitted one.</summary>
    public string? DriverName { get; init; }

    /// <summary>Gets what the driver resolved to.</summary>
    public required CurveDriverKind DriverKind { get; init; }

    /// <summary>Gets the registered driver, when the name resolved to one.</summary>
    public ScheduleRole? DriverRole { get; init; }

    /// <summary>Gets whether the ends continue the slope rather than holding.</summary>
    /// <value><see langword="true"/> only when the header wrote <c>extrapolated</c>.</value>
    public required bool IsExtrapolated { get; init; }

    /// <summary>Gets the .NET format each <c>x</c> is read with, from <c>format=</c> (<c>D-60</c>).</summary>
    /// <value>
    /// <see langword="null"/> when the header states none, in which case a timestamp is ISO 8601 or a
    /// count of Unix seconds. Culture-inferred layouts are deliberately not supported: a format that
    /// depends on the reader's locale means the same file means different things on two machines.
    /// </value>
    public string? TimeFormat { get; init; }

    /// <summary>Gets the table, sorted by <see cref="CurvePoint.X"/>.</summary>
    /// <value>
    /// Rows written out of order are sorted here; two rows at one <c>x</c> are <c>FS1529</c> and the
    /// later one wins, because a step is a legitimate thing to write. A row whose columns did not
    /// parse is absent, so this can be shorter than the section the user wrote.
    /// </value>
    public required ImmutableArray<CurvePoint> Points { get; init; }

    /// <summary>Gets where the header sits in the source.</summary>
    public required TextSpan DeclarationSpan { get; init; }

    /// <summary>Reads the curve at one point on its driver.</summary>
    /// <param name="x">The driver's value, in the same terms as <see cref="Points"/>.</param>
    /// <returns>
    /// The interpolated <c>y</c>, or <see cref="double.NaN"/> when the table is empty — which only
    /// happens for a curve that already reported <c>FS1530</c>.
    /// </returns>
    /// <remarks>
    /// Linear between the bracketing rows, and beyond the ends either held or continued on the slope
    /// of the outermost pair. A one-row table is constant in both directions whatever the end rule
    /// says, since one point has no slope.
    /// </remarks>
    public double Evaluate(double x)
    {
        if (Points.IsEmpty)
        {
            return double.NaN;
        }

        if (Points.Length == 1)
        {
            return Points[0].Y;
        }

        if (x <= Points[0].X)
        {
            return x == Points[0].X || !IsExtrapolated
                ? Points[0].Y
                : Extend(Points[1], Points[0], x);
        }

        if (x >= Points[^1].X)
        {
            return x == Points[^1].X || !IsExtrapolated
                ? Points[^1].Y
                : Extend(Points[^2], Points[^1], x);
        }

        for (var i = 1; i < Points.Length; i++)
        {
            if (x > Points[i].X)
            {
                continue;
            }

            return Extend(Points[i - 1], Points[i], x);
        }

        return Points[^1].Y;
    }

    /// <summary>The line through two rows, read at <paramref name="x"/>.</summary>
    /// <remarks>
    /// Two rows at the same <c>x</c> cannot reach here — <c>FS1529</c> keeps only the later — so the
    /// denominator is never zero.
    /// </remarks>
    private static double Extend(CurvePoint from, CurvePoint to, double x) =>
        from.Y + ((to.Y - from.Y) * ((x - from.X) / (to.X - from.X)));
}

/// <summary>One driver's value at the design condition (<c>D-58</c>).</summary>
/// <param name="WrittenName">The driver as the <c>design</c> line spelled it.</param>
/// <param name="Role">The registered driver it resolved to, or <see langword="null"/>.</param>
/// <param name="Value">
/// The quantity, in SI, or <see langword="null"/> when the expression could not be evaluated.
/// </param>
/// <param name="Number">
/// The number a curve is read at: <paramref name="Value"/> in the role's canonical unit, or its bare
/// SI value when the role names no dimension. This is what makes <c>design tout=-26</c> and
/// <c>design tout=-26 C</c> pick the same row of a table written in degrees.
/// </param>
/// <param name="Span">Where the assignment sits in the source.</param>
public sealed record DesignValue(
    string WrittenName,
    ScheduleRole? Role,
    Quantity? Value,
    double? Number,
    TextSpan Span);
