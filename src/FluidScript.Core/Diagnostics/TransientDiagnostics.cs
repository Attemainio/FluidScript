using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>Everything a run can report before its first step.</summary>
/// <remarks>
/// <para>
/// The <c>FS31xx</c> range (<c>plan/30-solver/33-transient-time-domain.md</c>). The code here is the one
/// the assembly raises from the schedule alone; the run-time ones — the step floor, the horizon, a
/// state leaving its range — arrive with the integrator in P6.1.
/// </para>
/// </remarks>
public static class TransientDiagnostics
{
    /// <summary>A schedule target is also a control line's actuator.</summary>
    /// <value><c>FS3109</c>, error.</value>
    /// <remarks>
    /// A parameter has one owner in a run: the schedule or the controller (<c>D-140</c>). A schedule that
    /// moved an actuator would fight the controller for it at every step, and whichever wrote last
    /// would win, which is neither a disturbance nor a control loop. The schedule is refused; move the
    /// setpoint instead, which is what a schedule on a controlled loop means.
    /// </remarks>
    public static DiagnosticDescriptor ScheduledActuator { get; } = new(
        "FS3109",
        DiagnosticSeverity.Error,
        "'{target}' is driven by {controller}; a schedule cannot also move it.");

    /// <summary>Gets every code this area defines, in code order.</summary>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        ScheduledActuator,
    ];
}
