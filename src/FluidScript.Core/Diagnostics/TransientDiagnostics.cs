using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>Everything a run can report before its first step.</summary>
/// <remarks>
/// <para>
/// The <c>FS31xx</c> range (<c>plan/30-solver/33-transient-time-domain.md</c>). <c>FS3109</c> is raised
/// by the assembly from the schedule alone; the rest by the integrator, on the frame whose step raised
/// them. <c>FS3108</c> arrives with the tank's remix in P6.2 and <c>FS3110</c> with per-circuit modes.
/// </para>
/// </remarks>
public static class TransientDiagnostics
{
    /// <summary>The step is limited by the residence time of one control volume.</summary>
    /// <value><c>FS3101</c>, informational.</value>
    /// <remarks>
    /// Raised once when the CFL limit first binds below the frame interval, and again whenever the
    /// limiting state changes. It names what the user can change: the cell count of the pipe whose
    /// cells are smallest.
    /// </remarks>
    public static DiagnosticDescriptor StepLimited { get; } = new(
        "FS3101",
        DiagnosticSeverity.Info,
        "Step limited to {step} s by '{component}'. Fewer internal nodes would run faster.");

    /// <summary>The step fell below <c>transient.min_step</c>.</summary>
    /// <value><c>FS3102</c>, error.</value>
    public static DiagnosticDescriptor StepTooSmall { get; } = new(
        "FS3102",
        DiagnosticSeverity.Error,
        "The simulation cannot advance past {time} s. Something is changing faster than the model can follow.");

    /// <summary>The algebraic solve inside a step did not converge.</summary>
    /// <value><c>FS3103</c>, error.</value>
    public static DiagnosticDescriptor StepNotBalanced { get; } = new(
        "FS3103",
        DiagnosticSeverity.Error,
        "Could not balance the circuit at t = {time} s: {inner}.");

    /// <summary>The horizon was reached before every state stopped moving.</summary>
    /// <value><c>FS3104</c>, informational.</value>
    /// <remarks>Settled is <c>36</c>'s <c>transient.settle_tol</c> over <c>transient.settle_frames</c> consecutive frame intervals.</remarks>
    public static DiagnosticDescriptor NotSettled { get; } = new(
        "FS3104",
        DiagnosticSeverity.Info,
        "Still changing at {horizon} s. Extend the run to see it settle.");

    /// <summary>A scheduled target is a parameter the run cannot move.</summary>
    /// <value><c>FS3105</c>, error.</value>
    /// <remarks>
    /// The binder has checked that the component and the parameter exist; what a run can move is
    /// narrower: a parameter the component resolves at solve time — an exchanger's <c>power</c>, a
    /// valve's <c>position</c> or <c>kv</c>, a pump's <c>head</c>. A boundary's temperature, a pipe's
    /// diameter or a tank's volume enter the model at assembly and have no slot to write at t &gt; 0
    /// (<c>S-77</c> for the boundary case).
    /// </remarks>
    public static DiagnosticDescriptor NotSchedulable { get; } = new(
        "FS3105",
        DiagnosticSeverity.Error,
        "Cannot change '{target}' — {reason}.");

    /// <summary>The stored energy drifted from the integrated heat and boundary flows.</summary>
    /// <value><c>FS3106</c>, warning.</value>
    public static DiagnosticDescriptor EnergyDrift { get; } = new(
        "FS3106",
        DiagnosticSeverity.Warning,
        "Energy balance drifted by {pct} % over the run. Results may be unreliable.");

    /// <summary>A run invariant failed and the run stopped at its last verified frame.</summary>
    /// <value><c>FS3107</c>, error.</value>
    public static DiagnosticDescriptor InvariantFailed { get; } = new(
        "FS3107",
        DiagnosticSeverity.Error,
        "Simulation stopped at {time} s because {invariant} failed. The last verified frame is {sequence}.");

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
        StepLimited,
        StepTooSmall,
        StepNotBalanced,
        NotSettled,
        NotSchedulable,
        EnergyDrift,
        InvariantFailed,
        ScheduledActuator,
    ];
}
