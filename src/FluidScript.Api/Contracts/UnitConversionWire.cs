namespace FluidScript.Api.Contracts;

/// <summary>One unit symbol's conversion to its dimension's SI base unit: <c>si = value × Factor + Offset</c> (<c>13</c>).</summary>
/// <param name="Symbol">The symbol as a script writes it.</param>
/// <param name="Factor">The multiplier to the SI base unit: 1000 for <c>kW</c>.</param>
/// <param name="Offset">Added after scaling, in the SI base unit: 273.15 for <c>°C</c>; zero for every ratio unit.</param>
public sealed record UnitConversionWire(string Symbol, double Factor, double Offset);
