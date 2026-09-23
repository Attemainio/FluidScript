using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Solvers;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Diagnostics.Explanations;

public static partial class SolveExplanation
{
    private static void Equations(
        StringBuilder report, EquationSystem? system, StateVector seed, SolveResult? solve)
    {
        report.AppendLine();
        report.AppendLine("--- equations, and how far each is from satisfied");

        if (system is null)
        {
            report.AppendLine("    (not assembled: the circuit is not square, so no system was built)");

            return;
        }

        // At the seed when there is no solve, because a circuit that never stepped is the one whose
        // starting residuals are worth reading.
        var at = solve?.Solution ?? seed;
        var residuals = new double[system.Rows];
        var evaluated = system.TryEvaluateResiduals(at.Values.AsSpan(), residuals);
        var scales = system.ResidualScales;

        report.AppendLine(CultureInfo.InvariantCulture,
            $"    evaluated at {(solve is null ? "the seed" : "the solved iterate")}");
        report.AppendLine(
            "      # kind             owner        name                       residual unit        scaled");

        for (var row = 0; row < system.Equations.Rows.Length; row++)
        {
            var declaration = system.Equations.Rows[row];
            var known = evaluated && row < residuals.Length;
            var value = known ? residuals[row].ToString("G4", CultureInfo.InvariantCulture) : "-";
            var scaled = known && row < scales.Length
                ? (residuals[row] / scales[row]).ToString("G4", CultureInfo.InvariantCulture)
                : "-";

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {row,3} {declaration.Kind,-16} {declaration.OwnerComponentId,-12} "
                + $"{declaration.Name,-24} {value,10} {declaration.ResidualSiUnit,-6} {scaled,12}");
        }

        foreach (var dropped in system.Equations.Dropped)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"    dropped as redundant: {dropped.Equation} (hydraulic {dropped.Hydraulic})");
        }

        if (!evaluated)
        {
            return;
        }

        // What the reported norm is actually made of. The termination line quotes a **scaled** residual
        // and this table used to print unscaled ones, so a norm of 1.96 named no row and could not be
        // attributed --- which is how a hundredfold change to the enthalpy seed was made and measured
        // without noticing it left the norm untouched (`S-44`).
        var worst = Enumerable.Range(0, Math.Min(system.Rows, scales.Length))
            .Select(row => (Row: row, Scaled: Math.Abs(residuals[row] / scales[row])))
            .OrderByDescending(static entry => entry.Scaled)
            .Take(5)
            .ToArray();

        // The largest single row, because that is what the termination line quotes: `SolveResult`'s
        // residual is a max-norm, not a Euclidean one. Reporting a 2-norm here would print a number the
        // solver never mentions next to one it does, and the first version of this section did exactly
        // that -- 4.56 beside the solver's 1.96, which reads as a disagreement rather than as two norms.
        var largest = worst.Length == 0 ? 0 : worst[0].Scaled;
        var euclidean = Math.Sqrt(Enumerable
            .Range(0, Math.Min(system.Rows, scales.Length))
            .Sum(row => Math.Pow(residuals[row] / scales[row], 2)));

        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"    what the scaled residual of {largest:G4} is made of, largest first "
            + $"(Euclidean norm {euclidean:G4} over {system.Rows} rows):");
        foreach (var (row, scaled) in worst)
        {
            var declaration = system.Equations.Rows[row];

            report.AppendLine(CultureInfo.InvariantCulture,
                $"        {scaled,10:G4}  {declaration.Kind} {declaration.Name} "
                + $"(scale {scales[row]:G4} {declaration.ResidualSiUnit})");
        }
    }

    private static void Sized(
        StringBuilder report,
        CircuitGraph graph,
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
            // The bases are keyed `HX1.flow2` because the wire's `sizes` map is (`D-120`); the report is
            // read beside the script, so the line says `HX1.in[2].flow` (`L-56`).
            foreach (var (key, basis) in bases.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"    {Spelled(graph, key),-20} {basis}");
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

    /// <summary>What every extended-mode exchanger achieves at the solved state, by both routes.</summary>
    /// <param name="report">The report.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">The unknown layout the solution is addressed by.</param>
    /// <param name="solve">The solve, or <see langword="null"/> when none ran.</param>
    /// <remarks>
    /// The arithmetic is <see cref="SolvedStates.Exchanger"/>'s, shared with the deferred evaluation
    /// that reads <c>HE1.out[2].t</c> off a rated exchanger (<c>L-59</c>); this only prints it.
    /// </remarks>
    private static void Ratings(StringBuilder report, CircuitGraph graph, SystemLayout layout, SolveResult? solve)
    {
        if (solve is not { Converged: true })
        {
            return;
        }

        var written = false;

        for (var index = 0; index < graph.Components.Length; index++)
        {
            if (graph.Components[index] is not HeatExchangerComponent exchanger
                || SolvedStates.Exchanger(graph, layout, solve.Solution, index) is not { } at)
            {
                continue;
            }

            if (!written)
            {
                report.AppendLine();
                report.AppendLine("--- exchanger ratings at the solution");
                written = true;
            }

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {exchanger.Name,-12} {at.Duty / 1000:0.###} kW {exchanger.Mode}: side 1 {at.Inlet1 - 273.15:0.##} -> {at.Outlet1 - 273.15:0.##} °C at {at.Capacity1 / 1000:0.###} kW/K, side 2 {at.Inlet2 - 273.15:0.##} -> {at.Outlet2 - 273.15:0.##} °C at {at.Capacity2 / 1000:0.###} kW/K");
            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {string.Empty,-12} NTU {at.Ntu:0.###}, ε {at.Effectiveness:0.####}, Cr {at.CapacityRatio:0.###}, approach {at.Approach:0.##} K; UA {at.Rating.Conductance / 1000:0.###} kW/K rated, {at.ConductanceByLogMean / 1000:0.###} kW/K by LMTD {at.Lmtd:0.###} K{(at.Rating.Arrangement == ExchangerArrangement.Crossflow ? " (counterflow log-mean, no F correction)" : string.Empty)}");
        }
    }
}
