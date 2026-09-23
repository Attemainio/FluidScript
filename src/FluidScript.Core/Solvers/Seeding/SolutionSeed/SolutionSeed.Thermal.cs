using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Solvers.Seeding;

public static partial class SolutionSeed
{
    /// <summary>Fills every pressure, enthalpy and component-owned state.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">The state vector's layout.</param>
    /// <param name="values">The iterate being built, written in place.</param>
    /// <remarks>
    /// <para>
    /// <strong>One temperature level for the whole graph, and a stepped pressure field rather than a
    /// level.</strong> The temperature needs no refinement: a stated value is used where the script
    /// gives one, and nothing else in the seed reads a temperature difference.
    /// </para>
    /// <para>
    /// <strong>A uniform field is a singular Jacobian, in pressure and in temperature alike</strong>
    /// (<c>S-25</c>). A valve's law is <c>ṁ = Kv·f(x)·√(Δp·ρ)</c>, so at <c>Δp = 0</c> its derivative
    /// with respect to <c>Kv</c> and to <c>position</c> is zero — the cooling loop's promoted
    /// <c>3WV.position</c> was measured as a column of zeros. A node's energy balance carries
    /// <c>ṁ(h_arriving − h_own)</c>, so at a uniform enthalpy its derivative with respect to <em>flow</em>
    /// is zero, and the simple loop's promoted <c>PU1.head</c> came out of a rank-11 12×12 system for
    /// that reason. Both are the same lesson as <c>S-21</c> one variable over: the seed must make every
    /// difference a residual reads non-zero, not merely every value.
    /// </para>
    /// <para>
    /// A component's own state is seeded from its declared SI unit rather than from its kind
    /// (<c>D-74</c>): the layout knows the unit and nothing here should know what a tank is. A promoted
    /// parameter carries no unit yet and falls to zero, which is honest and is the thing the outer loop
    /// replaces when promotion becomes live.
    /// </para>
    /// <para>
    /// <strong>A part with no stated pressure is not seeded with its datum at 0</strong>, although
    /// since <c>D-121</c> the substance would allow it. <see cref="Integrate"/> starts its walk at the
    /// pressure scale and the datum row pins the picked node to 0, so every closed circuit begins
    /// 100 kPa above where its own datum row says it must end and Newton's first step slides the whole
    /// field down by that. Sliding the seed there instead was measured twice (<c>S-66</c>): first with
    /// the seed nearly singular in its promoted Kv, then again on 2026-09-20 with that fixed (the walk
    /// now reads promoted values, pivot ratio 3e-6 to 1e-2), and the substation still went
    /// <c>NonFinite</c> at zero steps -- the walk from a datum at 0 puts <c>NSUP</c> at −70 kPa
    /// absolute before Newton starts, because a seeded 2.2 m head cannot lift what the ring's drops
    /// take at the seeded flow. The offset is a linear residual Newton removes exactly, and the seed
    /// keeps it.
    /// </para>
    /// </remarks>
    private static void Thermal(CircuitGraph graph, SystemLayout layout, double[] values)
    {
        var pressures = graph.Nodes
            .Select(static node => HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure))
            .Where(static stated => stated is not null)
            .Select(static stated => stated!.Value)
            .ToArray();

        var level = pressures.Length > 0 ? pressures.Average() : Tolerances.PressureScale;
        var datum = Datum(graph);
        var steps = Steps(graph);
        var levels = Levels(graph, layout, values, datum, SpecificHeat(graph, level, datum));

        // Promotions first, so the pressure walk reads the Kv and head the solve will start from
        // rather than the bootstrap's provisional 630 (`S-66`): walked with the provisional, the
        // substation's promoted valve was seeded with 11 Pa across it and the Kv column's pivot was
        // 1.2e-4 at the seed, which made the first Newton step enormous in every direction.
        Promoted(graph, layout, values);

        var integrated = Integrate(graph, layout, values, level, datum);

