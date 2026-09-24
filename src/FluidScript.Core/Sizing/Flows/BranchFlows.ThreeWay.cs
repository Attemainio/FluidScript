using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Sizing.Flows;

public static partial class BranchFlows
{
    /// <summary>Propagates one common circulation estimate as a partition across a three-way valve.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="estimates">The estimates being completed.</param>
    /// <param name="valve">The mixing or diverting junction.</param>
    /// <returns><see langword="true"/> when an estimate was added.</returns>
    private static bool PropagateThreeWay(
        CircuitGraph graph,
        BranchFlow[] estimates,
        ThreeWayValveComponent valve)
    {
        var common = -1;
        var first = -1;
        var second = -1;

        foreach (var branch in graph.Branches)
        {
            if (!Meets(branch, valve))
            {
                continue;
            }

            switch (FluidScript.Core.Solvers.Results.ValveLegs.PortName(branch, valve))
            {
                case "ab":
                    common = branch.Index;
                    break;
                case "a":
                    first = branch.Index;
                    break;
                case "b":
                    second = branch.Index;
                    break;
            }
        }

        if (common < 0 || first < 0 || second < 0)
        {
            return false;
        }

        var commonKnown = estimates[common].Basis > FlowBasis.Nominal;
        var firstKnown = estimates[first].Basis > FlowBasis.Nominal;
        var secondKnown = estimates[second].Basis > FlowBasis.Nominal;

        // A leg partitioned from a common leg that a duty fixed carries that duty's authority: the two
        // legs sum to the coil's flow exactly, and the seed's forest keeps them as chords so that the
        // coil, their sum, comes out at its rating (`S-68`).
        var partitioned = estimates[common].Basis >= FlowBasis.Duty ? FlowBasis.Partitioned : FlowBasis.Propagated;

        if (commonKnown && !firstKnown && !secondKnown)
        {
                var firstMagnitude = estimates[common].Magnitude
                    * (MixingFraction(graph, estimates[common].Source, graph.Branches[first], valve) ?? 0.5);
                var secondMagnitude = estimates[common].Magnitude - firstMagnitude;

                estimates[first] =
                    new BranchFlow(firstMagnitude, partitioned, estimates[common].Source);
                estimates[second] =
                    new BranchFlow(secondMagnitude, partitioned, estimates[common].Source);

            return true;
        }

        if (commonKnown && firstKnown != secondKnown)
        {
            var known = firstKnown ? first : second;
            var missing = firstKnown ? second : first;
            var remainder = estimates[common].Magnitude - estimates[known].Magnitude;

            if (remainder > Solvers.Tolerances.FlowZero)
            {
                estimates[missing] =
                    new BranchFlow(remainder, partitioned, estimates[common].Source);
                return true;
            }
        }

        if (!commonKnown && firstKnown && secondKnown)
        {
            estimates[common] = new BranchFlow(
                estimates[first].Magnitude + estimates[second].Magnitude,
                FlowBasis.Propagated,
                estimates[first].Source);
            return true;
        }

        // Only the feed is known: a block on a series ring, whose `a` leg carries the ring's flow and
        // whose coil has no duty to size a flow from (`S-69`). The stated `in` and `out` still say what
        // share of the coil's stream the feed is, so the coil is the feed divided by that share and the
        // recirculating leg is the rest. Without the temperatures to read a share from, nothing is said.
        if (!commonKnown && firstKnown && !secondKnown
            && MixingFraction(graph, LoadOn(graph, graph.Branches[common]), graph.Branches[first], valve) is { } share)
        {
            var coil = estimates[first].Magnitude / share;

            estimates[common] = new BranchFlow(coil, FlowBasis.Propagated, estimates[first].Source);
            estimates[second] = new BranchFlow(coil - estimates[first].Magnitude, FlowBasis.Propagated, estimates[first].Source);
            return true;
        }

        return false;
    }

    /// <summary>The name of the exchanger on a branch, for the mixing fraction its temperatures imply.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="branch">The branch, a mixing valve's common leg.</param>
    /// <returns>The first exchanger's name, or an empty string when the branch holds none.</returns>
    private static string LoadOn(CircuitGraph graph, Branch branch) =>
        branch.Path.OfType<HeatExchangerComponent>().FirstOrDefault()?.Name ?? string.Empty;

