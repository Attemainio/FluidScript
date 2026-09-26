using FluidScript.Core.Diagnostics;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>One driver's value at a component's sizing point (<c>D-94</c>, <c>D-175</c>).</summary>
/// <param name="WrittenName">The <c>let</c> the point names, as spelled.</param>
/// <param name="Value">
/// The quantity, in SI, or <see langword="null"/> when the expression could not be evaluated.
/// </param>
/// <param name="Number">
/// The number a curve is read at: <paramref name="Value"/> in the unit the <c>let</c> is written in, which is the
/// unit the curve's rows are written in.
/// </param>
/// <param name="Span">Where the assignment sits in the source.</param>
public sealed record DesignValue(
    string WrittenName,
    Quantity? Value,
    double? Number,
    TextSpan Span);
