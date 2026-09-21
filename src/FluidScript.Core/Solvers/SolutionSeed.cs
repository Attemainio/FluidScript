using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Language;
using FluidScript.Core.Sizing;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;

namespace FluidScript.Core.Solvers;

/// <summary>The iterate a solve starts from: <c>31</c>'s <c>seedFromStatedDuties</c>.</summary>
/// <remarks>
/// <para>
/// <strong>A seed is not a convenience, and zero is not a neutral one.</strong> A pipe's momentum
/// relation is <c>Δp = R·ṁ|ṁ|</c> and a pump curve is <c>H₀ − kṁ²</c>; both have a derivative of
/// exactly zero at <c>ṁ = 0</c>, so a zero-flow start is a genuinely singular Jacobian rather than a
/// poor guess, and every solve from one reports <c>FS3002</c> however well-posed the circuit is
/// (<c>S-21</c>).
/// </para>
/// <para>
/// <strong>Non-zero is not enough either, and this is the half that is easy to miss.</strong> A
/// branch's orientation is the decomposition's choice, so seeding every flow to the same positive
/// number leaves some node with every port an inflow — and a node nothing leaves is a node whose own
/// enthalpy enters no equation, which is a zero column and a singular Jacobian again. The fix is not a
/// sign heuristic: it is that the seed <em>satisfies the mass balances</em>, at which point every node
/// with an inflow has an outflow by construction.
/// </para>
/// <para>
/// <strong>How the field is made mass-consistent.</strong> The branch graph is spanned by a forest.
/// Every non-tree branch — one per independent loop (<c>23</c>) — takes its own estimate outright, and
/// every boundary flux is chosen so the fluxes of a hydraulic component sum to zero. The tree branches
/// are then <em>solved</em>, leaves inward: at each vertex all but the parent branch is known, so the
/// parent's flow is whatever closes that vertex's balance. The last vertex closes identically because
/// its component's fluxes were made to sum to zero, which is the same statement one level up.
/// </para>
/// <para>
/// This is exactly the decomposition <c>23</c>'s cycle basis describes, used for its other purpose: a
/// divergence-free field on a graph is a particular solution plus the cycle space, so choosing the
/// chords and the boundary freely and solving for the tree reaches every such field and nothing else.
/// </para>
/// </remarks>
public static partial class SolutionSeed
{
    /// <summary>The temperature a node's state falls back to when the script fixes none.</summary>
    /// <value>K. 20 °C — room temperature, valid for every substance the catalogue carries.</value>
    public const double ReferenceTemperature = 293.15;

    /// <summary>The pressure step the seed puts between one node and the next along a branch.</summary>
    /// <value>
    /// Pa. 10 kPa — a tenth of <c>Tolerances.PressureScale</c>, so a circuit of a dozen nodes stays
    /// inside a plausible range while no two adjacent nodes agree. The magnitude is not a claim about
    /// any circuit; being non-zero is the whole of it.
    /// </value>
    public const double NominalDrop = 1e4;
    /// <summary>Metres of head used only to keep a promoted bare pump inside a driven seed.</summary>
    private const double NominalPumpHead = 2.2;

    /// <summary>The temperature step the seed puts between one node and the next along a branch.</summary>
    /// <value>
    /// K. Two degrees — small enough that <see cref="Band"/> steps stay well inside any fluid's
    /// validated range, large enough that an enthalpy difference is far from the noise floor of a
    /// finite-difference derivative.
    /// </value>
    /// <remarks>
    /// <strong>Two degrees is load-bearing at both ends, and reducing it was tried and reverted.</strong>
    /// The spread is chosen in kelvin and paid for in watts: an interior node's energy balance reads
    /// <c>m*(h_out - h_in)</c>, so a step of <c>dT</c> costs <c>m*cp*dT</c> of residual, and the header's
    /// 1.65 kg/s primary turns the eight-degree wrap into 55 kW against a plant whose whole duty is 54 kW.
    /// That looked like the reason its first Newton step leaves the property domain, and it is not.
    /// Measured at 0.02 K: the scaled residual stayed at 1.96 — unchanged by a hundredfold reduction, so
    /// the energy rows never dominated it — the header variant that had converged at 2.85e-10 turned
    /// <c>NonFinite</c>, and the shipped header's measured rank fell from 44 to 43. The last of those is
    /// the point: a smaller spread brings back the very column degeneracy the spread exists to prevent
    /// (<c>S-21</c>). The noise-floor bound is on the <em>derivative</em>; what actually binds is that the
    /// enthalpy differences keep the flow columns distinguishable, and that needs far more than
    /// resolvability. See <c>S-44</c>.
    /// </remarks>
    public const double NominalRise = 2;

