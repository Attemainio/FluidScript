using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Language.Binding.Symbols;

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
