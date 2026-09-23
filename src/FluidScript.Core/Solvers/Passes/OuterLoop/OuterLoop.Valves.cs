using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Sizing;
using FluidScript.Core.Sizing.Flows;
using FluidScript.Core.Sizing.Sizers;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Solvers.Passes;

public sealed partial class OuterLoop
{
    /// <summary>Sizes every three-way valve that stands as a junction element (<c>24</c>, <c>C-63</c>).</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">Where the iterate keeps each unknown.</param>
    /// <param name="iterate">The current estimate the sizing is read off.</param>
    /// <param name="overlay">The overlay being built, added to in place.</param>
    /// <param name="bases">Why each value was chosen, keyed <c>component.parameter</c>.</param>
    /// <param name="notes">Anything the user should be told.</param>
    /// <param name="promoted">Labels the counting pass has already claimed as solver unknowns.</param>
    /// <remarks>
    /// <para>
    /// <strong>This is a pass rather than an <c>ISizer</c> because of what the context needs to be, not
    /// because the arithmetic differs.</strong> A three-way valve with its bypass connected is a junction
    /// element: it appears in no branch's <c>Path</c>, so <see cref="Context"/> finds it nothing and the
    /// ordinary loop skips it. What it needs is a context built from <em>one of its three legs</em>, and
    /// choosing which one is a comparison across sibling branches that a rule handed a single branch
    /// cannot make (<c>C-49</c>, <c>C-61</c>). Once the context exists, <see cref="ValveSizer"/> is the
    /// rule -- the same Kv law, authority definition and catalogue a two-way valve gets.
    /// </para>
    /// <para>
    /// <strong>Neither leg is identified by its port letter, and using them would be wrong on half of
    /// all scripts.</strong> <c>22</c> names the ports <c>a</c> common, <c>b</c> controlled, <c>c</c>
    /// bypass, but binding is positional -- ports take connections in the order the script writes them.
    /// Measured on <c>m2-cooling-loop</c>, <c>b</c> carries the <em>recirculation</em> and <c>c</c> the
    /// primary draw, because <c>3WV - N2</c> was written before <c>3WV - P1</c>. So both legs are found
    /// from the circuit instead: the <strong>common</strong> leg is the one carrying what the other two
    /// split, which mass balance settles, and the <strong>variable</strong> leg is the one reaching a
    /// stated pressure rather than closing back into the valve's own loop, which is the same criterion
    /// the authority definition uses.
    /// </para>
    /// <para>
    /// <strong>Whether the drop is chosen or determined is decided by looking for a free pump on the
    /// path the variable flow actually takes</strong> -- the variable leg and the common leg, which are
    /// the two the drawn flow crosses. A pump whose head is promoted or unstated makes the driving
    /// pressure free, so the authority target chooses the drop; with no such pump the boundary pressures
    /// fix it and the valve takes what the rest of the path leaves. Looking graph-wide instead would
    /// misread a pumped secondary beside a genuinely bounded primary, which is the arrangement this rule
    /// exists for.
    /// </para>
    /// </remarks>
    private void ThreeWay(
        CircuitGraph graph,
        SystemLayout layout,
        StateVector iterate,
        ref SizingOverlay overlay,
        ImmutableDictionary<string, string>.Builder bases,
        ImmutableArray<string>.Builder notes,
        HashSet<string> promoted)
    {
        if (sizers.OfType<ValveSizer>().FirstOrDefault() is not { } rule)
        {
            return;
        }

        foreach (var component in graph.Components)
        {
            if (component is not ThreeWayValveComponent { BypassConnected: true } valve)
            {
                continue;
            }

            var (built, declined) = ThreeWayContext(graph, layout, iterate, valve);

            if (declined is not null)
            {
                Declined(valve, overlay, bases, notes, declined);

                continue;
            }

            if (built is not { } context)
            {
                continue;
            }

            var sized = rule.Size(valve, context);

            if (!sized.IsSuccess)
            {
                Declined(valve, overlay, bases, notes, sized.Error?.Message ?? "the rule declined it");

                continue;
            }

            foreach (var (parameter, value) in sized.Value.Values)
            {
                if (Claimed(valve, parameter, promoted))
                {
                    continue;
                }

                overlay = overlay.With(valve.Name, parameter, value.Value);
                bases[Ownership.Key(valve.Name, parameter)] = value.Basis;
            }

            notes.AddRange(sized.Value.Notes);
        }
    }

