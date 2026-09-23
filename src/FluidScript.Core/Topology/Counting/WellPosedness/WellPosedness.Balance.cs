using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Topology.Counting;

public static partial class WellPosedness
{
    /// <summary>Reports a closed circuit whose stated duties cannot sum to zero (<c>FS2203</c>).</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="hydraulics">Its hydraulic partition.</param>
    /// <param name="diagnostics">Where to report.</param>
    /// <remarks>
    /// <para>
    /// <strong>Consistency is a different question from squareness.</strong> A closed ring with a 30 kW
    /// source and no sink has exactly as many equations as unknowns and no solution, because summing its
    /// energy balances gives <c>Σ Q̇ = 0</c> against stated duties that do not. The counting pass cannot
    /// see it and no amount of promotion will, so it is checked here.
    /// </para>
    /// <para>
    /// <strong>Steady mode only.</strong> The same circuit in a transient is a warm-up study: the water
    /// heats up, which is what the storage term is for.
    /// </para>
    /// </remarks>
    private static void ReportClosure(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (graph.Mode is not SolveMode.Steady)
        {
            return;
        }

        foreach (var hydraulic in hydraulics)
        {
            if (!hydraulic.IsClosed || Imbalance(hydraulics, hydraulic) is not { } imbalance)
            {
                continue;
            }

            diagnostics.Add(Diagnostic.Create(
                TopologyDiagnostics.UnbalancedClosedCircuit,
                span: null,
                new DiagnosticArgument("circuit", Name(graph, hydraulics, hydraulic)),
                new DiagnosticArgument(
                    "power",
                    (imbalance / 1000).ToString("0.###", CultureInfo.InvariantCulture) + " kW")));
        }
    }

    /// <summary>The heat a closed circuit's stated duties leave with nowhere to go.</summary>
    /// <param name="hydraulics">The hydraulic partition, which is what says whether a side is wired.</param>
    /// <param name="hydraulic">The closed component to sum.</param>
    /// <returns>
    /// The signed imbalance in W, positive when heat is added; or <see langword="null"/> when the
    /// circuit can balance, when a duty is still to be sized, or when the sign search is too wide to
    /// mean anything.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>An unstated duty ends the sum rather than contributing zero</strong> (<c>D-02</c>). A duty
    /// the script never wrote is one sizing may still choose, and a total missing a term says nothing
    /// about the total.
    /// </para>
    /// <para>
    /// <strong>A coupled exchanger's sign is not readable from the graph.</strong> <c>power</c> is
    /// positive when side 1 gains, and which side is in <em>this</em> component is a fact about ports
    /// that the branch decomposition does not carry — a branch records its ends' ports and an exchanger
    /// is interior to a branch on each side. So every assignment is tried and the circuit is reported
    /// only when none of them balances, which is the reading that cannot produce a false error: the
    /// substation's closed secondary balances under exactly one of its two.
    /// </para>
    /// <para>
    /// A pump contributes nothing here. It writes a pressure relation and no energy row, so a closed
    /// loop of pumps and pipes balances at exactly zero rather than nearly zero.
    /// </para>
    /// </remarks>
    private static double? Imbalance(
        ImmutableArray<HydraulicComponent> hydraulics, HydraulicComponent hydraulic)
    {
        // Four couplings is sixteen assignments; past that a balance found by search says more about
        // arithmetic luck than about the circuit, so the check stands down instead of reporting one.
        const int searchLimit = 4;

        var known = 0d;
        var scale = 0d;
        var coupled = new List<double>();

        foreach (var element in hydraulic.Elements)
        {
            if (element is not HeatExchangerComponent exchanger)
            {
                continue;
            }

            if (HydraulicPartition.Stated(exchanger, "power") is null)
            {
                return null;
            }

            var power = exchanger.Power;

                scale += Math.Abs(power);

            if (IsCoupled(hydraulics, exchanger))
            {
                coupled.Add(power);
            }
            else
            {
                known += power;
            }
        }

        if (coupled.Count > searchLimit)
        {
            return null;
        }

        var smallest = double.PositiveInfinity;
        var imbalance = 0d;

        for (var assignment = 0; assignment < 1 << coupled.Count; assignment++)
        {
            var total = known;

            for (var i = 0; i < coupled.Count; i++)
            {
                total += (assignment & (1 << i)) == 0 ? coupled[i] : -coupled[i];
            }

            if (Math.Abs(total) < smallest)
            {
                smallest = Math.Abs(total);
                imbalance = total;
            }
        }

        // Stated duties are exact to the digit the script wrote, so the only slack the sum needs is the
        // rounding of adding them together.
        return smallest > 1e-9 * Math.Max(scale, 1) ? imbalance : null;
    }

