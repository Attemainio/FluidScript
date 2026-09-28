using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Sizing;
using FluidScript.Core.Sizing.Flows;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Solvers.Passes;

public sealed partial class OuterLoop
{
    /// <summary>The driving pressure a circuit with no free pump offers a valve.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <returns>Pa, positive, or <see langword="null"/> when the graph does not settle it.</returns>
    /// <remarks>
    /// The span between the two stated boundary pressures. With any other count the pair the variable
    /// flow runs between is a path question rather than a set one, and answering it by taking the
    /// extremes would quietly size against a differential no fluid crosses — so the rule declines and
    /// says so rather than guessing.
    /// </remarks>
    private static double? Offered(CircuitGraph graph)
    {
        var stated = graph.Nodes
            .Select(node => HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure))
            .Where(static pressure => pressure is not null)
            .Select(static pressure => pressure!.Value)
            .ToArray();

        return stated.Length == 2 ? Math.Abs(stated[0] - stated[1]) : null;
    }

    /// <summary>Whether a free pump on a circuit through a component absorbs whatever it drops.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="component">The component being sized.</param>
    /// <returns>
    /// <see langword="true"/> when some loop through it carries a pump whose head is unstated or promoted,
    /// which makes the driving pressure a free variable rather than something the boundaries fix.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>This is what decides which way a valve's catalogue selection rounds</strong> (<c>C-62</c>,
    /// <c>D-89</c>). With a free pump the valve's drop is a <em>choice</em> and the authority target makes
    /// it, rounding down because more authority is the safe direction and the pump absorbs the extra. With
    /// no free pump the boundary pressures fix the driving pressure, the valve takes what the rest of the
    /// path leaves, and rounding down would put the design flow out of reach at every position.
    /// </para>
    /// <para>
    /// <strong>The scope is the component's own circuits, never the graph</strong> — the same reason
    /// <see cref="ThreeWay"/> asks about the legs the drawn flow crosses rather than about every pump
    /// present. Looking graph-wide would read a pumped secondary beside a genuinely bounded primary as
    /// driven, which is the arrangement the distinction exists for. A component on no loop at all answers
    /// <see langword="false"/>, which is right: an open path between two boundaries is the bounded case.
    /// </para>
    /// </remarks>
    private static bool Driven(CircuitGraph graph, IFlowComponent component)
    {
        // The component's block, not its fundamental cycle (S-55): a free pump anywhere in the same
        // biconnected block reaches this branch. The boundaries do not join the blocks here, so a bounded
        // primary beside a pumped secondary keeps reading as bounded (D-89).
        var blocks = HydraulicBlocks.ForFreePumps(graph);

        return graph.Branches.Any(branch => branch.Path.Contains(component) && blocks.Drives(branch));
    }

    /// <summary>The flow through a component and the fluid state there.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">Where the iterate keeps each unknown.</param>
    /// <param name="iterate">The current values.</param>
    /// <param name="component">The component being sized.</param>
    /// <returns>The context, or <see langword="null"/> when no branch carries this component.</returns>
    /// <remarks>
    /// <strong><see cref="SizingContext.AvailableDrop"/> is filled here as well as in
    /// <see cref="ThreeWay"/></strong> (<c>C-62</c>). It used to be set only in the three-way pass, so a
    /// two-way valve was never told what the boundaries offer, <c>ValveSizer</c>'s <c>bounded</c> branch
    /// could not run for one, and it rounded down on a pressure-bounded circuit where that puts the design
    /// flow out of reach. The arithmetic was already there; only the context was missing.
    /// </remarks>
    private static SizingContext? Context(
        CircuitGraph graph, SystemLayout layout, StateVector iterate, IFlowComponent component)
    {
        // A coupled exchanger sits on a branch of each side, and every rule that reads a flow through it
        // means side 1's -- the side the unsuffixed parameters describe.
        var branch = graph.Branches.FirstOrDefault(candidate =>
            candidate.Path.Contains(component)
            && (component is not HeatExchangerComponent exchanger || BranchFlows.Side(graph, candidate, exchanger) == 1));

        if (branch is null || Inlet(graph, layout, iterate, component) is not { } state)
        {
            return null;
        }

        var flow = iterate.Values[layout.BranchFlow(branch.Index)];

        return new SizingContext
        {
            State = state,
            MassFlow = flow,

            // What the rest of the branch resists, run the way its water runs (see Traversal), and its pumps left
            // out: a pump is what absorbs a valve's drop, not part of the resistance its authority is read against.
            BranchDrop = Passive(graph, state, branch, flow, component),
            LoopDrop = Circuit(graph, layout, iterate, component, state),
            AvailableDrop = Driven(graph, component) ? null : Offered(graph),
            HeldOutlet = component is HeatExchangerComponent exchanger && !exchanger.StatedParameters.ContainsKey("out")
                ? BranchFlows.DownstreamTemperature(graph, exchanger, "out")?.SiValue
                : null,
        };
    }

    /// <summary>The fluid state at a component's own inlet.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">Where the iterate keeps each unknown.</param>
    /// <param name="iterate">The current values.</param>
    /// <param name="component">The component.</param>
    /// <returns>The state, or <see langword="null"/> when its inlet reaches no node.</returns>
    /// <remarks>
    /// <strong>Its own inlet, not the loop mean, and <c>24</c> flags the difference as a trap.</strong>
    /// A pump develops head against the fluid actually entering it, so the worked example converts
    /// 51.7 kPa at 998.2 kg/m³ and reads 5.28 m; the same drop at the loop's 35 °C mean of 994 kg/m³
    /// reads 5.30 m. Under half a percent there, and it grows with the loop's temperature spread — so
    /// an implementation that silently takes the mean disagrees with the document by more than rounding
    /// while looking right.
    /// </remarks>
    private static FluidState? Inlet(
        CircuitGraph graph, SystemLayout layout, StateVector iterate, IFlowComponent component)
    {
        var element = graph.Components.IndexOf(component);
        var peer = element < 0 ? PortRef.None : graph.Adjacency.Peer(element, 0);
        var index = peer.Exists
            ? Array.FindIndex(
                [.. graph.Nodes],
                node => ReferenceEquals(node.Component, graph.Components[peer.Component]))
            : -1;

        if (index < 0)
        {
            return null;
        }

        var state = graph.Substance.FromPressureEnthalpy(
            Quantity.FromSi(iterate.Values[layout.NodePressure(index)], Dimension.Pressure),
            Quantity.FromSi(iterate.Values[layout.NodeEnthalpy(index)], Dimension.Enthalpy));

        return state.IsSuccess ? state.Value : null;
    }

    /// <summary>The drop around the largest circuit through a component, excluding the component.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">Where the iterate keeps each unknown.</param>
    /// <param name="iterate">The current values.</param>
    /// <param name="component">The component the circuit runs through.</param>
    /// <param name="state">The fluid at it.</param>
    /// <returns>
    /// Pa, or <see langword="null"/> when no cycle contains the component at all — a different fact from
    /// a cycle that resists nothing, and only the caller can say which one matters (<c>C-57</c>).
    /// </returns>
    /// <remarks>
    /// <para>
    /// The largest, because a pump on a set of parallel circuits has to reach the worst of them — which
    /// is the index circuit, and sizing to any other one leaves a branch short of its design flow.
    /// </para>
    /// <para>
    /// <strong>A loop that carries another pump is not this pump's to drive</strong> (<c>D-93</c>). Two
    /// pumps in series on one loop have one head between them and nothing in the loop equation says how
    /// it divides (<c>S-37</c>); sizing each to the whole loop hands the loop twice its drop. Measured on
    /// two pumped sources feeding three pumped consumers: the source pump took 46.8 kPa from a loop
    /// through a consumer's own pump, the header differential that produced over-drove the direct
    /// consumer, and its promoted pump was asked for a negative head. The convention is
    /// primary–secondary practice — the primary pump is sized for the primary circuit, each secondary
    /// pump for its own — so a pump whose every loop is shared is sized to its own branch, and a loop
    /// with a second pump on it is left out of the index search. Only a pump is treated this way: a
    /// valve's circuit is still every loop through it, since a valve shares head with nothing.
    /// </para>
    /// <para>
    /// A pump's loop also counts the mixing valves it crosses (<see cref="MixingValves"/>, <c>S-92</c>), which
    /// stand at the ends of branches and so in no branch's path.
    /// </para>
    /// </remarks>
    private static double? Circuit(
        CircuitGraph graph,
        SystemLayout layout,
        StateVector iterate,
        IFlowComponent component,
        FluidState state)
    {
        double? worst = null;
        var onALoop = false;

        foreach (var loop in graph.Loops)
        {
            if (!loop.Branches.Any(branch => branch.Path.Contains(component)))
            {
                continue;
            }

            onALoop = true;

            if (component is PumpComponent
                && (loop.Branches.Any(branch => branch.Path.Any(
                        element => element is PumpComponent && !ReferenceEquals(element, component)))
                    || SwitchedOnly(graph, loop)))
            {
                continue;
            }

            var drop = 0.0;
            var sense = Traversal(loop, component, layout, iterate);

            foreach (var branch in loop.Branches)
            {
                var flow = iterate.Values[layout.BranchFlow(branch.Index)];
                var along = sense[branch.Index] * (flow < 0 ? -1 : 1);

                drop += along * Resistance(graph, state, branch, Math.Abs(flow), component);
            }

            if (component is PumpComponent)
            {
                drop += MixingValves(graph, layout, iterate, loop.Branches, state);
            }

            worst = Math.Max(worst ?? drop, drop);
        }

        if (worst is null && onALoop
            && graph.Branches.FirstOrDefault(candidate => candidate.Path.Contains(component)) is { } own)
        {
            // Its own branch, and the mixing valve it draws through when that branch is a valve's common leg: the
            // consumer circuit primary-secondary practice sizes a secondary pump for (S-92).
            var mixing = component is PumpComponent
                ? MixingValves(graph, layout, iterate, [own], state)
                : 0;

            return Resistance(graph, state, own, Math.Abs(iterate.Values[layout.BranchFlow(own.Index)]), component) + mixing;
        }

        return worst;
    }

    /// <summary>A two-way valve on a pump's circuit whose position the design solve moves, for the head's control reserve.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="pump">The pump.</param>
    /// <param name="promoted">The promoted parameters' labels, <c>CV1.position</c>.</param>
    /// <returns>The first such valve's name, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The circuits are <see cref="Circuit"/>'s: every loop through the pump that carries no other pump and is not a
    /// valve's two switched legs, else the pump's own branch. A position is promoted where a stated value needs an
    /// answer -- a controller's setpoint (<c>D-141</c>), or a temperature the script wrote -- and a two-way valve
    /// answering one sets the loop's flow. Sized to that loop with the valve fully open, the pump leaves it nothing to
    /// open into, and the controlled steps 5, 11a and 12b pinned it on its stop (<c>S-93</c>, <c>D-188</c>). A
    /// three-way valve is not counted: its position splits a flow the pump sets, it does not throttle it.
    /// </remarks>
    private static string? HeldValve(CircuitGraph graph, IFlowComponent pump, HashSet<string> promoted)
    {
        string? Held(IEnumerable<Branch> branches) => branches
            .SelectMany(static branch => branch.Path)
            .OfType<ValveComponent>()
            .FirstOrDefault(valve => promoted.Contains(Ownership.Key(valve.Name, "position")))?.Name;

        var driven = graph.Loops
            .Where(loop => loop.Branches.Any(branch => branch.Path.Contains(pump))
                && !loop.Branches.Any(branch => branch.Path.Any(element => element is PumpComponent && !ReferenceEquals(element, pump)))
                && !SwitchedOnly(graph, loop))
            .ToList();

        return driven.Count > 0
            ? driven.Select(loop => Held(loop.Branches)).FirstOrDefault(static name => name is not null)
            : Held(graph.Branches.Where(branch => branch.Path.Contains(pump)));
    }

    /// <summary>Whether a loop crosses a mixing valve from one switched leg to the other, which no water circulates round.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="loop">The loop.</param>
    /// <returns><see langword="true"/> when it meets a three-way valve by its <c>a</c> and <c>b</c> legs and not by <c>ab</c>.</returns>
    /// <remarks>
    /// Both switched legs of a mixing valve carry water into it, and both of a diverting valve's carry it out: a cycle
    /// through the two is a cycle of the branch graph, not a path the water takes, and a pump is not sized to it. On the
    /// plant with two pumped sources, every loop through a source's pump but one carried another pump, and the one
    /// left ran from the header through an AHU valve's <c>a</c> and out of its <c>b</c>; summed in the order it is
    /// walked it made the source pump 3 kPa taller than its own branch, and the direct consumer's pump was pushed to
    /// zero head (<c>D-93</c>'s failure, back by another road).
    /// </remarks>
    private static bool SwitchedOnly(CircuitGraph graph, CircuitLoop loop)
    {
        foreach (var valve in graph.JunctionElements.OfType<ThreeWayValveComponent>())
        {
            var ports = loop.Branches
                .Where(branch => ReferenceEquals(branch.From.Element, valve) || ReferenceEquals(branch.To.Element, valve))
                .Select(branch => ValveLegs.PortName(branch, valve))
                .ToHashSet(StringComparer.Ordinal);

            if (ports.Contains("a") && ports.Contains("b") && !ports.Contains("ab"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The direction each branch of a loop is crossed in, walking it the way the water runs through a component on it.</summary>
    /// <param name="loop">The loop.</param>
    /// <param name="component">The component the walk starts at; its branch is crossed the way its flow runs.</param>
    /// <param name="layout">Where the iterate keeps each unknown.</param>
    /// <param name="iterate">The current values.</param>
    /// <returns>+1 or -1 per branch index -- +1 where the walk crosses the branch from its <c>From</c> end -- and 0 for branches off the loop.</returns>
    /// <remarks>
    /// <para>
    /// A branch's orientation is the decomposition's choice, not the water's (<c>32</c>), and
    /// <see cref="BranchResistance.Along(CircuitGraph, FluidState, Branch, double, IFlowComponent?)"/> evaluates each
    /// element at the flow it is handed as though it entered by its inlet. Handed a branch's signed flow, a branch the
    /// water runs through against its orientation read negative, and a loop's drops cancelled: on two radiators fed
    /// from one heater, all three branches oriented one way, the heater's +20 kPa and a radiator's -20 kPa made a loop
    /// that resists nothing, and a valve's branch read -20 kPa, which the authority rule clamps to nothing to size
    /// against (<c>S-91</c>). A pump is worse, because its law is not odd in the flow: flipping its sign turned its rise
    /// into resistance.
    /// </para>
    /// <para>
    /// So each branch is evaluated running forward, at the magnitude of its flow, and counted with the sign of how its
    /// water runs relative to the walk: +1 along it, -1 against. A loop the walk cannot follow as a cycle keeps each
    /// branch's own flow's sense.
    /// </para>
    /// </remarks>
    private static int[] Traversal(CircuitLoop loop, IFlowComponent component, SystemLayout layout, StateVector iterate)
    {
        var sense = new int[loop.Branches.Max(static branch => branch.Index) + 1];
        int Own(Branch branch) => iterate.Values[layout.BranchFlow(branch.Index)] < 0 ? -1 : 1;

        foreach (var branch in loop.Branches)
        {
            sense[branch.Index] = Own(branch);
        }

        if (loop.Branches.FirstOrDefault(branch => branch.Path.Contains(component)) is not { } start)
        {
            return sense;
        }

        var walked = new HashSet<int> { start.Index };
        var at = sense[start.Index] > 0 ? start.To.Element : start.From.Element;

        while (walked.Count < loop.Branches.Length
            && loop.Branches.FirstOrDefault(branch => !walked.Contains(branch.Index)
                && (ReferenceEquals(branch.From.Element, at) || ReferenceEquals(branch.To.Element, at))) is { } next)
        {
            sense[next.Index] = ReferenceEquals(next.From.Element, at) ? 1 : -1;
            at = sense[next.Index] > 0 ? next.To.Element : next.From.Element;
            walked.Add(next.Index);
        }

        return sense;
    }

    /// <summary>What a branch resists apart from a component on it and apart from its pumps, run the way its water runs.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="state">The fluid to evaluate the laws against.</param>
    /// <param name="branch">The branch.</param>
    /// <param name="flow">kg/s along it, in the branch's orientation; its magnitude is used.</param>
    /// <param name="component">The component being sized, left out.</param>
    /// <returns>Pa, positive against the flow.</returns>
    /// <remarks>
    /// A pump sharing a valve's branch is left out whether its head is promoted, stated or sized. Promoted, it carried no
    /// head into the graph and read as nothing, which is why a single loop with its pump solving for the flow sized its
    /// valve against the coils alone; sized by the rule, its rise was subtracted, and the controlled step 5's valve was
    /// chosen against a branch that resisted less than its coils (<c>S-86</c>).
    /// </remarks>
    private static double Passive(CircuitGraph graph, FluidState state, Branch branch, double flow, IFlowComponent component)
    {
        return BranchResistance.Along(
            graph, state, branch, Math.Abs(flow), element => element is PumpComponent || ReferenceEquals(element, component));
    }

    /// <summary>What the mixing valves a pump's loop crosses by their common port drop, fully open at the flow through it (<c>S-92</c>).</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">Where the iterate keeps each unknown.</param>
    /// <param name="iterate">The current values.</param>
    /// <param name="branches">The loop through the pump, or the pump's own branch.</param>
    /// <param name="state">The fluid at the pump's inlet, for the density.</param>
    /// <returns>Pa, non-negative.</returns>
    /// <remarks>
    /// <para>
    /// <strong>A pump's head is every drop in its circuit, the valve's included</strong>: Siemens,
    /// <em>Hydronics in building systems</em>, 2.4.2 -- <c>Δp_pump = Δp_generation + Δp_supply + Δp_valve +
    /// Δp_consumer + Δp_return</c> -- and in a mixing circuit the three-port valve stands in the consumer circuit
    /// its pump drives at constant flow (1.4.5). The valve is a junction element, in no branch's path, so the
    /// branch sum never saw it: the floor block of a series pair was sized to its coil's 20 kPa and asked to
    /// drive 20 kPa and the valve's 7.5, and no position could satisfy the valve's law.
    /// </para>
    /// <para>
    /// Fully open at the common port's flow, the figure the valve was selected to (<c>D-122</c>): with linear legs
    /// the drop through the valve is that band's whatever the mixing ratio. Only a loop that passes the common
    /// port counts one -- a loop from one switched leg to the other is not a path the water takes. The Kv is the
    /// graph's: a pump is sized on a graph lowered from its pass's other sizes (<see cref="Apply"/>), so it is the
    /// Kv the three-way rule chose in the same pass.
    /// </para>
    /// </remarks>
    private static double MixingValves(
        CircuitGraph graph, SystemLayout layout, StateVector iterate, IReadOnlyList<Branch> branches, FluidState state)
    {
        var drop = 0.0;

        foreach (var valve in graph.JunctionElements.OfType<ThreeWayValveComponent>())
        {
            if (!valve.BypassConnected
                || branches.FirstOrDefault(branch =>
                    (ReferenceEquals(branch.From.Element, valve) || ReferenceEquals(branch.To.Element, valve))
                    && ValveLegs.PortName(branch, valve) == "ab") is not { } common)
            {
                continue;
            }

            var across = ValveLaw.PressureDrop(valve.Kv, iterate.Values[layout.BranchFlow(common.Index)], state.Density.SiValue);

            if (double.IsFinite(across))
            {
                drop += across;
            }
        }

        return drop;
    }

    /// <summary>What a run of components resists at a flow, by their own laws.</summary>
    /// <param name="graph">The graph, for its substance.</param>
    /// <param name="state">The fluid to evaluate the laws against.</param>
    /// <param name="branch">The branch, ends included.</param>
    /// <param name="flow">kg/s through them.</param>
    /// <param name="exclude">The component whose own contribution is left out.</param>
    /// <returns>Pa, positive against the flow.</returns>
    /// <remarks>
    /// The rule itself moved to <see cref="BranchResistance"/> when the seed needed it as well: sizing
    /// wants a run's total and the seed wants each element in turn, and one rule in two places is how
    /// <c>D-86</c> came to be applied three times and missed a fourth.
    /// </remarks>
    private static double Resistance(
        CircuitGraph graph,
        FluidState state,
        Branch branch,
        double flow,
        IFlowComponent exclude) =>
        BranchResistance.Along(graph, state, branch, flow, exclude);
}