    /// <summary>Builds a mixing valve's context from the leg that actually varies (<c>C-63</c>).</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">Where the iterate keeps each unknown.</param>
    /// <param name="iterate">The current values.</param>
    /// <param name="valve">A three-way valve with its bypass connected.</param>
    /// <returns>
    /// The context; or why the valve cannot have one, for the caller to report; or neither, when the
    /// valve is not yet wired into three legs and there is nothing to say.
    /// </returns>
    /// <remarks>
    /// Taken out of <see cref="ThreeWay"/> so that a plant whose Kv was given reads its authority off the
    /// same context the rule chose it against (<c>C-121</c>). Two readers of one definition, rather than
    /// two definitions that could drift apart.
    /// </remarks>
    private static (SizingContext? Context, string? Declined) ThreeWayContext(
        CircuitGraph graph, SystemLayout layout, StateVector iterate, ThreeWayValveComponent valve)
    {
        if (Inlet(graph, layout, iterate, valve) is not { } state)
        {
            return (null, null);
        }

        var legs = graph.Branches
            .Where(branch =>
                ReferenceEquals(branch.From.Element, valve) || ReferenceEquals(branch.To.Element, valve))
            .ToArray();

        if (legs.Length != 3)
        {
            return (null, null);
        }

        var flows = Array.ConvertAll(
            legs, leg => Math.Abs(iterate.Values[layout.BranchFlow(leg.Index)]));

        var common = ValveLegs.Common(legs, flows, valve);
        var variable = ValveLegs.Variable(graph, legs, common, valve);

        if (variable < 0)
        {
            return (null,
                "its connections name no ports, and its two switched legs are the same distance from "
                + "the leg they split -- so nothing says which of them recirculates and which varies "
                + "when the valve strokes. Name them: `a` is the leg it controls, `b` the bypass");
        }

        // Not `Driven(graph, valve)`: a three-way valve with its bypass connected is a junction
        // element, so it sits in no branch's `Path`. The legs the drawn flow crosses answer instead --
        // by their block (S-55): the pump-free mixing header's main valve has no pump on either leg
        // and is driven by the consumer pumps that draw from its common port through the same block.
        var blocks = HydraulicBlocks.ForFreePumps(graph);
        var driven = blocks.Drives(legs[common]) || blocks.Drives(legs[variable]);

        var flow = flows[variable];
        var context = new SizingContext
        {
            State = state,
            MassFlow = flow,
            BranchDrop = Resistance(graph, state, legs[variable], flow, valve),
            LoopDrop = Circuit(graph, layout, iterate, valve, state),
            AvailableDrop = driven ? null : Offered(graph),
            CommonFlow = flows[common],
        };

        if (!driven && context.AvailableDrop is null)
        {
            return (null,
                "no pump on its path carries a free head, so the boundary pressures determine its "
                + "drop — and the circuit does not state exactly two of them, so which pair drives "
                + "this valve is not decided");
        }

        return (context, null);
    }

