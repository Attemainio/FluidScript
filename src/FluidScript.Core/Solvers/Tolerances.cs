namespace FluidScript.Core.Solvers;

/// <summary>
/// Every numerical tolerance, threshold and cap the solver stack uses.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is <c>36</c>'s table, transcribed once.</strong> That document's first invariant says
/// every tolerance used anywhere in Core comes from it and that no numeric tolerance literal appears
/// in solver code; this type is what makes the second half enforceable, and
/// <c>ToleranceTableTests</c> parses the document's own markdown table and asserts both directions —
/// every key here carries the documented value, and every documented key is implemented.
/// </para>
/// <para>
/// <strong>It exists because two of these numbers had already drifted out of reach.</strong>
/// <c>valve.dp_regularization</c> and <c>upwind.smoothing_band</c> were hand-copied into two unrelated
/// component files, and a change to the table reached neither (<c>S-6</c>). They are numerical-method
/// parameters that happen to be evaluated inside a component, which is why
/// <see cref="Components.ValveLaw.RegularizationDrop"/> and
/// <see cref="Components.Smoothing.UpwindBand"/> now read from here rather than the other way round:
/// the table is the source, and a component is one of its consumers.
/// </para>
/// <para>
/// <strong>The whole table ships, not the part in use.</strong> The transient and optimizer rows have
/// no consumer until <c>P4</c> and <c>M6</c>, and shipping them anyway is deliberate: a second table
/// for "the numbers we need now" is exactly the arrangement <c>S-6</c> records. The document test
/// covers a value nothing reads yet, which is the only thing that could otherwise transcribe wrong
/// and stay wrong.
/// </para>
/// <para>
/// <strong>These are defaults, not user-facing settings.</strong> A user who needs to change a solver
/// tolerance has hit a bug in this table.
/// </para>
/// </remarks>
public static class Tolerances
{
    /// <summary>The scaled residual norm below which Newton has converged.</summary>
    /// <value>
    /// <c>newton.residual_tol</c>, dimensionless: <c>‖F‖∞</c> after scaling. Eight digits on a scaled
    /// residual is roughly 1e-3 Pa and 2e-9 kg/s at the worked example's scales — far below anything
    /// physical, and reachable in four or five iterations from a warm start.
    /// </value>
    public const double NewtonResidual = 1e-8;

    /// <summary>The scaled step norm below which Newton is stalled rather than converging.</summary>
    /// <value><c>newton.step_tol</c>, dimensionless: <c>‖Δx‖∞</c> after scaling.</value>
    public const double NewtonStep = 1e-10;

    /// <summary>The most Newton iterations one solve may take.</summary>
    /// <value>
    /// <c>newton.max_iterations</c>. Ten times a normal solve: reaching it means something is wrong,
    /// not that the circuit is slow.
    /// </value>
    public const int NewtonMaxIterations = 50;

    /// <summary>The residual growth ratio that counts as divergence.</summary>
    /// <value>
    /// <c>newton.divergence_factor</c>, dimensionless: <c>‖F_k‖ / ‖F_{k−1}‖</c>. A tenfold increase is
    /// divergence, not a line-search excursion.
    /// </value>
    public const double NewtonDivergenceFactor = 10.0;

    /// <summary>The smallest line-search step the backtracking loop will try.</summary>
    /// <value>
    /// <c>newton.line_search_min</c>, dimensionless: <c>α</c>. Six halvings; beyond that the step
    /// length is not what is wrong.
    /// </value>
    public const double NewtonLineSearchMin = 1.0 / 64.0;

    /// <summary>The relative perturbation a forward-difference Jacobian column uses.</summary>
    /// <value>
    /// <c>newton.fd_step</c>, dimensionless and relative: <c>√ε</c> for double precision, which
    /// balances truncation error against round-off. Written as its literal value because
    /// <c>Math.Sqrt</c> is not a compile-time constant; a test asserts the two agree exactly.
    /// </value>
    public const double NewtonFiniteDifferenceStep = 1.4901161193847656e-8;

    /// <summary>The pivot magnitude, relative to the matrix norm, below which the Jacobian is singular.</summary>
    /// <value>
    /// <c>jacobian.singular_tol</c>, dimensionless: <c>pivot / ‖J‖∞</c>. Twelve orders below the
    /// matrix norm is numerically zero.
    /// </value>
    public const double JacobianSingular = 1e-12;

    /// <summary>The most passes the sizing and deferred-expression loop may take.</summary>
    /// <value><c>outer.max_passes</c>. Typical convergence is three.</value>
    public const int OuterMaxPasses = 10;

    /// <summary>The relative change below which a sized or deferred value has settled.</summary>
    /// <value>
    /// <c>outer.relative_tol</c>, dimensionless and relative. Half a percent — below the accuracy of
    /// the correlations themselves, so tightening it buys nothing real.
    /// </value>
    public const double OuterRelative = 5e-3;

    /// <summary>The margin the transient step keeps below its stability limit.</summary>
    /// <value><c>transient.cfl_safety</c>, dimensionless: <c>Δt / τ_min</c>.</value>
    public const double TransientCflSafety = 0.9;

    /// <summary>The scaled per-step local error a transient step must stay under.</summary>
    /// <value><c>transient.local_error_tol</c>, dimensionless, relative to the state scales.</value>
    public const double TransientLocalError = 1e-4;

    /// <summary>The longest transient step, whatever stability would allow.</summary>
    /// <value><c>transient.max_step</c>, seconds. Keeps frames responsive.</value>
    public const double TransientMaxStep = 10.0;

