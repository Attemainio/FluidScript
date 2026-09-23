using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Solvers.Equations;

public sealed partial class EquationSystem
{
    /// <summary>Resolves each stated constraint to the state its residual reads.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="posedness">The counting table, whose constraint order the rows follow.</param>
    /// <param name="equations">The row layout, for the offset the constraint block starts at.</param>
    /// <param name="ports">Which node and branch each component port attaches to.</param>
    /// <param name="byComponent">Each node component's index among the graph's nodes.</param>
    /// <param name="unknowns">The unknown layout, for a volume-flow row's node columns.</param>
    /// <param name="seed">The starting iterate, whose density at the node scales a volume-flow row.</param>
    /// <returns>One entry per constraint, in row order.</returns>
    /// <remarks>
    /// A fixed-flow outlet with known duty and inlet is evaluated in its derived flow form (<c>D-92</c>).
    /// This is algebraically equivalent to the outlet-temperature form away from zero, but it does not
    /// admit the artificial near-zero-flow root created when duty upwinding blends a finite duty across
    /// both ports. The target is the duty ratio from the three stated constants —
    /// <see cref="FluidScript.Core.Sizing.Flows.BranchFlows.RatedFlow"/>, the same arithmetic the seed uses, and not the seed's
    /// estimate for the branch (<c>S-57</c>). The flow residual is multiplied by ΔT/ṁ so the existing
    /// kelvin scale and diagnostics remain valid. Other absolute and difference constraints continue to
    /// read node temperatures directly.
    /// </remarks>
    private static Constraint[] Constraints(
        CircuitGraph graph,
        WellPosednessResult posedness,
        EquationLayout equations,
        PortMap ports,
        Dictionary<object, int> byComponent,
        SystemLayout unknowns,
        StateVector seed)
    {
        var resolved = new Constraint[posedness.Counting.Constraints.Length];

        for (var index = 0; index < resolved.Length; index++)
        {
            var constraint = posedness.Counting.Constraints[index];
            var row = equations.ConstraintOffset + index;
            var target = 0.0;
            var element = Array.FindIndex(
                [.. graph.Components],
                candidate => string.Equals(candidate.Name, constraint.Component, StringComparison.Ordinal));

            resolved[index] = new Constraint(row, -1, -1, 0, 1);

            if (element < 0
                || HydraulicPartition.Stated(graph.Components[element], constraint.Parameter) is not { } stated)
            {
                continue;
            }

            target = stated;

            // A stated flow pins its branch outright (P5.13b, `S-72`): `flow` at the number, `vflow` at
            // the number times the density of the side's inlet node *as solved* -- `ṁ − ρ(p, h)·V̇ = 0`,
            // the identity V̇ = ṁ/ρ written where it holds rather than converted once at bind time with
            // a density the solve then contradicts (water is 2.5 % lighter at 80 °C than at 20 °C). The
            // row keeps the constraint block's kelvin scale: a 100 % flow error reads as the
            // temperature scale, so convergence on the row is convergence on the flow.
            if (constraint.Kind is ConstraintKind.FixedFlow
                && WellPosedness.IsStatedFlow(constraint.Parameter)
                && graph.Components[element] is HeatExchanger or Pump)
            {
                var side = ports[element, constraint.Parameter.EndsWith('2') ? 2 : 0];

                if (!side.CarriesFlow || stated <= 0)
                {
                    continue;
                }

                if (constraint.Parameter.StartsWith("vflow", StringComparison.Ordinal))
                {
                    var density = side.Node >= 0 ? SeedDensity(graph, unknowns, seed, side.Node) : double.NaN;

                    if (side.Node < 0 || !double.IsFinite(density) || density <= 0)
                    {
                        continue;
                    }

                    resolved[index] = new Constraint(
                        row, -1, -1, 0, side.Sign, side.Branch, 0, Tolerances.TemperatureScale / (density * stated), side.Node, stated);
                    continue;
                }

                resolved[index] = new Constraint(
                    row, -1, -1, 0, side.Sign, side.Branch, stated, Tolerances.TemperatureScale / stated);
                continue;
            }

            // A switched-off coil's flow pin is a pin at zero: `power=0` with its terminals stated is
            // m = 0/(h_out - h_in), and the temperature form it used to fall back to -- the outlet node
            // *at* 30 °C -- is a statement about a node nothing flows through, which the header may or
            // may not happen to satisfy (`S-56`). The residual keeps the row's kelvin scale through the
            // nominal seed flow: 0.1 kg/s of leakage reads as the coil's whole design span.
            if (constraint.Kind is ConstraintKind.FixedFlow
                && !WellPosedness.IsStatedFlow(constraint.Parameter)
                && graph.Components[element] is HeatExchanger stopped
                && WellPosedness.ZeroDuty(stopped))
            {
                var side = ports[element, constraint.Parameter.EndsWith('2') ? 2 : 0];
                var span = SideSpan(stopped, constraint.Parameter);

                if (side.CarriesFlow && span > 0)
                {
                    resolved[index] = new Constraint(
                        row, -1, -1, 0, side.Sign, side.Branch, 0, span / FluidScript.Core.Sizing.Flows.BranchFlows.Nominal);
                    continue;
                }
            }

            // Either side: `out` with `in` pins side 1's branch through port 0, `out2` with `in2` pins side
            // 2's through port 2 (`D-97`).
            if (constraint.Kind is ConstraintKind.FixedFlow
                && constraint.Parameter is "out" or "out2"
                && graph.Components[element] is HeatExchanger exchanger
                && exchanger.StatedParameters.TryGetValue(constraint.Parameter is "out" ? "in" : "in2", out var inlet)
                && exchanger.StatedParameters.TryGetValue(constraint.Parameter, out var outlet))
            {
                var binding = ports[element, constraint.Parameter is "out" ? 0 : 2];
                var rated = binding.CarriesFlow
                    ? FluidScript.Core.Sizing.Flows.BranchFlows.RatedFlow(graph.Substance, exchanger.Power, inlet, outlet)
                    : null;
                var temperatureSpan = Math.Abs(outlet.SiValue - inlet.SiValue);

                if (rated is { } magnitude && magnitude > Tolerances.FlowZero && temperatureSpan > 0)
                {
                    resolved[index] = new Constraint(
                        row,
                        -1,
                        -1,
                        0,
                        binding.Sign,
                        binding.Branch,
                        magnitude,
                        temperatureSpan / magnitude);
                    continue;
                }
            }

            if (graph.Components[element] is CircuitNode node)
            {
                resolved[index] = new Constraint(row, byComponent[node], -1, target, 1);
                continue;
            }

            var difference = constraint.Parameter is "dt" or "dt2";
            var suffix = constraint.Parameter.EndsWith('2') ? "2" : string.Empty;
            var outletNode = Attached(graph, ports, element, "out" + suffix);

            if (!difference)
            {
                resolved[index] = new Constraint(
                    row, Attached(graph, ports, element, constraint.Parameter), -1, target, 1);
                continue;
            }

            // The kind's sign, not the script's: `load power=20 dt=20` is stated positive and carried
            // negative (`ComponentFactory`), and reading the statement made the row demand that the load
            // heat its stream by 20 K. Only `power=-150` on a bare `heat_exchanger` had ever met this row,
            // which is why it held (`S-73`).
            var duty = (graph.Components[element] as HeatExchanger)?.Power ?? 0;

            resolved[index] = new Constraint(
                row,
                outletNode,
                Attached(graph, ports, element, "in" + suffix),
                target,
                duty < 0 ? -1 : 1);
        }

        return resolved;
    }

