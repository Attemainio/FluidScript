namespace FluidScript.Core.Topology.Construction;

/// <summary>The sign a role word gives an exchanger's duty (<c>D-91</c>).</summary>
/// <remarks>
/// <c>load power=24</c> is a 24 kW consumer and reaches the core as −24 kW; <c>heater power=24</c> as
/// +24 kW; <c>heat_exchanger</c> keeps the sign written. One home, because every path that writes a
/// duty into the solver -- lowering, a <c>schedule</c> line, a curve followed in time -- has to agree,
/// and a scheduled load that heated the loop was the cost of two homes.
/// </remarks>
public static class ExchangerRoles
{
    /// <summary>Applies a role word's sign to a duty.</summary>
    /// <param name="writtenKind">The kind as written, normalised.</param>
    /// <param name="power">W, as the script stated it.</param>
    /// <returns>W, positive when side 1 gains heat.</returns>
    public static double Duty(string writtenKind, double power) => writtenKind switch
    {
        "load" or "cooler" or "radiator" or "chiller" => -Math.Abs(power),
        "heater" or "boiler" => Math.Abs(power),
        _ => power,
    };
}
