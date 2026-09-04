using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Fluids;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Solvers;

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
public sealed class NewtonSolver : ISolver
{
    private const double ArmijoFactor = 1e-4;

    private readonly NewtonSettings _settings;

    /// <summary>Initializes a solver.</summary>
    /// <param name="settings">Tuning, or <see langword="null"/> for <c>36</c>'s defaults.</param>
    public NewtonSolver(NewtonSettings? settings = null) => _settings = settings ?? new NewtonSettings();

    /// <inheritdoc/>
    public string Name => "newton";

    /// <inheritdoc/>
    /// <remarks>
    /// Two refusals. A transient system needs the solver that integrates rather than the one that
    /// balances; a system whose rows and columns disagree has no unique solution to look for, and that
    /// disagreement is an assembly defect rather than a user's, so the message says so.
    /// </remarks>
    public Result<Unit> CanSolve(EquationSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        if (system.Mode is not SolveMode.Steady)
        {
            return Refuse($"it is solved in time rather than as a balance ({system.Mode})");
        }

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

        for (var iteration = 1; iteration <= _settings.MaxIterations; iteration++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                diagnostics.Add(Diagnostic.Create(
                    SolverDiagnostics.Cancelled, null, Count("steps", iteration - 1)));

                return Stop(system, x, iteration - 1, previous, SolveTermination.Cancelled, diagnostics);
            }

            if (!system.TryEvaluateScaled(x, residuals))
            {
                return OutOfDomain(system, x, iteration - 1, previous, diagnostics);
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

                return Stop(system, x, iteration - 1, double.NaN, SolveTermination.NonFinite, diagnostics);
            }

            var norm = Norm(residuals);

            if (norm < _settings.ResidualTolerance)
            {
                return Stop(system, x, iteration - 1, norm, SolveTermination.Converged, diagnostics);
            }

            if (norm > previous * _settings.DivergenceFactor)
            {
                diagnostics.Add(Diagnostic.Create(
                    SolverDiagnostics.Diverging,
                    null,
                    Number("residual", norm),
                    Count("steps", iteration - 1),
                    Number("previous", previous)));

                return Stop(system, x, iteration - 1, norm, SolveTermination.Diverging, diagnostics);
            }

            if (!Jacobian(system, x, residuals, trial, perturbed, jacobian))
            {
                return OutOfDomain(system, x, iteration - 1, norm, diagnostics);
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

                return Stop(system, x, iteration - 1, norm, SolveTermination.Singular, diagnostics);
            }

            for (var row = 0; row < rows; row++)
            {
                step[row] = -residuals[row];
            }

            factored.Solve(step);

            var alpha = LineSearch(system, x, step, norm, trial, perturbed);

            if (alpha < 0)
            {
                return OutOfDomain(system, x, iteration - 1, norm, diagnostics);
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

            for (var column = 0; column < columns; column++)
            {
                scaledStep = Math.Max(scaledStep, Math.Abs(alpha * step[column]));
                x[column] += alpha * step[column] * system.UnknownScales[column];
            }

            progress?.Report(new SolveProgress(iteration, norm, alpha));

            if (scaledStep < _settings.StepTolerance)
            {
                var worst = system.Equations.Rows[Worst(residuals)];

                diagnostics.Add(Diagnostic.Create(
                    SolverDiagnostics.Stalled,
                    null,
                    Number("residual", norm),
                    new DiagnosticArgument("component", worst.OwnerComponentId),
                    new DiagnosticArgument("equation", worst.Name)));

                return Stop(system, x, iteration, norm, SolveTermination.Stalled, diagnostics);
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

        return Stop(system, x, _settings.MaxIterations, final, SolveTermination.IterationCap, diagnostics);
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
            var delta = Tolerances.NewtonFiniteDifferenceStep * Math.Max(Math.Abs(x[column]), scale);

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

    /// <summary>Refuses a system with <c>FS3005</c>.</summary>
    /// <param name="reason">Why, as a clause the message completes.</param>
    /// <returns>The failure.</returns>
    private static Result<Unit> Refuse(string reason) =>
        Result.Failure<Unit>(new ResultError(
            SolverDiagnostics.Refused,
            [new DiagnosticArgument("solver", "The steady solver"), new DiagnosticArgument("reason", reason)]));

    /// <summary>Formats a count.</summary>
    /// <param name="name">The placeholder's name.</param>
    /// <param name="value">The count.</param>
    /// <returns>The argument.</returns>
    private static DiagnosticArgument Count(string name, int value) =>
        new(name, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Formats a dimensionless number, such as a scaled norm.</summary>
    /// <param name="name">The placeholder's name.</param>
    /// <param name="value">The number.</param>
    /// <returns>The argument.</returns>
    private static DiagnosticArgument Number(string name, double value) =>
        new(name, value.ToString("G4", CultureInfo.InvariantCulture));

    /// <summary>Formats a residual with the unit it is measured in.</summary>
    /// <param name="value">The residual, in SI.</param>
    /// <param name="unit">Its SI unit.</param>
    /// <returns>The rendered text.</returns>
    /// <remarks>
    /// SI, not a display unit. Choosing one is <c>D-14</c>'s and belongs to whatever renders for a
    /// person; a message built in Core that guessed at kilowatts would be guessing on behalf of a
    /// setting it cannot see. <see cref="ResidualReport"/> carries the number and the unit separately
    /// so a caller that does know can render it properly.
    /// </remarks>
    private static string Amount(double value, string unit) =>
        $"{value.ToString("G4", CultureInfo.InvariantCulture)} {unit}";

    /// <summary>Stops because no reachable point had a fluid state.</summary>
    /// <param name="system">The assembled system.</param>
    /// <param name="x">The last iterate.</param>
    /// <param name="iterations">How many were taken.</param>
    /// <param name="norm">The last known norm.</param>
    /// <param name="diagnostics">Everything reported so far.</param>
    /// <returns>The result.</returns>
    private static SolveResult OutOfDomain(
        EquationSystem system,
        double[] x,
        int iterations,
        double norm,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        diagnostics.Add(Diagnostic.Create(
            SolverDiagnostics.NonFinite,
            null,
            new DiagnosticArgument("component", system.NodeName(system.OutOfDomainNode)),
            new DiagnosticArgument("equation", "its fluid state"),
            Count("steps", iterations)));

        return Stop(system, x, iterations, norm, SolveTermination.NonFinite, diagnostics);
    }

    /// <summary>Packages the last iterate and the worst rows into a result.</summary>
    /// <param name="system">The assembled system.</param>
    /// <param name="x">The last iterate.</param>
    /// <param name="iterations">How many were taken.</param>
    /// <param name="norm">The final scaled norm.</param>
    /// <param name="termination">Why it stopped.</param>
    /// <param name="diagnostics">Everything reported.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// The last iterate is returned whatever happened. A circuit that got most of the way to a balance
    /// shows a user where it was heading; an empty canvas shows them nothing. What must never happen is
    /// presenting it as solved, which <see cref="SolveResult.Converged"/> is for.
    /// </remarks>
    private static SolveResult Stop(
        EquationSystem system,
        double[] x,
        int iterations,
        double norm,
        SolveTermination termination,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var raw = new double[system.Rows];
        var scaled = new double[system.Rows];

        if (!system.TryEvaluateResiduals(x, raw))
        {
            Array.Clear(raw);
        }

        for (var row = 0; row < system.Rows; row++)
        {
            scaled[row] = raw[row] / system.ResidualScales[row];
        }

        var worst = Enumerable.Range(0, system.Rows)
            .OrderByDescending(row => Math.Abs(scaled[row]))
            .ThenBy(static row => row)
            .Take(Math.Min(3, system.Rows))
            .Select(row => new ResidualReport(
                system.Equations.Rows[row].OwnerComponentId,
                system.Equations.Rows[row].Name,
                raw[row],
                system.Equations.Rows[row].ResidualSiUnit,
                scaled[row]))
            .ToImmutableArray();

        return new SolveResult
        {
            Converged = termination is SolveTermination.Converged,
            Solution = new StateVector([.. x]),
            Iterations = iterations,
            ResidualNorm = norm,
            Termination = termination,
            WorstResiduals = worst,
            Diagnostics = diagnostics.ToImmutable(),
        };
    }
}
