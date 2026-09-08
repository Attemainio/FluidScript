using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using FluidScript.Core.Solvers;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Diagnostics;

/// <summary>Everything the engine knows about one circuit's solve, rendered as text.</summary>
/// <remarks>
/// <para>
/// <strong>This exists because the questions it answers were being answered by throwaway probes.</strong>
/// "Which pump did this constraint promote?", "is this parameter sized or solved for?", "why is the table
/// one equation short?", "how far off is the rank?" -- each was a fifteen-line test written, run once and
/// deleted, and two of them were answered wrongly along the way. Every one of them is a line in this
/// report, so the next session reads instead of guessing.
/// </para>
/// <para>
/// <strong>It never throws and never refuses.</strong> A circuit that cannot be counted, cannot be
/// assembled or cannot be solved is exactly the circuit whose explanation is wanted, so every section
/// degrades to saying what it could not do and the rest of the report still renders.
/// </para>
/// </remarks>
public static class SolveExplanation
{
    /// <summary>How small a scaled pivot has to be before it counts as a rank deficiency.</summary>
    /// <remarks>
    /// Relative to the largest pivot, not absolute: the scaled Jacobian's rows and columns are already
    /// normalised, so what matters is the spread. A ratio below this is a column the elimination could
    /// not distinguish from zero.
    /// </remarks>
    private const double RankTolerance = 1e-10;

    /// <summary>Explains a completed run, sizing and solve included.</summary>
    /// <param name="result">What the outer loop produced.</param>
    /// <param name="name">The script's name, for the report's first line.</param>
    /// <returns>The report, as lines of text.</returns>
    public static string Render(OuterLoopResult result, string name = "circuit")
    {
        ArgumentNullException.ThrowIfNull(result);

        return Render(result.Graph, name, result.Solve, result.Bases, result.Notes, result.Passes);
    }

    /// <summary>Explains a circuit that has not been solved, or could not be.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="name">The script's name.</param>
    /// <returns>The report, as lines of text.</returns>
    public static string Render(CircuitGraph graph, string name = "circuit") =>
        Render(graph, name, solve: null, bases: null, notes: default, passes: 0);

    private static string Render(
        CircuitGraph graph,
        string name,
        SolveResult? solve,
        ImmutableDictionary<string, string>? bases,
        ImmutableArray<string> notes,
        int passes)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var report = new StringBuilder();
        var posedness = WellPosedness.Check(graph);

        Summary(report, name, posedness, solve, passes);
        Counting(report, posedness);
        Partition(report, posedness);
        Claims(report, posedness);

        var layout = SystemLayout.Build(graph, posedness.Counting);
        var seed = SolutionSeed.Build(graph, layout);
        // Assembled whether or not the count balanced. A circuit the check refuses is precisely the one
        // whose rank is worth measuring: the counting table can say the shortfall is one and only the
        // matrix can say *which* unknown nothing determines. `EquationSystem.Build` never required a
        // square system; only this line did.
        var system = Assemble(graph, posedness, seed);

        Unknowns(report, layout, seed, solve);
        Equations(report, system, solve);
        Sized(report, bases, notes);
        Conditioning(report, system, seed, solve);

