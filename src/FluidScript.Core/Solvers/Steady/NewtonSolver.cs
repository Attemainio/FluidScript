using System.Collections.Immutable;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Primitives;
using FluidScript.Core.Solvers.Equations;

namespace FluidScript.Core.Solvers.Steady;

/// <summary>Finds the state where every residual is zero.</summary>
/// <remarks>
/// <para>
/// Newton is right here for a specific reason: the system is square, its Jacobian is cheap relative to
/// a property evaluation, and the physics is smooth almost everywhere. The word doing the work is
/// "almost" — the line search, the domain guard and the singularity diagnosis are all about the places
/// it is not.
/// </para>
/// <para>
/// <strong>Everything is solved scaled, and the raw system never is</strong> (<c>32</c>'s invariant 1).
/// The Newton direction is invariant under scaling in exact arithmetic, so this changes no answer; what
/// it changes is that one tolerance means one thing for a pressure row and a mass row, that pivots are
/// chosen by physics rather than by units, and that the line search's merit is not simply the pressure
/// residual.
/// </para>
/// <para>
/// <strong>It is deterministic, and that rules things out.</strong> Jacobian columns are independent
/// and tempting to evaluate in parallel; doing so must preserve accumulation order or not be done. A
/// solver that answers slightly differently across runs makes every golden file flaky and every user
/// report unreproducible (<c>32</c>'s invariant 6).
/// </para>
/// </remarks>
public sealed partial class NewtonSolver : ISolver
{
    private const double ArmijoFactor = 1e-4;

    /// <summary>How many participants in a null direction <c>FS3009</c> names before summarising.</summary>
    /// <remarks>
    /// Six. A direction with more participants than that is describing a circuit under-specified in a way
    /// no list fixes, and the first six are the ones carrying the largest share of it.
    /// </remarks>
    private const int NamedParticipants = 6;

    private readonly NewtonSettings _settings;

    /// <summary>Initializes a solver.</summary>
    /// <param name="settings">Tuning, or <see langword="null"/> for <c>36</c>'s defaults.</param>
    public NewtonSolver(NewtonSettings? settings = null) => _settings = settings ?? new NewtonSettings();

    /// <inheritdoc/>
    public string Name => "newton";

    /// <inheritdoc/>
    /// <remarks>
    /// One refusal: a system whose rows and columns disagree has no unique solution to look for, and
    /// that disagreement is an assembly defect rather than a user's, so the message says so. A transient
    /// graph's system is accepted in both of its forms (<c>31</c>): unpinned it is the equilibrium the
    /// design solve wants (<c>D-141</c>), pinned it is one step's algebraic problem (<c>D-139</c>), and
    /// both are square balances this solver is for. Stiffness is not checked here; it is the step's,
    /// <c>FS3102</c>.
    /// </remarks>
    public Result<Unit> CanSolve(EquationSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        return system.Rows == system.Columns
            ? Result.Success(Unit.Value)
            : Refuse($"it has {system.Rows} equations for {system.Columns} unknowns");
    }

    /// <inheritdoc/>
    public Task<SolveResult> SolveAsync(
        EquationSystem system,
        StateVector initialGuess,
        IProgress<SolveProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(initialGuess);

        return Task.FromResult(Solve(system, initialGuess, progress, cancellationToken));
    }

