using FluidScript.Core.Diagnostics;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>A controller bound to what it drives and what it reads (<c>D-40</c>).</summary>
/// <remarks>
/// Every field comes from a named argument, so transposing two of them is a binding error rather than
/// a silent reversal that drives the valve the wrong way.
/// </remarks>
public sealed record ControlBindingSymbol
{
    /// <summary>Gets the controller component named by <c>by=</c>.</summary>
    public required ComponentSymbol Controller { get; init; }

    /// <summary>Gets the settable parameter named by <c>actuate=</c>, such as <c>TV1.position</c>.</summary>
    /// <remarks>
    /// Always qualified. A bare component name is <c>FS1515</c> (<c>D-43</c>): there is no per-kind
    /// default actuator to fall back on, deliberately, because a valve has more than one thing that
    /// could move.
    /// </remarks>
    public required PropertyReference Actuator { get; init; }

    /// <summary>Gets the property named by <c>measure=</c>, such as <c>N2.t</c>.</summary>
    public required PropertyReference Measurement { get; init; }

    /// <summary>Gets the target value named by <c>setpoint=</c>, in the measurement's dimension.</summary>
    public Quantity? Setpoint { get; init; }

    /// <summary>Gets where the line sits in the source.</summary>
    public required TextSpan Span { get; init; }
}
