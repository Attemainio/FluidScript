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
/// <c>FS3012</c> is raised by <c>31</c>'s outer loop rather than by this solver: the solver is handed
/// exactly one starting vector, and on a re-solve that vector <em>is</em> the warm start, so it has no
/// second seed to retry from. The loop holds both, discards a warm start that did not converge and
/// runs the pass again from the sizing seed, and says so with this code (<c>S-20</c>).
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

    /// <summary>The answer wanted a promoted parameter outside the range its component allows.</summary>
    /// <value><c>FS3008</c>, a warning.</value>
    /// <remarks>
    /// A warning rather than an error, and the distinction is the user's problem rather than the
    /// solver's: the circuit is well-posed and square, and what it is asking for is a valve open past
    /// fully open or a pump with negative head. The number reported is the bound, because that is what
    /// the solve actually used — reporting the unclamped value would name a state no component was ever
    /// evaluated in. It is raised for the <em>final</em> iterate only: a bound the path merely passed
    /// through says nothing about the answer (<c>S-61</c>).
    /// </remarks>
    public static DiagnosticDescriptor ParameterPinned { get; } = new(
        "FS3008",
        DiagnosticSeverity.Warning,
        "{parameter} was held at {bound}, which is as far as it goes. The circuit is asking for more "
        + "than this component can give: check the duty, the resistance, or a stated temperature it "
        + "cannot reach.");

    /// <summary>The combination of unknowns a singular Jacobian left undetermined.</summary>
    /// <value><c>FS3009</c>, an error, and it rides alongside <see cref="Singular"/> rather than replacing it.</value>
    /// <remarks>
    /// <para>
    /// <strong><see cref="Singular"/> names one component and that is often the wrong one.</strong>
    /// Partial pivoting stops at whichever column it reaches first, which need not be the column most
    /// responsible; on <c>m2-distribution-header</c> it named <c>PU_RAD</c> for a direction in which
    /// <c>PU_MAIN</c> participates just as strongly, and then suggested checking for a missing pressure
    /// datum on a script that states one. This descriptor carries what was measured instead of what was
    /// guessed (<c>S-33</c>).
    /// </para>
    /// <para>
    /// <strong>Two codes for one stop is deliberate.</strong> <c>FS3002</c> is why the run ended and
    /// stays the termination's code; this is what the run found, and it is absent when the direction is
    /// not recoverable — a matrix of zeros leaves everything undetermined, which names nothing.
    /// </para>
    /// </remarks>
    public static DiagnosticDescriptor Undetermined { get; } = new(
        "FS3009",
        DiagnosticSeverity.Error,
        "Nothing in the circuit determines {combination}. These move together and no equation "
        + "separates them, so a value stated for any one of them determines the rest.");

    /// <summary>The equation a singular system's other rows already imply.</summary>
    /// <value><c>FS3010</c>, an error, reported alongside <see cref="Undetermined"/> and never instead of it.</value>
    /// <remarks>
    /// <para>
    /// <strong>This is the half of a singular system that names the defect</strong> (<c>S-36</c>). A
    /// square system short by one has two null directions: <see cref="Undetermined"/> reports which
    /// <em>unknowns</em> are left free, which reads like a cause and is not — after full pivoting those
    /// are whichever columns the elimination happened to leave over. This reports which
    /// <em>equation</em> the others already imply, which is the row that has to change.
    /// </para>
    /// <para>
    /// <strong>The cost of having only the first one is measured.</strong> On
    /// <c>m2-distribution-header</c> the unknown direction named pumps every time, and three sessions
    /// followed it — four pump arrangements built, measured and eliminated, each deficient by exactly
    /// one, because the deficiency was never about pumps. The equation direction on the same system
    /// names one node's <em>mass</em> balance against <em>every energy balance in the circuit</em>.
    /// </para>
    /// <para>
    /// Both are absent when the direction is not recoverable, and a user seeing only <c>FS3002</c> is
    /// looking at a system with no rank at all rather than one short by one.
    /// </para>
    /// </remarks>
    public static DiagnosticDescriptor Redundant { get; } = new(
        "FS3010",
        DiagnosticSeverity.Error,
        "{combination} are not independent: one of them is already implied by the others, so the "
        + "circuit constrains one thing fewer than it appears to. Stating something elsewhere will not "
        + "help — one of these has to change.");

    /// <summary>A converged solve runs a directional component backwards.</summary>
    /// <value><c>FS3013</c>, a warning.</value>
    /// <remarks>
    /// <para>
    /// <strong>A reversal is a real answer and this does not overrule it</strong> (<c>D-69</c>): a heater
    /// heats whichever node it discharges into. What it does is say so, because the statements that
    /// mention a port stay on that port. <c>HE1 in=50</c> is bound to the port the connection order made
    /// the inlet; if the water enters at <c>out</c>, the constraint now holds the temperature the water
    /// <em>leaves</em> at — the automation-system failure it mirrors, where T1 stays mounted where the
    /// installer assumed the flow ran (<c>L-47</c>).
    /// </para>
    /// <para>
    /// Only pumps and exchangers are reported. A pipe's <c>in</c> and <c>out</c> record the order the
    /// author typed the connection and assert nothing about the water; a pump and a stated terminal do.
    /// </para>
    /// </remarks>
    public static DiagnosticDescriptor ReversedFlow { get; } = new(
        "FS3013",
        DiagnosticSeverity.Warning,
        "{component} carries {flow} kg/s from '{outlet}' to '{inlet}', against its written direction{note}.");
    /// <summary>A warm start did not converge and the pass was rerun from the sizing seed.</summary>
    /// <value><c>FS3012</c>, an info.</value>
    /// <remarks>
    /// Recovery, not failure: a user edits a value, the previous solution lands in the wrong basin, and
    /// the cold seed converges. Nothing in the answer changed; a support conversation wants to know the
    /// retry happened, which is what the console log shows it for (<c>32</c>, <c>56</c>).
    /// </remarks>
    public static DiagnosticDescriptor RestartedFromSeed { get; } = new(
        "FS3012",
        DiagnosticSeverity.Info,
        "Restarted from the initial estimate.");

    /// <summary>Gets every code this area defines, in code order.</summary>
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
        ParameterPinned,
        Undetermined,
        Redundant,
        ReducedStep,
        RestartedFromSeed,
        ReversedFlow,
    ];
}