    /// <summary>Runs the iteration.</summary>
    /// <param name="system">The assembled system.</param>
    /// <param name="initialGuess">The starting iterate.</param>
    /// <param name="progress">Per-iteration progress, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Checked between iterations only.</param>
    /// <returns>The result.</returns>
    private SolveResult Solve(
        EquationSystem system,
        StateVector initialGuess,
        IProgress<SolveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var columns = system.Columns;
        var rows = system.Rows;

        var x = initialGuess.Values.ToArray();
        var trial = new double[columns];
        var residuals = new double[rows];
        var perturbed = new double[rows];
        var jacobian = new double[rows * columns];
        var step = new double[columns];
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        var previous = double.PositiveInfinity;

        // Recorded once per parameter and reported at the end, and only for a bound the final iterate is
        // still on. A projection on the path says nothing about the answer: a promoted Kv seeded at the
        // catalogue's largest row overshoots to a negative Kv on its first step, is held at 0, and then
        // walks up to 0.77 -- reporting `FS3008` for that walk claimed the circuit asked for more than
        // the valve could give, of a valve that was three quarters open at the solution (`S-61`).
        var pinned = new Dictionary<int, double>();
        var history = ImmutableArray.CreateBuilder<IterationRecord>();

        for (var iteration = 1; iteration <= _settings.MaxIterations; iteration++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                diagnostics.Add(Diagnostic.Create(
                    SolverDiagnostics.Cancelled, null, Count("steps", iteration - 1)));

                return Stop(system, x, iteration - 1, previous, SolveTermination.Cancelled, diagnostics, pinned, history);
            }

            if (!system.TryEvaluateScaled(x, residuals))
            {
                return OutOfDomain(system, x, iteration - 1, previous, diagnostics, pinned, history);
            }

            var nonFinite = Array.FindIndex(residuals, static value => !double.IsFinite(value));

            if (nonFinite >= 0)
            {
                diagnostics.Add(Diagnostic.Create(
                    SolverDiagnostics.NonFinite,
                    null,
                    new DiagnosticArgument("component", system.Equations.Rows[nonFinite].OwnerComponentId),
                    new DiagnosticArgument("equation", system.Equations.Rows[nonFinite].Name),
                    Count("steps", iteration - 1)));

                return Stop(system, x, iteration - 1, double.NaN, SolveTermination.NonFinite, diagnostics, pinned, history);
            }

            var norm = Norm(residuals);

            if (norm < _settings.ResidualTolerance)
            {
                return Stop(system, x, iteration - 1, norm, SolveTermination.Converged, diagnostics, pinned, history);
            }

            if (norm > previous * _settings.DivergenceFactor)
            {
                diagnostics.Add(Diagnostic.Create(
                    SolverDiagnostics.Diverging,
                    null,
                    Number("residual", norm),
                    Count("steps", iteration - 1),
                    Number("previous", previous)));

                return Stop(system, x, iteration - 1, norm, SolveTermination.Diverging, diagnostics, pinned, history);
            }

            if (!Jacobian(system, x, residuals, trial, perturbed, jacobian))
            {
                return OutOfDomain(system, x, iteration - 1, norm, diagnostics, pinned, history);
            }

            var factored = DenseLu.Factor(jacobian, columns);

            if (factored.IsSingular)
            {
                diagnostics.Add(Diagnostic.Create(
                    SolverDiagnostics.Singular,
                    null,
                    new DiagnosticArgument(
                        "component",
                        system.Unknowns.Unknowns[factored.SingularColumn].OwnerComponentId)));

                // DenseLu overwrote the Jacobian factorising it, so this rebuilds one to eliminate
                // again with full pivoting. N+1 residual evaluations on a run that has already stopped.
                var (free, implied) = Deficiency(system, x, residuals, trial, perturbed, columns);

                if (free is not null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        SolverDiagnostics.Undetermined,
                        null,
                        new DiagnosticArgument("combination", free)));
                }

