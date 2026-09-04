using FluidScript.Core.Solvers;

namespace FluidScript.Core.Components;

/// <summary>Blends that keep a residual differentiable where the physics switches.</summary>
/// <remarks>
/// <para>
/// <strong>Every switch in a residual is a cliff in the Jacobian, and Newton does not survive one.</strong>
/// Upwinding at zero flow, and the laminar-to-turbulent change in a pipe, are both places where the
/// correct physics is written as two cases. Selecting between them with an <c>if</c> gives a residual
/// that is continuous in value and discontinuous in slope, and a numerical Jacobian differences
/// straight across the join and returns a derivative belonging to neither branch.
/// </para>
/// <para>
/// The band widths are <c>36</c>'s, not this file's. It owns <c>upwind.smoothing_band</c> and the
/// transition Reynolds numbers; these are the functions that apply them.
/// </para>
/// </remarks>
internal static class Smoothing
{
    /// <summary>The signed flow over which upwinding blends between the two sides.</summary>
    /// <value>
    /// 1 × 10⁻³ kg/s, positive into the node, read from <see cref="Tolerances.UpwindSmoothingBand"/>
    /// rather than written here: it is <c>36</c>'s <c>upwind.smoothing_band</c>, and a second copy of
    /// a table's number is what <c>S-6</c> records. Deliberately wider than the zero-flow tolerance —
    /// detecting zero flow wants a tight threshold, and smoothing a derivative wants a band a Newton
    /// step can actually resolve.
    /// </value>
    public const double UpwindBand = Tolerances.UpwindSmoothingBand;

    /// <summary>Hermite smoothstep, clamped.</summary>
    /// <param name="t">The position across the band.</param>
    /// <returns>0 at or below 0, 1 at or above 1, and 3t² − 2t³ between.</returns>
    /// <remarks>
    /// Its derivative is zero at both ends, which is the property that matters: a blend built on it
    /// meets each branch with that branch's own slope, so the join is C¹ rather than merely continuous.
    /// </remarks>
    public static double SmoothStep(double t)
    {
        var clamped = Math.Clamp(t, 0, 1);

        return clamped * clamped * (3 - (2 * clamped));
    }

    /// <summary>How strongly the flow runs in the positive direction, blended across zero.</summary>
    /// <param name="massFlow">kg/s, positive in the direction the component's ports are ordered.</param>
    /// <returns>
    /// 1 at or above <see cref="UpwindBand"/>, 0 at or below its negative, and C¹ between. At rest it
    /// is 0.5, which is not a physical claim: it is what keeps the split differentiable through a
    /// reversal, and every quantity it divides is itself zero there.
    /// </returns>
    /// <remarks>
    /// <strong>Which side of a component an energy injection lands on is a direction question, and the
    /// direction is solved.</strong> A heat exchanger heats whichever node it discharges into, and a
    /// rising pipe takes potential energy out of whichever node it discharges into. Choosing with an
    /// <c>if</c> would make the system's contents depend on a solved sign; this makes the share depend
    /// on it smoothly instead (<c>D-69</c>).
    /// </remarks>
    public static double ForwardShare(double massFlow) =>
        SmoothStep((massFlow + UpwindBand) / (2 * UpwindBand));

    /// <summary>Chooses the enthalpy a stream carries, smoothly across zero flow.</summary>
    /// <param name="massFlow">kg/s, positive into the component.</param>
    /// <param name="upstream">J/kg, the enthalpy arriving when the flow is inward.</param>
    /// <param name="own">J/kg, the component's own enthalpy, which leaves when the flow is outward.</param>
    /// <returns>J/kg.</returns>
    /// <remarks>
    /// Exactly <paramref name="upstream"/> at or above <see cref="UpwindBand"/> and exactly
    /// <paramref name="own"/> at or below its negative, so nothing is approximated outside the band —
    /// which is everywhere a converged solution normally sits.
    /// </remarks>
    public static double Upwind(double massFlow, double upstream, double own)
    {
        var blend = ForwardShare(massFlow);

        return (blend * upstream) + ((1 - blend) * own);
    }

    /// <summary>Blends two correlations across a range of a driving variable.</summary>
    /// <param name="value">Where the state sits.</param>
    /// <param name="from">Where the blend starts, giving <paramref name="low"/>.</param>
    /// <param name="to">Where it ends, giving <paramref name="high"/>.</param>
    /// <param name="low">The correlation that holds below <paramref name="from"/>.</param>
    /// <param name="high">The correlation that holds above <paramref name="to"/>.</param>
    /// <returns>The blended value.</returns>
    public static double Blend(double value, double from, double to, double low, double high)
    {
        var blend = SmoothStep((value - from) / (to - from));

        return (blend * high) + ((1 - blend) * low);
    }
}
