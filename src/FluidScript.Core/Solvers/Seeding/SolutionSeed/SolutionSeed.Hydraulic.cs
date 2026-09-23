using FluidScript.Core.Components;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Solvers.Seeding;

public static partial class SolutionSeed
{
    /// <summary>The pressure field the components' own laws imply at the seeded flows.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">The state vector's layout, for where each branch keeps its flow.</param>
    /// <param name="values">The iterate being built; its branch flows must already be filled.</param>
    /// <param name="level">Pa. What a part of the circuit starts at when no node in it states a pressure.</param>
    /// <param name="datum">K. The temperature the laws are evaluated at.</param>
    /// <returns>One gauge pressure in Pa per node, indexed as <c>graph.Nodes</c> is.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The seed reconciled its flows and then laid its pressures by a rule that never asked a
    /// component anything</strong> (<c>S-44</c>). Every unstated node took <c>level − 10 kPa × step</c>,
    /// so what the seed offered a valve was a step count, not a pressure difference its own law would
    /// produce: measured on <c>m2-distribution-header</c>, <c>TV_AHU</c>'s Kv law came out at 1.125 kg/s
    /// on a leg carrying 0.574 — the single largest scaled residual in the circuit, and one the seed
    /// created rather than inherited.
    /// </para>
    /// <para>
    /// <strong>So integrate the laws instead of inventing a ladder.</strong> The flows are already
    /// mass-consistent and already in <paramref name="values"/>, and each component states its own drop
    /// at a flow through <see cref="BranchResistance"/>. Walk out from a starting element, branch by
    /// branch, subtracting each component's contribution as the walk crosses it and handing every
    /// interior node the running pressure as it passes. A pump raises the running value because its law
    /// returns a negative resistance, which is what a pump does to a loop.
    /// </para>
    /// <para>
    /// <strong>This cannot satisfy every component law, and it is not meant to.</strong> The walk is a
    /// spanning tree of the branch graph: tree branches come out consistent and each independent loop
    /// leaves one chord carrying the loop's whole closure error — the Hardy Cross structure. For a seed
    /// that is the right trade. It moves the circuit from every pressure row wrong to one row per loop
    /// wrong, and closing the chords is precisely what Newton is for.
    /// </para>
    /// <para>
    /// A stated pressure wins wherever the walk meets one and the walk continues from it, so a datum
    /// anchors the part it sits in rather than merely overwriting one node. A part whose substance
    /// refuses a state at the level gets that level flat: a seed may be wrong, and well-posedness has
    /// already reported the refusal (<c>FS2205</c>).
    /// </para>
    /// </remarks>
    private static double[] Integrate(
        CircuitGraph graph, SystemLayout layout, double[] values, double level, double datum)
    {
        var pressures = new double[graph.Nodes.Length];
        var index = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

        Array.Fill(pressures, level);

        for (var node = 0; node < graph.Nodes.Length; node++)
        {
            index[graph.Nodes[node].Component] = node;
        }

        if (!graph.Substance.FromPressureTemperature(
                Quantity.FromSi(level, Dimension.Pressure),
                Quantity.FromSi(datum, Dimension.Temperature)).TryGetValue(out var state))
        {
            return pressures;
        }

        var incident = new Dictionary<object, List<int>>(ReferenceEqualityComparer.Instance);
        var carried = new Dictionary<object, double[]>(ReferenceEqualityComparer.Instance);

        foreach (var branch in graph.Branches)
        {
            Attach(incident, branch.From.Element, branch.Index);
            Attach(incident, branch.To.Element, branch.Index);

            // Into the element is positive, which is the convention a component's own residual reads its
            // flows by. A branch leaves its `From` end and arrives at its `To` end.
            Carried(carried, branch.From.Element)[branch.From.Port] -= values[layout.BranchFlow(branch.Index)];
            Carried(carried, branch.To.Element)[branch.To.Port] += values[layout.BranchFlow(branch.Index)];
        }

        var across = new Dictionary<object, double[]>(ReferenceEqualityComparer.Instance);
        var reference = new Dictionary<object, double>(ReferenceEqualityComparer.Instance);
        var walked = new bool[graph.Branches.Length];
        var anchored = new Dictionary<object, bool>(ReferenceEqualityComparer.Instance);

        // Whether anything the branch graph connects to an element states a pressure -- on a junction
        // element or on any element inside a branch's path. A node with two connections is inline (`D-114`)
        // and lives in a path, and a datum is usually written on exactly such a node; reading only the
        // endpoints missed it, started the walk at the pressure scale, and left a 150 kPa closure error
        // wherever the walk happened to meet the stated value (`S-63`: on the series header that was a
        // three-way valve's leg, whose √Δp law Newton could not step through).
        bool Anchored(IFlowComponent start)
        {
            if (anchored.TryGetValue(start, out var known))
            {
                return known;
            }

            var seen = new HashSet<IFlowComponent>();
            var pending = new Queue<IFlowComponent>();
            var found = false;

            pending.Enqueue(start);
            seen.Add(start);

            while (pending.Count > 0)
            {
                var element = pending.Dequeue();

                found |= HydraulicPartition.Stated(element, HydraulicPartition.Pressure) is not null;

                if (!incident.TryGetValue(element, out var edges))
                {
                    continue;
                }

                foreach (var edge in edges)
                {
                    var branch = graph.Branches[edge];

                    foreach (var part in branch.Path)
                    {
                        found |= HydraulicPartition.Stated(part, HydraulicPartition.Pressure) is not null;
                    }

                    foreach (var next in new[] { branch.From.Element, branch.To.Element })
                    {
                        if (seen.Add(next))
                        {
                            pending.Enqueue(next);
                        }
                    }
                }
            }

            foreach (var element in seen)
            {
                anchored[element] = found;
            }

            return found;
        }

        // What an element's own laws put between its ports, at the flows it is carrying. A node has one
        // pressure and comes back flat; a three-way valve comes back with its two legs where its Kv laws
        // put them, which is what stops its ports collapsing onto one value (`S-25`, `S-35`, `S-46`).
        double[] Ports(IFlowComponent element)
        {
            if (!across.TryGetValue(element, out var offsets))
            {
                offsets = carried.TryGetValue(element, out var flows)
                    ? BranchResistance.Across(
                        graph, state, element, flows, Parameters(graph, layout, values, element), Tolerances.SeedValveExcursion)
                    : new double[Math.Max(element.Ports.Length, 1)];

                across[element] = offsets;
            }

            return offsets;
        }

        // What the walk leaves at an element: whatever it states, else the pressure it arrived with. The
        // element is anchored by the port the walk reached, so its other ports follow from its own laws;
        // the value placed is returned so the walk continues from a datum rather than past it.
        double Place(IFlowComponent element, int port, double running)
        {
            var placed = HydraulicPartition.Stated(element, HydraulicPartition.Pressure) ?? running;

            reference[element] = placed - Ports(element)[port];

            if (index.TryGetValue(element, out var node))
            {
                pressures[node] = placed;
            }

            return placed;
        }

        foreach (var start in graph.Branches.SelectMany(static branch =>
            new[] { branch.From.Element, branch.To.Element }))
        {
            if (reference.ContainsKey(start))
            {
                continue;
            }

            // A part with no stated pressure anywhere is a closed circuit whose datum well-posedness picks,
            // and it starts where a lone closed circuit always has: at the pressure scale, not at the
            // average of pressures stated in some other circuit. The substation's secondary was seeded
            // at 475 kPa from its primary's 600/350, and Newton's step to the datum row 475 kPa away was
            // what its line search kept cutting (`P4.1`). Not at the datum's own 0 Pa, though since
            // `D-121` water would allow it: sliding the finished field so that the datum sits at 0 was
            // tried and withdrawn twice (`S-66`) -- the second time with the seed well conditioned --
            // because the walk then leaves nodes below zero absolute before Newton starts. The offset
            // is a datum-row residual Newton removes in one linear step.
            _ = Place(start, 0, Anchored(start) ? level : Tolerances.PressureScale);

            var queue = new Queue<IFlowComponent>();

            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var vertex = queue.Dequeue();

                // A branch carrying a promoted pump is walked last from every vertex, so that where the
                // walk's tree closes a loop through such a pump the closure error falls across the pump
                // and nowhere else (`D-122`). The seeded head is a nominal 2.2 m and the loop's actual
                // need is whatever it is -- 54 kPa on the parallel header -- so every loop with a free
                // pump carries that gap somewhere. Across the pump it is the promoted head's own column,
                // which Newton moves in one linear step. Across a three-way valve's leg it is a √Δp law
                // seeded the wrong way round: on the cooling loop the recirculating leg was seeded 41.6
                // kPa against its flow, and the first step shut the leg and parked the pump at zero.
                foreach (var edge in incident[vertex].OrderBy(edge => CarriesPromotedPump(graph, layout, edge)))
                {
                    if (walked[edge])
                    {
                        continue;
                    }

                    walked[edge] = true;

                    var branch = graph.Branches[edge];
                    var forward = ReferenceEquals(branch.From.Element, vertex);
                    var flow = values[layout.BranchFlow(edge)];
                    var leaving = forward ? branch.From : branch.To;

                    // The leg starts at the port it leaves by, not at the element as a whole.
                    var running = reference[vertex] + Ports(vertex)[leaving.Port];

                    IEnumerable<IFlowComponent> path = branch.Path;

                    if (!forward)
                    {
                        path = path.Reverse();
                    }

                    foreach (var part in path)
                    {
                        if (part is NodeComponent)
                        {
                            running = Place(part, 0, running);
                            continue;
                        }
                        var drop = part is PumpComponent { ShutOffHead: 0 } pump && PromotesHead(layout, pump)
                            ? -Hydrostatic.Pressure(state.Density.SiValue, NominalPumpHead)
                            : BranchResistance.Of(
                                graph, state, part, flow, Parameters(graph, layout, values, part), Tolerances.SeedValveExcursion);

                        running += forward ? -drop : drop;
                    }

                    var arriving = forward ? branch.To : branch.From;

                    if (!reference.ContainsKey(arriving.Element))
                    {
                        _ = Place(arriving.Element, arriving.Port, running);
                        queue.Enqueue(arriving.Element);
                    }
                }
            }
        }

        return pressures;
    }

    /// <summary>Whether a branch's path holds a pump whose head the solver is determining.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">Where the state vector keeps each unknown.</param>
    /// <param name="branch">The branch's index.</param>
    /// <returns><see langword="true"/> when it does, which the pressure walk uses to walk it last.</returns>
    private static bool CarriesPromotedPump(CircuitGraph graph, SystemLayout layout, int branch) =>
        graph.Branches[branch].Path.Any(part => part is PumpComponent pump && PromotesHead(layout, pump));

    /// <summary>The flow buffer an element accumulates its ports' flows into.</summary>
    /// <param name="carried">The map being built.</param>
    /// <param name="element">The element.</param>
    /// <returns>One entry per port, kg/s into the element, created zeroed on first use.</returns>
    private static double[] Carried(Dictionary<object, double[]> carried, IFlowComponent element)
    {
        if (!carried.TryGetValue(element, out var flows))
        {
            flows = new double[element.Ports.Length];
            carried[element] = flows;
        }

        return flows;
    }
}