                // The row direction is the one that names the defect: which equation the others already
                // imply. `FS3009` names whichever columns the elimination left free, and following that
                // instead cost three sessions of pump arrangements on the header (`S-36`).
                if (implied is not null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        SolverDiagnostics.Redundant,
                        null,
                        new DiagnosticArgument("combination", implied)));
                }

                return Stop(system, x, iteration - 1, norm, SolveTermination.Singular, diagnostics, pinned, history);
            }

            for (var row = 0; row < rows; row++)
            {
                step[row] = -residuals[row];
            }

            factored.Solve(step);

            var alpha = LineSearch(system, x, step, norm, trial, perturbed);

            if (alpha < 0)
            {
                return OutOfDomain(system, x, iteration - 1, norm, diagnostics, pinned, history);
            }

            if (alpha <= _settings.MinLineSearchStep)
            {
                diagnostics.Add(Diagnostic.Create(
                    SolverDiagnostics.ReducedStep,
                    null,
                    new DiagnosticArgument(
                        "component", system.Equations.Rows[Worst(residuals)].OwnerComponentId)));
            }

            var scaledStep = 0.0;
            var moved = 0;

            for (var column = 0; column < columns; column++)
            {
                if (Math.Abs(alpha * step[column]) > scaledStep)
                {
                    moved = column;
                }

                scaledStep = Math.Max(scaledStep, Math.Abs(alpha * step[column]));
                x[column] += alpha * step[column] * system.UnknownScales[column];
            }

            // The trajectory, for the report (`S-71`): what this step set out to remove, how much of it
            // the line search took, which row led and which column moved.
            var leading = system.Equations.Rows[Worst(residuals)];

            history.Add(new IterationRecord(
                iteration,
                norm,
                alpha,
                $"{leading.OwnerComponentId}: {leading.Name}",
                system.Unknowns.Unknowns[moved].Name,
                scaledStep));

            // `S-26b`. A promoted `position` is a fraction and a `head` is not negative; Newton knows
            // neither, and left alone the cooling loop's split walks to about 5.5. Projecting after the
            // step rather than constraining the step keeps the line search's own arithmetic untouched,
            // and it is safe only because `S-26a` made the residual differentiable at a bound -- while
            // `Opening` clamped, landing on a bound put the iterate exactly where its column was dead.
            if (system.Project(x, out var bound))
            {
                pinned[bound] = x[bound];
            }

            progress?.Report(new SolveProgress(iteration, norm, alpha));

            if (scaledStep < _settings.StepTolerance)
            {
                // The step was tiny, and where it landed has not been measured yet. Under quadratic
                // convergence the last step is the size of the residual it removes, so a residual just
                // above the tolerance produces a step under the step tolerance and a point that is
                // converged: S-29's series ring went 1 -> 0.37 -> 1.4e-3 -> 1.16e-8 -> 2.4e-12 and was
                // reported stalled at the fourth figure (`S-67`). Read the new point before calling it
                // a stall; a stall is a tiny step that leaves the residual where it was.
                if (system.TryEvaluateScaled(x, residuals))
                {
                    norm = Norm(residuals);

                    if (norm < _settings.ResidualTolerance)
                    {
                        return Stop(system, x, iteration, norm, SolveTermination.Converged, diagnostics, pinned, history);
                    }
                }

                var worst = system.Equations.Rows[Worst(residuals)];

                diagnostics.Add(Diagnostic.Create(
                    SolverDiagnostics.Stalled,
                    null,
                    Number("residual", norm),
                    new DiagnosticArgument("component", worst.OwnerComponentId),
                    new DiagnosticArgument("equation", worst.Name)));

                return Stop(system, x, iteration, norm, SolveTermination.Stalled, diagnostics, pinned, history);
            }

            previous = norm;
        }

        var final = system.TryEvaluateScaled(x, residuals) ? Norm(residuals) : double.NaN;
        var furthest = Worst(residuals);
        var declaration = system.Equations.Rows[furthest];

        diagnostics.Add(Diagnostic.Create(
            SolverDiagnostics.IterationCap,
            null,
            Count("steps", _settings.MaxIterations),
            new DiagnosticArgument("component", declaration.OwnerComponentId),
            new DiagnosticArgument("equation", declaration.Name),
            new DiagnosticArgument(
                "amount",
                Amount(residuals[furthest] * system.ResidualScales[furthest], declaration.ResidualSiUnit))));

        return Stop(system, x, _settings.MaxIterations, final, SolveTermination.IterationCap, diagnostics, pinned, history);
    }

    /// <summary>Builds the scaled Jacobian by forward differences.</summary>
    /// <param name="system">The assembled system.</param>
    /// <param name="x">The iterate, whose node states the system has cached.</param>
    /// <param name="residuals">The scaled residuals at <paramref name="x"/>.</param>
    /// <param name="trial">Scratch, one per unknown.</param>
    /// <param name="perturbed">Scratch, one per equation.</param>
    /// <param name="jacobian">Destination, row-major.</param>
    /// <returns><see langword="false"/> when a perturbed point left the property domain.</returns>
    /// <remarks>
    /// <para>
    /// The step is <c>√ε · max(|x_j|, scale_j)</c> rather than <c>√ε · |x_j|</c>, and the difference is
    /// not cosmetic: an unknown that is legitimately zero — a closed valve's branch flow — would get a
    /// step of zero and a column of zeros, producing a singular Jacobian at exactly the state a real
    /// circuit sits in.
    /// </para>
    /// <para>
    /// Columns go through <see cref="EquationSystem.TryEvaluateScaledAt"/>, which re-fixes only the one
    /// node a perturbation can change. Perturbing a flow, a flux or a parameter changes none at all, so
    /// most of a Jacobian costs no property call whatever — and it is the <em>scaled</em> overload,
    /// because differencing a scaled base against a raw perturbation multiplies every entry by that
    /// row's reference magnitude and reports a healthy circuit as singular.
    /// </para>
    /// </remarks>
    private static bool Jacobian(
        EquationSystem system,
        double[] x,
        double[] residuals,
        double[] trial,
        double[] perturbed,
        double[] jacobian)
    {
        var columns = system.Columns;
        var rows = system.Rows;

        Array.Copy(x, trial, columns);

        for (var column = 0; column < columns; column++)
        {
            var scale = system.UnknownScales[column];
            var kind = system.Unknowns.Unknowns[column].Kind;
            var delta = Tolerances.FiniteDifferenceStep(kind) * Math.Max(Math.Abs(x[column]), scale);

            trial[column] = x[column] + delta;

            if (!system.TryEvaluateScaledAt(trial, column, perturbed))
            {
                return false;
            }

            // The scaled derivative. The residuals are already divided by their own references and the
            // step by this column's, which is what makes this the Jacobian of the scaled system rather
            // than the raw one with two corrections applied afterwards.
            var scaledDelta = delta / scale;

            for (var row = 0; row < rows; row++)
            {
                jacobian[(row * columns) + column] = (perturbed[row] - residuals[row]) / scaledDelta;
            }

            // A pressure column is read twice: once at the step that clears the flash's noise, for every
            // row, and once at √ε for the rows a valve marks steep, whose √Δp law may sit at a drop
            // smaller than the first step (`S-74`). Nothing else a pressure column feeds is steep.
            if (kind == Components.Declarations.UnknownKind.NodePressure)
            {
                var fine = Tolerances.NewtonFiniteDifferenceStep * Math.Max(Math.Abs(x[column]), scale);

                trial[column] = x[column] + fine;

                if (!system.TryEvaluateScaledAt(trial, column, perturbed))
                {
                    return false;
                }

                var scaledFine = fine / scale;

                for (var row = 0; row < rows; row++)
                {
                    if (system.Equations.Rows[row].SteepInPressure)
                    {
                        jacobian[(row * columns) + column] = (perturbed[row] - residuals[row]) / scaledFine;
                    }
                }
            }

            trial[column] = x[column];
        }

        return true;
    }

    /// <summary>Finds how far along a Newton direction to move.</summary>
    /// <param name="system">The assembled system.</param>
    /// <param name="x">The current iterate.</param>
    /// <param name="step">The scaled Newton direction.</param>
    /// <param name="norm">The scaled residual norm at <paramref name="x"/>.</param>
    /// <param name="trial">Scratch, one per unknown.</param>
    /// <param name="residuals">Scratch, one per equation.</param>
    /// <returns>The factor to take, or <c>-1</c> when no tried point had a fluid state.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The domain guard comes before the Armijo test, not after it.</strong> A trial point
    /// outside the substance's range is rejected and the step halved without residuals ever being asked
    /// for there. Without that, the first thing a poor initial guess does is ask the property backend
    /// for water at minus three bar.
    /// </para>
    /// <para>
    /// <strong>At the smallest step the move is taken anyway.</strong> Refusing to move is how a solver
    /// stalls forever; the divergence check catches a bad step next iteration, and <c>FS3011</c> says it
    /// happened.
    /// </para>
    /// </remarks>
    private double LineSearch(
        EquationSystem system,
        double[] x,
        double[] step,
        double norm,
        double[] trial,
        double[] residuals)
    {
        var alpha = 1.0;
        var accepted = -1.0;

        while (true)
        {
            for (var column = 0; column < system.Columns; column++)
            {
                trial[column] = x[column] + (alpha * step[column] * system.UnknownScales[column]);
            }

            if (system.TryEvaluateScaled(trial, residuals))
            {
                accepted = alpha;

                if (Norm(residuals) <= (1 - (ArmijoFactor * alpha)) * norm)
                {
                    return alpha;
                }
            }

            if (alpha <= _settings.MinLineSearchStep)
            {
                return accepted;
            }

            alpha /= 2;
        }
    }

    /// <summary>The scaled infinity norm.</summary>
    /// <param name="residuals">The scaled residuals.</param>
    /// <returns>The largest magnitude among them.</returns>
    private static double Norm(double[] residuals)
    {
        var norm = 0.0;

        foreach (var residual in residuals)
        {
            norm = Math.Max(norm, Math.Abs(residual));
        }

        return norm;
    }

    /// <summary>The row furthest from satisfied.</summary>
    /// <param name="residuals">The scaled residuals.</param>
    /// <returns>Its index.</returns>
    private static int Worst(double[] residuals)
    {
        var worst = 0;

        for (var row = 1; row < residuals.Length; row++)
        {
            if (Math.Abs(residuals[row]) > Math.Abs(residuals[worst]))
            {
                worst = row;
            }
        }

        return worst;
    }
}
