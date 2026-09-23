using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Topology.Counting;

public static partial class WellPosedness
{
    // ---- constraints and promotion -----------------------------------------------------------------

    /// <summary>Every stated parameter the circuit must satisfy rather than merely read.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="hydraulics">Its hydraulic partition.</param>
    /// <returns>The constraints, in graph order then parameter order.</returns>
    /// <remarks>
    /// <para>
    /// <strong>A stated value is a constraint only when the circuit has to work to meet it.</strong>
    /// <c>HE1 power=30</c> is a coefficient in an energy balance that already exists; <c>HE1 out=50</c>
    /// is a demand on a node enthalpy the balance also determines, and the two together fix a flow the
    /// hydraulics determine as well. That collision is the whole subject of this pass.
    /// </para>
    /// <para>
    /// <strong>A temperature on a boundary node is not a constraint.</strong> <c>N1 t=6 p=300</c> states
    /// what enters the model there; there is no upstream enthalpy for it to contradict. The same
    /// <c>t</c> on a node with no external mass is a demand on a temperature the circuit computes, and
    /// something has to move to meet it.
    /// </para>
    /// <para>
    /// <strong>Nor are a coupled or rated exchanger's terminal temperatures.</strong> Once both sides are
    /// wired, or a second-side profile is stated, <c>in</c>, <c>out</c>, <c>in2</c> and <c>out2</c> are the
    /// <em>rating design point</em> that <c>24</c> sizes UA from (<c>D-19</c>), not demands on the solved
    /// state. Counting them as constraints reports the substation over-specified by three, on the
    /// reference circuit written to demonstrate that two circuits can be solved together. A rated
    /// exchanger that cannot rate -- no size, or a profile too thin to fix its second side -- delivers
    /// its stated duty and is counted exactly as a Duty one.
    /// </para>
    /// </remarks>

    private static ImmutableArray<ComponentConstraint> Constraints(
        CircuitGraph graph, ImmutableArray<HydraulicComponent> hydraulics)
    {
        var constraints = ImmutableArray.CreateBuilder<ComponentConstraint>();

        // In this order, because `Promote` is first come: a terminal's rows before any stated flow's, and a
        // coupled design point last, where it pins only what nothing above already did.
        TerminalConstraints(graph, hydraulics, constraints);
        StatedFlowConstraints(graph, hydraulics, constraints);
        DesignPointConstraints(graph, hydraulics, constraints);

        return constraints.ToImmutable();
    }

    /// <summary>A node's stated temperature, and an exchanger's stated inlet, outlet or change: a setpoint, a demand on its split, a flow pin or the circuit's enthalpy level.</summary>
    private static void TerminalConstraints(
        CircuitGraph graph, ImmutableArray<HydraulicComponent> hydraulics, ImmutableArray<ComponentConstraint>.Builder constraints)
    {
        // Which components have already had their dropped enthalpy level paid for (`D-90`). A level pays
        // for exactly one statement, so the first lone terminal in each takes it and the rest are flow
        // pins as before.
        var levelled = new HashSet<int>();

        foreach (var element in graph.Components)
        {
            var hydraulic = Owner(hydraulics, element);

            if (element is NodeComponent)
            {
                if (HydraulicPartition.Stated(element, HydraulicPartition.Temperature) is not null
                    && HydraulicPartition.Stated(element, HydraulicPartition.Pressure) is null
                    && HydraulicPartition.Stated(element, HydraulicPartition.Flow) is null)
                {
                    constraints.Add(new ComponentConstraint(
                        element.Name,
                        HydraulicPartition.Temperature,
                        ConstraintKind.NodeTemperature,
                        hydraulic,
                        Spelled(element, HydraulicPartition.Temperature)));
                }

                continue;
            }

            if (element is not HeatExchangerComponent || Rates(hydraulics, element))
            {
                continue;
            }

            // A stated inlet is a demand on the mix feeding the coil -- but only while the coil takes
            // something. At zero duty there is nothing to deliver 50 °C to: no flow crosses the coil, the
            // node the split feeds is whatever the header leaves there, and a row asking the split to
            // hold it is 0/0 (`S-56`). `in=` on a coil that is off is documentation of its design point,
            // not a constraint, and adds neither a row nor the promotion that would answer it.
            var off = ZeroDuty(element);

            foreach (var parameter in Inlets)
            {
                if (!off && HydraulicPartition.Stated(element, parameter) is not null)
                {
                    constraints.Add(new ComponentConstraint(
                        element.Name, parameter, ConstraintKind.MixedInlet, hydraulic, Spelled(element, parameter)));
                }
            }

            // A terminal pins a flow only when the other end of the same side is known too. With `in` and
            // `out` both stated, `power` gives m = Q/(h_out - h_in) and the flow follows; with `out`
            // alone that is one equation in two unknowns and pins nothing -- but it does fix an absolute
            // temperature, which is precisely what a closed circuit's dropped level needs. An open
            // circuit takes its inlet from a boundary, so the inlet is known without being stated and a
            // lone `out` pins the flow there exactly as before.
            var owed = hydraulics.FirstOrDefault(candidate => candidate.Index == hydraulic) is { } block
                && NeedsEnthalpyLevel(graph, hydraulics, block);

            foreach (var parameter in FlowPins)
            {
                if (HydraulicPartition.Stated(element, parameter) is null)
                {
                    continue;
                }

                var pays = owed
                    && Partner(parameter) is { } partner
                    && HydraulicPartition.Stated(element, partner) is null
                    && levelled.Add(hydraulic);

                constraints.Add(new ComponentConstraint(
                    element.Name,
                    parameter,
                    pays ? ConstraintKind.EnthalpyLevel : ConstraintKind.FixedFlow,
                    hydraulic,
                    Spelled(element, parameter)));
            }
        }

    }

