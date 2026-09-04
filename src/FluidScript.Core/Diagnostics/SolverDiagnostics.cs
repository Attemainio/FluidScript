using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>Everything a solve can report.</summary>
/// <remarks>
/// <para>
/// The <c>FS30xx</c> range (<c>plan/30-solver/31-solver-architecture.md</c>,
/// <c>plan/30-solver/32-steady-state-newton.md</c>). Unlike the binder's codes these are rarely about
/// what the user wrote — a script can be perfectly well-formed and still describe a circuit Newton
/// cannot reach — so the messages say what the solver was doing and which component it was worst at,
/// rather than pointing at a span.
/// </para>
/// <para>
/// <strong><c>FS3012</c> is not registered here.</strong> It reports a retry from the sizing seed after
/// a warm start failed, and this solver is handed exactly one starting vector — the warm start
/// <em>is</em> its <c>initialGuess</c>, so it has no second one to retry from. Retrying needs both
/// seeds at once and belongs to <c>31</c>'s outer loop, which holds them (<c>S-20</c>). Registering it
/// now would put a code on the documentation page that no path produces.
/// </para>
/// </remarks>
public static class SolverDiagnostics
{
    /// <summary>The iteration cap was reached with the residual still above tolerance.</summary>
    /// <value><c>FS3001</c>, an error.</value>
    /// <remarks>
    /// The message names the worst row rather than the norm, because a norm is a number a user can do
    /// nothing with and "N3 energy balance by 17.4 kW" points at a place in their own circuit.
    /// </remarks>
    public static DiagnosticDescriptor IterationCap { get; } = new(
        "FS3001",
        DiagnosticSeverity.Error,
        "Could not solve in {steps} steps. Furthest off: {component} {equation} by {amount}.");

    /// <summary>The Jacobian was singular.</summary>
    /// <value><c>FS3002</c>, an error.</value>
    /// <remarks>
    /// <para>
    /// The causes worth naming, in the order they occur: no pressure datum in a connected component, no
    /// stated temperature in a closed circuit (<c>D-65</c>), a loop with no flow driver, and a
    /// duplicated equation from an over-specified component.
    /// </para>
    /// <para>
    /// <strong>Reaching here is also a bug report.</strong> All of those are topology problems that
    /// <c>23</c> checks for before the solve, so a singularity arriving at the linear solve means a
    /// pre-check missed a case.
    /// </para>
    /// </remarks>
    public static DiagnosticDescriptor Singular { get; } = new(
        "FS3002",
        DiagnosticSeverity.Error,
        "The circuit has no unique solution around {component}. Check for a missing pressure datum, a "
        + "closed circuit with no stated temperature, or a loop with no driver.");

    /// <summary>The residual grew away from a balance.</summary>
    /// <value><c>FS3003</c>, an error.</value>
    public static DiagnosticDescriptor Diverging { get; } = new(
        "FS3003",
        DiagnosticSeverity.Error,
        "The solution is moving away from a balance: {residual} after {steps} steps, from {previous}.");

    /// <summary>Steps fell below tolerance while the residual stayed above it.</summary>
    /// <value><c>FS3004</c>, an error.</value>
    /// <remarks>
    /// Distinct from <see cref="IterationCap"/> on purpose: the cap means it was still moving and ran
    /// out of steps, and this means it stopped moving. The first is answered by allowing more steps and
    /// the second never is, so telling a user to wait longer would be wrong exactly here.
    /// </remarks>
    public static DiagnosticDescriptor Stalled { get; } = new(
        "FS3004",
        DiagnosticSeverity.Error,
        "Stuck at {residual}: {component} {equation} may have conflicting requirements.");

    /// <summary>A solver refused a system before trying to solve it.</summary>
    /// <value><c>FS3005</c>, an error.</value>
    /// <remarks>
    /// Checked before solving so the answer is a sentence rather than a divergence. Two refusals exist
    /// in v1: a steady solver handed a model that integrates in time, and a system whose rows and
    /// columns disagree — which is an assembly defect rather than a user's, and says so.
    /// </remarks>
    public static DiagnosticDescriptor Refused { get; } = new(
        "FS3005",
        DiagnosticSeverity.Error,
        "{solver} cannot solve this: {reason}.");

    /// <summary>The caller cancelled between iterations.</summary>
    /// <value><c>FS3006</c>, informational.</value>
    public static DiagnosticDescriptor Cancelled { get; } = new(
        "FS3006",
        DiagnosticSeverity.Info,
        "Solve cancelled after {steps} steps.");

    /// <summary>A residual or an iterate was not a finite number.</summary>
    /// <value><c>FS3007</c>, an error.</value>
    /// <remarks>
    /// Separated from the property domain guard, which is an ordinary event a line search handles. This
    /// is a residual that came back as a NaN or an infinity from a state the fluid accepted, which is a
    /// component defect rather than a hard circuit.
    /// </remarks>
    public static DiagnosticDescriptor NonFinite { get; } = new(
        "FS3007",
        DiagnosticSeverity.Error,
        "{component} produced an impossible value in {equation} after {steps} steps.");

    /// <summary>The line search reached its smallest step without improving.</summary>
    /// <value><c>FS3011</c>, informational.</value>
    /// <remarks>
    /// Information rather than a warning: the step is taken anyway and the divergence check catches it
    /// next iteration, because refusing to move is how a solver stalls forever. A user does not need
    /// this; a support conversation does.
    /// </remarks>
    public static DiagnosticDescriptor ReducedStep { get; } = new(
        "FS3011",
        DiagnosticSeverity.Info,
        "Taking a reduced step near {component}; the solution is hard to reach here.");

    /// <summary>Gets every code this area defines, in code order.</summary>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        IterationCap,
        Singular,
        Diverging,
        Stalled,
        Refused,
        Cancelled,
        NonFinite,
        ReducedStep,
    ];
}
