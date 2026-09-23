using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FluidScript.Core.Solvers;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Steady;

namespace FluidScript.Core.Diagnostics.Explanations;

public static partial class SolveExplanation
{
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

        // The seed's conditioning beside the solution's, because the seed is where a first step goes
        // wrong: a nearly singular pivot there is a first step enormous in every direction, and it is
        // invisible at the solved iterate (S-66). Printed only when the two differ.
        if (solve is not null && Jacobian(system, seed.Values.AsSpan(), rows, columns) is { } atSeed)
        {
            var seedPivots = Pivots([.. atSeed], rows, columns);
            var seedLargest = seedPivots.Length == 0 ? 0 : seedPivots.Max();
            var seedSmallest = seedPivots.Length == 0 ? 0 : seedPivots.Min();
            var seedRank = seedPivots.Count(pivot => seedLargest > 0 && pivot / seedLargest >= RankTolerance);

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    at the seed  pivots largest {seedLargest:G4}, smallest {seedSmallest:G4}, "
                + $"ratio {(seedLargest > 0 ? seedSmallest / seedLargest : 0):G4}; rank {seedRank}");
        }

        // A full-rank answer can still be a place on a valley (`S-37`): the weakest direction is printed
        // whenever the pivot ratio is under the valley tolerance, so a session can see which unknowns
        // move together before `FS3016` -- or anything else -- is claimed about them.
        if (undetermined == 0 && dependent == 0 && rows == columns
            && largest > 0 && smallest / largest < Tolerances.JacobianValley)
        {
            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture,
                $"    a valley: the weakest direction, at pivot ratio under {Tolerances.JacobianValley:G1}, "
                + $"along which these move together:");
            var valley = NullDirection.Of([.. matrix], columns, Tolerances.JacobianValley);

            Direction(report, valley, index => system.Unknowns.Unknowns[index].Name);
        }

        if (undetermined == 0 && dependent == 0)
        {
            return;
        }

        // `NullDirection` reads a square matrix and a rectangular system has to be padded to reach it. The
        // padding is only harmless in one direction at a time, and which one depends on the shape.
        //
        // Squaring a system with more unknowns than equations adds zero ROWS. A zero row constrains
        // nothing, so it adds no column freedom and the free-unknown direction is exact -- but it is
        // dependent on everything, so the redundancy direction would name it ahead of anything real.
        // Squaring the other shape adds zero COLUMNS, and the two guarantees swap.
        //
        // So each direction is reported only when the padding cannot have invented it. The section says
        // which one it withheld rather than printing an answer it cannot stand behind.
        var side = Math.Max(rows, columns);
        var padded = new double[side * side];

        for (var row = 0; row < rows; row++)
        {
            Array.Copy(matrix, row * columns, padded, row * side, columns);
        }

        if (undetermined > 0)
        {
            report.AppendLine();

            if (rows > columns)
            {
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"    {undetermined} unknown(s) are undetermined and the column direction is withheld: "
                    + $"squaring a {rows}x{columns} system adds columns, which are free by construction");
            }
            else
            {
                report.AppendLine(
                    "    unknowns nothing separates (the column direction — where pivoting landed):");
                Direction(report, NullDirection.Of([.. padded], side),
                    index => index < system.Unknowns.Unknowns.Length
                        ? system.Unknowns.Unknowns[index].Name
                        : $"column {index}");
            }
        }

        if (dependent == 0)
        {
            return;
        }

        report.AppendLine();

        if (columns > rows)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {dependent} equation(s) are dependent and the row direction is withheld: "
                + $"squaring a {rows}x{columns} system adds rows, which are dependent by construction");

            return;
        }

        report.AppendLine("    equations that are not independent (the row direction — the redundancy):");
        Direction(report, NullDirection.Redundancy([.. padded], side),
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
