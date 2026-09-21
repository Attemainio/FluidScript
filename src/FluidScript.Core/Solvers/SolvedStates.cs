using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Sizing;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;

namespace FluidScript.Core.Solvers;

/// <summary>One port's solved condition, in SI.</summary>
/// <param name="Node">The graph node the port reads its state from.</param>
/// <param name="Flow">Mass flow, kg/s, positive <em>into</em> the component (<c>22</c>'s convention); zero for a port no branch reaches.</param>
/// <param name="Pressure">Absolute pressure, Pa.</param>
/// <param name="Enthalpy">Specific enthalpy, J/kg.</param>
/// <param name="Temperature">Temperature, K.</param>
/// <param name="Density">Density, kg/m³.</param>
/// <param name="SpecificHeat">Specific heat, J/(kg·K).</param>
public readonly record struct SolvedPort(
    int Node, double Flow, double Pressure, double Enthalpy, double Temperature, double Density, double SpecificHeat);

/// <summary>An extended-mode exchanger at the solution: both sides, the duty and the two routes to its conductance.</summary>
/// <param name="Inlet1">Side 1 entering temperature, K.</param>
/// <param name="Outlet1">Side 1 leaving temperature, K.</param>
/// <param name="Capacity1">Side 1 capacity rate, W/K.</param>
/// <param name="Inlet2">Side 2 entering temperature, K -- the stated profile when the side is not wired.</param>
/// <param name="Outlet2">Side 2 leaving temperature, K.</param>
/// <param name="Capacity2">Side 2 capacity rate, W/K.</param>
/// <param name="Duty">Heat into side 1, W, positive when side 1 gains.</param>
/// <param name="Ntu">Number of transfer units on Cmin.</param>
/// <param name="Effectiveness">ε for the arrangement.</param>
/// <param name="CapacityRatio">Cmin / Cmax.</param>
/// <param name="Lmtd">The log-mean temperature difference, K.</param>
/// <param name="ConductanceByLogMean">|Duty| / Lmtd, W/K -- the validation route.</param>
/// <param name="Approach">The closest approach, K.</param>
/// <param name="Rating">The rating the exchanger was built with.</param>
public sealed record SolvedExchanger(
    double Inlet1,
    double Outlet1,
    double Capacity1,
    double Inlet2,
    double Outlet2,
    double Capacity2,
    double Duty,
    double Ntu,
    double Effectiveness,
    double CapacityRatio,
    double Lmtd,
    double ConductanceByLogMean,
    double Approach,
    ExchangerRating Rating);