    /// <summary>Reports a system that is not square, naming what would square it.</summary>
    /// <remarks>
    /// <strong>The list is the whole value of the message.</strong> "Over-specified by 1" is a puzzle;
    /// "remove HE1.in" is a fix. The candidates for an over-specification are the constraints that found
    /// nothing to promote, because those are precisely the statements the circuit cannot meet.
    /// </remarks>
    private static void ReportBalance(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        CountingTable counting,
        Assignment.Result assignment,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (counting.Excess == 0)
        {
            return;
        }

        var promotions = assignment.Promotions;
        var absorbed = promotions.Select(static promotion => promotion.Constraint).ToHashSet();

        if (counting.Excess > 0)
        {
            var unmatched = counting.Constraints.Where(constraint => !absorbed.Contains(constraint)).ToArray();

            // A closed hydraulic stating two pressures has a level stated twice, and the row the rank
            // analysis finds redundant is one of those two -- not the inlet the dropped enthalpy level
            // paid for, which is what this named first (`PU1 pump in.p=100 out.p=250`, P5.13b). The
            // levels pay for that many unmatched constraints; only the rest are candidates.
            var doubled = hydraulics
                .Where(static block => block.Boundaries.IsEmpty && block.StatedPressures.Length > 1)
                .SelectMany(static block => block.StatedPressures)
                .Select(static node => PressureLabel(node))
                .ToArray();

            // Which unmatched statement each level pays for: a temperature in that hydraulic, the same rule
            // `Understated` reads the list by (`D-90`), and only failing that the first in constraint order.
            var paid = new HashSet<ComponentConstraint>();

            foreach (var level in counting.LevelComponents)
            {
                var pays = unmatched.FirstOrDefault(constraint =>
                        constraint.Hydraulic == level.Index
                        && constraint.Kind is ConstraintKind.MixedInlet or ConstraintKind.NodeTemperature
                        && !paid.Contains(constraint))
                    ?? unmatched.FirstOrDefault(constraint => constraint.Hydraulic == level.Index && !paid.Contains(constraint));

                if (pays is not null)
                {
                    paid.Add(pays);
                }
            }

            // An unmatched constraint names its whole group (`D-133`): every statement it competes with for
            // the same actuators is as much the one too many as it is. Two temperatures demanding one
            // level are both named, as before; only beside a doubled pressure is the level's own skipped.
            var groups = assignment.Unmatched;
            var named = groups
                .SelectMany(static group => group.Sharing)
                .Distinct()
                .OrderBy(constraint => counting.Constraints.IndexOf(constraint))
                .ToArray();
            var beyondLevels = named.Where(constraint => !paid.Contains(constraint)).Select(static constraint => constraint.Label).ToArray();

            var candidates = doubled.Length > 0
                ? doubled.Concat(beyondLevels)
                : unmatched.Length > 0
                    ? named.Select(static constraint => constraint.Label)
                    : Overstated(graph);

            // A flow nothing on its branch can change is not a statement to remove but a valve to add
            // (23's promotion rules, C-28): the second sentence names the branch by its component, for
            // every pinned flow in an unmatched group whose branch holds no valve. A temperature no
            // split's stream reaches is likewise a mixing valve to add.
            var unreachable = named
                .Where(constraint => !paid.Contains(constraint)
                    && constraint.Kind == ConstraintKind.FixedFlow
                    && !HasBranchValve(graph, constraint.Component))
                .Select(static constraint => constraint.Component)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var unmixed = groups
                .Where(group => !paid.Contains(group.Constraint)
                    && group.Actuators.IsEmpty
                    && group.Constraint.Kind is ConstraintKind.MixedInlet or ConstraintKind.NodeTemperature)
                .Select(static group => group.Constraint.Label)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var advice = unreachable.Length == 0
                ? string.Empty
                : $", or add a valve: nothing on the branch through {string.Join(", ", unreachable)} can change its flow";

            if (unmixed.Length > 0)
            {
                advice += $", or add a mixing valve: no mixing valve's stream reaches {string.Join(", ", unmixed)}";
            }

            foreach (var group in groups.Where(static group => group.Sharing.Length > 1).DistinctBy(static group => string.Join(",", group.Sharing.Select(static c => c.Label))))
            {
                advice += $"; {string.Join(", ", group.Sharing.Select(static constraint => constraint.Label))} share {string.Join(", ", group.Actuators)}";
            }

            // A stated pressure on a node with one pipe is a datum on a stub (D-86): it holds the level and passes
            // no mass, which is the surplus whenever the plant already has a level. What the user nearly always
            // meant is a boundary, which only the keyword makes (D-115); the message says which word.
            var stubs = graph.Nodes
                .Where(static node => node.Component.Boundary == BoundaryRole.Interior
                    && node.Component.Ports.Length == 1
                    && HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure) is not null)
                .Select(static node => node.Name)
                .ToArray();

            if (stubs.Length > 0)
            {
                advice += $", or write '{stubs[0]} outlet' (or inlet) if fluid crosses there: a node's p= holds the pressure level and passes no mass";
            }

            diagnostics.Add(Diagnostic.Create(
                TopologyDiagnostics.OverSpecified,
                span: null,
                new DiagnosticArgument("n", counting.Excess.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticArgument("list", string.Join(", ", candidates)),
                new DiagnosticArgument("advice", advice)));

            return;
        }

        diagnostics.Add(Diagnostic.Create(
            TopologyDiagnostics.UnderSpecified,
            span: null,
            new DiagnosticArgument("n", (-counting.Excess).ToString(CultureInfo.InvariantCulture)),
            new DiagnosticArgument("list", string.Join(", ", Understated(graph, hydraulics, counting.Constraints, absorbed)))));
    }

