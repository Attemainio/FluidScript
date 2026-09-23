using FluidScript.Core.Diagnostics;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>One driver's value at the design condition (<c>D-58</c>).</summary>
/// <param name="WrittenName">The driver as the <c>design</c> line spelled it.</param>
/// <param name="Role">The registered driver it resolved to, or <see langword="null"/>.</param>
/// <param name="Value">
/// The quantity, in SI, or <see langword="null"/> when the expression could not be evaluated.
/// </param>
/// <param name="Number">
/// The number a curve is read at: <paramref name="Value"/> in the role's canonical unit, or its bare
/// SI value when the role names no dimension. This is what makes <c>design tout=-26</c> and
/// <c>design tout=-26 C</c> pick the same row of a table written in degrees.
/// </param>
/// <param name="Span">Where the assignment sits in the source.</param>
public sealed record DesignValue(
    string WrittenName,
    ScheduleRole? Role,
    Quantity? Value,
    double? Number,
    TextSpan Span);
