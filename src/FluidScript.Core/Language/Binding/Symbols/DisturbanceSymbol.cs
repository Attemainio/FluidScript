using FluidScript.Core.Diagnostics;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>One scheduled change: a value stepped at an instant or ramped over an interval.</summary>
/// <param name="Circuit">The circuit whose schedule this belongs to.</param>
/// <param name="Target">The component parameter being changed.</param>
/// <param name="From">When it starts.</param>
/// <param name="To">When it ends, equal to <paramref name="From"/> for a step.</param>
/// <param name="FromValue">The value at the start, or <see langword="null"/> for a step.</param>
/// <param name="ToValue">The value at the end, which is the whole change for a step.</param>
/// <param name="Span">Where the line sits in the source.</param>
/// <remarks>
/// <c>15</c>'s binding order never mentioned the schedule — eleven steps, and nothing consumed a
/// disturbance. This is the symbol the step that was missing produces.
/// </remarks>
public sealed record DisturbanceSymbol(
    string Circuit,
    PropertyReference Target,
    Quantity? From,
    Quantity? To,
    Quantity? FromValue,
    Quantity? ToValue,
    TextSpan Span);
