namespace FluidScript.Core.Components;

/// <summary>The ε-NTU relations: what fraction of the most heat two streams could exchange they do.</summary>
/// <remarks>
/// <para>
/// <strong>This is the residual's route and the sizer's, and <see cref="LogMeanTemperatureDifference"/>
/// is the other one.</strong> They share no code on purpose (<c>P4.1</c>, <c>22</c>): the substation's
/// UA = 12 071 W/K reached by both is a validation only because neither could have copied the other's
/// sign or its <c>Cmin</c>. Nothing in this file references that one and nothing there references this.
/// </para>
/// <para>
/// Everything is dimensionless. <c>NTU = UA / Cmin</c>, <c>Cr = Cmin / Cmax</c> in <c>[0, 1]</c>, and the
/// duty is <c>ε · Cmin · (T_hot,in − T_cold,in)</c>. The relations are the standard ones
/// (Incropera &amp; DeWitt, <em>Fundamentals of Heat and Mass Transfer</em>, table 11.3; Kays &amp; London,
/// <em>Compact Heat Exchangers</em>), looked up rather than derived, as <c>22</c> tabulates them.
/// </para>
/// <para>
/// <strong>Counterflow at <c>Cr = 1</c> is a removable singularity, blended.</strong> The closed form
/// divides <c>0/0</c> there, and balanced counterflow is where a great many exchangers are deliberately
/// designed. Over <c>1 − Cr &lt; </c><see cref="BalancedBand"/> the limit <c>NTU/(1 + NTU)</c> is blended
/// in with the smoothstep <c>36</c> owns, so the function and its slope are continuous -- the property a
/// Newton iterate crossing the balance point needs.
/// </para>
/// </remarks>
public static class Effectiveness
{
    /// <summary>How far below <c>Cr = 1</c> the balanced-counterflow limit is blended in.</summary>
    /// <value>1e-3. Outside it the closed form is exact and conditioned to better than 1e-12.</value>
    public const double BalancedBand = 1e-3;

    /// <summary>The largest NTU the crossflow inversion searches; beyond it ε is 1 to double precision.</summary>
    private const double CrossflowNtuCeiling = 50;

    /// <summary>The effectiveness of an exchanger of a given size.</summary>
    /// <param name="ntu">Number of transfer units, <c>UA / Cmin</c>, at least 0.</param>
    /// <param name="capacityRatio"><c>Cmin / Cmax</c>, 0 to 1. Zero is a phase-changing side.</param>
    /// <param name="arrangement">How the streams run.</param>
    /// <returns>ε in <c>[0, 1]</c>; <see cref="double.NaN"/> for an argument outside its range.</returns>
    public static double Of(double ntu, double capacityRatio, ExchangerArrangement arrangement)
    {
        if (!(ntu >= 0) || !(capacityRatio >= 0) || !(capacityRatio <= 1))
        {
            return double.NaN;
        }

        return arrangement switch
        {
            ExchangerArrangement.Counter => Counter(ntu, capacityRatio),
            ExchangerArrangement.Parallel => (1 - Math.Exp(-ntu * (1 + capacityRatio))) / (1 + capacityRatio),
            ExchangerArrangement.Crossflow => Crossflow(ntu, capacityRatio),
            _ => double.NaN,
        };
    }

    /// <summary>The effectiveness an infinitely large exchanger reaches.</summary>
    /// <param name="capacityRatio"><c>Cmin / Cmax</c>, 0 to 1.</param>
    /// <param name="arrangement">How the streams run.</param>
    /// <returns>1 for counterflow and crossflow; <c>1/(1 + Cr)</c> for parallel flow.</returns>
    /// <remarks>
    /// The feasibility bound a duty is checked against before anything inverts (<c>24</c>, step 3): a
    /// requested ε at or above this is not a large exchanger but an impossible one.
    /// </remarks>
    public static double Maximum(double capacityRatio, ExchangerArrangement arrangement) =>
        arrangement == ExchangerArrangement.Parallel ? 1 / (1 + capacityRatio) : 1;

