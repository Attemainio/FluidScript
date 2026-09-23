using System.Collections.Immutable;

namespace FluidScript.Api.Contracts;

/// <summary>One dimension and the units the script may write for it.</summary>
/// <param name="Name">The dimension's name, as parameters refer to it.</param>
/// <param name="SiUnit">The unit Core computes in.</param>
/// <param name="CanonicalUnit">The unit a bare number means and the wire reports in, or <see langword="null"/>.</param>
/// <param name="Units">Every unit symbol accepted for this dimension.</param>
/// <param name="Conversions">How each of <paramref name="Units"/> converts to <paramref name="SiUnit"/>, in the same order (<c>A-6</c>): the editor's quantity hover shows a value in SI and in the alternative units from this, so no second unit table exists on the client.</param>
public sealed record DimensionWire(string Name, string SiUnit, string? CanonicalUnit, ImmutableArray<string> Units, ImmutableArray<UnitConversionWire> Conversions);
