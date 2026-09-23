namespace FluidScript.Api.Contracts;

/// <summary>A closed range in the parameter's own unit.</summary>
/// <param name="Min">The lowest value.</param>
/// <param name="Max">The highest value.</param>
public sealed record RangeMetaWire(double Min, double Max);