    /// <summary>The size that reaches a given effectiveness: ε-NTU inverted.</summary>
    /// <param name="effectiveness">The ε required, 0 to below <see cref="Maximum"/>.</param>
    /// <param name="capacityRatio"><c>Cmin / Cmax</c>, 0 to 1.</param>
    /// <param name="arrangement">How the streams run.</param>
    /// <returns>NTU, or <see cref="double.NaN"/> when no finite exchanger reaches the effectiveness.</returns>
    /// <remarks>
    /// Counterflow and parallel flow invert in closed form, with the same balanced blend as
    /// <see cref="Of"/>. Crossflow does not, and is bisected over <c>[0, 50]</c>: ε is monotone in NTU,
    /// so bisection is safe and about fifty evaluations, which the sizer pays once per pass.
    /// </remarks>
    public static double Ntu(double effectiveness, double capacityRatio, ExchangerArrangement arrangement)
    {
        if (!(effectiveness >= 0) || !(capacityRatio >= 0) || !(capacityRatio <= 1)
            || effectiveness >= Maximum(capacityRatio, arrangement))
        {
            return double.NaN;
        }

        switch (arrangement)
        {
            case ExchangerArrangement.Counter:
            {
                var balanced = effectiveness / (1 - effectiveness);

                if (capacityRatio >= 1 - BalancedBand)
                {
                    var general = capacityRatio < 1
                        ? Math.Log((1 - (effectiveness * capacityRatio)) / (1 - effectiveness)) / (1 - capacityRatio)
                        : balanced;

                    return Smoothing.Blend(1 - capacityRatio, 0, BalancedBand, balanced, general);
                }

                return Math.Log((1 - (effectiveness * capacityRatio)) / (1 - effectiveness)) / (1 - capacityRatio);
            }

            case ExchangerArrangement.Parallel:
                return -Math.Log(1 - (effectiveness * (1 + capacityRatio))) / (1 + capacityRatio);

            case ExchangerArrangement.Crossflow:
            {
                double low = 0, high = CrossflowNtuCeiling;

                if (Crossflow(high, capacityRatio) < effectiveness)
                {
                    return double.NaN;
                }

                for (var step = 0; step < 60; step++)
                {
                    var middle = 0.5 * (low + high);

                    if (Crossflow(middle, capacityRatio) < effectiveness)
                    {
                        low = middle;
                    }
                    else
                    {
                        high = middle;
                    }
                }

                return 0.5 * (low + high);
            }

            default:
                return double.NaN;
        }
    }

    private static double Counter(double ntu, double capacityRatio)
    {
        var balanced = ntu / (1 + ntu);

        if (capacityRatio >= 1 - BalancedBand)
        {
            var general = capacityRatio < 1 ? CounterGeneral(ntu, capacityRatio) : balanced;

            return Smoothing.Blend(1 - capacityRatio, 0, BalancedBand, balanced, general);
        }

        return CounterGeneral(ntu, capacityRatio);
    }

    private static double CounterGeneral(double ntu, double capacityRatio)
    {
        var decay = Math.Exp(-ntu * (1 - capacityRatio));

        return (1 - decay) / (1 - (capacityRatio * decay));
    }

    private static double Crossflow(double ntu, double capacityRatio)
    {
        // Cr -> 0 is the phase-change limit 1 - e^(-NTU); the closed form divides by Cr, so it is taken
        // directly below a threshold where the two agree to double precision.
        if (capacityRatio < 1e-9)
        {
            return 1 - Math.Exp(-ntu);
        }

        if (ntu == 0)
        {
            return 0;
        }

        var exponent = Math.Pow(ntu, 0.22) / capacityRatio * (Math.Exp(-capacityRatio * Math.Pow(ntu, 0.78)) - 1);

        return 1 - Math.Exp(exponent);
    }
}
