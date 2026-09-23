using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Model.Contract;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Passes;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Model;

public static partial class ModelContractBuilder
{
    private static Dictionary<string, ParameterWire> Parameters(
        IComponent component,
        ComponentKindInfo? kind,
        OuterLoopResult? run,
        SystemLayout? layout,
        ImmutableArray<Diagnostic>.Builder raised)
    {
        var parameters = new Dictionary<string, ParameterWire>(StringComparer.Ordinal);

        // Stated outranks solved outranks sized outranks default: the factory's own precedence, with
        // the solver's answer above the overlay because a promoted Kv's overlay entry is only its seed.
        // Ordinal order on the wire so two builds of one model agree.
        foreach (var (name, quantity) in component.DefaultParameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var (value, unit) = Canonical(quantity.SiValue, quantity.Dimension, component.Name, name, raised);
            parameters[name] = new ParameterWire
            {
                Value = value,
                Unit = unit,
                Source = "default",
                Basis = kind?.Parameters.GetValueOrDefault(name)?.DefaultBasis ?? "the registry's default",
            };
        }

        foreach (var (name, quantity) in component.SizedParameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var (value, unit) = Canonical(quantity.SiValue, quantity.Dimension, component.Name, name, raised);
            parameters[name] = new ParameterWire
            {
                Value = value,
                Unit = unit,
                Source = "sized",
                Basis = run?.Bases.GetValueOrDefault(Ownership.Key(component.Name, name)) ?? "chosen by a sizing rule",
            };
        }

        // A parameter the solver was asked to find -- a promoted head or Kv -- is sized too: not by a
        // rule but by the constraints it holds, and the basis says so (23's promotion).
        if (run is not null && layout is not null)
        {
            foreach (var (name, value, _) in SolvedStates.Parameters(layout, run.Solve.Solution, component.Name))
            {
                var dimension = kind?.Parameters.GetValueOrDefault(name)?.Dimension ?? Dimension.Dimensionless;
                var (canonical, unit) = Canonical(value, dimension, component.Name, name, raised);
                parameters[name] = new ParameterWire
                {
                    Value = canonical,
                    Unit = unit,
                    Source = "sized",
                    Basis = run.Bases.GetValueOrDefault(Ownership.Key(component.Name, name)) ?? "found by the solver, to hold the stated constraints",
                };
            }
        }

        // A parameter the outer loop evaluated from a deferred expression is stated -- the script wrote
        // it, and the counting treats it so -- and carries the expression and the pass as its basis (L-59).
        foreach (var (name, quantity) in component.StatedParameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var (value, unit) = Canonical(quantity.SiValue, quantity.Dimension, component.Name, name, raised);
            parameters[name] = new ParameterWire
            {
                Value = value,
                Unit = unit,
                Source = "stated",
                Basis = run?.Bases.GetValueOrDefault(Ownership.Key(component.Name, name)),
            };
        }

        return parameters
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, ParameterWire> StatedOnly(ComponentSymbol symbol, ImmutableArray<Diagnostic>.Builder raised)
    {
        var parameters = new Dictionary<string, ParameterWire>(StringComparer.Ordinal);

        foreach (var (name, parameter) in symbol.Parameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (parameter.Value is not { } quantity)
            {
                continue;
            }

            var (value, unit) = Canonical(quantity.SiValue, quantity.Dimension, symbol.Name, name, raised);
            parameters[name] = new ParameterWire { Value = value, Unit = unit, Source = "stated" };
        }

        return parameters;
    }

    private static ImmutableArray<PortWire> Ports(CircuitGraph graph, int index, IComponent component)
    {
        if (component is not IFlowComponent flow)
        {
            return [];
        }

        var ports = ImmutableArray.CreateBuilder<PortWire>(flow.Ports.Length);

        for (var port = 0; port < flow.Ports.Length; port++)
        {
            var peer = graph.Adjacency.Peer(index, port);
            var tank = component as TankComponent;

            ports.Add(new PortWire
            {
                Name = flow.Ports[port].Name,
                Role = flow.Ports[port].Role.ToString().ToLowerInvariant(),
                ConnectedTo = peer.Exists ? graph.Components[peer.Component].Name : null,
                Elevation = tank is null ? null : Round(tank.PortLevels[port]),
                Layer = tank?.LayerForPort(port),
            });
        }

        return ports.ToImmutable();
    }

    private static ComponentStateWire? State(
        CircuitGraph graph,
        int index,
        IComponent component,
        ComponentKindInfo? kind,
        ImmutableArray<SolvedPort?> ports,
        SystemLayout layout,
        StateVector solution,
        ImmutableArray<Diagnostic>.Builder raised)
    {
        var name = component.Name;
        QuantityWire Q(double si, Dimension dimension, string field) => Wire(si, dimension, name, field, raised);

        if (component is NodeComponent)
        {
            return ports.Length > 0 && ports[0] is { } own
                ? new ComponentStateWire { T = Q(own.Temperature, Dimension.Temperature, "t"), P = Q(own.Pressure, Dimension.Pressure, "p") }
                : null;
        }

        if (component is TankComponent tank)
        {
            var enthalpy = layout.Unknowns.FirstOrDefault(unknown =>
                unknown.Kind == UnknownKind.NodeEnthalpy && string.Equals(unknown.OwnerComponentId, name, StringComparison.Ordinal));
            var reference = ports.FirstOrDefault(static port => port is not null);

            if (enthalpy is null || reference is not { } at || enthalpy.Index >= solution.Values.Length)
            {
                return null;
            }

            var mixed = graph.Substance.FromPressureEnthalpy(
                Quantity.FromSi(at.Pressure, Dimension.Pressure),
                Quantity.FromSi(solution.Values[enthalpy.Index], Dimension.Enthalpy));

            return mixed.IsSuccess
                ? new ComponentStateWire { T = Q(mixed.Value.Temperature.SiValue, Dimension.Temperature, "t"), P = Q(at.Pressure, Dimension.Pressure, "p") }
                : null;
        }

        if (component is not IFlowComponent flow || ports.Length < 2 || ports[0] is not { } first || ports[1] is not { } second)
        {
            return null;
        }

        // The inlet is whichever of the first pair the flow enters; at zero flow the declared inlet.
        var forward = first.Flow > 0 || (first.Flow == 0 && second.Flow <= 0);
        var (inlet, outlet) = forward ? (first, second) : (second, first);
        var massFlow = Math.Abs(inlet.Flow);
        var solved = Solved(layout, solution, name, kind, raised);

        var state = new ComponentStateWire
        {
            Flow = Q(first.Flow, Dimension.MassFlow, "flow"),
            TIn = Q(inlet.Temperature, Dimension.Temperature, "tIn"),
            TOut = Q(outlet.Temperature, Dimension.Temperature, "tOut"),
            PIn = Q(inlet.Pressure, Dimension.Pressure, "pIn"),
            POut = Q(outlet.Pressure, Dimension.Pressure, "pOut"),
            Dp = Q(inlet.Pressure - outlet.Pressure, Dimension.PressureDelta, "dp"),
            Solved = solved.Count == 0 ? null : solved,
        };

        switch (component)
        {
            case HeatExchangerComponent exchanger:
                state = state with { Power = Q(massFlow * (outlet.Enthalpy - inlet.Enthalpy), Dimension.Power, "power") };

                if (exchanger.SecondarySideConnected && ports.Length >= 4 && ports[2] is { } third && ports[3] is { } fourth)
                {
                    var forward2 = third.Flow > 0 || (third.Flow == 0 && fourth.Flow <= 0);
                    var (in2, out2) = forward2 ? (third, fourth) : (fourth, third);
                    state = state with
                    {
                        Flow2 = Q(third.Flow, Dimension.MassFlow, "flow2"),
                        TIn2 = Q(in2.Temperature, Dimension.Temperature, "tIn2"),
                        TOut2 = Q(out2.Temperature, Dimension.Temperature, "tOut2"),
                    };
                }

                break;

            case PumpComponent pump:
                if (SolvedStates.Pump(layout, solution, pump, ports) is { } at)
                {
                    state = state with { Head = Q(at.Head, Dimension.Head, "head") };
                }

                break;

            case PipeComponent pipe:
            {
                // The same mean properties the pipe's own residual used (Pipe.EvaluateResiduals), so
                // the velocity reported is the one its pressure drop was computed at (A-6).
                var density = (inlet.Density + outlet.Density) / 2;
                var viscosity = (inlet.DynamicViscosity + outlet.DynamicViscosity) / 2;

                if (density > 0 && pipe.FlowArea > 0)
                {
                    var velocity = massFlow / (density * pipe.FlowArea);
                    state = state with { Velocity = Q(velocity, Dimension.Velocity, "velocity") };

                    if (viscosity > 0)
                    {
                        state = state with { Re = Q(density * velocity * pipe.InsideDiameter / viscosity, Dimension.Dimensionless, "re") };
                    }
                }

                break;
            }
        }

        return state;
    }

    private static Dictionary<string, QuantityWire> Solved(
        SystemLayout layout, StateVector solution, string component, ComponentKindInfo? kind, ImmutableArray<Diagnostic>.Builder raised)
    {
        var solved = new Dictionary<string, QuantityWire>(StringComparer.Ordinal);

        foreach (var (parameter, value, _) in SolvedStates.Parameters(layout, solution, component))
        {
            var dimension = kind?.Parameters.GetValueOrDefault(parameter)?.Dimension ?? Dimension.Dimensionless;
            solved[parameter] = Wire(value, dimension, component, parameter, raised);
        }

        return solved;
    }
}
