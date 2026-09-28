using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Language.Registry;
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
    /// A diverting valve has no hot port to read: its share is its recirculation's, from <see cref="DivertedShare"/>.
    /// </remarks>
    private static double? MixingFraction(CircuitGraph graph, string loadName, Branch feed, ThreeWayValveComponent valve)
    {
        var load = graph.Components
            .OfType<HeatExchangerComponent>()
            .SingleOrDefault(component => string.Equals(component.Name, loadName, StringComparison.Ordinal));

        // Its own terminals, else the temperatures held on the nodes either side of it -- the setpoints a controlled
        // block states in their place (S-86): step 8c with its controls holds each coil's inlet at the node after its
        // valve, and read only off the coil the three unrated blocks lost the partition their stated inlets gave.
        if (load is null
            || (load.StatedParameters.TryGetValue("in", out var statedMixed) ? statedMixed : UpstreamOutlet(graph, load)) is not { } mixed
            || (load.StatedParameters.TryGetValue("out", out var statedCold) ? statedCold : DownstreamTemperature(graph, load, "out")) is not { } cold)
        {
            return null;
        }

        if (Diverts(graph, valve))
        {
            return DivertedShare(graph, feed, valve, mixed, cold);
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

        return Bounded(hotState.Enthalpy.SiValue - coldState.Enthalpy.SiValue, mixedState.Enthalpy.SiValue - coldState.Enthalpy.SiValue, valve);
    }

    /// <summary>A leg's share from an enthalpy balance, kept off zero flow and refused where the temperatures cannot make it.</summary>
    /// <param name="span">J/kg, the difference between the two streams mixed.</param>
    /// <param name="part">J/kg, the mixed stream's difference from the one the share is not of.</param>
    /// <param name="valve">The valve, whose leakage bounds the share.</param>
    /// <returns>The share, or <see langword="null"/> when no share in [0, 1] gives the mix or the span is not positive.</returns>
    private static double? Bounded(double span, double part, ThreeWayValveComponent valve)
    {
        var fraction = part / span;

        // A design that needs no bypass -- the supply held at the very temperature the feed arrives at, the syntax
        // tour's winter case -- is a fraction of 1, and a real answer: the valve's recirculating leg passes only its
        // leakage (D-135). So is 0. Rounding either to the leakage keeps the leg off zero flow, which no Newton step
        // leaves (S-21); anything past them is a design the temperatures cannot make, and is not guessed (S-94).
        const double Rounding = 1e-6;

        return span > 0 && fraction > -Rounding && fraction < 1 + Rounding
            ? Math.Clamp(fraction, valve.Leakage, 1 - valve.Leakage)
            : null;
    }

    /// <summary>Whether a three-way valve splits the stream at its common port rather than mixing two into it.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="valve">The valve.</param>
    /// <returns><see langword="true"/> for a <c>diverting_valve</c>, or a bare body whose <c>ab</c> is fed by an outlet.</returns>
    private static bool Diverts(CircuitGraph graph, ThreeWayValveComponent valve) =>
        valve.Arrangement == ValveArrangement.Diverting
        || (valve.Arrangement == ValveArrangement.Unspecified
            && ValvePort(graph, graph.Components.IndexOf(valve), PortNamed(valve, "a"), []) == PortRole.Outlet);

    /// <summary>The share of a diverting valve's stream one leg takes, from the mix its recirculating leg makes upstream of the load.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="feed">The leg whose share is wanted.</param>
    /// <param name="valve">The valve.</param>
    /// <param name="mixed">The load's inlet temperature: what the recirculated and the fresh water mix to.</param>
    /// <param name="returned">The load's outlet temperature: what both legs carry.</param>
    /// <returns>A fraction of the common flow, or <see langword="null"/> without a direct recirculation or one fresh temperature.</returns>
    /// <remarks>
    /// <para>
    /// A load ahead of a diverting valve whose one leg returns to the junction the load draws from is an injection
    /// circuit: the recirculating leg brings the load's outlet back, the other stream at that junction brings fresh water,
    /// and the load's inlet is their mix. So the recirculated share is (h<sub>in</sub> − h<sub>fresh</sub>) /
    /// (h<sub>out</sub> − h<sub>fresh</sub>) -- the cooling loop's 6 °C primary into a 20/50 coil is 14/44, 0.32 of the
    /// coil's 0.239 kg/s back to the node and 0.68 out to the primary.
    /// </para>
    /// <para>
    /// The mixing rule read this valve as though its <c>a</c> port were fed hot, found the coil's own 50 °C outlet there,
    /// and with no span left fell back to half and half, so the node's fresh water was seeded at the recirculation's
    /// 0.12 kg/s rather than 0.16 (<c>S-85</c>).
    /// </para>
    /// </remarks>
    private static double? DivertedShare(CircuitGraph graph, Branch feed, ThreeWayValveComponent valve, Quantity mixed, Quantity returned)
    {
        var legs = graph.Branches.Where(branch => Meets(branch, valve)).ToArray();
        var common = legs.FirstOrDefault(branch => FluidScript.Core.Solvers.Results.ValveLegs.PortName(branch, valve) == "ab");

        if (common is null)
        {
            return null;
        }

        var junction = ReferenceEquals(common.From.Element, valve) ? common.To.Element : common.From.Element;
        var recirculating = legs
            .Where(branch => branch.Index != common.Index
                && ReferenceEquals(ReferenceEquals(branch.From.Element, valve) ? branch.To.Element : branch.From.Element, junction))
            .ToArray();

        if (junction is not NodeComponent || recirculating.Length != 1)
        {
            return null;
        }

        Quantity? fresh = null;

        foreach (var branch in graph.Branches)
        {
            if (branch.Index == common.Index || branch.Index == recirculating[0].Index || !Meets(branch, junction))
            {
                continue;
            }

            if (FeedTemperature(graph, branch, junction) is not { } arriving
                || (fresh is { } agreed && !arriving.IsCloseTo(agreed)))
            {
                return null;
            }

            fresh = arriving;
        }

        var reference = Quantity.FromSi(0, Dimension.Pressure);
        if (fresh is null
            || !graph.Substance.FromPressureTemperature(reference, fresh.Value).TryGetValue(out var freshState)
            || !graph.Substance.FromPressureTemperature(reference, mixed).TryGetValue(out var mixedState)
            || !graph.Substance.FromPressureTemperature(reference, returned).TryGetValue(out var returnedState)
            || Bounded(
                Math.Abs(returnedState.Enthalpy.SiValue - freshState.Enthalpy.SiValue),
                Math.Sign(returnedState.Enthalpy.SiValue - freshState.Enthalpy.SiValue) * (mixedState.Enthalpy.SiValue - freshState.Enthalpy.SiValue),
                valve) is not { } share)
        {
            return null;
        }

        return feed.Index == recirculating[0].Index ? share : 1 - share;
    }

    /// <summary>The temperature of the water arriving along a branch at one of its ends, read from the exchanger that last touched it.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="feed">The branch: a valve's <c>a</c> leg, or a stream into a junction.</param>
    /// <param name="valve">The element the water arrives at.</param>
    /// <returns>A stated outlet temperature, or <see langword="null"/> when nothing on the way states one.</returns>
    /// <remarks>
    /// Walks the feed branch away from the valve: a source in the path (the boiler on the parallel
    /// header) gives its <c>out</c>. Reaching the far end without one, the far junction is another
    /// block's mixing node, and the exchanger on any branch meeting it that states an <c>out</c> is
    /// what discharges there -- the first block's load on the series header. A stated temperature on
    /// the junction node itself wins over both.
    /// </remarks>
    private static Quantity? FeedTemperature(CircuitGraph graph, Branch feed, IFlowComponent valve)
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

            // A coupled exchanger heating the feed on its side 2 -- a substation's secondary -- gives that side's
            // outlet; its `power` is side 1's, and may be left to the solve (S-94).
            if (element is HeatExchangerComponent coupled
                && Side(graph, feed, coupled) == 2
                && coupled.StatedParameters.TryGetValue("out2", out var secondary))
            {
                return secondary;
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
    /// And the circuit must be closed: an open one takes heat in or out through its boundaries, so its exchangers'
    /// temperatures are not the design difference of its loads.
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

        // Only a closed circuit: water arriving through a boundary brings heat of its own, so the exchangers are not
        // the only design reference -- on the heat pump's cooling side the bores' 10 C water joins the coil's 7/12,
        // and borrowing 7/12 for the evaporator sent a converging solve to its valve's stop.
        if (reachable.Any(static element => element is NodeComponent { Boundary: not BoundaryRole.Interior }))
        {
            return null;
        }

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