    /// <summary>The nodes inside every branch pinned at zero flow, each with the node it takes its temperature from.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="ports">Which node each component port attaches to.</param>
    /// <param name="byComponent">Each node component's index among the nodes.</param>
    /// <param name="constraints">The resolved constraints; the flow pins at zero name the branches.</param>
    /// <returns>Node and anchor pairs, both indices among the graph's nodes.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Stagnant water has no steady-state temperature</strong> (<c>S-56</c>). A node's energy
    /// balance is Σ ṁ·h over its ports, and on a branch held at exactly zero flow every term is zero
    /// whatever the enthalpy: the row is satisfied by anything, its column is empty, and the Jacobian
    /// is singular by one for each node inside the stopped branch -- which is what the rad-off header
    /// reported, naming the pump head and the coil's outlet enthalpy as the pair nothing separates.
    /// </para>
    /// <para>
    /// The rule that replaces those rows: <em>the water in a stopped branch sits at the temperature of
    /// the header node it hangs from</em> -- the branch's <c>To</c> end when that is a node, else its
    /// <c>From</c> end. It is a modelling choice and a mild one: in a plant the stopped coil cools to the
    /// room, and nothing steady says what it holds. What the rule buys is a row with a slope, and a
    /// reported temperature on the stopped branch that is the header's rather than an artefact of the
    /// seed. A branch whose ends are both junction elements has no anchor and keeps its rows; the
    /// singularity is then reported as before.
    /// </para>
    /// </remarks>
    private static (int Node, int Anchor)[] Stagnant(
        CircuitGraph graph,
        PortMap ports,
        Dictionary<object, int> byComponent,
        Constraint[] constraints)
    {
        var stagnant = new List<(int Node, int Anchor)>();
        var index = new Dictionary<IFlowComponent, int>(ReferenceEqualityComparer.Instance);

        for (var element = 0; element < graph.Components.Length; element++)
        {
            index[graph.Components[element]] = element;
        }

        foreach (var constraint in constraints)
        {
            // A pin at zero, and only that: a volume-flow row carries its target on the volume side and
            // a mass target of zero, which is not a stopped branch.
            if (constraint.FlowBranch < 0 || constraint.VolumeNode >= 0 || constraint.FlowTarget != 0)
            {
                continue;
            }

            var branch = graph.Branches[constraint.FlowBranch];
            var ends = new[] { branch.To.Element, branch.From.Element }
                .Where(end => end is CircuitNode)
                .Select(end => byComponent[end])
                .ToArray();

            if (ends.Length == 0)
            {
                continue;
            }

            var anchor = ends[0];
            var inside = new SortedSet<int>();

            foreach (var component in branch.Path)
            {
                var element = index[component];

                for (var port = 0; port < component.Ports.Length; port++)
                {
                    var binding = ports[element, port];

                    if (binding.Branch == branch.Index && binding.Node >= 0 && !ends.Contains(binding.Node))
                    {
                        inside.Add(binding.Node);
                    }
                }
            }

            foreach (var node in inside)
            {
                stagnant.Add((node, anchor));
            }
        }

        // A dead leg is the same closure with no constraint behind it (S-23): a branch ending at a
        // terminal node the script gave no role has its flow forced to zero by that node's own mass
        // balance, and then the node's energy balance is ṁ·h with ṁ = 0 -- a zero row on a zero
        // column, singular for a reason that has nothing to do with the circuit. The dead-end node and
        // everything inside its branch take the temperature of the node at the live end. A leg whose
        // live end is a junction element rather than a node has no anchor and keeps its rows.
        foreach (var branch in graph.Branches)
        {
            var deadEnd = DeadEnd(branch.To) ? branch.To : DeadEnd(branch.From) ? branch.From : null;

            if (deadEnd is null)
            {
                continue;
            }

            var live = ReferenceEquals(deadEnd, branch.To) ? branch.From : branch.To;

            if (live.Element is not CircuitNode || DeadEnd(live))
            {
                continue;
            }

            var anchor = byComponent[live.Element];
            var claimed = stagnant.Select(static pair => pair.Node).ToHashSet();
            var nodes = new SortedSet<int> { byComponent[deadEnd.Element] };

            foreach (var component in branch.Path)
            {
                var element = index[component];

                for (var port = 0; port < component.Ports.Length; port++)
                {
                    var binding = ports[element, port];

                    if (binding.Branch == branch.Index && binding.Node >= 0 && binding.Node != anchor)
                    {
                        nodes.Add(binding.Node);
                    }
                }
            }

            foreach (var node in nodes)
            {
                if (!claimed.Contains(node))
                {
                    stagnant.Add((node, anchor));
                }
            }
        }

        return [.. stagnant];

        // A terminal node with nothing stated: one connection, no boundary role. A supply or a return
        // with one connection is a boundary whose flux is an unknown, not a dead end (D-64).
        static bool DeadEnd(BranchEnd end) =>
            end.Element is CircuitNode { Boundary: BoundaryRole.Interior, Ports.Length: 1 };
    }
}