    /// <summary>Returns the hot-leg fraction implied by a load's design temperatures and the temperature that feeds its valve's <c>a</c> port.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="loadName">The exchanger that supplied the common-leg duty estimate.</param>
    /// <param name="feed">The branch on the valve's <c>a</c> port, which is where the hot water arrives from.</param>
    /// <param name="valve">The valve, so the walk along <paramref name="feed"/> starts from its end.</param>
    /// <returns>A fraction strictly between zero and one, or <see langword="null"/> without enough evidence.</returns>
    /// <remarks>
    /// The hot temperature is what the <c>a</c> port actually receives, not the hottest source in the
    /// graph: on a series header the second block's valve is fed by the first block's return (<c>S-63</c>),
    /// and reading the boiler's 60 &#176;C there made a 35/30 coil's stream a sixth of its circulation
    /// where it is half. <see cref="FeedTemperature"/> walks the feed branch for the exchanger that
    /// last touched the water; the hottest source is the fallback when the walk finds nothing.
    /// </remarks>
    private static double? MixingFraction(CircuitGraph graph, string loadName, Branch feed, ThreeWayValveComponent valve)
    {
        var load = graph.Components
            .OfType<HeatExchangerComponent>()
            .SingleOrDefault(component => string.Equals(component.Name, loadName, StringComparison.Ordinal));

        if (load is null
            || !load.StatedParameters.TryGetValue("in", out var mixed)
            || !load.StatedParameters.TryGetValue("out", out var cold))
        {
            return null;
        }

        var hot = FeedTemperature(graph, feed, valve);

        if (hot is null)
        {
            foreach (var source in graph.Components.OfType<HeatExchangerComponent>())
            {
                if (source.Power > 0
                    && source.StatedParameters.TryGetValue("out", out var outlet)
                    && (hot is null || outlet.SiValue > hot.Value.SiValue))
                {
                    hot = outlet;
                }
            }
        }

        if (hot is null)
        {
            return null;
        }

        var reference = Quantity.FromSi(0, Dimension.Pressure);
        if (!graph.Substance.FromPressureTemperature(reference, cold).TryGetValue(out var coldState)
            || !graph.Substance.FromPressureTemperature(reference, mixed).TryGetValue(out var mixedState)
            || !graph.Substance.FromPressureTemperature(reference, hot.Value).TryGetValue(out var hotState))
        {
            return null;
        }

        var span = hotState.Enthalpy.SiValue - coldState.Enthalpy.SiValue;
        var fraction = (mixedState.Enthalpy.SiValue - coldState.Enthalpy.SiValue) / span;

        return span > 0 && fraction > 0 && fraction < 1 ? fraction : null;
    }

    /// <summary>The temperature of the water arriving at a valve's <c>a</c> port, read from the exchanger that last touched it.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="feed">The branch on the <c>a</c> port.</param>
    /// <param name="valve">The valve the branch ends at.</param>
    /// <returns>A stated outlet temperature, or <see langword="null"/> when nothing on the way states one.</returns>
    /// <remarks>
    /// Walks the feed branch away from the valve: a source in the path (the boiler on the parallel
    /// header) gives its <c>out</c>. Reaching the far end without one, the far junction is another
    /// block's mixing node, and the exchanger on any branch meeting it that states an <c>out</c> is
    /// what discharges there -- the first block's load on the series header. A stated temperature on
    /// the junction node itself wins over both.
    /// </remarks>
    private static Quantity? FeedTemperature(CircuitGraph graph, Branch feed, ThreeWayValveComponent valve)
    {
        var forward = ReferenceEquals(feed.To.Element, valve);
        IEnumerable<IFlowComponent> path = feed.Path;

        if (forward)
        {
            path = path.Reverse();
        }

        foreach (var element in path)
        {
            if (element is HeatExchangerComponent { Power: > 0 } source
                && source.StatedParameters.TryGetValue("out", out var outlet))
            {
                return outlet;
            }
        }

        var far = forward ? feed.From.Element : feed.To.Element;

        if (far is NodeComponent node && node.StatedParameters.TryGetValue("t", out var stated))
        {
            return stated;
        }

        // The junction the feed leaves from, and every junction reachable from it through branches that
        // hold no exchanger: on a series header the block's feed node joins the previous block's mixing
        // node through a bare pipe, and the water arriving is that block's coil outlet, one junction
        // further than the feed's own (`S-69`). A branch with an exchanger on it ends the walk there,
        // because the exchanger's stated outlet is the answer.
        var seen = new HashSet<IFlowComponent> { far };
        var pending = new Queue<IFlowComponent>();
        pending.Enqueue(far);

        while (pending.Count > 0)
        {
            var junction = pending.Dequeue();

            foreach (var branch in graph.Branches)
            {
                if (branch.Index == feed.Index || !Meets(branch, junction))
                {
                    continue;
                }

                var exchanger = branch.Path.OfType<HeatExchangerComponent>().FirstOrDefault();

                if (exchanger is not null)
                {
                    if (exchanger.StatedParameters.TryGetValue("out", out var outlet))
                    {
                        return outlet;
                    }

                    continue;
                }

                var beyond = ReferenceEquals(branch.From.Element, junction) ? branch.To.Element : branch.From.Element;

                if (beyond is NodeComponent && seen.Add(beyond))
                {
                    pending.Enqueue(beyond);
                }
            }
        }

        return null;
    }

