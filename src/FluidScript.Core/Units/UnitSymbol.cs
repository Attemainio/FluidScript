namespace FluidScript.Core.Units;

/// <summary>
/// One accepted unit spelling, and how it converts to its dimension's SI base unit.
/// </summary>
/// <param name="Text">The symbol exactly as a script writes it, such as <c>kPaa</c> or <c>kJ/(kg*K)</c>.</param>
/// <param name="Dimension">The dimension the symbol denotes.</param>
/// <param name="Factor">
/// Multiplier from this unit to the SI base unit. 1000 for <c>kW</c>, 0.001 for <c>mm</c>.
/// </param>
/// <param name="Offset">
/// Added after scaling, in the SI base unit. 273.15 for <c>°C</c>; −101325 for an absolute pressure
/// spelling, because a pressure's SI value is stored as gauge. Zero for every ratio unit.
/// </param>
/// <param name="IsCaseInsensitive">
/// Whether the spelling may be matched ignoring case. True only for multi-letter non-SI names such as
/// <c>bar</c> and <c>psi</c>; SI spellings stay case-sensitive so that <c>mm</c> and <c>Mm</c> cannot
/// be confused.
/// </param>
/// <remarks>
/// Conversion is affine — <c>si = value × Factor + Offset</c> — because two dimensions need it and
/// pretending otherwise is how a gauge pressure becomes an absolute one. Everything else has a zero
/// offset and the affine form collapses to a multiply.
/// </remarks>
public sealed record UnitSymbol(
    string Text,
    Dimension Dimension,
    double Factor,
    double Offset = 0,
    bool IsCaseInsensitive = false)
{
    /// <summary>Converts a value written in this unit to the dimension's SI base unit.</summary>
    /// <param name="value">The magnitude as written in the script.</param>
    /// <returns>The magnitude in SI. Pressures come back as gauge, temperatures as kelvin.</returns>
    public double ToSi(double value) => (value * Factor) + Offset;

    /// <summary>Converts an SI magnitude to this unit.</summary>
    /// <param name="siValue">The magnitude in the dimension's SI base unit.</param>
    /// <returns>The magnitude as it would be written in this unit.</returns>
    public double FromSi(double siValue) => (siValue - Offset) / Factor;

    /// <summary>Renders the symbol.</summary>
    /// <returns><see cref="Text"/>.</returns>
    public override string ToString() => Text;
}
