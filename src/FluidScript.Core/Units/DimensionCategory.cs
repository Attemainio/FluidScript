namespace FluidScript.Core.Units;

/// <summary>
/// How a dimension behaves under arithmetic.
/// </summary>
/// <remarks>
/// The category is what keeps <c>20 °C + 30 °C</c> from compiling while <c>20 °C + 30 dK</c> works.
/// Dimensional analysis alone cannot do it: an absolute temperature and a temperature difference have
/// the same exponent vector and are still different things.
/// </remarks>
public enum DimensionCategory
{
    /// <summary>An ordinary ratio quantity that takes part fully in the exponent algebra.</summary>
    /// <remarks>Length, power, mass flow. Two of these may be added when their vectors match, and any two may be multiplied.</remarks>
    Linear,

    /// <summary>An affine quantity measured from a reference point rather than from zero.</summary>
    /// <remarks>
    /// Temperature and gauge pressure. Two may be subtracted, yielding the paired
    /// <see cref="Delta"/>; they may not be added, negated or scaled, because none of those has a
    /// meaning once the zero is arbitrary.
    /// </remarks>
    Absolute,

    /// <summary>A difference between two <see cref="Absolute"/> values.</summary>
    /// <remarks>Shares its exponent vector with its absolute partner but behaves linearly.</remarks>
    Delta,

    /// <summary>A designation or defined coefficient that is not a physical quantity.</summary>
    /// <remarks>
    /// DN50 is a name, not 50 of anything; a valve's <c>Kv</c> is a coefficient defined by a test
    /// condition. Neither may be produced by arithmetic, which is what stops a <c>Kv</c> being added
    /// to a volume flow it happens to share a unit with.
    /// </remarks>
    Nominal,
}
