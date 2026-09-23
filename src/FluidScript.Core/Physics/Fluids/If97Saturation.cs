namespace FluidScript.Core.Fluids;

/// <summary>Water's saturation line, IAPWS-IF97 Region 4: the closed-form <c>p_s(T)</c> and its exact inverse <c>T_s(p)</c>.</summary>
/// <remarks>
/// <para>
/// <strong>Why a formula and not a backend call.</strong> Water is measured through the property
/// package's IF97 backend (<c>D-137</c>), and that backend, as the package reaches it, refuses a
/// quality input, so the boiling line cannot be asked for through it. The line is what <see cref="Water"/> checks every
/// (p, T) fix against — a state on it is one constraint, not two, and a state above it is steam and
/// not an out-of-range liquid — so it is read on every stated temperature and must be cheap. Region 4
/// is a quadratic in a transformed temperature, solved in closed form both ways; it costs nanoseconds
/// and agrees with IAPWS-95's line to millikelvins over the whole liquid range this project validates.
/// </para>
/// <para>
/// The equations are IAPWS R7-97(2012) §8, equations 30 and 31, with the ten coefficients of its
/// table 34; the verification values of its tables 35 and 36 are pinned by <c>If97SaturationTests</c>.
/// The formulation is published by IAPWS for exactly this use and is not a standard's table.
/// </para>
/// </remarks>
public static class If97Saturation
{
    private const double N1 = 0.11670521452767e4;
    private const double N2 = -0.72421316703206e6;
    private const double N3 = -0.17073846940092e2;
    private const double N4 = 0.12020824702470e5;
    private const double N5 = -0.32325550322333e7;
    private const double N6 = 0.14915108613530e2;
    private const double N7 = -0.48232657361591e4;
    private const double N8 = 0.40511340542057e6;
    private const double N9 = -0.23855557567849;
    private const double N10 = 0.65017534844798e3;

    /// <summary>The lowest temperature the line is stated for: the triple point.</summary>
    /// <value>K.</value>
    public const double MinimumTemperature = 273.15;

    /// <summary>The highest temperature the line is stated for: the critical point.</summary>
    /// <value>K.</value>
    public const double MaximumTemperature = 647.096;

    /// <summary>The saturation pressure at a temperature.</summary>
    /// <param name="temperature">K, between the triple and critical points.</param>
    /// <returns>Pa absolute, or <see cref="double.NaN"/> outside the line's range.</returns>
    public static double Pressure(double temperature)
    {
        if (!(temperature >= MinimumTemperature && temperature <= MaximumTemperature))
        {
            return double.NaN;
        }

        var theta = temperature + (N9 / (temperature - N10));
        var a = (theta * theta) + (N1 * theta) + N2;
        var b = (N3 * theta * theta) + (N4 * theta) + N5;
        var c = (N6 * theta * theta) + (N7 * theta) + N8;
        var root = 2 * c / (-b + Math.Sqrt((b * b) - (4 * a * c)));

        return root * root * root * root * 1e6;
    }

    /// <summary>The saturation temperature at a pressure.</summary>
    /// <param name="absolutePressure">Pa absolute, between the triple-point and critical pressures.</param>
    /// <returns>K, or <see cref="double.NaN"/> outside the line's range.</returns>
    public static double Temperature(double absolutePressure)
    {
        if (!(absolutePressure >= 611.213 && absolutePressure <= 22.064e6))
        {
            return double.NaN;
        }

        var beta = Math.Sqrt(Math.Sqrt(absolutePressure * 1e-6));
        var e = (beta * beta) + (N3 * beta) + N6;
        var f = (N1 * beta * beta) + (N4 * beta) + N7;
        var g = (N2 * beta * beta) + (N5 * beta) + N8;
        var d = 2 * g / (-f - Math.Sqrt((f * f) - (4 * e * g)));

        return (N10 + d - Math.Sqrt(((N10 + d) * (N10 + d)) - (4 * (N9 + (N10 * d))))) / 2;
    }
}
