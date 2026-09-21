using System.Globalization;

namespace FluidScript.Core.Model;

/// <summary>The ends of a colour scale's domain, settled to the legend's precision before they are rounded outward (<c>57</c>, <c>C-112</c>).</summary>
/// <remarks>
/// <para>
/// <strong>A nice bound flips on a nanounit if it is taken from the raw double.</strong> The legend
/// rounds the domain outward to a 1-2-5 step, and a value one ulp past a step boundary opens a whole
/// step of colour range with nothing in it: the substation's lowest node sits on the datum, and its
/// gauge pressure solved at −7e-10 kPa one day and +7e-10 the next, so the floor read −200 kPa or 0
/// for a plant that spans 560. The storage header's inlet stated at 45 °C solved a hair either side
/// of 45 and the floor read 40 or 45; its 60 °C inlet solves a hair over and the ceiling read 65.
/// </para>
/// <para>
/// <strong>So the ends are settled first, to the precision the legend can show.</strong> Six
/// significant digits of the larger end's magnitude is the contract's own precision
/// (<c>ModelContractBuilder.SignificantDigits</c>); each end is rounded to that unit, and an end whose
/// magnitude is under the resolution the solve claims for the property — <c>newton.residual_tol</c>
/// times the property's scale, 1e-3 Pa for a pressure — is zero. Two ends that agree after settling
/// are a degenerate domain, and the legend says <c>all 0 kPa</c> rather than <c>all 6.55e-10 kPa</c>.
/// Only then is the domain niced.
/// </para>
/// </remarks>
public static class ScaleDomain
{
    /// <summary>The significant digits an end is settled to; the contract's own.</summary>
    public const int SignificantDigits = 6;

    /// <summary>The domain the legend shows for the raw extremes of a property over every element.</summary>
    /// <param name="min">The smallest element value, in the scale's display unit.</param>
    /// <param name="max">The largest element value, in the same unit.</param>
    /// <param name="resolution">The magnitude below which a value is zero, in the same unit; 0 for a property with no physical zero, such as a temperature in °C.</param>
    /// <param name="diverging">Whether the scale is diverging about zero, in which case the niced domain is made symmetric.</param>
    /// <returns>The ends, settled and rounded outward; equal ends for a degenerate domain.</returns>
    public static (double Min, double Max) Settle(double min, double max, double resolution, bool diverging)
    {
        var magnitude = Math.Max(Math.Abs(min), Math.Abs(max));
        var (low, high) = (Snap(min, magnitude, resolution), Snap(max, magnitude, resolution));

        if (high <= low)
        {
            return (low, low);
        }

        return diverging ? Symmetric(low, high) : Nice(low, high);
    }

    /// <summary>Rounded outward to a 1-2-5 step giving about five ticks (<c>57</c>'s "nice").</summary>
    /// <param name="min">The low end.</param>
    /// <param name="max">The high end.</param>
    /// <returns>The ends on the step; unchanged when the range is empty.</returns>
    public static (double Min, double Max) Nice(double min, double max)
    {
        if (max <= min)
        {
            return (min, max);
        }

        var raw = (max - min) / 5;
        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var fraction = raw / power;
        var step = (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * power;

        return (Clean(Math.Floor(min / step) * step), Clean(Math.Ceiling(max / step) * step));
    }

    /// <summary>A diverging domain: niced, then made symmetric about zero so the neutral colour sits at zero (<c>57</c>).</summary>
    /// <param name="min">The low end.</param>
    /// <param name="max">The high end.</param>
    /// <returns>Ends of equal magnitude either side of zero.</returns>
    public static (double Min, double Max) Symmetric(double min, double max)
    {
        var (low, high) = Nice(Math.Min(min, 0), Math.Max(max, 0));
        var reach = Math.Max(-low, high);
        return (-reach, reach);
    }

    /// <summary>One end at the legend's precision: a multiple of the larger magnitude's sixth digit, zero under the resolution.</summary>
    private static double Snap(double value, double magnitude, double resolution)
    {
        if (Math.Abs(value) < resolution || magnitude == 0 || !double.IsFinite(magnitude))
        {
            return 0;
        }

        var unit = Math.Pow(10, Math.Floor(Math.Log10(magnitude)) - (SignificantDigits - 1));
        return Clean(Math.Round(value / unit) * unit);
    }

    /// <summary>Through the decimal string and back, so a step product prints as the tick it is and not one ulp off it.</summary>
    private static double Clean(double value) =>
        value == 0 || !double.IsFinite(value)
            ? (value == 0 ? 0 : value)
            : double.Parse(value.ToString("G" + SignificantDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