        return report.ToString();
    }

    /// <summary>Assembles the system, or reports nothing rather than throwing out of a diagnostic.</summary>
    /// <remarks>
    /// A report is asked for when something is already wrong, and a malformed graph is the normal case
    /// rather than the exception (<c>no pipeline stage throws on user input</c>). Assembly walks lookups
    /// that a graph refused by an earlier stage can leave incomplete, so a failure here becomes an absent
    /// section rather than an exception thrown from the tool the user reached for to explain the failure.
    /// </remarks>
    private static EquationSystem? Assemble(
        CircuitGraph graph, WellPosednessResult posedness, StateVector seed)
    {
        try
        {
            return EquationSystem.Build(graph, posedness, seed);
        }
#pragma warning disable CA1031 // See the remarks: a diagnostic that throws is worse than one that omits.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static void Summary(
        StringBuilder report,
        string name,
        WellPosednessResult posedness,
        SolveResult? solve,
        int passes)
    {
        report.AppendLine(CultureInfo.InvariantCulture, $"=== {name}");

        var counting = posedness.Counting;
        var verdict = counting.Excess switch
        {
            0 => "square",
            > 0 => $"over-specified by {counting.Excess}",
            _ => $"under-specified by {-counting.Excess}",
        };

        report.AppendLine(CultureInfo.InvariantCulture,
            $"    counting     {counting.Unknowns} unknowns, {counting.Equations} equations — {verdict}");

        if (solve is null)
        {
            report.AppendLine("    solve        not run");
        }
        else
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"    solve        {solve.Termination} after {solve.Iterations} iterations, "
                + $"scaled residual {solve.ResidualNorm:G4}");

            if (passes > 0)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"    sizing       {passes} pass(es)");
            }
        }

        foreach (var diagnostic in posedness.Diagnostics)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {diagnostic.Code}       {diagnostic.Message}");
        }

        if (solve is not null)
        {
            foreach (var diagnostic in solve.Diagnostics)
            {
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"    {diagnostic.Code}       {diagnostic.Message}");
            }
        }
    }

    private static void Counting(StringBuilder report, WellPosednessResult posedness)
    {
        var t = posedness.Counting;

        report.AppendLine();
        report.AppendLine("--- counting table");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"    unknowns   branch flows {t.BranchFlows}, node pressures {t.NodePressures}, "
            + $"node enthalpies {t.NodeEnthalpies}, component-owned {t.ComponentUnknowns.Length}, "
            + $"external fluxes {t.ExternalFluxes}, promotions {t.Promotions.Length}");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"    equations  pressure relations {t.PressureRelations}, mass balances {t.MassBalances}, "
            + $"energy balances {t.EnergyBalances}, control volumes {t.ControlVolumeBalances}, "
            + $"stated pressures {t.StatedPressures}, constraints {t.Constraints.Length}, "
            + $"datums {t.Datums}, less enthalpy levels {t.EnthalpyLevels}");
    }

    /// <summary>
    /// The hydraulic partition, which is what decides half the counting table's negative terms.
    /// </summary>
    /// <remarks>
    /// Whether a part is closed decides both whether a mass balance is redundant and whether an energy
    /// balance is, and a coupled element suppresses the second on its own. Reading `less enthalpy
    /// levels 0` without being able to see which of those produced it is the position twelve throwaway
    /// probes were written from.
    /// </remarks>
    private static void Partition(StringBuilder report, WellPosednessResult posedness)
    {
        report.AppendLine();
        report.AppendLine("--- hydraulic partition");

        foreach (var hydraulic in posedness.Hydraulics)
        {
            var level = posedness.Counting.LevelComponents.Contains(hydraulic)
                ? "one energy balance dropped as its level"
                : "no energy level dropped";
            var datum = hydraulic.DatumWasStated ? "stated" : "picked";
            var coupled = hydraulic.Elements
                .Where(element => posedness.Hydraulics.Count(
                    other => other.Elements.Contains(element)) > 1)
                .Select(static element => element.Name)
                .ToArray();

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    [{hydraulic.Index}] {(hydraulic.IsClosed ? "closed" : "open")}, "
                + $"{hydraulic.Nodes.Length} nodes, {hydraulic.Branches.Length} branches, "
                + $"{hydraulic.Elements.Length} elements");
            report.AppendLine(CultureInfo.InvariantCulture,
                $"        datum {hydraulic.Datum} ({datum}), {hydraulic.Boundaries.Length} boundaries, "
                + $"{hydraulic.StatedPressures.Length} stated pressures, "
                + $"unknown flux {hydraulic.HasUnknownFlux}");
            report.AppendLine(CultureInfo.InvariantCulture,
                $"        {level}; coupled elements: "
                + $"{(coupled.Length == 0 ? "none" : string.Join(", ", coupled))}");
        }
    }

    private static void Claims(StringBuilder report, WellPosednessResult posedness)
    {
        var t = posedness.Counting;

        report.AppendLine();
        report.AppendLine("--- constraints, and what answers each");

        if (t.Constraints.Length == 0)
        {
            report.AppendLine("    (none)");

            return;
        }

        var unanswered = 0;

        foreach (var constraint in t.Constraints)
        {
            var match = t.Promotions.FirstOrDefault(promotion =>
                promotion.Constraint.Kind == constraint.Kind
                && string.Equals(
                    promotion.Constraint.Component, constraint.Component, StringComparison.Ordinal));

            if (match is null)
            {
                unanswered++;
            }

            var answer = match is null
                ? "no promotion"
                : $"solved for as {match.Component}.{match.Parameter}";

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {constraint.Kind,-12} on {constraint.Component,-10} -> {answer}");
        }

        if (unanswered == 0)
        {
            return;
        }

        // A constraint with no promotion is not by itself a defect, and saying so was this report's own
        // first bug: `m2-simple-loop` has one and counts square. A stated inlet can be paid for by the
        // enthalpy level it removes instead of by an unknown it adds. So state the arithmetic and leave
        // the verdict to the counting line -- unanswered constraints beyond the levels dropped are the
        // ones with nothing behind them.
        var arithmetic =
            $"{unanswered} with no promotion, against {t.EnthalpyLevels} enthalpy level(s) dropped. "
            + "A level pays for one; anything beyond that is over-specification.";

        report.AppendLine(CultureInfo.InvariantCulture, $"    {arithmetic}");
    }

    private static void Unknowns(
        StringBuilder report,
        SystemLayout layout,
        StateVector seed,
        SolveResult? solve)
    {
        report.AppendLine();
        report.AppendLine("--- unknowns, seeded and solved");
        report.AppendLine(
            "      # kind             owner        name                            seed        solved");

        var solved = solve?.Solution.Values;

        for (var index = 0; index < layout.Unknowns.Length; index++)
        {
            var declaration = layout.Unknowns[index];
            var start = index < seed.Values.Length ? seed.Values[index] : double.NaN;
            var end = solved is { } values && index < values.Length ? values[index] : double.NaN;
            var endText = double.IsNaN(end) ? "-" : end.ToString("G6", CultureInfo.InvariantCulture);

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {index,3} {declaration.Kind,-16} {declaration.OwnerComponentId,-12} "
                + $"{declaration.Name,-28} {start,11:G6} {endText,13} {declaration.SiUnit}");
        }
    }

    private static void Equations(StringBuilder report, EquationSystem? system, SolveResult? solve)
    {
        report.AppendLine();
        report.AppendLine("--- equations, and how far each is from satisfied");

        if (system is null)
        {
            report.AppendLine("    (not assembled: the circuit is not square, so no system was built)");

            return;
        }

        var residuals = new double[system.Rows];
        var evaluated = solve is not null
            && system.TryEvaluateResiduals(solve.Solution.Values.AsSpan(), residuals);

        report.AppendLine(
            "      # kind             owner        name                            residual   unit");

        for (var row = 0; row < system.Equations.Rows.Length; row++)
        {
            var declaration = system.Equations.Rows[row];
            var value = evaluated && row < residuals.Length
                ? residuals[row].ToString("G4", CultureInfo.InvariantCulture)
                : "-";

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {row,3} {declaration.Kind,-16} {declaration.OwnerComponentId,-12} "
                + $"{declaration.Name,-28} {value,10} {declaration.ResidualSiUnit}");
        }

        foreach (var dropped in system.Equations.Dropped)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"    dropped as redundant: {dropped.Equation} (hydraulic {dropped.Hydraulic})");
        }
    }

    private static void Sized(
        StringBuilder report,
        ImmutableDictionary<string, string>? bases,
        ImmutableArray<string> notes)
    {
        report.AppendLine();
        report.AppendLine("--- values chosen by a sizing rule");

        if (bases is null || bases.IsEmpty)
        {
            report.AppendLine("    (none)");
        }
        else
        {
            foreach (var (key, basis) in bases.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"    {key,-20} {basis}");
            }
        }

        if (notes.IsDefaultOrEmpty)
        {
            return;
        }

        report.AppendLine();
        report.AppendLine("--- what sizing wanted to say");

        foreach (var note in notes)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"    {note}");
        }
    }

    private static void Conditioning(
        StringBuilder report,
        EquationSystem? system,
        StateVector seed,
        SolveResult? solve)
    {
        report.AppendLine();
        report.AppendLine("--- rank and conditioning");

        if (system is null)
        {
            report.AppendLine("    (not assembled)");

            return;
        }

        var at = solve?.Solution ?? seed;
        var rows = system.Rows;
        var columns = system.Columns;
        var matrix = Jacobian(system, at.Values.AsSpan(), rows, columns);

        if (matrix is null)
        {
            report.AppendLine(
                "    the residuals could not be evaluated here, so no Jacobian could be built");

            return;
        }

        var pivots = Pivots([.. matrix], rows, columns);
        var largest = pivots.Length == 0 ? 0 : pivots.Max();
        var smallest = pivots.Length == 0 ? 0 : pivots.Min();
        var rank = pivots.Count(pivot => largest > 0 && pivot / largest >= RankTolerance);
        var undetermined = columns - rank;
        var dependent = rows - rank;

        report.AppendLine(CultureInfo.InvariantCulture,
            $"    evaluated at {(solve is null ? "the seed" : "the solved iterate")}, "
            + $"{rows} equations x {columns} unknowns");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"    pivots       largest {largest:G4}, smallest {smallest:G4}, "
            + $"ratio {(largest > 0 ? smallest / largest : 0):G4}");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"    rank         {rank}: {undetermined} unknown(s) nothing determines, "
            + $"{dependent} equation(s) the others imply");

        if (undetermined == 0 && dependent == 0)
        {
            return;
        }

        // `NullDirection` reads a square matrix, and a refused circuit's is not. Padding with zero ROWS
        // leaves the column null space untouched -- a zero row constrains nothing -- so the free-unknown
        // direction is exact whatever the shape. The row direction is not recoverable the same way: a
        // padded zero row is trivially dependent and would be named ahead of any real redundancy, so it is
        // reported only when the system is genuinely square.
        var side = Math.Max(rows, columns);
        var padded = new double[side * side];

        for (var row = 0; row < rows; row++)
        {
            Array.Copy(matrix, row * columns, padded, row * side, columns);
        }

        if (undetermined > 0)
        {
            report.AppendLine();
            report.AppendLine(
                "    unknowns nothing separates (the column direction — where pivoting landed):");
            Direction(report, NullDirection.Of([.. padded], side),
                index => index < system.Unknowns.Unknowns.Length
                    ? system.Unknowns.Unknowns[index].Name
                    : $"column {index}");
        }

        if (rows != columns)
        {
            var shape =
                $"the row direction is not reported on a {rows}x{columns} system: the count already "
                + "names the shortfall, and a padded row would be named ahead of any real redundancy";

            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"    {shape}");
            return;
        }

        if (dependent == 0)
        {
            return;
        }

        report.AppendLine();
        report.AppendLine("    equations that are not independent (the row direction — the redundancy):");
        Direction(report, NullDirection.Redundancy([.. matrix], columns),
            index => index < system.Equations.Rows.Length
                ? system.Equations.Rows[index].Name
                : $"row {index}");
    }

    private static void Direction(
        StringBuilder report,
        ImmutableArray<NullDirection.Participant> direction,
        Func<int, string> name)
    {
        if (direction.IsDefaultOrEmpty)
        {
            report.AppendLine("        (none found)");

            return;
        }

        foreach (var participant in direction.OrderByDescending(static p => Math.Abs(p.Weight)))
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"        {participant.Weight,8:0.###}  {name(participant.Index)}");
        }
    }

    private static double[]? Jacobian(EquationSystem system, ReadOnlySpan<double> x, int rows, int columns)
    {
        var matrix = new double[rows * columns];
        var basis = new double[rows];
        var perturbed = new double[rows];
        var trial = new double[columns];

        if (!system.TryEvaluateResiduals(x, basis))
        {
            return null;
        }

        var scales = system.UnknownScales;
        var residualScales = system.ResidualScales;

        for (var column = 0; column < columns; column++)
        {
            x.CopyTo(trial);

            var step = Math.Max(1e-8, Math.Abs(trial[column]) * 1e-7);

            trial[column] += step;

            if (!system.TryEvaluateResiduals(trial, perturbed))
            {
                return null;
            }

            for (var row = 0; row < rows; row++)
            {
                // The scaled Jacobian, because that is what the solver factors: an unscaled matrix's
                // conditioning is a statement about units rather than about the circuit.
                var derivative = (perturbed[row] - basis[row]) / step;
                var scale = scales[column] / residualScales[row];

                matrix[(row * columns) + column] = derivative * scale;
            }
        }

        return matrix;
    }

    /// <summary>The pivot magnitudes a full-pivot elimination meets, largest first.</summary>
    /// <remarks>
    /// Rectangular on purpose. The circuits worth a rank measurement are exactly the ones the counting
    /// check refuses, and those are never square -- a square system that is refused does not exist.
    /// </remarks>
    private static double[] Pivots(double[] matrix, int rows, int columns)
    {
        var steps = Math.Min(rows, columns);
        var pivots = new List<double>(steps);
        var rowOrder = Enumerable.Range(0, rows).ToArray();
        var columnOrder = Enumerable.Range(0, columns).ToArray();

        for (var step = 0; step < steps; step++)
        {
            var best = 0.0;
            var bestRow = step;
            var bestColumn = step;

            for (var row = step; row < rows; row++)
            {
                for (var column = step; column < columns; column++)
                {
                    var magnitude = Math.Abs(matrix[(rowOrder[row] * columns) + columnOrder[column]]);

                    if (magnitude > best)
                    {
                        best = magnitude;
                        bestRow = row;
                        bestColumn = column;
                    }
                }
            }

            pivots.Add(best);

            if (best == 0)
            {
                continue;
            }

            (rowOrder[step], rowOrder[bestRow]) = (rowOrder[bestRow], rowOrder[step]);
            (columnOrder[step], columnOrder[bestColumn]) = (columnOrder[bestColumn], columnOrder[step]);

            var pivot = matrix[(rowOrder[step] * columns) + columnOrder[step]];

            for (var row = step + 1; row < rows; row++)
            {
                var factor = matrix[(rowOrder[row] * columns) + columnOrder[step]] / pivot;

                if (factor == 0)
                {
                    continue;
                }

                for (var column = step; column < columns; column++)
                {
                    matrix[(rowOrder[row] * columns) + columnOrder[column]] -=
                        factor * matrix[(rowOrder[step] * columns) + columnOrder[column]];
                }
            }
        }

        return [.. pivots];
    }
}