    /// <summary>How many steps the seed takes before wrapping back to the start of its band.</summary>
    /// <value>
    /// Five. A cumulative walk down a long branch leaves the fluid's validated range; wrapping bounds
    /// the excursion at four steps while still giving every pair of adjacent nodes different values,
    /// which is the only property the seed needs from this.
    /// </value>
    public const int Band = 5;

    /// <summary>Builds the starting iterate for a graph.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">The state vector's layout, which fixes where each value goes.</param>
    /// <returns>One value per unknown, in SI, in the layout's order.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static StateVector Build(CircuitGraph graph, SystemLayout layout)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(layout);

        var values = new double[layout.Count];
        var field = new Field(graph);

        field.Solve(BranchFlows.Estimate(graph));

        for (var branch = 0; branch < graph.Branches.Length; branch++)
        {
            values[layout.BranchFlow(branch)] = field.Flows[branch];
        }

        for (var index = 0; index < layout.FluxNodes.Length; index++)
        {
            values[layout.ExternalFluxOffset + index] = field.Injection(layout.FluxNodes[index].Component);
        }

        Thermal(graph, layout, values);

        return new StateVector([.. values]);
    }

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

    /// <summary>A component's resolvable parameters as the seed holds them, promoted values included.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">Where the state vector keeps each promoted parameter.</param>
    /// <param name="values">The seed so far; <see cref="Promoted"/> must have run.</param>
    /// <param name="element">The component.</param>
    /// <returns>
    /// One value per <c>IFlowComponent.Resolvable</c> entry, the promoted ones read from the seed, or
    /// <see langword="null"/> when nothing of this component's is promoted and its own values serve.
    /// </returns>
    private static double[]? Parameters(CircuitGraph graph, SystemLayout layout, double[] values, IFlowComponent element)
    {
        double[]? parameters = null;

        foreach (var (index, _, parameter) in PromotedColumns(layout, element.Name))
        {
            // Kv and head only. A promoted position is seeded at mid-travel (`S-50`), and letting the
            // walk lay the legs' drops at 0.5 instead of the valve's own 1.0 was measured (2026-09-20):
            // it cost one to two first-pass iterations on every three-way-valve circuit in the corpus
            // (cooling loop 5 to 7, header 5 to 6, s55 9 to 11) and bought nothing the solve needed.
            // The Kv and head columns are the ones that were nearly singular; the position column was not.
            if (string.Equals(parameter, "position", StringComparison.Ordinal))
            {
                continue;
            }

            for (var slot = 0; slot < element.Resolvable.Length; slot++)
            {
                if (string.Equals(element.Resolvable[slot].Name, parameter, StringComparison.Ordinal))
                {
                    parameters ??= [.. element.Resolvable.Select(static resolvable => resolvable.Value)];
                    parameters[slot] = values[index];
                }
            }
        }

        return parameters;
    }

    /// <summary>The promoted columns of the layout: each one's index, its owner and the parameter it holds.</summary>
    /// <param name="layout">The unknown layout.</param>
    /// <param name="owner">An owner to keep to, or <see langword="null"/> for every column.</param>
    /// <returns>In column order.</returns>
    /// <remarks>
    /// A promoted column's name is the promotion's label, <c>3WV.position</c>, so the parameter is whatever
    /// follows the owner's name and the dot. Three walks read it that way before <c>70</c>'s R4; this is the one.
    /// </remarks>
    private static IEnumerable<(int Index, string Owner, string Parameter)> PromotedColumns(SystemLayout layout, string? owner = null)
    {
        for (var index = layout.PromotionOffset; index < layout.Count; index++)
        {
            var declaration = layout.Unknowns[index];

            if (owner is not null && !string.Equals(declaration.OwnerComponentId, owner, StringComparison.Ordinal))
            {
                continue;
            }

            yield return (index, declaration.OwnerComponentId, declaration.Name[(declaration.OwnerComponentId.Length + 1)..]);
        }
    }

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
                    ? BranchResistance.Across(graph, state, element, flows, Parameters(graph, layout, values, element))
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
                        if (part is CircuitNode)
                        {
                            running = Place(part, 0, running);
                            continue;
                        }
                        var drop = part is Pump { ShutOffHead: 0 } pump && PromotesHead(layout, pump)
                            ? -Hydrostatic.Pressure(state.Density.SiValue, NominalPumpHead)
                            : BranchResistance.Of(graph, state, part, flow, Parameters(graph, layout, values, part));

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
        graph.Branches[branch].Path.Any(part => part is Pump pump && PromotesHead(layout, pump));

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

    /// <summary>Seeds every promoted parameter from the value its own component holds.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">Where the state vector keeps each unknown.</param>
    /// <param name="values">The seed being filled.</param>
    /// <remarks>
    /// <para>
    /// <strong>Zero is not a neutral starting point for a promoted parameter; for two of the three
    /// promotable kinds it is a <em>bound</em>.</strong> <see cref="Components.ValveLaw.Opening"/>
    /// clamps position into <c>[0, 1]</c>, so a column seeded at 0 has a one-sided derivative at best
    /// and a dead one at worst -- and <c>m2-distribution-header</c> came out <c>Singular</c> at
    /// <em>iteration zero</em> with both its promoted positions sitting exactly there. A pump seeded at
    /// zero head is a loop with no driver, which is the same problem one variable over (<c>S-26</c>).
    /// </para>
    /// <para>
    /// <strong>The component is the only thing that knows.</strong> <c>Resolvable</c> exists to say what
    /// a component uses when nothing supplies one, in SI, and <see cref="SystemLayout"/> already reads
    /// the unit off it for the same reason (<c>D-30</c>). Reading the value here needs no knowledge of
    /// the kind, which is what keeps this seed free of a table of defaults that would drift.
    /// </para>
    /// <para>
    /// The old behaviour was a placeholder and said so: "a promoted parameter carries no unit yet and
    /// falls to zero, which is honest and is the thing the outer loop replaces when promotion becomes
    /// live". Promotion is live.
    /// </para>
    /// <para>
    /// <strong>One is a bound as surely as zero is, and a valve's default position sits on it</strong>
    /// (<c>S-50</c>). <c>ThreeWayValve.Position</c> defaults to 1 -- correct for a valve nobody is
    /// controlling, and the worst place to start one the solver has to move. On the equal-percentage
    /// characteristic every promotable position uses, phi(1) = 1 with slope 3.91 while the complementary
    /// leg sits at phi(0) = 0.02 with slope 0.078: **fifty times flatter**, so the column is dominated by
    /// the control leg and carries almost no signal about the bypass. Mid-travel is symmetric --- both
    /// legs at 0.141, both slopes 0.553 --- and it is where a valve sized for authority 0.5 is meant to
    /// sit anyway. So a promoted parameter bounded on both sides whose component value lies *on* a bound
    /// is seeded at the middle of its range instead.
    /// </para>
    /// <para>
    /// It applies to nothing else in the corpus: <c>kv</c> is bounded below only and <c>head</c> not at
    /// all, so both keep the component's own value, which is what the paragraph above is about.
    /// </para>
    /// <para>
    /// <strong>A promoted <c>kv</c> is the exception, and it is seeded from the Kv law</strong>
    /// (<see cref="PromotedKv"/>). Its own value is the bootstrap's provisional -- the catalogue's largest
    /// row, 630, chosen to disturb the bootstrap least (<c>D-96</c>) -- which puts a few pascals across a
    /// valve the solve has to close to a hundred kilopascals. The law's slope in <c>√Δp</c> is steepest
    /// exactly there, and on the substation Newton never recovered from it (<c>P4.1</c>); on the
    /// <c>head=15</c> loop it cost six iterations where two suffice (<c>C-75</c>).
    /// </para>
    /// </remarks>
    private static void Promoted(CircuitGraph graph, SystemLayout layout, double[] values)
    {
        foreach (var (index, name, parameter) in PromotedColumns(layout))
        {
            var owner = graph.Components.FirstOrDefault(element => string.Equals(element.Name, name, StringComparison.Ordinal));

            if (owner is null)
            {
                continue;
            }

            foreach (var resolvable in owner.Resolvable)
            {
                if (!string.Equals(resolvable.Name, parameter, StringComparison.Ordinal)
                    || !double.IsFinite(resolvable.Value))
                {
                    continue;
                }

                values[index] = owner is Pump && resolvable.Name is "head" && resolvable.Value == 0
                            ? NominalPumpHead
                            : owner is Valve && resolvable.Name is "kv" && PromotedKv(graph, layout, values, owner) is { } kv
                            ? kv
                            : Interior(resolvable);
            }
        }
    }
    /// <summary>Whether a bare pump's head is an unknown this solve is expected to choose.</summary>
    private static bool PromotesHead(SystemLayout layout, Pump pump) =>
        PromotedColumns(layout, pump.Name).Any(static column => column.Parameter is "head");

    /// <summary>The Kv a promoted valve is seeded at: the Kv law at the seeded flow, taking half of what the circuit offers.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">Where branch flows are stored.</param>
    /// <param name="values">The seed so far, with its flows and pressures laid.</param>
    /// <param name="valve">The valve whose <c>kv</c> is promoted.</param>
    /// <returns>Kv in m³/h, or <see langword="null"/> when nothing says what the circuit offers.</returns>
    /// <remarks>
    /// What the circuit offers is the difference between the stated pressures at the branch's two ends,
    /// else the stated head of a pump on the valve's branch; the valve is seeded to take half
    /// of it, which is the authority every sizing rule aims at (<c>24</c>). A seed, not a claim: the solve
    /// moves it wherever the promotion's constraint needs, and this only starts it on the right side of
    /// the square root.
    /// </remarks>
    private static double? PromotedKv(CircuitGraph graph, SystemLayout layout, double[] values, IFlowComponent valve)
    {
        var branch = graph.Branches.FirstOrDefault(candidate => candidate.Path.Contains(valve));

        if (branch is null)
        {
            return null;
        }

        var flow = Math.Abs(values[layout.BranchFlow(branch.Index)]);

        // The branch's own ends first -- an open circuit's supply and return, which is what the
        // substation's primary offers its valve -- then a stated head on the branch.
        double? offered =
            HydraulicPartition.Stated(branch.From.Element, HydraulicPartition.Pressure) is { } from
            && HydraulicPartition.Stated(branch.To.Element, HydraulicPartition.Pressure) is { } to
                ? Math.Abs(from - to)
                : null;

        if (offered is null)
        {
            foreach (var element in branch.Path)
            {
                // A stated rise is the drop the loop has to spend; a stated head is that rise at the
                // reference density (C-109).
                if (element is Pump { StatedRise: { } rise })
                {
                    offered = rise;
                    break;
                }

                if (element is Pump && HydraulicPartition.Stated(element, "head") is { } head)
                {
                    offered = Hydrostatic.Pressure(ReferenceDensity, head);
                    break;
                }
            }
        }

        if (offered is not { } drop || !(flow > 0))
        {
            return null;
        }

        var kv = ValveLaw.RequiredKv(flow, 0.5 * drop, ReferenceDensity);

        return double.IsFinite(kv) && kv > 0 ? kv : null;
    }

    /// <summary>Water's density at the reference state, kg/m³, for a seed that needs one before any state is fixed.</summary>
    private const double ReferenceDensity = 1000;

    /// <summary>Where a promoted parameter starts: its own value, unless that value is a bound.</summary>
    /// <param name="resolvable">The component's declaration of the parameter.</param>
    /// <returns>
    /// The middle of the range for a two-sided parameter sitting on either bound, and the component's own
    /// value otherwise. Dimensionless or SI, whichever the parameter is.
    /// </returns>
    /// <remarks>
    /// <strong>A bound is not somewhere the iterate may not go; it is somewhere the derivative stops
    /// existing</strong> --- <see cref="Components.ValveLaw.Opening"/> makes that argument for the clamp
    /// it had to remove, and starting on one is the same mistake made a step earlier. Only a parameter
    /// bounded on <em>both</em> sides has a middle to fall back to; one bounded on one side has no
    /// non-arbitrary interior point, so its own value stands.
    /// </remarks>
    private static double Interior(ResolvedParameter resolvable)
    {
        if (resolvable.Minimum is not { } minimum || resolvable.Maximum is not { } maximum
            || !double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum)
        {
            return resolvable.Value;
        }

        return resolvable.Value <= minimum || resolvable.Value >= maximum
            ? (minimum + maximum) / 2
            : resolvable.Value;
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
                    && (exchanger.StatedParameters.ContainsKey("power") || exchanger.SizedParameters.ContainsKey("power")))
                {
                    var sign = Sizing.BranchFlows.Side(graph, branch, exchanger) == 2 ? -1 : 1;

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

    /// <summary>The fluid's specific heat at the seed's level and datum, or water's when it cannot be fixed there.</summary>
    private static double SpecificHeat(CircuitGraph graph, double level, double datum) =>
        graph.Substance.FromPressureTemperature(
            Quantity.FromSi(level, Dimension.Pressure), Quantity.FromSi(datum, Dimension.Temperature))
            .TryGetValue(out var state)
            ? state.SpecificHeat.SiValue
            : 4180;

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

    /// <summary>The temperature every unstated node is seeded at.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <returns>K.</returns>
    /// <remarks>
    /// A stated boundary temperature first, then an exchanger's stated inlet — which is the enthalpy
    /// datum of a closed circuit (<c>D-65</c>), so it is the one absolute temperature such a circuit
    /// has — and <see cref="ReferenceTemperature"/> only when the script names neither.
    /// </remarks>
    private static double Datum(CircuitGraph graph) =>
        graph.Nodes
            .Select(static node => HydraulicPartition.Stated(node.Component, HydraulicPartition.Temperature))
            .FirstOrDefault(static stated => stated is not null)
        ?? graph.Components
            .Select(static component =>
                component.StatedParameters.TryGetValue("in", out var inlet) ? inlet.SiValue : (double?)null)
            .FirstOrDefault(static stated => stated is not null)
        ?? ReferenceTemperature;

    /// <summary>The specific enthalpy of a state, or zero when the substance cannot evaluate it.</summary>
    /// <param name="substance">The circuit's fluid.</param>
    /// <param name="pressure">Gauge pressure, Pa.</param>
    /// <param name="temperature">K.</param>
    /// <returns>J/kg.</returns>
    /// <remarks>
    /// Zero rather than a throw or a diagnostic: a state the substance refuses has already been
    /// reported by well-posedness (<c>FS2205</c>), and a seed is allowed to be wrong. Nothing here is
    /// the right place to tell the user about it a second time.
    /// </remarks>
    private static double Enthalpy(ISubstance substance, double pressure, double temperature) =>
        substance.FromPressureTemperature(
            Quantity.FromSi(pressure, Dimension.Pressure),
            Quantity.FromSi(temperature, Dimension.Temperature)).TryGetValue(out var state)
            ? state.Enthalpy.SiValue
            : 0;
}