    /// <summary>Reads every control valve's authority off a solve whose sizes were given (<c>C-121</c>).</summary>
    /// <param name="graph">The graph, built from the given sizes.</param>
    /// <param name="layout">Where the solution keeps each unknown.</param>
    /// <param name="solution">The converged solution.</param>
    /// <param name="overlay">The given sizes.</param>
    /// <param name="bases">Their bases.</param>
    /// <param name="posedness">The counting pass, for which parameters are the script's.</param>
    /// <returns>The sizes and bases with each valve's <c>authority</c> added, and the readings behind them.</returns>
    /// <remarks>
    /// <para>
    /// <strong>A calculation over a solved graph, not a sizing pass.</strong> The Kv is the one given;
    /// running <see cref="ValveSizer"/> here would choose it again for this case alone and undo the
    /// merge the frozen solve exists to check. What is reused is the definition: the same context the
    /// rule chose the Kv against, and <see cref="ValveSizer.Achieved"/>, so the figure a merged plant
    /// reports is the figure a single run would have reported for the same valve.
    /// </para>
    /// <para>
    /// The valves it skips are the ones the sizer skips: a balancing valve on a switched leg, whose Kv
    /// <c>BypassValves</c> sets for a different job (<c>C-111</c>), and a valve that carries no flow in
    /// this case, which has no operating point to read — a valve that is shut is not controlling.
    /// A stated <c>authority</c> is the script's target and is not overwritten; its reading is still
    /// returned, so the check against the minimum sees the valve as built.
    /// </para>
    /// </remarks>
    private static (SizingOverlay Overlay, ImmutableDictionary<string, string> Bases, ImmutableArray<ValveReading> Readings) Readings(
        CircuitGraph graph,
        SystemLayout layout,
        StateVector solution,
        SizingOverlay overlay,
        ImmutableDictionary<string, string> bases,
        WellPosednessResult posedness)
    {
        var promoted = posedness.Counting.Promotions.Select(static promotion => promotion.Label).ToHashSet(StringComparer.Ordinal);
        var written = bases.ToBuilder();
        var readings = ImmutableArray.CreateBuilder<ValveReading>();

        foreach (var component in graph.Components)
        {
            var (context, characteristic, kv) = component switch
            {
                ValveComponent balancing when OnSwitchedLeg(graph, balancing) is not null && !Claimed(balancing, "kv", promoted)
                    => (null, default, 0),
                ValveComponent valve => (Context(graph, layout, solution, valve), valve.Characteristic, valve.Kv),
                ThreeWayValveComponent { BypassConnected: true } mixing
                    => (ThreeWayContext(graph, layout, solution, mixing).Context, mixing.Characteristic, mixing.Kv),
                ThreeWayValveComponent twoWay => (Context(graph, layout, solution, twoWay), twoWay.Characteristic, twoWay.Kv),
                _ => ((SizingContext?)null, default(ValveCharacteristic), 0.0),
            };

            if (context is not { } at || Math.Abs(at.MassFlow) <= Tolerances.FlowZero)
            {
                continue;
            }

            var kvs = overlay.For(component.Name, "kv") ?? kv;
            var (authority, valveDrop) = ValveSizer.Achieved(at, kvs);

            if (!double.IsFinite(authority))
            {
                continue;
            }

            var flow = Math.Abs(at.MassFlow);
            var rest = Math.Max(0, at.BranchDrop);

            readings.Add(new ValveReading(component.Name, authority, flow, valveDrop, rest, characteristic));

            if (Claimed(component, "authority", promoted))
            {
                continue;
            }

            overlay = overlay.With(component.Name, "authority", Quantity.FromSi(authority, Dimension.Dimensionless));
            written[Ownership.Key(component.Name, "authority")] = string.Create(
                CultureInfo.InvariantCulture,
                $"{authority:0.##} on the plant as given — Kv {kvs:0.##} drops {valveDrop / 1000:0.##} kPa of the "
                + $"branch's {(rest + valveDrop) / 1000:0.##} kPa at {SizingContext.LitresPerSecond(flow, at.State.Density.SiValue):0.###} l/s");
        }

        return (overlay, written.ToImmutable(), readings.ToImmutable());
    }

