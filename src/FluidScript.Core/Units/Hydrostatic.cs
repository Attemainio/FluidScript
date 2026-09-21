namespace FluidScript.Core.Units;

/// <summary>The hydrostatic identities at standard gravity: a column's pressure, a pressure's head, a lift's energy.</summary>
/// <remarks>
/// One place for <c>ρ g h</c> and its inverse, so a pump's head, a riser's static column and a bare link's
/// hydrostatic drop are all the same arithmetic in the same order. Everything is SI; <see cref="UnitTable.StandardGravity"/>
/// is the only gravity the model knows (<c>D-126</c>).
/// </remarks>
public static class Hydrostatic
{
    /// <summary>The pressure a column exerts: <c>ρ g h</c>.</summary>
    /// <param name="density">kg/m³.</param>
    /// <param name="height">m: a head, or an elevation difference, positive upward.</param>
    /// <returns>Pa, with the sign of <paramref name="height"/>.</returns>
    public static double Pressure(double density, double height) => density * UnitTable.StandardGravity * height;

    /// <summary>The head a pressure difference is worth: <c>Δp / (ρ g)</c>.</summary>
    /// <param name="pressure">Pa, positive for a rise.</param>
    /// <param name="density">kg/m³.</param>
    /// <returns>m of the fluid, with the sign of <paramref name="pressure"/>.</returns>
    public static double Head(double pressure, double density) => pressure / (density * UnitTable.StandardGravity);

    /// <summary>The specific potential energy of a lift: <c>g Δz</c>.</summary>
    /// <param name="height">m, positive upward.</param>
    /// <returns>J/kg, with the sign of <paramref name="height"/>.</returns>
    public static double Lift(double height) => UnitTable.StandardGravity * height;

    /// <summary>The power a mass flow spends climbing: <c>ṁ g Δz</c>.</summary>
    /// <param name="massFlow">kg/s.</param>
    /// <param name="height">m, positive upward.</param>
    /// <returns>W, positive when the flow climbs.</returns>
    public static double Power(double massFlow, double height) => massFlow * UnitTable.StandardGravity * height;
}
