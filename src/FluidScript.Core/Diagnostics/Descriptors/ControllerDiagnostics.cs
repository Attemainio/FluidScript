using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics.Descriptors;

/// <summary>Everything a <c>control</c> line can report about the design solve.</summary>
/// <remarks>
/// <para>
/// The <c>FS32xx</c> range (<c>plan/30-solver/34-controllers.md</c>). The codes here are the two the
/// design solve raises before any controller runs (<c>D-141</c>); the run-time ones — default gains,
/// saturation, oscillation — arrive with the control law in P6.3.
/// </para>
/// </remarks>
public static class ControllerDiagnostics
{
    /// <summary>The run starts off setpoint because the setpoint is not a constraint of the design solve.</summary>
    /// <value><c>FS3210</c>, informational.</value>
    /// <remarks>
    /// A setpoint holds in the design solve when the actuator is the solve's to choose (<c>D-141</c>).
    /// With the actuator stated the solve has nothing to hold the measurement with, and with the
    /// measured node stating its own temperature the script has said what it wants there; either way
    /// the setpoint is not a constraint and the controller's first step sees whatever offset the design
    /// solve leaves. Informational: a stated position with a controller on it is a legitimate way to
    /// write a loop that starts away from its setpoint.
    /// </remarks>
    public static DiagnosticDescriptor StartsOffSetpoint { get; } = new(
        "FS3210",
        DiagnosticSeverity.Info,
        "{controller} may start off its setpoint: {reason}, so the design solve did not hold {measurement} at {setpoint}.");

    /// <summary>The measurement is not one the design solve can hold at a setpoint.</summary>
    /// <value><c>FS3211</c>, informational.</value>
    /// <remarks>
    /// What the design solve can hold is a plain node's temperature (<c>N2.t</c>, or a sensor on it): the
    /// constraint row and the promotion exist for it already (<c>23</c>). A boundary's temperature is what
    /// enters the model and is not a demand; a flow, a pressure or an exchanger's terminal has no row of
    /// its own yet. The loop still runs from wherever the design solve lands.
    /// </remarks>
    public static DiagnosticDescriptor MeasurementNotHeld { get; } = new(
        "FS3211",
        DiagnosticSeverity.Info,
        "{controller} measures {measurement}, which the design solve cannot hold at a setpoint; only a node's temperature can be. The run starts wherever the design solve lands.");

    /// <summary>Gets every code this area defines, in code order.</summary>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        StartsOffSetpoint,
        MeasurementNotHeld,
    ];
}