    /// <summary>A stated flow is a constraint (<c>D-120</c>, <c>S-72</c>).</summary>
    /// <remarks>
    /// <c>flow</c> on an exchanger's side or on a pump pins that branch at the number, and <c>vflow</c> pins
    /// it at that volume flow times the density at the side's inlet, which only the solve knows. Until
    /// P5.13b, <c>HE1 flow=0.3</c> set the flow its 20 kPa was measured at and <c>PU1 flow=0.3</c> its
    /// curve's duty point, and the circuit solved to whatever the loop drop gave: 0.086 kg/s on the simple
    /// loop, then out of the fluid's range. A pump whose head is stated too is describing its curve, not
    /// pinning the circuit (the head and the duty flow give the curve through that point), and adds no
    /// row. A node's <c>flow</c> is a boundary flux, not this (<c>D-64</c>).
    /// </remarks>
    private static void StatedFlowConstraints(
        CircuitGraph graph, ImmutableArray<HydraulicComponent> hydraulics, ImmutableArray<ComponentConstraint>.Builder constraints)
    {
        foreach (var element in graph.Components)
        {
            var hydraulic = Owner(hydraulics, element);

            foreach (var parameter in StatedFlows)
            {
                if (HydraulicPartition.Stated(element, parameter) is null)
                {
                    continue;
                }

                var pins = element switch
                {
                    HeatExchangerComponent => true,
                    PumpComponent pump => HydraulicPartition.Stated(pump, "head") is null && pump.StatedRise is null,
                    _ => false,
                };

                // Side 2 runs in its own hydraulic when the exchanger couples two.
                var side = parameter.EndsWith('2') && element is HeatExchangerComponent
                    ? SideHydraulic(graph, hydraulics, element, 2)
                    : hydraulic;

                if (pins && side is { } owned)
                {
                    constraints.Add(new ComponentConstraint(element.Name, parameter, ConstraintKind.FixedFlow, owned, Spelled(element, parameter)));
                }
            }
        }

    }

