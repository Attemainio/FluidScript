namespace FluidScript.Api.Contracts;

/// <summary>One readable property of a kind.</summary>
/// <param name="Name">The name after the dot in an expression.</param>
/// <param name="Dimension">The dimension's name.</param>
/// <param name="Unit">The unit it is reported in.</param>
/// <param name="Availability">When it has a value: <c>declared</c>, <c>sized</c> or <c>solved</c>.</param>
public sealed record PropertyMetaWire(string Name, string? Dimension, string Unit, string Availability);
