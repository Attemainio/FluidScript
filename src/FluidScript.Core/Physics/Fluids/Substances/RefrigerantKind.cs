namespace FluidScript.Core.Physics.Fluids.Substances;

/// <summary>The refrigerants a vapour-compression cycle can be built on.</summary>
/// <remarks>
/// <para>
/// A closed set rather than a free string, because each entry carries a claim: the backend has the
/// fluid, the cycle's four state points are inside its validated range, and <c>27</c>'s provenance
/// rule is satisfiable for whatever catalogue rows reference it. Adding one is a deliberate act.
/// </para>
/// <para>
/// <strong>They are not interchangeable, and the difference is structural rather than numeric</strong>
/// (<c>D-81</c>). <see cref="Ammonia"/> and <see cref="Propane"/> condense: the high-side pressure is
/// <c>P_sat(T_c)</c> and there is nothing to choose. <see cref="CarbonDioxide"/> has a critical
/// temperature of 31.0 °C, so a heat pump raising water above that runs transcritical, the gas-cooler
/// outlet temperature stops determining the pressure, and the machine carries <em>one more unknown</em>
/// that something has to close.
/// </para>
/// </remarks>
public enum RefrigerantKind
{
    /// <summary>R717. Critical point 132.25 °C, 113.33 bar.</summary>
    /// <remarks>
    /// High latent heat and a high discharge temperature — roughly 150 °C at −7/40 °C and an isentropic
    /// efficiency of 0.7, which puts about 22 % of the condenser duty in the desuperheat zone. That
    /// split is why <c>D-80</c> zones the exchanger.
    /// </remarks>
    Ammonia = 0,

    /// <summary>R290. Critical point 96.74 °C, 42.51 bar.</summary>
    /// <remarks>
    /// Subcritical for hydronic supply temperatures, but not by much: a 70 °C supply condenses near
    /// 75 °C, which is 0.94 of the critical temperature in absolute terms and where the latent heat has
    /// already fallen far from its low-temperature value.
    /// </remarks>
    Propane = 1,

    /// <summary>R744. Critical point 30.98 °C, 73.77 bar.</summary>
    /// <remarks>
    /// Transcritical for every useful heating application, so it has no condensing temperature and no
    /// latent heat on the high side — the gas cooler is entirely sensible, with a large temperature
    /// glide. Its high-side pressure is a free variable closed by <c>D-81</c>.
    /// </remarks>
    CarbonDioxide = 2,
}