        for (var index = 0; index < graph.Nodes.Length; index++)
        {
            var node = graph.Nodes[index];

            var pressure = integrated[index];

            var temperature = HydraulicPartition.Stated(node.Component, HydraulicPartition.Temperature)
                    // Centred on the level rather than walking down from it: a one-sided excursion of
                    // `Band` steps is 8 K, which puts a 6 C chilled-water node below freezing and ends
                    // the seed in a failed property call rather than a bad guess (`S-35`).
                    ?? levels[index] + (NominalRise * (steps[index] - (Band / 2)));

            values[layout.NodePressure(index)] = pressure;
            values[layout.NodeEnthalpy(index)] = Enthalpy(graph.Substance, pressure, temperature);
        }

        for (var index = layout.ComponentUnknownOffset; index < layout.ExternalFluxOffset; index++)
        {
            values[index] = layout.Unknowns[index].SiUnit switch
            {
                "J/kg" => Enthalpy(graph.Substance, level, datum),
                "Pa" => level,
                "K" => datum,
                _ => 0,
            };
        }
    }

    /// <summary>The temperature level each node sits near, propagated downstream from what is known.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">Where the already-seeded branch flows are stored.</param>
    /// <param name="values">The iterate whose flow signs establish upstream and downstream.</param>
    /// <param name="fallback">The level for a node no anchor reaches.</param>
    /// <param name="specificHeat">J/(kg·K), for what a duty does to the temperature of the seeded flow.</param>
    /// <returns>One level per node, in K, indexed as <c>graph.Nodes</c> is.</returns>
    /// <remarks>
    /// <para>
    /// <strong>One global level cannot serve a circuit whose nodes sit at several</strong> (<c>S-30</c>).
    /// <c>m2-cooling-loop</c> runs a 6 &#176;C primary into a secondary loop <c>HE1</c> rates at 20/50, and
    /// seeding every unstated node by walking <em>down</em> from the first stated one put the whole
    /// secondary at 2 to 6 &#176;C -- on the wrong side of the mixing split, with the node after the pump
    /// colder than the node before it. The solve reached <c>Singular</c> at eleven iterations; substituting
    /// the loop's own temperatures by hand reached <c>01</c>'s figures instead.
    /// </para>
    /// <para>
    /// <strong>A rated <c>in</c>/<c>out</c> is a design condition, not a boundary, and it is used here
    /// only because a seed is not a claim.</strong> The script saying an exchanger is rated 20/50 does not
    /// assert that the nodes at its ports are at 20 and 50 -- in this circuit those are produced by the
    /// mixing valve recirculating hot return water, and how far the fluid actually rises depends on the
    /// flow the pump delivers. Newton moves off a seed freely, so a design point cannot make an
    /// unreachable design look reachable; it is the designer's own statement of where the circuit is meant
    /// to sit, which is the best prior available before anything is solved. The line that must hold is
    /// that this informs the <em>seed</em> and never a constraint row, which the counting pass owns.
    /// </para>
    /// <para>
    /// <strong>Placing those ports alone is worse than not placing them.</strong> Measured: the unplaced
    /// neighbours stay at the old level, the seed then carries a 46 K jump across one component, and the
    /// first step leaves the fluid's range. Propagation is what makes the placement usable -- a node with
    /// no anchor of its own takes the mean of the anchors reaching it from upstream, which at a mixing
    /// node is the two streams it mixes and everywhere else is the one thing feeding it.
    /// </para>
    /// <para>
    /// Direction comes from the mass-consistent branch-flow seed already built before this pass. A
    /// negative flow reverses the stored branch orientation; ignoring that sign strands a return-temperature
    /// anchor on the consumer side and can seed the source inlet tens of kelvins away from it.
    /// </para>
    /// </remarks>
    private static double[] Levels(
        CircuitGraph graph, SystemLayout layout, double[] values, double fallback, double specificHeat)
    {
        var count = graph.Nodes.Length;
        var levels = new double[count];
        var known = new bool[count];
        var index = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

        for (var node = 0; node < count; node++)
        {
            index[graph.Nodes[node].Component] = node;
        }

        var ported = Ported(graph, index);

        for (var node = 0; node < count; node++)
        {
            var anchor =
                HydraulicPartition.Stated(graph.Nodes[node].Component, HydraulicPartition.Temperature)
                ?? ported[node];

            if (anchor is not null)
            {
                levels[node] = anchor.Value;
                known[node] = true;
            }
        }

        var flows = Downstream(graph, index, layout, values, specificHeat);

        // Bounded by the node count: each pass fixes at least one node or stops, so a circuit whose
        // anchors reach everything settles well inside it and one whose anchors reach nothing exits at once.
        for (var pass = 0; pass < count; pass++)
        {
            var moved = false;

            for (var node = 0; node < count; node++)
            {
                if (known[node])
                {
                    continue;
                }

                var sum = 0.0;
                var arriving = 0;

                // What arrives is the upstream level plus what the segment's duties do to it: a load between
                // two nodes drops the fluid by Q/(m·cp), and a level laid across it without that drop is a
                // seed 20 K wrong at every node past it (`P4.1`).
                foreach (var (from, to, shift) in flows)
                {
                    if (to == node && known[from])
                    {
                        sum += levels[from] + shift;
                        arriving++;
                    }
                }

                if (arriving == 0)
                {
                    continue;
                }

                levels[node] = sum / arriving;
                known[node] = true;
                moved = true;
            }

            if (!moved)
            {
                break;
            }
        }

        for (var node = 0; node < count; node++)
        {
            if (!known[node])
            {
                levels[node] = fallback;
            }
        }

        return levels;
    }

    /// <summary>The temperature a component states at the port facing each node.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="index">Node component to its position in <c>graph.Nodes</c>.</param>
    /// <returns>One entry per node: K where a neighbour states it, <see langword="null"/> otherwise.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The port map decides which node an <c>in</c> belongs to, and walking the branch does
    /// not.</strong> A parameter is named after the port it describes, so <c>in</c> is the temperature at
    /// the port called <c>in</c> whichever way round the graph was walked. Reading the neighbours out of
    /// <c>branch.Path</c> instead looks equivalent and is not: path order is the order the walk crossed
    /// the branch, which need not match the component's own orientation. Measured on
    /// <c>m2-cooling-loop</c>, that put 46 &#176;C on the node before <c>HE1</c> and 18 &#176;C on the node
    /// after it -- the rated 20/50 laid on backwards, so the seed said the exchanger cooled.
    /// </para>
    /// <para>
    /// The dimension is checked rather than assumed. <c>in</c> and <c>out</c> are temperatures on every
    /// kind carrying them today, and seeding an enthalpy from something that turned out to be a flow would
    /// be a property call far outside the fluid's range rather than a slightly wrong guess.
    /// </para>
    /// </remarks>
    private static double?[] Ported(CircuitGraph graph, Dictionary<object, int> index)
    {
        var ported = new double?[graph.Nodes.Length];

        for (var element = 0; element < graph.Components.Length; element++)
        {
            var component = graph.Components[element];

            if (element >= graph.Adjacency.ComponentCount)
            {
                continue;
            }

            for (var port = 0; port < component.Ports.Length; port++)
            {
                if (!component.StatedParameters.TryGetValue(component.Ports[port].Name, out var stated)
                    || stated.Dimension != Dimension.Temperature)
                {
                    continue;
                }

                var peer = graph.Adjacency.Peer(element, port);

                if (peer.Exists && index.TryGetValue(graph.Components[peer.Component], out var node))
                {
                    ported[node] ??= stated.SiValue;
                }
            }
        }

        return ported;
    }

    /// <summary>Which node feeds which, in the direction of the seeded branch flow, and by how much the fluid's temperature changes on the way.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="index">Node component to its position in <c>graph.Nodes</c>.</param>
    /// <param name="layout">Where branch flows are stored.</param>
    /// <param name="values">The iterate containing the mass-consistent flow seed.</param>
    /// <param name="specificHeat">J/(kg·K), the fluid's, for turning a duty into a temperature change.</param>
    /// <returns>Directed triples, upstream first, with the shift in K the segment's duties make.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Branches meet at junction <em>elements</em>, not only at nodes, so consecutive nodes within
    /// a path are not the whole adjacency.</strong> A three-way valve is a junction: the node before it on
    /// one branch feeds the nodes after it on the others, and without that link the level anchored on an
    /// exchanger's outlet never reaches the pipe on the far side of the valve -- which is the 46 K jump
    /// that made placement alone worse than nothing.
    /// </para>
    /// <para>
    /// The shift is every exchanger's stated duty between the two nodes, divided by the seeded flow's
    /// capacity rate: a 150 kW load on 1.79 kg/s of water is −20 K, whichever way the branch was walked,
    /// since a duty heats or cools the fluid however it runs. A coupled exchanger contributes its duty to
    /// side 1 and the negative of it to side 2. A seed is not a claim, and a duty from a stated
    /// <c>power</c> is the designer's own statement of what the load does -- the same standing the port
    /// temperatures have (<c>S-30</c>).
    /// </para>
    /// </remarks>
    private static List<(int From, int To, double Shift)> Downstream(
        CircuitGraph graph,
        Dictionary<object, int> index,
        SystemLayout layout,
        double[] values,
        double specificHeat)
    {
        var flows = new List<(int From, int To, double Shift)>();
        var into = new Dictionary<object, List<int>>(ReferenceEqualityComparer.Instance);
        var outOf = new Dictionary<object, List<int>>(ReferenceEqualityComparer.Instance);

        foreach (var branch in graph.Branches)
        {
            var nodes = new List<int>();
            var shifts = new List<double>();
            var flow = values[layout.BranchFlow(branch.Index)];
            var capacity = Math.Abs(flow) * specificHeat;
            var pending = 0.0;

            foreach (var part in new[] { branch.From.Element }
                .Concat(branch.Path)
                .Append(branch.To.Element))
            {
                if (part is CircuitNode && index.TryGetValue(part, out var node))
                {
                    if (nodes.Count > 0)
                    {
                        shifts.Add(pending);
                    }

                    nodes.Add(node);
                    pending = 0;
                    continue;
                }

                if (part is HeatExchanger exchanger && capacity > 0
                    && Ownership.Of(exchanger, "power") is ParameterState.Stated or ParameterState.SizedFinal)
                {
                    var sign = FluidScript.Core.Sizing.Flows.BranchFlows.Side(graph, branch, exchanger) == 2 ? -1 : 1;

                    pending += sign * exchanger.Power / capacity;
                }
            }

            var forward = flow >= 0;

            for (var step = 1; step < nodes.Count; step++)
            {
                flows.Add(forward
                    ? (nodes[step - 1], nodes[step], shifts[step - 1])
                    : (nodes[step], nodes[step - 1], shifts[step - 1]));
            }

            // A bare node-to-node connection is an ideal link, so its two temperatures are equal
            // independently of the arbitrary branch orientation lowering chose. Let an anchor cross it
            // in either direction; ordinary branches remain directed by their declared path.
            if (branch.Path.IsEmpty && nodes.Count == 2)
            {
                flows.Add(forward ? (nodes[1], nodes[0], 0) : (nodes[0], nodes[1], 0));
            }

            if (nodes.Count == 0)
            {
                continue;
            }

            var upstream = forward ? branch.From.Element : branch.To.Element;
            var downstream = forward ? branch.To.Element : branch.From.Element;

            if (upstream is not CircuitNode)
            {
                Attach(outOf, upstream, forward ? nodes[0] : nodes[^1]);
            }

            if (downstream is not CircuitNode)
            {
                Attach(into, downstream, forward ? nodes[^1] : nodes[0]);
            }
        }

        foreach (var (junction, arriving) in into)
        {
            if (!outOf.TryGetValue(junction, out var leaving))
            {
                continue;
            }

            foreach (var upstream in arriving)
            {
                foreach (var downstream in leaving)
                {
                    flows.Add((upstream, downstream, 0));
                }
            }
        }

        return flows;
    }

    /// <summary>Records one node against the junction element it meets.</summary>
    /// <param name="sides">The map being built.</param>
    /// <param name="junction">The junction element.</param>
    /// <param name="node">The node's position in <c>graph.Nodes</c>.</param>
    private static void Attach(Dictionary<object, List<int>> sides, object junction, int node)
    {
        if (!sides.TryGetValue(junction, out var nodes))
        {
            nodes = [];
            sides[junction] = nodes;
        }

        nodes.Add(node);
    }

    /// <summary>How many steps from its branch's start each node is, wrapped into a narrow band.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <returns>One step index per node, indexed as <c>graph.Nodes</c> is; zero at every branch end.</returns>
    /// <remarks>
    /// <para>
    /// <strong>What this exists to guarantee is that adjacent nodes differ</strong>, in pressure and in
    /// temperature alike. Every residual the solver differentiates reads a <em>difference</em> across a
    /// component — <c>√Δp</c> in a valve law, <c>ṁ(hᵢₙ − hₒᵤₜ)</c> in an energy balance — and a uniform
    /// field makes the derivative with respect to the <em>other</em> variable vanish. That is how a
    /// promoted <c>position</c> and a promoted <c>head</c> both came out singular on well-posed circuits
    /// (<c>S-25</c>), one through pressure and one through enthalpy.
    /// </para>
    /// <para>
    /// <strong>Adjacency is not only along a branch, and a per-branch walk misses the other kind
    /// (<c>S-35</c>).</strong> Two branches leaving one junction element are adjacent <em>through</em> it,
    /// so a counter that restarts at each branch gives both their first interior nodes the same step and
    /// the junction sees no difference across itself at all. A three-way valve is exactly that shape:
    /// with a component in each leg its three ports seeded to one pressure, <c>√Δp</c> went to zero, and
    /// <c>position</c> was again a column of zeros — the very failure the paragraph above records as
    /// fixed. The cooling loop escaped it only because its bypass leg was empty, which put that side on a
    /// branch <em>end</em> instead of an interior node.
    /// </para>
    /// <para>
    /// So each branch starts where its own leg of the junction says, not at zero: the <em>n</em>th branch
    /// leaving an element begins at step <em>n</em>, and the walk down the branch proceeds from there.
    /// Within a branch nothing changes — consecutive nodes still differ by one step — and across a
    /// junction of fewer than <see cref="Band"/> legs the first nodes of any two legs now differ by
    /// construction rather than by luck.
    /// </para>
    /// <para>
    /// <strong>Wrapped rather than cumulative, and the wrap is what keeps it safe.</strong> A long
    /// branch stepped monotonically would walk a seed out of the fluid's validated range — twenty nodes
    /// at 10 kPa and 2 K a step is 200 kPa and 40 K from where it started, and either end of that is a
    /// property call that fails. Consecutive indices still differ, which is the whole requirement, and
    /// the excursion is bounded by <see cref="Band"/> steps whatever the circuit.
    /// </para>
    /// <para>
    /// A branch end keeps step zero. It belongs to more than one branch and there is no walk position it
    /// could take that both would agree on; a stated boundary is usually there anyway.
    /// </para>
    /// </remarks>
    private static int[] Steps(CircuitGraph graph)
    {
        var steps = new int[graph.Nodes.Length];
        var index = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var legs = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

        for (var node = 0; node < graph.Nodes.Length; node++)
        {
            index[graph.Nodes[node].Component] = node;
        }

        foreach (var branch in graph.Branches)
        {
            // The leg this branch is, counted at the element it leaves. Legs of the same junction start
            // one step apart, so their first interior nodes cannot seed to the same value.
            var leg = legs.GetValueOrDefault(branch.From.Element);
            var step = leg;

            legs[branch.From.Element] = leg + 1;

            foreach (var part in branch.Path)
            {
                if (part is not CircuitNode || !index.TryGetValue(part, out var node))
                {
                    continue;
                }

                steps[node] = ++step % Band;
            }
        }

        return steps;
    }
}