    /// <summary>A coupled or rated exchanger's design point pins a side nothing else pins (<c>D-97</c>).</summary>
    /// <remarks>
    /// The design point is what sizes UA, and it also says what each side runs at: <c>power</c> with
    /// <c>in2</c>/<c>out2</c> is a flow on the side-2 branch as surely as <c>LOAD.dt</c> is one on the
    /// secondary. It pins a side only where nothing else already does -- the substation's secondary is
    /// pinned by <c>LOAD.dt</c>, its primary by <c>HX1</c>'s 85/45 -- because two pins on one hydraulic
    /// are one constraint too many, and the exchanger's is the one a designer would drop. A rated
    /// exchanger has the same design point and one wired side, so side 2 finds no hydraulic and side 1
    /// is pinned by the same rule.
    /// </remarks>
    private static void DesignPointConstraints(
        CircuitGraph graph, ImmutableArray<HydraulicComponent> hydraulics, ImmutableArray<ComponentConstraint>.Builder constraints)
    {
        foreach (var element in graph.Components)
        {
            if (element is not HeatExchangerComponent || !Rates(hydraulics, element))
            {
                continue;
            }
            foreach (var (inlet, outlet, change, port) in CoupledSides)
            {
                var pin = HydraulicPartition.Stated(element, outlet) is not null
                    && HydraulicPartition.Stated(element, inlet) is not null
                        ? outlet
                        : HydraulicPartition.Stated(element, change) is not null ? change : null;

                if (pin is null || SideHydraulic(graph, hydraulics, element, port) is not { } side)
                {
                    continue;
                }

                var pinned = constraints.Any(existing =>
                    existing.Hydraulic == side
                    && existing.Kind is ConstraintKind.FixedFlow or ConstraintKind.EnthalpyLevel)
                    || hydraulics[side].Boundaries.Any(static boundary =>
                        HydraulicPartition.Stated(boundary.Component, HydraulicPartition.Flow) is not null);

                if (!pinned)
                {
                    constraints.Add(new ComponentConstraint(element.Name, pin, ConstraintKind.FixedFlow, side, Spelled(element, pin)));
                }
            }
        }
    }

    /// <summary>A parameter key as the script spells it on this element's kind: <c>out2</c> as <c>out[2].t</c> (<c>D-120</c>, <c>L-56</c>).</summary>
    /// <param name="element">The element that states it.</param>
    /// <param name="key">The model key.</param>
    /// <returns>The script spelling, or the key when the kind is not in the registry or spells it the same.</returns>
    private static string Spelled(IComponent element, string key) =>
        ComponentRegistry.Default.ByKeyword(element.Kind)?.ParameterName(key) ?? key;

    /// <summary>The hydraulic component one side of a coupled exchanger runs in.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="hydraulics">The partition.</param>
    /// <param name="element">The exchanger.</param>
    /// <param name="port">The side's inlet port index: 0 for side 1, 2 for side 2.</param>
    /// <returns>The hydraulic's index, or <see langword="null"/> when the port is not connected.</returns>
    /// <remarks>
    /// The exchanger itself belongs to both hydraulics, so its membership says nothing; the element on the
    /// other end of the port -- a node, by rule I2 -- belongs to exactly one.
    /// </remarks>
    private static int? SideHydraulic(
        CircuitGraph graph, ImmutableArray<HydraulicComponent> hydraulics, IFlowComponent element, int port)
    {
        var index = graph.Components.IndexOf(element);

        if (index < 0)
        {
            return null;
        }

        var peer = graph.Adjacency.Peer(index, port);

        if (!peer.Exists)
        {
            return null;
        }

        var neighbour = graph.Components[peer.Component];

        foreach (var hydraulic in hydraulics)
        {
            if (hydraulic.Elements.Contains(neighbour))
            {
                return hydraulic.Index;
            }
        }

        return null;
    }

    /// <summary>The terminal at the other end of the same side, whose value makes a flow computable.</summary>
    /// <param name="parameter">A terminal or difference parameter.</param>
    /// <returns>
    /// The partner terminal, or <see langword="null"/> for a parameter that is already a difference and so
    /// has none -- <c>dt</c> states the rise outright and needs no second temperature to pin a flow.
    /// </returns>
    private static string? Partner(string parameter) => parameter switch
    {
        "out" => "in",
        "out2" => "in2",
        _ => null,
    };

    /// <summary>Whether both of a component's sides carry flow.</summary>
    /// <param name="hydraulics">The hydraulic partition.</param>
    /// <param name="element">The component to classify.</param>
    /// <returns><see langword="true"/> when it belongs to more than one hydraulic component.</returns>
    /// <remarks>
    /// <strong>Read from the partition rather than from a mode field</strong>, because the mode is
    /// computed from what the script connected and there is no <c>mode=</c> parameter to read
    /// (<c>D-19</c>). A component in two hydraulic components is one whose second flow group is wired,
    /// which is exactly the condition <c>Coupled</c> names.
    /// </remarks>
    private static bool IsCoupled(
        ImmutableArray<HydraulicComponent> hydraulics, IFlowComponent element)
    {
        var sides = 0;

        foreach (var hydraulic in hydraulics)
        {
            if (hydraulic.Elements.Contains(element))
            {
                sides++;
            }
        }

        return sides > 1;
    }
}