/// <summary>Reads a solved state vector back as per-port conditions and per-component solved parameters.</summary>
/// <remarks>
/// <para>
/// The solver stores a flat vector; the contract, the solve report and a hover panel all want the
/// same thing back out of it -- what is at each port, and what the solver was asked to find. This is
/// that reading, done once. It follows <see cref="PortMap"/> for which node and branch a port sees,
/// which is the same map the residuals were evaluated against, so a state reported here is the one
/// the equations were satisfied at and not a second reading of the topology.
/// </para>
/// <para>
/// A property evaluation that fails -- a node solved outside the substance's range -- leaves that port
/// <see langword="null"/>; the caller reports what it can. Nothing here throws on a bad solution.
/// </para>
/// </remarks>
public static class SolvedStates
{
    /// <summary>Every port of every component, indexed as <see cref="CircuitGraph.Components"/> and each component's ports are.</summary>
    /// <param name="graph">The solved graph.</param>
    /// <param name="layout">The unknown layout the solution is in.</param>
    /// <param name="solution">The solved vector.</param>
    /// <param name="system">
    /// The assembled system the solution satisfies, or <see langword="null"/> to read every port from
    /// its node. With it, a port a component discharges through reads the component's own outlet
    /// state rather than the state of the node it discharges into (<c>C-103</c>, see the remarks).
    /// </param>
    /// <returns>Per component, per port, the solved condition or <see langword="null"/> where none could be read.</returns>
    /// <remarks>
    /// <para>
    /// A port touches a node, and the node's state is the <em>mixed</em> state where every stream
    /// arriving there meets. For a port that carries flow into the component that is what the
    /// component sees. For a port it discharges through it is not: a valve passing 50 °C into a node
    /// where a 10 °C return also arrives discharges 50 °C, and the node reads 20 °C. The equations
    /// know the difference -- the node's energy balance takes the stream in as <c>ṁ·h_in + injection</c>
    /// -- and this reading reconstructs it: the outlet's enthalpy is its inlet's plus the component's
    /// energy injection at that port over the flow, at the node's pressure. It applies where the
    /// outlet has exactly one inlet in its flow group; a port whose group mixes several inflows (a
    /// mixing valve's common port, a vessel with two returns) keeps the node's state, which is the
    /// mixed state the component itself produces.
    /// </para>
    /// </remarks>
    public static ImmutableArray<ImmutableArray<SolvedPort?>> Ports(
        CircuitGraph graph, SystemLayout layout, StateVector solution, EquationSystem? system = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(solution);

        var map = PortMap.Build(graph);
        var values = solution.Values;
        var nodes = new SolvedPort?[graph.Nodes.Length];

        for (var node = 0; node < graph.Nodes.Length; node++)
        {
            var pressure = layout.NodePressure(node);
            var enthalpy = layout.NodeEnthalpy(node);

            if (pressure >= values.Length || enthalpy >= values.Length)
            {
                continue;
            }

            var state = graph.Substance.FromPressureEnthalpy(
                Quantity.FromSi(values[pressure], Dimension.Pressure),
                Quantity.FromSi(values[enthalpy], Dimension.Enthalpy));

            if (state.IsSuccess)
            {
                nodes[node] = new SolvedPort(
                    node,
                    0,
                    values[pressure],
                    values[enthalpy],
                    state.Value.Temperature.SiValue,
                    state.Value.Density.SiValue,
                    state.Value.SpecificHeat.SiValue);
            }
        }

        var result = ImmutableArray.CreateBuilder<ImmutableArray<SolvedPort?>>(graph.Components.Length);

        for (var component = 0; component < graph.Components.Length; component++)
        {
            var ports = ImmutableArray.CreateBuilder<SolvedPort?>(map.PortCount(component));

            // A node's own ports read the node's own state; the map points them at what is attached.
            var own = -1;

            if (graph.Components[component] is CircuitNode)
            {
                for (var node = 0; node < graph.Nodes.Length; node++)
                {
                    if (ReferenceEquals(graph.Nodes[node].Component, graph.Components[component]))
                    {
                        own = node;
                        break;
                    }
                }
            }

            for (var port = 0; port < map.PortCount(component); port++)
            {
                var binding = map[component, port];
                var node = own >= 0 ? own : binding.Node;

                if (node < 0 || nodes[node] is not { } at)
                {
                    ports.Add(null);
                    continue;
                }

                var column = binding.CarriesFlow ? layout.BranchFlow(binding.Branch) : -1;
                var flow = column >= 0 && column < values.Length ? values[column] * binding.Sign : 0;

                ports.Add(at with { Flow = flow });
            }

            if (system is not null && own < 0 && values.Length == system.Columns)
            {
                Discharge(graph, system, component, values.AsSpan(), ports);
            }

            result.Add(ports.ToImmutable());
        }

        return result.ToImmutable();
    }

    /// <summary>Gives each port the component discharges through its own outlet state (<c>C-103</c>).</summary>
    /// <param name="graph">The solved graph.</param>
    /// <param name="system">The assembled system, whose injection is read at the solution.</param>
    /// <param name="element">The component's index.</param>
    /// <param name="values">The solved vector.</param>
    /// <param name="ports">The component's ports as read from their nodes; outflow entries are rewritten in place.</param>
    private static void Discharge(
        CircuitGraph graph, EquationSystem system, int element, ReadOnlySpan<double> values, ImmutableArray<SolvedPort?>.Builder ports)
    {
        var component = graph.Components[element];
        var groups = component.FlowGroups;

        if (groups.Length != ports.Count)
        {
            return;
        }

        var injection = new double[ports.Count];

        if (!system.TryEvaluateInjection(values, element, injection))
        {
            return;
        }

        for (var outlet = 0; outlet < ports.Count; outlet++)
        {
            if (ports[outlet] is not { Flow: < 0 } at)
            {
                continue;
            }

            SolvedPort? inlet = null;
            var inlets = 0;

            for (var port = 0; port < ports.Count; port++)
            {
                if (groups[port] == groups[outlet] && ports[port] is { Flow: > 0 } candidate)
                {
                    inlet = candidate;
                    inlets++;
                }
            }

            if (inlets != 1 || inlet is not { } from)
            {
                continue;
            }

            var enthalpy = from.Enthalpy + (injection[outlet] / -at.Flow);
            var state = graph.Substance.FromPressureEnthalpy(
                Quantity.FromSi(at.Pressure, Dimension.Pressure),
                Quantity.FromSi(enthalpy, Dimension.Enthalpy));

            if (state.IsSuccess)
            {
                ports[outlet] = at with
                {
                    Enthalpy = enthalpy,
                    Temperature = state.Value.Temperature.SiValue,
                    Density = state.Value.Density.SiValue,
                    SpecificHeat = state.Value.SpecificHeat.SiValue,
                };
            }
        }
    }