    /// <summary>The shortest transient step before the run gives up.</summary>
    /// <value><c>transient.min_step</c>, seconds. Below this the run stops with <c>FS3102</c>.</value>
    public const double TransientMinStep = 1e-4;

    /// <summary>The relative energy drift over a run that is worth warning about.</summary>
    /// <value><c>transient.energy_drift_tol</c>, dimensionless and relative. One percent triggers <c>FS3106</c>.</value>
    public const double TransientEnergyDriftWarn = 1e-2;

    /// <summary>The relative energy drift over a run that stops it.</summary>
    /// <value><c>transient.energy_drift_fail</c>, dimensionless and relative. Five percent stops with <c>FS3107</c>.</value>
    public const double TransientEnergyDriftFail = 5e-2;

    /// <summary>The pressure drop below which a valve's square-root law is blended.</summary>
    /// <value>
    /// <c>valve.dp_regularization</c>, pascals, always compared against a magnitude so the law stays
    /// odd. 100 Pa is three orders below any meaningful valve drop, so the regularized region is never
    /// a real design's operating point — only somewhere the solver passes through.
    /// </value>
    public const double ValveRegularizationDrop = 100.0;

    /// <summary>The signed mass flow band over which node enthalpy upwinding blends.</summary>
    /// <value>
    /// <c>upwind.smoothing_band</c>, kg/s, applied to a signed flow: positive into the node. Wider
    /// than <see cref="FlowZero"/> deliberately — zero-flow <em>detection</em> wants a tight
    /// threshold, derivative <em>smoothing</em> wants a band the Newton step can resolve.
    /// </value>
    public const double UpwindSmoothingBand = 1e-3;

    /// <summary>The mass flow magnitude below which a branch carries no flow.</summary>
    /// <value>
    /// <c>flow.zero_tol</c>, kg/s, compared as a magnitude so direction does not matter. Roughly one
    /// microlitre per second of water: below any physical relevance.
    /// </value>
    public const double FlowZero = 1e-6;

    /// <summary>The relative tolerance two quantities are compared with.</summary>
    /// <value>
    /// <c>quantity.compare_rel_tol</c>, dimensionless and relative. Comparison, not convergence: it
    /// says when two computed numbers are the same number, not when a solve is finished.
    /// </value>
    public const double QuantityCompareRelative = 1e-9;

    /// <summary>The relative change below which a deferred expression has settled.</summary>
    /// <value>
    /// <c>fixed_point.rel_tol</c>, dimensionless and relative. The same value as
    /// <see cref="OuterRelative"/> because it is the same loop and the same convergence test
    /// (<c>31</c>); they are separate entries so a future change to one is a visible decision rather
    /// than a silent divergence.
    /// </value>
    public const double FixedPointRelative = 5e-3;

    /// <summary>The relative rounding applied to a continuous value in the evaluation cache key.</summary>
    /// <value><c>optimizer.cache_round</c>, dimensionless and relative.</value>
    public const double OptimizerCacheRound = 1e-4;

    /// <summary>The number of individuals in a generation.</summary>
    /// <value><c>optimizer.population_size</c>.</value>
    public const int OptimizerPopulationSize = 50;

    /// <summary>The most generations an optimization may run.</summary>
    /// <value><c>optimizer.generation_cap</c>. Ten thousand evaluations at the default population.</value>
    public const int OptimizerGenerationCap = 200;

    /// <summary>The consecutive generations without material improvement that end a run.</summary>
    /// <value><c>optimizer.stagnation_generations</c>.</value>
    public const int OptimizerStagnationGenerations = 25;

    /// <summary>The relative improvement in the best feasible objective that counts as progress.</summary>
    /// <value>
    /// <c>optimizer.stagnation_rel_tol</c>, dimensionless and relative, so it means the same thing
    /// across kWh, currency and kPa objectives.
    /// </value>
    public const double OptimizerStagnationRelative = 1e-4;

    /// <summary>The reference magnitude every node pressure is divided by.</summary>
    /// <value>
    /// <c>scale.pressure</c>, pascals. Roughly one bar, the natural magnitude of a hydronic circuit.
    /// <para>
    /// A scale is not a tolerance, and it is in this table for the same reason the tolerances are:
    /// <c>36</c> stated it in prose beside a table it did not key, so nothing checked it and nothing
    /// could. Keyed, it is covered by the same both-directions document test as every other row.
    /// </para>
    /// </value>
    public const double PressureScale = 1e5;

    /// <summary>The reference magnitude every node enthalpy is divided by.</summary>
    /// <value><c>scale.enthalpy</c>, J/kg: a typical liquid-water enthalpy over the working range.</value>
    public const double EnthalpyScale = 1e5;

    /// <summary>The floor under a branch's own flow scale.</summary>
    /// <value>
    /// <c>scale.flow_floor</c>, kg/s, applied to a magnitude. Mass flow is scaled <em>per branch</em>
    /// rather than per circuit, so a bypass at 0.05 kg/s beside a primary at 10 is measured against
    /// its own size; this stops a branch the seed puts at rest dividing by zero. Three orders above
    /// <see cref="FlowZero"/>, which detects a branch that is genuinely not flowing.
    /// </value>
    public const double FlowScaleFloor = 1e-3;

    /// <summary>The reference magnitude a directly solved temperature is divided by.</summary>
    /// <value>
    /// <c>scale.temperature</c>, kelvin: a typical ΔT. Unused while every thermal unknown is an
    /// enthalpy, and kept because the table is the table (<c>D-69</c> did not change what is solved,
    /// and a controller that solves a temperature directly will want it).
    /// </value>
    public const double TemperatureScale = 1e1;
}
