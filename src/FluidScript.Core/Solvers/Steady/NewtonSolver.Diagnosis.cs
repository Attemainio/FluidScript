using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Primitives;
using FluidScript.Core.Solvers.Equations;

namespace FluidScript.Core.Solvers.Steady;

public sealed partial class NewtonSolver
{
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

    /// <summary>Names the sizes a converged answer leaves sharing one valley, or nothing.</summary>
    /// <param name="system">The assembled system.</param>
    /// <param name="x">The solution.</param>
    /// <returns>
    /// The parameter unknowns' names, joined for a sentence, when two or more move together in the
    /// Jacobian's weakest direction at <see cref="Tolerances.JacobianValley"/>; <see langword="null"/>
    /// when the answer is a point, when fewer than two sizes share the direction, or when the Jacobian
    /// cannot be rebuilt.
    /// </returns>
    /// <remarks>
    /// The share floor is ten times <see cref="NullDirection.Significant"/>: a size that carries a
    /// fiftieth of the direction is being dragged by it, not sharing it. On the series header the two
    /// heads carry 1 and 0.73 and the AHU's valve position 0.36; the radiators' position at 0.12 is not
    /// named. On a single-pump circuit whose weakest direction is one branch's flow through a tiny valve,
    /// no size appears and nothing is said.
    /// </remarks>
    private static string? Valley(EquationSystem system, double[] x)
    {
        var columns = system.Columns;
        var residuals = new double[system.Rows];
        var rebuilt = new double[columns * columns];

        if (!system.TryEvaluateScaled(x, residuals)
            || !Jacobian(system, x, residuals, new double[columns], new double[system.Rows], rebuilt))
        {
            return null;
        }

        var sizes = NullDirection.Of(rebuilt, columns, Tolerances.JacobianValley)
            .Where(static participant => Math.Abs(participant.Weight) >= 10 * NullDirection.Significant)
            .Select(participant => system.Unknowns.Unknowns[participant.Index])
            .Where(static unknown => unknown.Kind == UnknownKind.Parameter)
            .Select(static unknown => unknown.Name)
            .ToArray();

        return sizes.Length switch
        {
            < 2 => null,
            2 => sizes[0] + " and " + sizes[1],
            _ => string.Join(", ", sizes[..^1]) + " and " + sizes[^1],
        };
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
    /// <param name="pinned">Each column a projection ever held, with the bound it was held at.</param>
    /// <param name="history">The iterations taken so far.</param>
    /// <returns>The result.</returns>
    private static SolveResult OutOfDomain(
        EquationSystem system,
        double[] x,
        int iterations,
        double norm,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        IReadOnlyDictionary<int, double> pinned,
        ImmutableArray<IterationRecord>.Builder history)
    {
        diagnostics.Add(Diagnostic.Create(
            SolverDiagnostics.NonFinite,
            null,
            new DiagnosticArgument("component", system.NodeName(system.OutOfDomainNode)),
            new DiagnosticArgument("equation", "its fluid state"),
            Count("steps", iterations)));

        return Stop(system, x, iterations, norm, SolveTermination.NonFinite, diagnostics, pinned, history);
    }

    /// <summary>Packages the last iterate and the worst rows into a result.</summary>
    /// <param name="system">The assembled system.</param>
    /// <param name="x">The last iterate.</param>
    /// <param name="iterations">How many were taken.</param>
    /// <param name="norm">The final scaled norm.</param>
    /// <param name="termination">Why it stopped.</param>
    /// <param name="diagnostics">Everything reported.</param>
    /// <param name="pinned">Each column a projection ever held, with the bound it was held at.</param>
    /// <param name="history">The iterations taken so far.</param>
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
        ImmutableArray<Diagnostic>.Builder diagnostics,
        IReadOnlyDictionary<int, double> pinned,
        ImmutableArray<IterationRecord>.Builder history)
    {
        // `FS3008` is a statement about the answer, so it is made here, about the last iterate: a bound
        // the solution still sits on is one the circuit is pressing against; one it passed through on the
        // way is not (`S-61`).
        foreach (var (column, bound) in pinned.OrderBy(static entry => entry.Key))
        {
            if (Math.Abs(x[column] - bound) <= 1e-12 * Math.Max(1, Math.Abs(bound)))
            {
                diagnostics.Add(Diagnostic.Create(
                    SolverDiagnostics.ParameterPinned,
                    null,
                    new DiagnosticArgument("parameter", system.Unknowns.Unknowns[column].Name),
                    Number("bound", bound)));
            }
        }

        // `FS3014` / `FS3015` likewise: a head that holds a stopped branch is read at the answer. Its sign
        // says which way the header pushes -- positive is a pump dead-heading against a backward push,
        // negative a forward push no pump can resist (`S-56`).
        foreach (var (column, holds) in system.Closing)
        {
            diagnostics.Add(Diagnostic.Create(
                x[column] < 0 ? SolverDiagnostics.HeldShut : SolverDiagnostics.DeadHeaded,
                null,
                new DiagnosticArgument("parameter", system.Unknowns.Unknowns[column].Name),
                Number("head", x[column]),
                new DiagnosticArgument("component", holds)));
        }

        // `FS3016` is a statement about the answer too: whether it is a point or a place on a valley
        // (`S-37`, `D-142`). One more Jacobian on a run that is over, and the direction its weakest
        // pivot leaves nearly free; two or more sizes moving together in it is the shared path.
        if (termination is SolveTermination.Converged && !system.Pinned)
        {
            var valley = Valley(system, x);

            if (valley is not null)
            {
                diagnostics.Add(Diagnostic.Create(
                    SolverDiagnostics.Valley,
                    null,
                    new DiagnosticArgument("parameters", valley)));
            }
        }

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
            History = history.ToImmutable(),
        };
    }
}