    /// <summary>The solved value of a parameter the solver was asked to find, SI.</summary>
    /// <param name="layout">The unknown layout.</param>
    /// <param name="solution">The solved vector.</param>
    /// <param name="component">The owning component.</param>
    /// <param name="parameter">The parameter name, as the promotion labelled it.</param>
    /// <returns>The value, or <see langword="null"/> when nothing was promoted for it.</returns>
    public static double? Parameter(SystemLayout layout, StateVector solution, string component, string parameter)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(solution);

        foreach (var unknown in layout.Unknowns)
        {
            if (unknown.Kind == UnknownKind.Parameter
                && string.Equals(unknown.OwnerComponentId, component, StringComparison.Ordinal)
                && unknown.Name.EndsWith("." + parameter, StringComparison.Ordinal)
                && unknown.Index < solution.Values.Length)
            {
                return solution.Values[unknown.Index];
            }
        }

        return null;
    }

    /// <summary>Every promoted parameter of one component, SI, in unknown order.</summary>
    /// <param name="layout">The unknown layout.</param>
    /// <param name="solution">The solved vector.</param>
    /// <param name="component">The owning component.</param>
    /// <returns>Parameter name to value.</returns>
    public static ImmutableArray<(string Parameter, double Value, string SiUnit)> Parameters(
        SystemLayout layout, StateVector solution, string component)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(solution);

        var result = ImmutableArray.CreateBuilder<(string, double, string)>();

        foreach (var unknown in layout.Unknowns)
        {
            if (unknown.Kind == UnknownKind.Parameter
                && string.Equals(unknown.OwnerComponentId, component, StringComparison.Ordinal)
                && unknown.Index < solution.Values.Length)
            {
                var dot = unknown.Name.LastIndexOf('.');
                result.Add((dot >= 0 ? unknown.Name[(dot + 1)..] : unknown.Name, solution.Values[unknown.Index], unknown.SiUnit));
            }
        }

        return result.ToImmutable();
    }

    /// <summary>An exchanger's two sides, duty and rating figures at a solution, by the ε-NTU route the residuals use.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">The unknown layout.</param>
    /// <param name="solution">The solved vector.</param>
    /// <param name="index">The exchanger's index in <see cref="CircuitGraph.Components"/>.</param>
    /// <returns>The figures, or <see langword="null"/> for a Duty exchanger, one not yet rated, or a side whose state cannot be read.</returns>
    /// <remarks>
    /// A rated exchanger's second side is the stated profile -- its entering temperature and capacity
    /// rate -- and its leaving temperature is <c>inlet2 − duty / capacity2</c>: the number a script reads
    /// as <c>HE1.out[2].t</c> and the report prints as the rating. Shared between the two so they cannot
    /// disagree.
    /// </remarks>
    public static SolvedExchanger? Exchanger(CircuitGraph graph, SystemLayout layout, StateVector solution, int index)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(solution);

        if (index < 0 || index >= graph.Components.Length
            || graph.Components[index] is not HeatExchanger { Rating: { CanRate: true } rating } exchanger
            || Side(graph, layout, solution, index, exchanger, side: 1) is not { } one)
        {
            return null;
        }

        double inlet2, capacity2;

        if (exchanger.SecondarySideConnected)
        {
            if (Side(graph, layout, solution, index, exchanger, side: 2) is not { } two)
            {
                return null;
            }

            (inlet2, capacity2) = two;
        }
        else
        {
            (inlet2, capacity2) = (rating.SecondaryInletTemperature, rating.SecondaryCapacityRate);
        }

        if (capacity2 <= 0)
        {
            return null;
        }

        var duty = HeatExchanger.Duty(rating, one.Capacity, one.Inlet, capacity2, inlet2);
        var minimum = Math.Min(one.Capacity, capacity2);
        var ratio = minimum / Math.Max(one.Capacity, capacity2);
        var ntu = rating.Conductance / minimum;
        var effectiveness = Effectiveness.Of(ntu, ratio, rating.Arrangement);
        var outlet1 = one.Inlet + (duty / one.Capacity);
        var outlet2 = inlet2 - (duty / capacity2);
        var hotSide1 = one.Inlet >= inlet2;
        var (hotIn, hotOut, coldIn, coldOut) = hotSide1
            ? (one.Inlet, outlet1, inlet2, outlet2)
            : (inlet2, outlet2, one.Inlet, outlet1);
        var lmtd = rating.Arrangement == ExchangerArrangement.Parallel
            ? LogMeanTemperatureDifference.Parallel(hotIn, hotOut, coldIn, coldOut)
            : LogMeanTemperatureDifference.Counterflow(hotIn, hotOut, coldIn, coldOut);
        var byLogMean = LogMeanTemperatureDifference.Conductance(Math.Abs(duty), lmtd);
        var approach = rating.Arrangement == ExchangerArrangement.Parallel
            ? hotOut - coldOut
            : Math.Min(hotIn - coldOut, hotOut - coldIn);

        return new SolvedExchanger(
            one.Inlet, outlet1, one.Capacity, inlet2, outlet2, capacity2, duty, ntu, effectiveness, ratio, lmtd, byLogMean, approach, rating);
    }

    /// <summary>One side of an exchanger as the solution left it: where it enters and what it carries.</summary>
    /// <returns>K and W/K, or <see langword="null"/> when the side is not wired or its state cannot be read.</returns>
    /// <remarks>
    /// The inlet is the port the solved flow arrives by, not the port named <c>in</c>: a branch's path
    /// direction and its solved sign together say which end the stream enters at.
    /// </remarks>
    private static (double Inlet, double Capacity)? Side(
        CircuitGraph graph, SystemLayout layout, StateVector solution, int index, HeatExchanger exchanger, int side)
    {
        var branch = graph.Branches.FirstOrDefault(
            candidate => candidate.Path.Contains(exchanger) && BranchFlows.Side(graph, candidate, exchanger) == side);

        if (branch is null)
        {
            return null;
        }

        var position = branch.Path.IndexOf(exchanger);
        var before = position > 0 ? branch.Path[position - 1] : branch.From.Element;
        var arrival = graph.Components.IndexOf(before);
        var first = side == 1 ? 0 : 2;
        var arrivalPort = graph.Adjacency.Peer(index, first).Component == arrival ? first : first + 1;
        var flow = solution.Values[layout.BranchFlow(branch.Index)];
        var inletPort = flow >= 0 ? arrivalPort : (arrivalPort == first ? first + 1 : first);
        var peer = graph.Adjacency.Peer(index, inletPort);

        if (!peer.Exists)
        {
            return null;
        }

        var node = -1;

        for (var candidate = 0; candidate < graph.Nodes.Length; candidate++)
        {
            if (ReferenceEquals(graph.Nodes[candidate].Component, graph.Components[peer.Component]))
            {
                node = candidate;
                break;
            }
        }

        if (node < 0)
        {
            return null;
        }

        var state = graph.Substance.FromPressureEnthalpy(
            Quantity.FromSi(solution.Values[layout.NodePressure(node)], Dimension.Pressure),
            Quantity.FromSi(solution.Values[layout.NodeEnthalpy(node)], Dimension.Enthalpy));

        if (!state.IsSuccess)
        {
            return null;
        }

        var capacity = Math.Abs(flow) * state.Value.SpecificHeat.SiValue;

        return capacity > 0 ? (state.Value.Temperature.SiValue, capacity) : null;
    }
}
