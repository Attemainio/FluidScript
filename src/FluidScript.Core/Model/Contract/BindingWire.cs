namespace FluidScript.Core.Model.Contract;

/// <summary>An evaluated <c>let</c>.</summary>
/// <param name="Name">The name.</param>
/// <param name="Value">The value in <paramref name="Unit"/>, or <see langword="null"/> when the binding is deferred to the solve.</param>
/// <param name="Unit">The canonical unit, or <see langword="null"/> when dimensionless or deferred.</param>
/// <param name="Dimension">The dimension's name, so the editor can filter completion by it (<c>52</c>); present for a deferred binding too when the binder could type its expression from the units and properties it names (<c>U-5</c>); <see langword="null"/> for a dimensionless or an unnamed one, or a deferred one nothing types.</param>
/// <param name="SiUnit">For an unnamed dimension, the SI spelling the value carries, shown dimmed by completion; otherwise <see langword="null"/>.</param>
public sealed record BindingWire(string Name, double? Value, string? Unit, string? Dimension, string? SiUnit);
