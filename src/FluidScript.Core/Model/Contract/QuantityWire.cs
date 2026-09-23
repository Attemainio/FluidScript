namespace FluidScript.Core.Model.Contract;

/// <summary>A solved value in its canonical unit.</summary>
/// <param name="Value">The value in <paramref name="Unit"/>, or <see langword="null"/> under <c>FS2501</c>.</param>
/// <param name="Unit">The canonical unit.</param>
public sealed record QuantityWire(double? Value, string Unit);
