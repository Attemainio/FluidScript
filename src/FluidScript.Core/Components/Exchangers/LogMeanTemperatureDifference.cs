namespace FluidScript.Core.Components.Exchangers;

/// <summary>The log-mean temperature difference: the second, independent route to an exchanger's conductance.</summary>
/// <remarks>
/// <para>
/// <strong>Reported, never a residual</strong> (<c>D-17</c>, <c>22</c>). <c>Q = UA · LMTD</c> needs all
/// four terminal temperatures and divides by <c>ln(ΔT₁/ΔT₂)</c>, which is <c>0/0</c> when the two end
/// differences are equal -- balanced counterflow, a common design point. That is why the solver runs
/// on <see cref="Effectiveness"/> and this is computed afterwards from the solved terminals.
/// </para>
/// <para>
/// <strong>It shares no code with the ε-NTU route, and that is what makes it a check.</strong> For
/// counterflow the two are algebraically identical, so an implementation where <c>Q / LMTD</c> and
/// <c>NTU · Cmin</c> disagree by more than rounding has a sign or a <c>Cmin</c> error in one of them.
/// The substation's 12 071 W/K by both is the test (<c>01</c>).
/// </para>
/// </remarks>
public static class LogMeanTemperatureDifference
{
    /// <summary>The log mean of two end differences.</summary>
    /// <param name="endDifferenceA">K, the stream-to-stream difference at one end; positive.</param>
    /// <param name="endDifferenceB">K, the difference at the other end; positive.</param>
    /// <returns>
    /// K. The common value when the two are equal, the log mean otherwise, and <see cref="double.NaN"/>
    /// when either is not positive -- a temperature cross, which no exchanger has.
    /// </returns>
    /// <remarks>
    /// Near-equal ends use the series rather than the quotient: with <c>r = (a − b)/(a + b)</c>,
    /// <c>LMTD = (a + b)/2 · (1 − r²/3 − r⁴/45 …)</c>, which is exact to double precision below
    /// <c>|r| = 1e-4</c> where <c>ln(a/b)</c> has lost its digits.
    /// </remarks>
    public static double Of(double endDifferenceA, double endDifferenceB)
    {
        if (!(endDifferenceA > 0) || !(endDifferenceB > 0))
        {
            return double.NaN;
        }

        var mean = 0.5 * (endDifferenceA + endDifferenceB);
        var ratio = (endDifferenceA - endDifferenceB) / (endDifferenceA + endDifferenceB);

        if (Math.Abs(ratio) < 1e-4)
        {
            var squared = ratio * ratio;

            return mean * (1 - (squared / 3) - (squared * squared / 45));
        }

        return (endDifferenceA - endDifferenceB) / Math.Log(endDifferenceA / endDifferenceB);
    }

    /// <summary>The LMTD of a counterflow exchanger from its four terminals.</summary>
    /// <param name="hotIn">K, the hot stream entering.</param>
    /// <param name="hotOut">K, the hot stream leaving.</param>
    /// <param name="coldIn">K, the cold stream entering.</param>
    /// <param name="coldOut">K, the cold stream leaving.</param>
    /// <returns>K, or <see cref="double.NaN"/> when the streams cross.</returns>
    /// <remarks>
    /// Counterflow pairs the hot inlet with the cold <em>outlet</em>: the ends of the exchanger are where
    /// each stream enters, and they are opposite ends.
    /// </remarks>
    public static double Counterflow(double hotIn, double hotOut, double coldIn, double coldOut) =>
        Of(hotIn - coldOut, hotOut - coldIn);

    /// <summary>The LMTD of a parallel-flow exchanger from its four terminals.</summary>
    /// <param name="hotIn">K, the hot stream entering.</param>
    /// <param name="hotOut">K, the hot stream leaving.</param>
    /// <param name="coldIn">K, the cold stream entering.</param>
    /// <param name="coldOut">K, the cold stream leaving.</param>
    /// <returns>K, or <see cref="double.NaN"/> when the streams cross.</returns>
    public static double Parallel(double hotIn, double hotOut, double coldIn, double coldOut) =>
        Of(hotIn - coldIn, hotOut - coldOut);

    /// <summary>The conductance a duty across a log-mean difference implies.</summary>
    /// <param name="duty">W, either sign; only its magnitude matters.</param>
    /// <param name="lmtd">K, positive.</param>
    /// <returns>W/K, or <see cref="double.NaN"/> when the difference is not positive.</returns>
    public static double Conductance(double duty, double lmtd) =>
        lmtd > 0 ? Math.Abs(duty) / lmtd : double.NaN;
}
