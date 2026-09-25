using System.Collections.Immutable;

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
    /// <value>SI, in the measurement's dimension: K for a temperature, Pa for a pressure. The design case's value when it varies.</value>
    public Quantity? Setpoint { get; init; }

    /// <summary>Gets the setpoint in every case, when it follows a driver (<c>D-167</c>); empty when it is one value.</summary>
    /// <value>One element per declared case, in <see cref="ProjectSettings.Scenarios"/>' order; SI, as <see cref="Setpoint"/>.</value>
    /// <remarks><see cref="ScenarioProjection"/> moves a case's element into <see cref="Setpoint"/>, as it does a parameter's.</remarks>
    public ImmutableArray<Quantity?> Setpoints { get; init; } = [];

    /// <summary>Gets the proportional band: the error over which the output travels its whole range (language 2, <c>D-168</c>).</summary>
    /// <value>SI, a difference in the measurement's dimension: K for a temperature, Pa for a pressure; positive. <see langword="null"/> when not stated.</value>
    public Quantity? Band { get; init; }

    /// <summary>Gets an on/off controller's switching differential (language 2).</summary>
    /// <value>SI, a difference in the measurement's dimension; positive. <see langword="null"/> when not stated.</value>
    public Quantity? Differential { get; init; }

    /// <summary>Gets the lower output limit, in the actuated parameter's dimension (language 2's <c>output = low..high</c>).</summary>
    /// <value>SI in the actuator's dimension: a valve position is a fraction, 0 shut to 1 open. <see langword="null"/> for the actuator's full range.</value>
    public Quantity? OutputLow { get; init; }

    /// <summary>Gets the upper output limit.</summary>
    /// <value>As <see cref="OutputLow"/>.</value>
    public Quantity? OutputHigh { get; init; }

    /// <summary>Gets the curve a <c>curve</c> controller follows: its output as a function of the reading.</summary>
    public string? Curve { get; init; }

    /// <summary>Gets where the line sits in the source.</summary>
    public required TextSpan Span { get; init; }
}
