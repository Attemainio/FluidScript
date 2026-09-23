using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Solvers.Equations;

public sealed partial class EquationSystem
{
    /// <summary>Assembles the system of a lowered graph.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="posedness">The counting table and the hydraulic partition.</param>
    /// <param name="seed">The starting iterate, whose flow magnitudes set the flow scales.</param>
    /// <returns>The assembled system.</returns>
    public static EquationSystem Build(CircuitGraph graph, WellPosednessResult posedness, StateVector seed)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(posedness);
        ArgumentNullException.ThrowIfNull(seed);

        var unknowns = SystemLayout.Build(graph, posedness.Counting);
        var equations = EquationLayout.Build(graph, posedness);
        var ports = PortMap.Build(graph);
        var unknownScales = FluidScript.Core.Solvers.Equations.UnknownScales.Build(unknowns, seed);
        var residualScales = FluidScript.Core.Solvers.Equations.ResidualScales.Build(
            graph, equations, ports, unknownScales, unknowns);

        var nodeOf = new int[graph.Components.Length];
        var byComponent = new Dictionary<object, int>(graph.Nodes.Length, ReferenceEqualityComparer.Instance);

        Array.Fill(nodeOf, -1);

        for (var node = 0; node < graph.Nodes.Length; node++)
        {
            byComponent[graph.Nodes[node].Component] = node;
        }

        for (var element = 0; element < graph.Components.Length; element++)
        {
            if (byComponent.TryGetValue(graph.Components[element], out var node))
            {
                nodeOf[element] = node;
            }
        }

        var energyRow = new int[graph.Nodes.Length];
        var arriving = new ArrivingSource[graph.Components.Length][][];

        for (var element = 0; element < graph.Components.Length; element++)
        {
            if (graph.Components[element] is not CircuitNode node)
            {
                arriving[element] = [];
                continue;
            }

            energyRow[nodeOf[element]] =
                equations.Row(element, node.CarriesMassBalance ? 1 : 0);

            arriving[element] = new ArrivingSource[node.Ports.Length][];

            for (var port = 0; port < node.Ports.Length; port++)
            {
                arriving[element][port] = Sources(graph, ports, byComponent, element, port);
            }
        }

        var stated = new List<(int, double)>(posedness.Counting.PressureNodes.Length);

