using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Sizing.Flows;
using FluidScript.Core.Solvers;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Diagnostics.Explanations;

public static partial class SolveExplanation
{
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
            // The whole record, not kind and component: a two-sided exchanger states a flow on each
            // side, and matching on the component alone answered both with side 1's pump (S-70).
            var match = t.Promotions.FirstOrDefault(promotion => promotion.Constraint == constraint);

            if (match is null)
            {
                unanswered++;
            }

            var answer = match is null
                ? "no promotion"
                : $"solved for as {match.Component}.{match.Parameter}";

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {constraint.Kind,-12} on {constraint.Label,-12} -> {answer}");
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
        CircuitGraph graph,
        SystemLayout layout,
        StateVector seed,
        SolveResult? solve)
    {
        report.AppendLine();
        report.AppendLine("--- unknowns, seeded and solved");
        report.AppendLine(
            "      # kind             owner        name                            seed        solved       seed basis");

        var solved = solve?.Solution.Values;

        // The basis each branch flow was seeded on (`S-68`): which rule produced the estimate and from
        // what. The seed's failures were all "which estimate did the closure overwrite", and the number
        // alone never said (`S-71`).
        var estimates = Estimates(graph);

        for (var index = 0; index < layout.Unknowns.Length; index++)
        {
            var declaration = layout.Unknowns[index];
            var start = index < seed.Values.Length ? seed.Values[index] : double.NaN;
            var end = solved is { } values && index < values.Length ? values[index] : double.NaN;
            var endText = double.IsNaN(end) ? "-" : end.ToString("G6", CultureInfo.InvariantCulture);
            var branch = index - layout.BranchFlowOffset;
            var basis = declaration.Kind == UnknownKind.BranchFlow && branch >= 0 && branch < estimates.Length
                ? $"  {estimates[branch].Basis}({estimates[branch].Source})"
                : string.Empty;

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {index,3} {declaration.Kind,-16} {declaration.OwnerComponentId,-12} "
                + $"{declaration.Name,-28} {start,11:G6} {endText,13} {declaration.SiUnit}{basis}");
        }
    }

    /// <summary>The seed's branch-flow estimates, or none rather than an exception out of a report.</summary>
    private static ImmutableArray<BranchFlow> Estimates(CircuitGraph graph)
    {
        try
        {
            return BranchFlows.Estimate(graph);
        }
#pragma warning disable CA1031 // A diagnostic that throws is worse than one that omits.
        catch (Exception)
#pragma warning restore CA1031
        {
            return [];
        }
    }

    private static void Iterations(StringBuilder report, SolveResult? solve)
    {
        if (solve is null || solve.History.IsDefaultOrEmpty)
        {
            return;
        }

        // The trajectory (`S-71`): each row is what the step set out to remove, how much of the Newton
        // step the line search took, which equation led and which unknown moved most. A failed solve
        // is diagnosed here -- where the residual stopped falling, whether α was halving against a
        // wall -- and the final norm alone said none of it.
        report.AppendLine();
        report.AppendLine("--- iterations");
        report.AppendLine("      # residual before   step  leading equation                          moved most                    by");

        foreach (var record in solve.History)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {record.Iteration,3} {record.ResidualNorm,15:G4} {record.StepLength,6:0.###}  "
                + $"{Clip(record.WorstEquation, 40),-40}  {Clip(record.LargestMove, 28),-28}  {record.LargestMoveScaled:G3}");
        }

        report.AppendLine(CultureInfo.InvariantCulture,
            $"    end {solve.ResidualNorm,15:G4}         {solve.Termination}");
    }

    /// <summary>
    /// A branch's flow direction in words a reader can check against the script: <c>forward</c> or
    /// <c>reversed</c> against the first ported element's own <c>in</c>/<c>out</c>; for a direct link
    /// with a named port at one end, <c>into</c> or <c>out of</c> that port; for a bare node-to-node
    /// link, along or against the printed arrow.
    /// </summary>
    private static string Direction(CircuitGraph graph, PortMap ports, Branch branch, double flow)
    {
        if (Math.Abs(flow) <= Tolerances.FlowZero)
        {
            return "still";
        }

        if (WrittenSign(graph, ports, branch) is { } written)
        {
            return written * flow > 0 ? "forward" : "reversed";
        }

        // A direct link's orientation is the walk's, not the script's, and the script's is not kept
        // (the graph carries no syntax). What is checkable is which way water crosses the named port.
        foreach (var end in new[] { branch.From, branch.To })
        {
            var index = end.PortName is null ? -1 : graph.Components.IndexOf(end.Element);
            var binding = index < 0 ? PortBinding.Unconnected : ports[index, end.Port];

            if (binding.Branch == branch.Index && binding.Sign != 0)
            {
                return binding.Sign * flow > 0 ? $"into {Spelled(end)}" : $"out of {Spelled(end)}";
            }
        }

        return flow > 0 ? "along the arrow" : "against the arrow";
    }

    /// <summary>
    /// The sign that turns a branch's flow into the script's own direction: <c>+1</c> when the branch's
    /// orientation runs from the first ported element's <c>in</c> to its <c>out</c>, <c>-1</c> when the
    /// walk crossed that element backwards, and <see langword="null"/> for a branch with no ported element.
    /// </summary>
    private static int? WrittenSign(CircuitGraph graph, PortMap ports, Branch branch)
    {
        foreach (var element in branch.Path)
        {
            var index = graph.Components.IndexOf(element);

            if (index < 0)
            {
                continue;
            }

            for (var port = 0; port < element.Ports.Length; port++)
            {
                if (!string.Equals(element.Ports[port].Name, "in", StringComparison.Ordinal))
                {
                    continue;
                }

                var binding = ports[index, port];

                if (binding.Branch == branch.Index && binding.Sign != 0)
                {
                    return binding.Sign;
                }
            }
        }

        return null;
    }
}
