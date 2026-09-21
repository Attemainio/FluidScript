using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Language;
using FluidScript.Core.Sizing;
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

    private static string Clip(string text, int width) =>
        text.Length <= width ? text : text[..(width - 1)] + "…";

    private static void State(
        StringBuilder report,
        CircuitGraph graph,
        SystemLayout layout,
        StateVector seed,
        SolveResult? solve)
    {
        // Every node in °C and kPa and every branch in kg/s against its written direction (`S-71`). The
        // unknowns table is the solver's view -- enthalpies and pascals -- and an engineer cannot argue
        // with an enthalpy.
        report.AppendLine();
        report.AppendLine("--- state, in engineering units");

        var at = solve?.Solution ?? seed;
        var label = solve is null ? "seed" : "solved";

        report.AppendLine(CultureInfo.InvariantCulture,
            $"    nodes ({label})      t °C      p kPa");

        for (var node = 0; node < graph.Nodes.Length; node++)
        {
            var pressure = layout.NodePressure(node);
            var enthalpy = layout.NodeEnthalpy(node);

            if (pressure >= at.Values.Length || enthalpy >= at.Values.Length)
            {
                continue;
            }

            var state = graph.Substance.FromPressureEnthalpy(
                Units.Quantity.FromSi(at.Values[pressure], Units.Dimension.Pressure),
                Units.Quantity.FromSi(at.Values[enthalpy], Units.Dimension.Enthalpy));
            var temperature = state.IsSuccess
                ? (state.Value.Temperature.SiValue - 273.15).ToString("0.00", CultureInfo.InvariantCulture)
                : "(out of range)";

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {graph.Nodes[node].Name,-18} {temperature,9} {at.Values[pressure] / 1000,10:0.00}");
        }

        report.AppendLine(CultureInfo.InvariantCulture,
            $"    branches ({label})   kg/s      direction, against the script");

        // "Written order" is what the port map knows, not the branch's sign: a ring's path may start
        // at whichever element the walk reached first, and on the two-ring substation both branches
        // were labelled reversed while every pump pushed the way it was written (S-70). The sign of
        // the flow entering the first ported element's `in` is the direction the script would call
        // forward, which is what `OuterLoop.Reversals` reads for FS3013.
        var ports = PortMap.Build(graph);

        foreach (var branch in graph.Branches)
        {
            var column = layout.BranchFlow(branch.Index);

            if (column >= at.Values.Length)
            {
                continue;
            }

            var flow = at.Values[column];
            var direction = Direction(graph, ports, branch, flow);
            var path = branch.Path.Length == 0
                ? "(direct)"
                : string.Join(" - ", branch.Path.Select(static element => element.Name));

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {Spelled(branch.From)} -> {Spelled(branch.To),-14} {Math.Abs(flow),9:0.0000}  {direction,-9} {path}");
        }
    }

    private static void HeatBalance(
        StringBuilder report,
        CircuitGraph graph,
        WellPosednessResult posedness,
        SystemLayout layout,
        SolveResult? solve)
    {
        // The first line an engineer checks (`S-71`): what goes in, what comes out, per hydraulic. The
        // duties are the lowered ones -- stated, or what the closure chose -- and a boundary stream
        // carries the enthalpy of the node it crosses at, so an open circuit's sum is its net enthalpy
        // flux. `FS2203` checks this and says nothing about the numbers.
        report.AppendLine();
        report.AppendLine("--- heat balance");

        var at = solve?.Solution;

        foreach (var hydraulic in posedness.Hydraulics)
        {
            var sources = new List<string>();
            var loads = new List<string>();
            var sourceTotal = 0.0;
            var loadTotal = 0.0;

            foreach (var element in hydraulic.Elements)
            {
                if (element is not HeatExchanger exchanger || exchanger.Power == 0)
                {
                    continue;
                }

                // `Power` is side 1's gain. A coupled exchanger sits in two hydraulics and gives the one
                // holding its side 2 the same duty with the opposite sign; which side is here is read
                // off the branch that carries it.
                var side = hydraulic.Branches
                    .Where(branch => branch.Path.Contains(exchanger))
                    .Select(branch => BranchFlows.Side(graph, branch, exchanger))
                    .DefaultIfEmpty(1)
                    .First();
                var duty = side == 2 ? -exchanger.Power : exchanger.Power;
                var entry = $"{exchanger.Name} {duty / 1000:+0.###;-0.###}";

                if (duty > 0)
                {
                    sources.Add(entry);
                    sourceTotal += duty;
                }
                else
                {
                    loads.Add(entry);
                    loadTotal += duty;
                }
            }

            var crossing = 0.0;
            var streams = new List<string>();

            if (at is not null)
            {
                for (var flux = 0; flux < layout.FluxNodes.Length; flux++)
                {
                    var node = layout.FluxNodes[flux];

                    if (!hydraulic.Nodes.Contains(node))
                    {
                        continue;
                    }

                    var index = graph.Nodes.IndexOf(node);
                    var column = layout.ExternalFluxOffset + flux;

                    if (index < 0 || column >= at.Values.Length)
                    {
                        continue;
                    }

                    var mass = at.Values[column];
                    var energy = mass * at.Values[layout.NodeEnthalpy(index)];
                    crossing += energy;
                    streams.Add($"{node.Name} {mass:+0.####;-0.####} kg/s, {energy / 1000:+0.###;-0.###} kW");
                }
            }

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    [{hydraulic.Index}] sources {sourceTotal / 1000:+0.###;-0.###} kW"
                + $"{(sources.Count > 0 ? $" ({string.Join(", ", sources)})" : string.Empty)}, "
                + $"loads {loadTotal / 1000:+0.###;-0.###} kW"
                + $"{(loads.Count > 0 ? $" ({string.Join(", ", loads)})" : string.Empty)}, "
                + $"boundary streams {crossing / 1000:+0.###;-0.###} kW"
                + $"{(streams.Count > 0 ? $" ({string.Join(", ", streams)})" : string.Empty)}"
                + $" — net {(sourceTotal + loadTotal + crossing) / 1000:+0.###;-0.###} kW");
        }
    }

    private static void OperatingPoints(
        StringBuilder report,
        CircuitGraph graph,
        SystemLayout layout,
        SolveResult? solve)
    {
        if (solve is null)
        {
            return;
        }

        // Where each pump sits on its curve and what each valve is doing (`S-71`): the ratings section
        // covers exchangers, and pumps and valves appeared only as promoted unknowns and sizing lines.
        // A promoted parameter's solved value is read off the solution; a sized or stated one is the
        // component's own.
        var ports = PortMap.Build(graph);
        var promoted = Promoted(graph, layout, solve);
        var values = solve.Solution.Values;
        var written = false;

        for (var index = 0; index < graph.Components.Length; index++)
        {
            var component = graph.Components[index];

            if (component is Pump pump)
            {
                var inlet = ports[index, 0];
                var outlet = ports[index, 1];

                if (!inlet.CarriesFlow)
                {
                    continue;
                }

                var flow = inlet.Sign * values[layout.BranchFlow(inlet.Branch)];
                var head = promoted.TryGetValue((pump.Name, "head"), out var solvedHead)
                    ? solvedHead
                    : pump.Head(flow);
                var rise = outlet.Node >= 0 && inlet.Node >= 0
                    ? values[layout.NodePressure(outlet.Node)] - values[layout.NodePressure(inlet.Node)]
                    : double.NaN;

                var origin = promoted.ContainsKey((pump.Name, "head"))
                    ? "(solved for)"
                    : string.Create(CultureInfo.InvariantCulture, $"(on its curve, shut-off {pump.ShutOffHead:0.00} m)");

                Header(report, ref written);
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"    {pump.Name,-12} pump   {flow,9:0.0000} kg/s  head {head,7:0.00} m  rise {rise / 1000,8:0.00} kPa  {origin}");
            }
            else if (component is Valve valve)
            {
                var inlet = ports[index, 0];
                var outlet = ports[index, 1];

                if (!inlet.CarriesFlow)
                {
                    continue;
                }

                var flow = inlet.Sign * values[layout.BranchFlow(inlet.Branch)];
                var drop = outlet.Node >= 0 && inlet.Node >= 0
                    ? values[layout.NodePressure(inlet.Node)] - values[layout.NodePressure(outlet.Node)]
                    : double.NaN;

                Header(report, ref written);
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"    {valve.Name,-12} valve  {flow,9:0.0000} kg/s  Kv {Parameter(promoted, valve.Name, "kv", valve.Kv),7:0.##}"
                    + $"  position {Parameter(promoted, valve.Name, "position", valve.Position),5:0.###}  drop {drop / 1000,8:0.00} kPa");
            }
            else if (component is ThreeWayValve three)
            {
                var common = ports[index, 0];

                if (!common.CarriesFlow)
                {
                    continue;
                }

                var legs = new List<string>();

                for (var port = 0; port < three.Ports.Length; port++)
                {
                    var binding = ports[index, port];

                    if (!binding.CarriesFlow)
                    {
                        continue;
                    }

                    var into = binding.Sign * values[layout.BranchFlow(binding.Branch)];
                    var pressure = binding.Node >= 0 && common.Node >= 0
                        ? values[layout.NodePressure(binding.Node)] - values[layout.NodePressure(common.Node)]
                        : double.NaN;
                    var drop = port == 0 ? string.Empty : $", {pressure / 1000:0.00} kPa to {three.Ports[0].Name}";

                    var signed = (into < 0 ? "-" : "+") + Math.Abs(into).ToString("0.0000", CultureInfo.InvariantCulture);

                    legs.Add(string.Create(CultureInfo.InvariantCulture, $"{three.Ports[port].Name} {signed}{drop}"));
                }

                Header(report, ref written);
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"    {three.Name,-12} 3-way  Kv {Parameter(promoted, three.Name, "kv", three.Kv):0.##}"
                    + $"  position {Parameter(promoted, three.Name, "position", three.Position):0.###}  "
                    + $"kg/s into it: {string.Join("; ", legs)}");
            }
        }

        static void Header(StringBuilder report, ref bool written)
        {
            if (!written)
            {
                report.AppendLine();
                report.AppendLine("--- operating points");
                written = true;
            }
        }

        static double Parameter(Dictionary<(string, string), double> promoted, string owner, string name, double own) =>
            promoted.TryGetValue((owner, name), out var solved) ? solved : own;
    }

    /// <summary>Every promoted parameter's solved value, by owner and name.</summary>
    private static Dictionary<(string Owner, string Parameter), double> Promoted(
        CircuitGraph graph, SystemLayout layout, SolveResult solve)
    {
        var promoted = new Dictionary<(string, string), double>();
        var promotions = WellPosedness.Check(graph).Counting.Promotions;

        for (var index = 0; index < promotions.Length; index++)
        {
            var column = layout.PromotionOffset + index;

            if (column < solve.Solution.Values.Length)
            {
                promoted[(promotions[index].Component, promotions[index].Parameter)] = solve.Solution.Values[column];
            }
        }

        return promoted;
    }

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
    /// <para>
    /// <strong>LMTD is a reported property, never a residual (<c>22</c>).</strong> The solve transferred
    /// <c>ε·Cmin·(T_in2 − T_in1)</c>; this section takes the terminal temperatures that solve produced,
    /// forms the log-mean difference from them, and divides the duty by it. The conductance that comes
    /// out must be the one the exchanger was rated with, and the two share no code -- which is what makes
    /// printing both a check rather than a restatement. A crossflow exchanger is reported against the
    /// counterflow log-mean with no correction factor, and the line says so.
    /// </para>
    /// <para>
    /// Read only at a converged solution: the terminals of an unconverged iterate rate nothing.
    /// </para>
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
            if (graph.Components[index] is not HeatExchanger { Rating: { CanRate: true } rating } exchanger
                || SideAtSolution(graph, layout, solve, index, exchanger, side: 1) is not { } one)
            {
                continue;
            }

            double inlet2, capacity2;

            if (exchanger.SecondarySideConnected)
            {
                if (SideAtSolution(graph, layout, solve, index, exchanger, side: 2) is not { } two)
                {
                    continue;
                }

                (inlet2, capacity2) = two;
            }
            else
            {
                (inlet2, capacity2) = (rating.SecondaryInletTemperature, rating.SecondaryCapacityRate);
            }

            var duty = HeatExchanger.Duty(rating, one.Capacity, one.Inlet, capacity2, inlet2);
            var minimum = Math.Min(one.Capacity, capacity2);
            var ratio = minimum / Math.Max(one.Capacity, capacity2);
            var ntu = rating.Conductance / minimum;
            var effectiveness = Effectiveness.Of(ntu, ratio, rating.Arrangement);
            var outlet1 = one.Inlet + (duty / one.Capacity);
            var outlet2 = inlet2 - (duty / capacity2);
            var hotSide1 = one.Inlet >= inlet2;
            var (hotIn, hotOut, coldIn, coldOut) = hotSide1
                ? (one.Inlet, outlet1, inlet2, outlet2)
                : (inlet2, outlet2, one.Inlet, outlet1);
            var lmtd = rating.Arrangement == ExchangerArrangement.Parallel
                ? LogMeanTemperatureDifference.Parallel(hotIn, hotOut, coldIn, coldOut)
                : LogMeanTemperatureDifference.Counterflow(hotIn, hotOut, coldIn, coldOut);
            var byLogMean = LogMeanTemperatureDifference.Conductance(Math.Abs(duty), lmtd);
            var approach = rating.Arrangement == ExchangerArrangement.Parallel
                ? hotOut - coldOut
                : Math.Min(hotIn - coldOut, hotOut - coldIn);

            if (!written)
            {
                report.AppendLine();
                report.AppendLine("--- exchanger ratings at the solution");
                written = true;
            }

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {exchanger.Name,-12} {duty / 1000:0.###} kW {exchanger.Mode}: side 1 {one.Inlet - 273.15:0.##} -> {outlet1 - 273.15:0.##} °C at {one.Capacity / 1000:0.###} kW/K, side 2 {inlet2 - 273.15:0.##} -> {outlet2 - 273.15:0.##} °C at {capacity2 / 1000:0.###} kW/K");
            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {string.Empty,-12} NTU {ntu:0.###}, ε {effectiveness:0.####}, Cr {ratio:0.###}, approach {approach:0.##} K; UA {rating.Conductance / 1000:0.###} kW/K rated, {byLogMean / 1000:0.###} kW/K by LMTD {lmtd:0.###} K{(rating.Arrangement == ExchangerArrangement.Crossflow ? " (counterflow log-mean, no F correction)" : string.Empty)}");
        }
    }

    /// <summary>One side of an exchanger as the solution left it: where it enters and what it carries.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">The unknown layout.</param>
    /// <param name="solve">The solve.</param>
    /// <param name="index">The exchanger's index in the graph.</param>
    /// <param name="exchanger">The exchanger.</param>
    /// <param name="side">1 or 2.</param>
    /// <returns>K and W/K, or <see langword="null"/> when the side is not wired or its state cannot be read.</returns>
    /// <remarks>
    /// The inlet is the port the solved flow arrives by, not the port named <c>in</c>: a branch's path
    /// direction and its solved sign together say which end the stream enters at.
    /// </remarks>
    private static (double Inlet, double Capacity)? SideAtSolution(
        CircuitGraph graph, SystemLayout layout, SolveResult solve, int index, HeatExchanger exchanger, int side)
    {
        var branch = graph.Branches.FirstOrDefault(
            candidate => candidate.Path.Contains(exchanger) && BranchFlows.Side(graph, candidate, exchanger) == side);

        if (branch is null)
        {
            return null;
        }

        var position = branch.Path.IndexOf(exchanger);
        var before = position > 0 ? branch.Path[position - 1] : branch.From.Element;
        var arrival = graph.Components.IndexOf(before);
        var first = side == 1 ? 0 : 2;
        var arrivalPort = graph.Adjacency.Peer(index, first).Component == arrival ? first : first + 1;
        var flow = solve.Solution.Values[layout.BranchFlow(branch.Index)];
        var inletPort = flow >= 0 ? arrivalPort : (arrivalPort == first ? first + 1 : first);
        var peer = graph.Adjacency.Peer(index, inletPort);

        if (!peer.Exists)
        {
            return null;
        }

        var node = -1;

        for (var candidate = 0; candidate < graph.Nodes.Length; candidate++)
        {
            if (ReferenceEquals(graph.Nodes[candidate].Component, graph.Components[peer.Component]))
            {
                node = candidate;
                break;
            }
        }

        if (node < 0)
        {
            return null;
        }

        var state = graph.Substance.FromPressureEnthalpy(
            Units.Quantity.FromSi(solve.Solution.Values[layout.NodePressure(node)], Units.Dimension.Pressure),
            Units.Quantity.FromSi(solve.Solution.Values[layout.NodeEnthalpy(node)], Units.Dimension.Enthalpy));

        if (!state.IsSuccess)
        {
            return null;
        }

        var capacity = Math.Abs(flow) * state.Value.SpecificHeat.SiValue;

        return capacity > 0 ? (state.Value.Temperature.SiValue, capacity) : null;
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
