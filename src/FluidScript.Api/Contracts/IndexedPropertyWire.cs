namespace FluidScript.Api.Contracts;

/// <summary>An indexed property family.</summary>
/// <param name="Pattern">The name with <c>{index}</c> where the index goes.</param>
/// <param name="MinIndex">The lowest index.</param>
/// <param name="MaxIndex">The highest index, or <see langword="null"/> when another parameter sets it.</param>
/// <param name="MaxIndexParameter">The parameter that sets the highest index, or <see langword="null"/>.</param>
/// <param name="Element">What each member of the family is.</param>
public sealed record IndexedPropertyWire(string Pattern, int MinIndex, int? MaxIndex, string? MaxIndexParameter, PropertyMetaWire Element);