        foreach (var node in posedness.Counting.PressureNodes)
        {
            stated.Add((
                byComponent[node.Component],
                HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure) ?? 0));
        }

        var datums = new List<int>(posedness.Counting.DatumComponents.Length);

        foreach (var hydraulic in posedness.Counting.DatumComponents)
        {
            var datum = graph.Nodes.FirstOrDefault(candidate => candidate.Name == hydraulic.Datum);

            datums.Add(datum is null ? -1 : byComponent[datum.Component]);
        }

        // D-70: a bare connection spans two heights like a pipe does, so its row carries the rise.
        var links = posedness.Counting.IdealLinks
            .Select(link => (
                byComponent[link.From.Component],
                byComponent[link.To.Component],
                Height(link.To.Component) - Height(link.From.Component)))
            .ToArray();

        // Where each component's own unknowns sit, walked in the order WellPosedness gathered them --
        // graph order over the non-node components. Two walks of one list, and they have to agree.
        var owned = new (int Offset, int Count)[graph.Components.Length];
        var running = 0;

        for (var element = 0; element < graph.Components.Length; element++)
        {
            if (graph.Components[element] is CircuitNode)
            {
                continue;
            }

            var count = graph.Components[element].DeclareUnknowns().Length;

            owned[element] = (running, count);
            running += count;
        }

        // An external mass flux enters a node's own balances, and until it does the column influences
        // nothing and the Jacobian is singular at it. The enthalpy it carries is settled once, here: a
        // boundary stating a temperature delivers fluid at that temperature, and one that does not is a
        // return, whose stream leaves carrying whatever the node holds.
        //
        // A *stated* flow is the same term with no column behind it (`S-22`). Well-posedness leaves it
        // out of `FluxNodes` because it declares no unknown, which is right for the count and was wrong
        // for the residuals: the flux then entered no equation at all, so `m4-storage-header` -- whose
        // every boundary is a stated flow -- had a circuit at rest as its exact solution, and reported
        // convergence for it.
        var fluxes = new List<(int Node, int MassRow, int Column, double Magnitude, double Enthalpy, bool Known)>(
            posedness.Counting.FluxNodes.Length);

        foreach (var boundary in graph.Nodes)
        {
            var column = posedness.Counting.FluxNodes.IndexOf(boundary);
            var given = HydraulicPartition.Stated(boundary.Component, HydraulicPartition.Flow);

            if (column < 0 && (given is null || !boundary.Component.CarriesMassBalance))
            {
                continue;
            }

            var element = Array.IndexOf([.. graph.Components], (IFlowComponent)boundary.Component);
            var temperature = HydraulicPartition.Stated(boundary.Component, HydraulicPartition.Temperature);
            var enthalpy = 0.0;
            var known = false;

            if (temperature is not null)
            {
                var state = graph.Substance.FromPressureTemperature(
                    Quantity.FromSi(
                        HydraulicPartition.Stated(boundary.Component, HydraulicPartition.Pressure) ?? 0,
                        Dimension.Pressure),
                    Quantity.FromSi(temperature.Value, Dimension.Temperature));

                if (state.TryGetValue(out var fluid))
                {
                    enthalpy = fluid.Enthalpy.SiValue;
                    known = true;
                }
            }

            fluxes.Add((
                byComponent[boundary.Component],
                equations.Row(element, 0),
                column < 0 ? -1 : unknowns.ExternalFluxOffset + column,
                boundary.Component.Boundary is BoundaryRole.Outlet ? -(given ?? 0) : given ?? 0,
                enthalpy,
                known));
        }

        // Every component gets a parameter buffer whether or not anything promotes into it, holding
        // what it would have used itself. That is what lets a residual read `context.Parameter` with no
        // idea whether the number came from the solve or from its own constructor.
        var parameters = new double[graph.Components.Length][];

        for (var element = 0; element < graph.Components.Length; element++)
        {
            var resolvable = graph.Components[element].Resolvable;

            parameters[element] = new double[resolvable.Length];

            for (var slot = 0; slot < resolvable.Length; slot++)
            {
                parameters[element][slot] = resolvable[slot].Value;
            }
        }

        var promoted = new List<(int Element, int Slot, int Column, string? Holds)>(posedness.Counting.Promotions.Length);
        var promotedRow = new List<int>(posedness.Counting.Promotions.Length);

        for (var index = 0; index < posedness.Counting.Promotions.Length; index++)
        {
            var promotion = posedness.Counting.Promotions[index];
            var element = Array.FindIndex(
                [.. graph.Components],
                candidate => string.Equals(candidate.Name, promotion.Component, StringComparison.Ordinal));

            if (element < 0)
            {
                continue;
            }

            // A head promoted to hold a switched-off coil's branch at zero flow is the shut check valve
            // the plant does not have, and a check valve's drop has no lower bound (`S-56`).
            var holds = promotion.Constraint.Kind is ConstraintKind.FixedFlow
                && string.Equals(promotion.Parameter, "head", StringComparison.Ordinal)
                && graph.Components.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, promotion.Constraint.Component, StringComparison.Ordinal))
                    is { } pinned
                && WellPosedness.ZeroDuty(pinned)
                    ? pinned.Name
                    : null;

            var resolvable = graph.Components[element].Resolvable;

            for (var slot = 0; slot < resolvable.Length; slot++)
            {
                if (string.Equals(resolvable[slot].Name, promotion.Parameter, StringComparison.Ordinal))
                {
                    promoted.Add((element, slot, unknowns.PromotionOffset + index, holds));

                    var constraintIndex = posedness.Counting.Constraints.IndexOf(promotion.Constraint);

                    promotedRow.Add(constraintIndex < 0 ? -1 : equations.ConstraintOffset + constraintIndex);

                    break;
                }
            }
        }

        var constraints = Constraints(graph, posedness, equations, ports, byComponent, unknowns, seed);

        return new EquationSystem(
            graph, unknowns, equations, ports, unknownScales, residualScales,
            nodeOf, energyRow, arriving, [.. stated], [.. datums], links, owned, [.. fluxes],
            parameters, [.. promoted], constraints, Stagnant(graph, ports, byComponent, constraints), [.. promotedRow]);
    }

    /// <summary>A node's density at the seed, for scaling a volume-flow row.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="unknowns">The unknown layout.</param>
    /// <param name="seed">The starting iterate.</param>
    /// <param name="node">The node.</param>
    /// <returns>kg/m³, or NaN when the seed state cannot be read.</returns>
    private static double SeedDensity(CircuitGraph graph, SystemLayout unknowns, StateVector seed, int node)
    {
        var pressure = unknowns.NodePressure(node);
        var enthalpy = unknowns.NodeEnthalpy(node);

        if (pressure >= seed.Values.Length || enthalpy >= seed.Values.Length)
        {
            return double.NaN;
        }

        var state = graph.Substance.FromPressureEnthalpy(
            FluidScript.Core.Physics.Units.Quantity.FromSi(seed.Values[pressure], FluidScript.Core.Physics.Units.Dimension.Pressure),
            FluidScript.Core.Physics.Units.Quantity.FromSi(seed.Values[enthalpy], FluidScript.Core.Physics.Units.Dimension.Enthalpy));

        return state.IsSuccess ? state.Value.Density.SiValue : double.NaN;
    }

    /// <summary>The temperature span a flow pin was written with: <c>out</c> less <c>in</c>, or <c>dt</c> itself.</summary>
    /// <param name="exchanger">The exchanger.</param>
    /// <param name="parameter">The pinning parameter: <c>out</c>, <c>out2</c>, <c>dt</c> or <c>dt2</c>.</param>
    /// <returns>K, a magnitude; zero when the pair is not both stated.</returns>
    private static double SideSpan(HeatExchanger exchanger, string parameter)
    {
        if (parameter is "dt" or "dt2")
        {
            return Math.Abs(HydraulicPartition.Stated(exchanger, parameter) ?? 0);
        }

        var suffix = parameter.EndsWith('2') ? "2" : string.Empty;

        return HydraulicPartition.Stated(exchanger, "in" + suffix) is { } inlet
            && HydraulicPartition.Stated(exchanger, "out" + suffix) is { } outlet
                ? Math.Abs(outlet - inlet)
                : 0;
    }

    /// <summary>The node a named port of one component attaches to.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="ports">Which node each component port attaches to.</param>
    /// <param name="element">The component's index in the graph.</param>
    /// <param name="name">The port's name.</param>
    /// <returns>The node's index among the graph's nodes, or −1 when the port has none.</returns>
    private static int Attached(CircuitGraph graph, PortMap ports, int element, string name)
    {
        var declared = graph.Components[element].Ports;

        for (var port = 0; port < declared.Length; port++)
        {
            if (string.Equals(declared[port].Name, name, StringComparison.Ordinal))
            {
                return ports[element, port].Node;
            }
        }

        return -1;
    }

    /// <summary>Which nodes can deliver enthalpy to one node port.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="ports">The port map.</param>
    /// <param name="byComponent">Each node's index, by the component carrying its unknowns.</param>
    /// <param name="element">The node's index in the graph.</param>
    /// <param name="port">The port.</param>
    /// <returns>One entry per node that can, empty when nothing is attached.</returns>
    private static ArrivingSource[] Sources(
        CircuitGraph graph,
        PortMap ports,
        Dictionary<object, int> byComponent,
        int element,
        int port)
    {
        var peer = graph.Adjacency.Peer(element, port);

        if (!peer.Exists)
        {
            return [];
        }

        var attached = graph.Components[peer.Component];

        if (byComponent.TryGetValue(attached, out var direct))
        {
            // A node wired straight to a node: the ideal link, which carries gravity's share of the
            // enthalpy itself because there is no component between them to inject it (D-70).
            var lift = graph.Components[element] is CircuitNode here && attached is CircuitNode there
                ? Hydrostatic.Lift(here.Elevation - there.Elevation)
                : 0;

            return [new ArrivingSource(direct, peer.Component, peer.Port, lift)];
        }

        var groups = attached.FlowGroups;
        var sources = new List<ArrivingSource>(groups.Length);

        for (var candidate = 0; candidate < groups.Length; candidate++)
        {
            if (candidate == peer.Port || groups[candidate] != groups[peer.Port])
            {
                continue;
            }

            var far = ports[peer.Component, candidate].Node;

            if (far >= 0)
            {
                sources.Add(new ArrivingSource(far, peer.Component, candidate));
            }
        }

        return [.. sources];
    }

    /// <summary>A node's height above the project datum, for the links between nodes.</summary>
    /// <param name="component">The node's component.</param>
    /// <returns>m; 0 for anything that is not a <see cref="CircuitNode"/>.</returns>
    private static double Height(IFlowComponent component) =>
        component is CircuitNode node ? node.Elevation : 0;
}
