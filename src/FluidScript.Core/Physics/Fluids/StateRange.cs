namespace FluidScript.Core.Physics.Fluids;

/// <summary>The domain a substance's data is valid over (<c>07</c>'s engineering validity matrix).</summary>
/// <param name="MinimumTemperature">The coldest validated temperature, in K.</param>
/// <param name="MaximumTemperature">The warmest validated temperature, in K.</param>
/// <param name="MinimumAbsolutePressure">The lowest validated pressure, in Pa <strong>absolute</strong>.</param>
/// <param name="MaximumAbsolutePressure">The highest validated pressure, in Pa absolute.</param>
/// <remarks>
/// <para>
/// Pressures here are absolute, unlike everywhere else in the model, because that is how <c>07</c>
/// states the domain — "up to 1000 kPa absolute" — and converting the bound instead of the value would
/// put the atmosphere in two places.
/// </para>
/// <para>
/// <strong>The range has to be checked here rather than left to the backend.</strong> The M0 spike
/// measured what CoolProp does at the edges: below the melting line it throws, and above the upper
/// bound it returns a number. Water at 5000 °C comes back with a density and no complaint, and a
/// silently extrapolated property is indistinguishable from a good one at the call site.
/// </para>
/// </remarks>
public readonly record struct StateRange(
    double MinimumTemperature,
    double MaximumTemperature,
    double MinimumAbsolutePressure,
    double MaximumAbsolutePressure)
{
    /// <summary>Determines whether a state lies inside the validated domain.</summary>
    /// <param name="temperature">The temperature, in K.</param>
    /// <param name="absolutePressure">The pressure, in Pa absolute.</param>
    /// <returns><see langword="true"/> when both lie within their bounds, inclusive.</returns>
    public bool Contains(double temperature, double absolutePressure) =>
        temperature >= MinimumTemperature
        && temperature <= MaximumTemperature
        && absolutePressure >= MinimumAbsolutePressure
        && absolutePressure <= MaximumAbsolutePressure;
}