    /// <summary>Sets every two-way valve that sits on a three-way valve's switched leg so that the two legs see the same pressure (<c>24</c>, <c>C-111</c>).</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">Where the iterate keeps each unknown.</param>
    /// <param name="iterate">The current estimate the setting is read off.</param>
    /// <param name="overlay">The overlay being built, added to in place.</param>
    /// <param name="bases">Why each value was chosen, keyed <c>component.parameter</c>.</param>
    /// <param name="notes">Anything the user should be told.</param>
    /// <param name="promoted">Labels the counting pass has already claimed as solver unknowns.</param>
    /// <param name="solved">
    /// Whether <paramref name="iterate"/> is a converged solution. The bootstrap passes read the seed,
    /// whose pressure walk caps each component and so understates a ring's cost; a balancing valve set
    /// from it lands the first solve on a Kv it struggles with (measured: Kv 2.9-2.95 stated on the
    /// series header takes 34-46 iterations from the cold seed, 2.96 takes 6). A balancing valve is set
    /// from a solved field or not at all, so the bootstrap leaves it open and the first solved pass sets it.
    /// </param>
    /// <remarks>
    /// <para>
    /// <strong>A balancing valve is set, not selected.</strong> Its Kv is whatever presetting gives the
    /// drop the circuit needs at the design flow, read off the maker's Kv-per-turn curve at
    /// commissioning; there is no catalogue row to round to, so the value written is the law's own
    /// inverse and nothing rounds it. The drop it needs is what the three-way valve is dissipating on
    /// this leg beyond the other: the valve's own drop at this pass plus the imbalance
    /// <see cref="BypassBalance.Read"/> measures, which is the same arithmetic <c>FS4011</c> reports
    /// when no such valve exists. Each pass re-reads the imbalance with the previous setting in
    /// place, so the fixed point is the setting at which the legs are level and the three-way valve
    /// sits at the position its ratio implies.
    /// </para>
    /// <para>
    /// <strong>A valve on the harder leg has nothing to absorb</strong> and is left at its bootstrap
    /// value with a basis saying which leg the balancing valve belongs on. It is a pass rather than an
    /// <c>ISizer</c> for <see cref="ThreeWay"/>'s reason: the context is a comparison across the
    /// three-way valve's sibling legs, which a rule handed one branch cannot make.
    /// </para>
    /// </remarks>
    private static void BypassValves(
        CircuitGraph graph,
        SystemLayout layout,
        StateVector iterate,
        ref SizingOverlay overlay,
        ImmutableDictionary<string, string>.Builder bases,
        ImmutableArray<string>.Builder notes,
        HashSet<string> promoted,
        bool solved)
    {
        PortMap? ports = null;

        foreach (var component in graph.Components)
        {
            if (component is not ValveComponent valve
                || Claimed(valve, "kv", promoted)
                || OnSwitchedLeg(graph, valve) is not { } leg)
            {
                continue;
            }

            var flow = Math.Abs(iterate.Values[layout.BranchFlow(leg.Branch.Index)]);

            if (!solved)
            {
                // The seed's pressures are not worth setting a balancing valve to, but the fully-open
                // provisional sits in the Kv law's regularised band and the first solve creeps on its
                // row; the least drop a balancing valve is ever set to keeps it regular until a solved
                // field says what it must take.
                if (Inlet(graph, layout, iterate, valve) is { } seeded && flow > Tolerances.FlowZero
                    && ValveLaw.RequiredKv(flow, SizingDefaults.BalancingDropMinimum, seeded.Density.SiValue) is var opening
                    && double.IsFinite(opening) && opening > 0)
                {
                    // Provisional: a placeholder with a number in it (D-96), which the solved pass replaces
                    // or, on the harder leg, keeps and says so.
                    overlay = overlay.With(valve.Name, "kv", Quantity.FromSi(opening, Dimension.Kv), provisional: true);
                    bases[Ownership.Key(valve.Name, "kv")] = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Kv {opening:0.##} — the {SizingDefaults.BalancingDropMinimum / 1000:0} kPa a balancing valve is at least set to, at "
                        + $"{flow:0.###} kg/s; the first solved pass sets it to level {leg.Valve.Name}'s legs");
                }
                else
                {
                    Kept(valve, overlay, bases, notes, "a balancing valve is set from a solved field, and this pass is the seed");
                }

                continue;
            }

            ports ??= PortMap.Build(graph);

            var element = graph.Components.IndexOf(component);
            var three = graph.Components.IndexOf(leg.Valve);
            var reading = BypassBalance.Read(graph, ports, layout, iterate.Values.AsSpan(), three);
            var inlet = ports[element, 0];
            var outlet = ports[element, 1];

            if (reading is not { } legs || Inlet(graph, layout, iterate, valve) is not { } state
                || inlet.Node < 0 || outlet.Node < 0)
            {
                Kept(valve, overlay, bases, notes, $"{leg.Valve.Name}'s legs could not be read at this pass");

                continue;
            }

            if (flow <= Tolerances.FlowZero)
            {
                Kept(valve, overlay, bases, notes, $"the {leg.Port} leg of {leg.Valve.Name} carries no flow at this operating point");

                continue;
            }

            // Positive when this leg is the easy one: what the three-way valve dissipates here beyond
            // the other leg, which is what this valve must take over. The valve's own drop at this pass
            // is already part of the leg's path, so the setting keeps it and adds the remainder.
            var own = Math.Abs(iterate.Values[layout.NodePressure(inlet.Node)] - iterate.Values[layout.NodePressure(outlet.Node)]);
            var toward = leg.Port == "b" ? legs.Imbalance : -legs.Imbalance;
            var need = own + toward;
            var other = leg.Port == "b" ? "a" : "b";

            if (need <= ValveLaw.RegularizationDrop)
            {
                Kept(
                    valve,
                    overlay,
                    bases,
                    notes,
                    $"the {other} path of {leg.Valve.Name} is the easier one by {(-toward) / 1000:0.0} kPa, so there is nothing "
                    + $"on the {leg.Port} leg to absorb; a balancing valve belongs on the {other} leg");

                continue;
            }

            var kv = ValveLaw.RequiredKv(flow, need, state.Density.SiValue);

            if (!double.IsFinite(kv) || kv <= 0)
            {
                Kept(valve, overlay, bases, notes, "the Kv law has no setting for the drop it would need");

                continue;
            }

            overlay = overlay.With(valve.Name, "kv", Quantity.FromSi(kv, Dimension.Kv));
            bases[Ownership.Key(valve.Name, "kv")] = string.Create(
                CultureInfo.InvariantCulture,
                $"Kv {kv:0.##} — set to level {leg.Valve.Name}'s legs: drops {need / 1000:0.0} kPa at {flow:0.###} kg/s "
                + $"so the {leg.Port} path meets the {other} path; a balancing valve is set to its drop, not selected from a series");
        }
    }

    /// <summary>The three-way valve whose switched leg a two-way valve sits on, or <see langword="null"/>.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="valve">The two-way valve.</param>
    /// <returns>The three-way valve, the leg's port name (<c>a</c> or <c>b</c>) and the branch, when the valve's branch ends at a three-way valve's switched port.</returns>
    private static (ThreeWayValveComponent Valve, string Port, Branch Branch)? OnSwitchedLeg(CircuitGraph graph, ValveComponent valve)
    {
        foreach (var branch in graph.Branches)
        {
            if (!branch.Path.Contains(valve))
            {
                continue;
            }

            foreach (var end in new[] { branch.From, branch.To })
            {
                if (end.Element is ThreeWayValveComponent { BypassConnected: true } three && end.PortName is "a" or "b")
                {
                    return (three, end.PortName, branch);
                }
            }

            return null;
        }

        return null;
    }
}
