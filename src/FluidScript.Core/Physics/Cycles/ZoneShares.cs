namespace FluidScript.Core.Physics.Cycles;

/// <summary>How a high-side exchanger's duty divides between its zones.</summary>
/// <param name="Desuperheat">The fraction above the saturated-vapour enthalpy.</param>
/// <param name="Latent">The fraction between the two saturation enthalpies.</param>
/// <param name="Subcool">The fraction below the saturated-liquid enthalpy.</param>
/// <remarks>
/// <c>D-80</c>'s split, measured from the backend's own saturation enthalpies rather than estimated
/// from a vapour specific heat. <strong>These are duty shares and not area shares</strong>: gas-side
/// <c>U</c> is roughly a sixth of the condensing value, so a zone carrying a fifth of the duty can
/// carry a third of the area. A transcritical cycle has no saturation line, so its whole gas cooler is
/// desuperheat by construction.
/// </remarks>
public readonly record struct ZoneShares(double Desuperheat, double Latent, double Subcool);