    /// <summary>Whether a branch terminates at any connected three-way valve.</summary>
    /// <param name="branch">The branch.</param>
    /// <returns><see langword="true"/> when either end is a three-way valve.</returns>
    private static bool MeetsThreeWay(Branch branch) =>
        branch.From.Element is ThreeWayValveComponent || branch.To.Element is ThreeWayValveComponent;

    /// <summary>Finds the one return temperature all opposing loads its water can reach state.</summary>
    /// <remarks>
    /// Loads its water can reach, not the model's: a cooling coil warms its water, so it reads as a
    /// source, and reading every load in the file handed it the heating loop's 40 °C return as the inlet
    /// of a 7/12 chilled stream -- 0.85 kg/s seeded where 100 kW needs 4.77 (<c>S-83</c>). The hydraulic
    /// partition, not the circuit: a subcircuit's loads share their parent's water under another circuit
    /// name, and the distribution header seeds its source from them.
    /// </remarks>
    private static Quantity? CommonReturn(CircuitGraph graph, HeatExchangerComponent source)
    {
        if (source.Power <= 0)
        {
            return null;
        }

        var reachable = HydraulicPartition.Of(graph)
            .Where(partition => partition.Elements.Contains(source))
            .SelectMany(static partition => partition.Elements)
            .ToHashSet();

        var returns = graph.Components
            .OfType<HeatExchangerComponent>()
            .Where(candidate => candidate.Power < 0 && reachable.Contains(candidate))
            .Select(candidate => candidate.StatedParameters.TryGetValue("out", out var outlet)
                ? (Quantity?)outlet
                : null)
            .ToArray();

        if (returns.Length == 0
            || returns.Any(static candidate => candidate is null)
            || returns.Any(candidate => !candidate!.Value.IsCloseTo(returns[0]!.Value)))
        {
            return null;
        }

        return returns[0];
    }

    /// <summary>The flow a load that states its duty and no terminal temperature carries at its sources' design temperatures.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="load">The exchanger; applies only to a load (negative duty) stating neither <c>in</c> nor <c>out</c>.</param>
    /// <returns>kg/s, or <see langword="null"/> when the sources its water can reach do not all state one supply and one return temperature.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="CommonReturn"/> read the other way (<c>S-85</c>). A zone written <c>LD1 heat_exchanger power=-20</c>
    /// has a duty and no temperature, so it had no rating, and its branch took whatever the copy rule handed a split:
    /// on the three-zone plant, the plant's whole 0.478 kg/s on every zone, with zone 1 then run backwards to balance
    /// the header. An emitter left without its own temperatures is designed to the system's -- the plant's 70/40 --
    /// so 20 kW is 0.159 kg/s, which is where the zones settle.
    /// </para>
    /// <para>
    /// A seed, not a rating: nothing here enters the equations, and the zones' flows are still their hydraulics'. It
    /// matters because a load's design flow is sized from the flow the previous pass found, a fixed point the sizing
    /// loop approaches by about 20 / (20 + the valve's 2 kPa) per pass; started 15 % off it needs some fifty passes. The
    /// sources must agree: a plant with a 70/40 and an 80/60 source has no one design difference, and none is invented.
    /// </para>
    /// </remarks>
    private static double? DesignFlow(CircuitGraph graph, HeatExchangerComponent load)
    {
        if (load.Power >= 0 || load.StatedParameters.ContainsKey("in") || load.StatedParameters.ContainsKey("out") || load.StatedParameters.ContainsKey("dt"))
        {
            return null;
        }

        var reachable = HydraulicPartition.Of(graph)
            .Where(partition => partition.Elements.Contains(load))
            .SelectMany(static partition => partition.Elements)
            .ToHashSet();

        var sources = graph.Components
            .OfType<HeatExchangerComponent>()
            .Where(candidate => candidate.Power > 0 && reachable.Contains(candidate))
            .ToArray();

        if (sources.Length == 0
            || !sources[0].StatedParameters.TryGetValue("in", out var back)
            || !sources[0].StatedParameters.TryGetValue("out", out var supply)
            || sources.Any(source => !source.StatedParameters.TryGetValue("in", out var i) || !i.IsCloseTo(back)
                || !source.StatedParameters.TryGetValue("out", out var o) || !o.IsCloseTo(supply)))
        {
            return null;
        }

        return RatedFlow(graph.Substance, load.Power, supply, back);
    }
}
