using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
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
            BranchDrop = Resistance(graph, state, branch, flow, component),
            LoopDrop = Circuit(graph, layout, iterate, component, state),
            AvailableDrop = Driven(graph, component) ? null : Offered(graph),
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
                && loop.Branches.Any(branch => branch.Path.Any(
                    element => element is PumpComponent && !ReferenceEquals(element, component))))
            {
                continue;
            }

            var drop = 0.0;

            foreach (var branch in loop.Branches)
            {
                drop += Resistance(
                    graph, state, branch, iterate.Values[layout.BranchFlow(branch.Index)], component);
            }

            worst = Math.Max(worst ?? drop, drop);
        }

        if (worst is null && onALoop
            && graph.Branches.FirstOrDefault(candidate => candidate.Path.Contains(component)) is { } own)
        {
            return Resistance(graph, state, own, iterate.Values[layout.BranchFlow(own.Index)], component);
        }

        return worst;
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
