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
        var system = posedness.CanSolve ? EquationSystem.Build(graph, posedness, seed) : null;

        Unknowns(report, layout, seed, solve);
        Equations(report, system, solve);
        Sized(report, bases, notes);
        Conditioning(report, system, seed, solve);

        return report.ToString();
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
        var order = system.Columns;
        var matrix = Jacobian(system, at.Values.AsSpan(), order);

        if (matrix is null)
        {
            report.AppendLine(
                "    the residuals could not be evaluated here, so no Jacobian could be built");

            return;
        }

        var pivots = Pivots([.. matrix], order);
        var largest = pivots.Length == 0 ? 0 : pivots.Max();
        var smallest = pivots.Length == 0 ? 0 : pivots.Min();
        var deficient = pivots.Count(pivot => largest > 0 && pivot / largest < RankTolerance);

        report.AppendLine(CultureInfo.InvariantCulture,
            $"    evaluated at {(solve is null ? "the seed" : "the solved iterate")}, order {order}");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"    pivots       largest {largest:G4}, smallest {smallest:G4}, "
            + $"ratio {(largest > 0 ? smallest / largest : 0):G4}");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"    rank         {order - deficient} of {order}, deficient by {deficient}");

        if (deficient == 0)
        {
            return;
        }

        var free = NullDirection.Of([.. matrix], order);
        var implied = NullDirection.Redundancy([.. matrix], order);

        report.AppendLine();
        report.AppendLine("    unknowns nothing separates (the column direction — where pivoting landed):");
        Direction(report, free, index => index < system.Unknowns.Unknowns.Length
            ? system.Unknowns.Unknowns[index].Name
            : $"column {index}");

        report.AppendLine();
        report.AppendLine("    equations that are not independent (the row direction — the redundancy):");
        Direction(report, implied, index => index < system.Equations.Rows.Length
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

    private static double[]? Jacobian(EquationSystem system, ReadOnlySpan<double> x, int order)
    {
        var matrix = new double[order * order];
        var basis = new double[order];
        var perturbed = new double[order];
        var trial = new double[order];

        if (!system.TryEvaluateResiduals(x, basis))
        {
            return null;
        }

        var scales = system.UnknownScales;
        var residualScales = system.ResidualScales;

        for (var column = 0; column < order; column++)
        {
            x.CopyTo(trial);

            var step = Math.Max(1e-8, Math.Abs(trial[column]) * 1e-7);

            trial[column] += step;

            if (!system.TryEvaluateResiduals(trial, perturbed))
            {
                return null;
            }

            for (var row = 0; row < order; row++)
            {
                // The scaled Jacobian, because that is what the solver factors: an unscaled matrix's
                // conditioning is a statement about units rather than about the circuit.
                var derivative = (perturbed[row] - basis[row]) / step;
                var scale = scales[column] / residualScales[row];

                matrix[(row * order) + column] = derivative * scale;
            }
        }

        return matrix;
    }

    /// <summary>The pivot magnitudes a full-pivot elimination meets, largest first.</summary>
    private static double[] Pivots(double[] matrix, int order)
    {
        var pivots = new List<double>(order);
        var rows = Enumerable.Range(0, order).ToArray();
        var columns = Enumerable.Range(0, order).ToArray();

        for (var step = 0; step < order; step++)
        {
            var best = 0.0;
            var bestRow = step;
            var bestColumn = step;

            for (var row = step; row < order; row++)
            {
                for (var column = step; column < order; column++)
                {
                    var magnitude = Math.Abs(matrix[(rows[row] * order) + columns[column]]);

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

            (rows[step], rows[bestRow]) = (rows[bestRow], rows[step]);
            (columns[step], columns[bestColumn]) = (columns[bestColumn], columns[step]);

            var pivot = matrix[(rows[step] * order) + columns[step]];

            for (var row = step + 1; row < order; row++)
            {
                var factor = matrix[(rows[row] * order) + columns[step]] / pivot;

                if (factor == 0)
                {
                    continue;
                }

                for (var column = step; column < order; column++)
                {
                    matrix[(rows[row] * order) + columns[column]] -=
                        factor * matrix[(rows[step] * order) + columns[column]];
                }
            }
        }

        return [.. pivots];
    }
}
