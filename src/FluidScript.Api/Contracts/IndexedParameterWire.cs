namespace FluidScript.Api.Contracts;

/// <summary>An indexed parameter family.</summary>
/// <param name="Pattern">The name with <c>{index}</c> where the index goes.</param>
/// <param name="MinIndex">The lowest index.</param>
/// <param name="MaxIndex">The highest index, or <see langword="null"/> when another parameter sets it.</param>
/// <param name="MaxIndexParameter">The parameter that sets the highest index, or <see langword="null"/>.</param>
/// <param name="Element">What each member of the family is.</param>
public sealed record IndexedParameterWire(string Pattern, int MinIndex, int? MaxIndex, string? MaxIndexParameter, ParameterMetaWire Element);
