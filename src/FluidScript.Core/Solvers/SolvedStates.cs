using System.Collections.Immutable;

using FluidScript.Core.Components;
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
}
