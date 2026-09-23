namespace FluidScript.Core.Model.Contract;

/// <summary>A scale domain.</summary>
/// <param name="Min">The low end, in the scale's unit.</param>
/// <param name="Max">The high end, in the scale's unit.</param>
/// <param name="Nice">Whether the ends were settled to six significant digits and rounded outward to legend ticks (<c>C-112</c>).</param>
public sealed record DomainWire(double Min, double Max, bool Nice);
