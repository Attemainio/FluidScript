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

        // Reported once per parameter rather than once per step: a bound that binds usually binds on
        // every remaining iteration, and a user needs to know it happened, not how often.
        var pinned = new List<int>();

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

            // `S-26b`. A promoted `position` is a fraction and a `head` is not negative; Newton knows
            // neither, and left alone the cooling loop's split walks to about 5.5. Projecting after the
            // step rather than constraining the step keeps the line search's own arithmetic untouched,
            // and it is safe only because `S-26a` made the residual differentiable at a bound -- while
            // `Opening` clamped, landing on a bound put the iterate exactly where its column was dead.
            if (system.Project(x, out var bound) && !pinned.Contains(bound))
            {
                pinned.Add(bound);

                diagnostics.Add(Diagnostic.Create(
                    SolverDiagnostics.ParameterPinned,
                    null,
                    new DiagnosticArgument("parameter", system.Unknowns.Unknowns[bound].Name),
                    Number("bound", x[bound])));
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

    /// <summary>Names the combination of unknowns a singular Jacobian left undetermined.</summary>
    /// <param name="system">The assembled system.</param>
    /// <param name="x">The iterate the factorisation failed at.</param>
    /// <param name="residuals">The scaled residuals at <paramref name="x"/>.</param>
    /// <param name="trial">Scratch, one per unknown.</param>
    /// <param name="perturbed">Scratch, one per equation.</param>
    /// <param name="columns">The system's order.</param>
    /// <returns>
    /// The combination, written as a signed sum of unknown names, or <see langword="null"/> when there is
    /// nothing to name — the rebuild left the property domain, or the direction is not recoverable.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>It rebuilds the Jacobian rather than keeping a copy of it.</strong>
    /// <see cref="DenseLu.Factor"/> overwrites the matrix it factorises, so by the time singularity is
    /// known the original is gone. Copying every iteration to serve a path that ends the run would put an
    /// <em>n²</em> copy in the hot loop for the benefit of the last iteration only; rebuilding costs N+1
    /// residual evaluations once, on a solve that is already over.
    /// </para>
    /// <para>
    /// A coefficient is printed only when it is far enough from 1 to matter. Most null directions in a
    /// hydraulic circuit come out as ±1 on each participant, because what is undetermined is a sum of
    /// heads or of pressures rather than a weighted one, and printing <c>1 ×</c> in front of every term
    /// would bury the two that are genuinely weighted.
    /// </para>
    /// </remarks>
    private static (string? Unknowns, string? Equations) Deficiency(
        EquationSystem system,
        double[] x,
        double[] residuals,
        double[] trial,
        double[] perturbed,
        int columns)
    {
        var rebuilt = new double[columns * columns];

        if (!Jacobian(system, x, residuals, trial, perturbed, rebuilt))
        {
            return (null, null);
        }

        // `Redundancy` reads the matrix and transposes into its own copy; `Of` overwrites what it is
        // given. So the row direction is taken first, off the matrix that is still intact.
        var rows = NullDirection.Redundancy(rebuilt, columns);
        var free = NullDirection.Of(rebuilt, columns);

        return (
            Render(free, index => system.Unknowns.Unknowns[index].Name),
            Render(rows, index => Equation(system, index)));
    }

    /// <summary>Names one equation the way a user would recognise it.</summary>
    /// <param name="system">The assembled system.</param>
    /// <param name="row">The equation's row.</param>
    /// <returns>The owner and the equation, e.g. <c>N2 mass balance</c>.</returns>
    private static string Equation(EquationSystem system, int row) =>
        row >= 0 && row < system.Equations.Rows.Length
            ? system.Equations.Rows[row].Name
            : $"row {row.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Writes a null direction as a signed sum a user can read.</summary>
    /// <param name="direction">The participants, largest share first.</param>
    /// <param name="name">What each participant's index is called.</param>
    /// <returns>The rendered sum, or <see langword="null"/> when there is no direction to render.</returns>
    private static string? Render(
        ImmutableArray<NullDirection.Participant> direction, Func<int, string> name)
    {
        if (direction.IsDefaultOrEmpty)
        {
            return null;
        }

        var terms = new List<string>();

        foreach (var participant in direction)
        {
            if (terms.Count == NamedParticipants)
            {
                terms.Add($"and {direction.Length - NamedParticipants} more");
                break;
            }

            var magnitude = Math.Abs(participant.Weight);
            var coefficient = magnitude < 0.95
                ? magnitude.ToString("0.##", CultureInfo.InvariantCulture) + " x "
                : string.Empty;
            var sign = terms.Count == 0
                ? participant.Weight < 0 ? "-" : string.Empty
                : participant.Weight < 0 ? "- " : "+ ";

            terms.Add(sign + coefficient + name(participant.Index));
        }

        return string.Join(" ", terms);
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