    /// <summary>Whether a branch through the component holds a valve whose <c>kv</c> could take its flow.</summary>
    private static bool HasBranchValve(CircuitGraph graph, string component) =>
        graph.Branches.Any(branch =>
            branch.Path.Any(element => string.Equals(element.Name, component, StringComparison.Ordinal))
            && branch.Path.Any(static element => element is ValveComponentBase));

    /// <summary>What could be removed when no constraint is the culprit.</summary>
    /// <remarks>
    /// A pressure stated on a node with no mass balance is the case that lands here: the node is
    /// interior to a branch, so there is nowhere for external mass to enter and nothing to absorb the
    /// statement.
    /// </remarks>
    private static IEnumerable<string> Overstated(CircuitGraph graph) =>
        // Every stated pressure, boundary or datum: with no unmatched constraint the surplus is a level stated
        // twice, and which of the two goes is the user's. The old filter on the mass balance listed nothing
        // once a boundary carried one (D-115), so FS2210 read "remove one of: ." on a dead-end datum.
        graph.Nodes
            .Where(static node => HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure) is not null)
            .Select(static node => PressureLabel(node));

    /// <summary>What could be added to square an under-specified circuit.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="hydraulics">Its hydraulic partition, which is what knows a level is unfilled.</param>
    /// <param name="constraints">The constraint list, which is what knows whether a statement paid for the level.</param>
    /// <param name="absorbed">The constraints a promotion matched.</param>
    /// <returns>The candidates, the thermal ones first.</returns>
    /// <remarks>
    /// <para>
    /// <strong>A missing temperature is named ahead of a missing pressure</strong>, because it is the one
    /// the graph could not have picked for itself. A circuit that states no pressure gets a datum and an
    /// <c>FS2201</c>, so it never arrives here short of one; a circuit whose temperature level nothing
    /// fixes has no such fallback.
    /// </para>
    /// <para>
    /// <strong>Whether the level is fixed is read from the constraint list, not from the script</strong>
    /// (<c>D-90</c>, <c>S-52</c>). A component can state four temperatures and still hold no level: a
    /// terminal that pins a flow and a node temperature that a valve position answers are each matched
    /// to a promotion, and what pays for the dropped level is a statement that promotes nothing. The
    /// distribution header states <c>HE_AHU.in/out</c> and <c>HE_RAD.in/out</c>, all four claiming an
    /// actuator, and was told to add a pressure to a hydraulic half that was already square; taking that
    /// advice made the circuit square and singular.
    /// </para>
    /// </remarks>
    private static string[] Understated(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ImmutableArray<ComponentConstraint> constraints,
        HashSet<ComponentConstraint> absorbed)
    {
        var candidates = new List<string>();

        foreach (var hydraulic in hydraulics)
        {
            var levelled = constraints.Any(constraint =>
                constraint.Hydraulic == hydraulic.Index
                && (constraint.Kind == ConstraintKind.EnthalpyLevel
                    || (constraint.Kind is ConstraintKind.NodeTemperature or ConstraintKind.MixedInlet && !absorbed.Contains(constraint))));

            if (NeedsEnthalpyLevel(graph, hydraulics, hydraulic) && !levelled)
            {
                candidates.AddRange(hydraulic.Nodes.Select(static node => $"a temperature on {node.Name}"));
            }
        }

        candidates.AddRange(graph.Nodes
            .Where(static node =>
                node.Component.CarriesMassBalance
                && HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure) is null)
            .Select(static node => $"a pressure on {node.Name}"));

        return candidates.Count > 0 ? [.. candidates] : ["a boundary condition"];
    }
}
