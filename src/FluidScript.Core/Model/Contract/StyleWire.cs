namespace FluidScript.Core.Model.Contract;

/// <summary>The script's presentation settings, resolved (<c>D-104</c>, <c>D-171</c>).</summary>
/// <param name="Spacing">The <c>spacing</c> value in world units, or <see langword="null"/> (<c>D-37</c>).</param>
/// <param name="Default">The project-level style, applied where a circuit states none.</param>
/// <remarks>
/// Contract 3.0 dropped <c>tokens</c> (the style line's words, which <see cref="Default"/> already carries classified)
/// and <c>named</c> (language 1's named styles, which language 2 does not have), <c>C-139</c>.
/// </remarks>
public sealed record StyleWire(double? Spacing, ResolvedStyleWire Default);
