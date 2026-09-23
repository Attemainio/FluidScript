using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Primitives;
using FluidScript.Core.Solvers;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Passes;
using FluidScript.Core.Solvers.Seeding;
using FluidScript.Core.Solvers.Steady;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Diagnostics.Explanations;

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
public static partial class SolveExplanation
{
    /// <summary>How small a scaled pivot has to be before it counts as a rank deficiency.</summary>
    /// <remarks>
    /// <strong>Borrowed rather than restated.</strong> This report ran its own copy of the rule and the
    /// two drifted apart: it measured a pivot against the largest and called the header deficient, while
    /// <see cref="NullDirection"/> measured against the matrix norm and returned no direction, so one
    /// report said `1 unknown nothing determines` and `(none found)` two lines apart. The number is the
    /// same either way; owning two of it is what made them disagree.
    /// </remarks>
    private const double RankTolerance = NullDirection.RankTolerance;

    /// <summary>Explains a completed run, sizing and solve included.</summary>
    /// <param name="result">What the outer loop produced.</param>
    /// <param name="name">The script's name, for the report's first line.</param>
    /// <returns>The report, as lines of text.</returns>
    public static string Render(OuterLoopResult result, string name = "circuit")
    {
        ArgumentNullException.ThrowIfNull(result);

        return Render(result.Graph, name, result.Solve, result.Bases, result.Notes, result.Passes, result.PassIterations);
    }

    /// <summary>Explains a circuit that has not been solved, or could not be.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="name">The script's name.</param>
    /// <returns>The report, as lines of text.</returns>
    public static string Render(CircuitGraph graph, string name = "circuit") =>
        Render(graph, name, solve: null, bases: null, notes: default, passes: 0, passIterations: default);

    /// <summary>Explains a run whether or not it produced a result.</summary>
    /// <param name="run">What <c>OuterLoop.RunAsync</c> returned.</param>
    /// <param name="fallback">The lowered circuit, used when the run produced no result.</param>
    /// <param name="name">The script's name.</param>
    /// <returns>The report, as lines of text.</returns>
    /// <remarks>
    /// The overload every caller actually wants. A run refused before the solver has no result to render
    /// and is exactly the run whose report is worth reading, so branching on success and picking the
    /// right overload was left to each caller --- and each one wrote the branch again. It belongs here:
    /// the two reports differ only in how much of the run they can fill in, and the sections that need a
    /// solve already say so themselves.
    /// </remarks>
    public static string Render(
        Result<OuterLoopResult> run, CircuitGraph fallback, string name = "circuit") =>
        run.IsSuccess ? Render(run.Value, name) : Render(fallback, name);

    private static string Render(
        CircuitGraph graph,
        string name,
        SolveResult? solve,
        ImmutableDictionary<string, string>? bases,
        ImmutableArray<string> notes,
        int passes,
        ImmutableArray<int> passIterations)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var report = new StringBuilder();
        var posedness = WellPosedness.Check(graph);

        Summary(report, name, posedness, solve, passes, passIterations);
        Iterations(report, solve);
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

        Unknowns(report, graph, layout, seed, solve);
        State(report, graph, layout, seed, solve);
        HeatBalance(report, graph, posedness, layout, solve);
        OperatingPoints(report, graph, layout, solve);
        Equations(report, system, seed, solve);
        Sized(report, graph, bases, notes);
        Ratings(report, graph, layout, solve);
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
        int passes,
        ImmutableArray<int> passIterations)
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
                // Per pass, because a seed change moves them unevenly (S-66): a sum hides a first pass
                // that got cheaper behind a third that got dearer.
                var perPass = passIterations.IsDefaultOrEmpty
                    ? string.Empty
                    : $", {string.Join(" + ", passIterations)} iterations";

                report.AppendLine(CultureInfo.InvariantCulture, $"    sizing       {passes} pass(es){perPass}");
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

    private static string Clip(string text, int width) =>
        text.Length <= width ? text : text[..(width - 1)] + "…";

    /// <summary>A branch end as the script spells its port: <c>T1.in[2]</c> for the port keyed <c>in2</c> (<c>L-56</c>).</summary>
    /// <remarks>The graph may not name the registry (<c>23</c> invariant 7), so the spelling is the report's, not <see cref="BranchEnd.Label"/>'s.</remarks>
    private static string Spelled(BranchEnd end) =>
        end.PortName is null
            ? end.Element.Name
            : $"{end.Element.Name}.{ComponentRegistry.Default.ByKeyword(end.Element.Kind)?.PortName(end.PortName) ?? end.PortName}";

    /// <summary>A sizing key <c>HX1.flow2</c> spelled as the script writes it, <c>HX1.in[2].flow</c> (<c>L-56</c>).</summary>
    private static string Spelled(CircuitGraph graph, string key)
    {
        var dot = key.IndexOf('.', StringComparison.Ordinal);

        if (dot < 0)
        {
            return key;
        }

        var owner = graph.Components.FirstOrDefault(element => string.Equals(element.Name, key[..dot], StringComparison.Ordinal));
        var kind = owner is null ? null : ComponentRegistry.Default.ByKeyword(owner.Kind);

        return kind is null ? key : key[..(dot + 1)] + kind.ParameterName(key[(dot + 1)..]);
    }
}
