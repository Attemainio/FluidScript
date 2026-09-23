using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Model.Contract;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Model;

public static partial class ModelContractBuilder
{
    // ---- connections and circuits ----------------------------------------------------------------------

    private static ImmutableArray<ConnectionWire> Connections(
        SemanticModel model,
        CircuitGraph graph,
        LayoutHints hints,
        ImmutableArray<ImmutableArray<SolvedPort?>>? ports,
        ImmutableArray<Diagnostic>.Builder raised)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < graph.Components.Length; i++)
        {
            index[graph.Components[i].Name] = i;
        }

        var connections = ImmutableArray.CreateBuilder<ConnectionWire>(model.Connections.Length);

        for (var i = 0; i < model.Connections.Length; i++)
        {
            var connection = model.Connections[i];
            var id = $"c{i}";
            ConnectionStateWire? state = null;

            // The flow along the connection as written is the flow leaving the `from` component
            // through the port that faces `to`.
            if (ports is { } solved
                && index.TryGetValue(connection.From.Component, out var from)
                && index.TryGetValue(connection.To.Component, out var to))
            {
                for (var port = 0; port < graph.Adjacency.PortCount(from); port++)
                {
                    var peer = graph.Adjacency.Peer(from, port);

                    if (peer.Exists && peer.Component == to && port < solved[from].Length && solved[from][port] is { } at)
                    {
                        state = new ConnectionStateWire(Wire(-at.Flow, Dimension.MassFlow, id, "flow", raised));
                        break;
                    }
                }
            }

            connections.Add(new ConnectionWire
            {
                Id = id,
                From = new EndpointWire(connection.From.Component, connection.From.Port.Length == 0 ? null : connection.From.Port),
                To = new EndpointWire(connection.To.Component, connection.To.Port.Length == 0 ? null : connection.To.Port),
                Flow = hints.Flow.GetValueOrDefault(id, FlowDirection.None).ToString().ToLowerInvariant(),
                State = state,
            });
        }

        return connections.ToImmutable();
    }

    private static ImmutableArray<CircuitWire> Circuits(
        SemanticModel model, CircuitGraph graph, LayoutHints hints, bool solved, bool statesOmitted)
    {
        var circuits = ImmutableArray.CreateBuilder<CircuitWire>(model.Circuits.Length);

        foreach (var circuit in model.Circuits)
        {
            var hint = hints.Circuits.FirstOrDefault(candidate => string.Equals(candidate.Name, circuit.Name, StringComparison.Ordinal));

            circuits.Add(new CircuitWire
            {
                Name = circuit.Name,
                Number = circuit.Number,
                NumberIsExplicit = circuit.NumberIsExplicit,
                Substance = circuit.Substance ?? graph.Substance.Name,
                Mode = ModeName(circuit.Mode) ?? "steady",
                Role = hint?.Role?.CanonicalName,
                ParentCircuit = hint?.ParentCircuit,
                InletAnchorId = hint?.InletAnchorId,
                OutletAnchorId = hint?.OutletAnchorId,
                Solved = solved,
                StatesOmitted = statesOmitted,
            });
        }

        return circuits.ToImmutable();
    }

    // ---- layout ------------------------------------------------------------------------------------------

    private static LayoutWire Layout(LayoutHints hints, Scene scene, Styles styles, ColourScales scales) => new()
    {
        Margin = scene.Margin,
        LabelMetric = new LabelMetricWire(Round(LabelLayout.Size), LabelLayout.Advance),
        Extent = Styles.BoxOf(scene.Extent),
        Placements = [.. scene.Placements.Select(p => new PlacementWire
        {
            ComponentId = p.ComponentId,
            SymbolId = p.SymbolId,
            Inner = Styles.BoxOf(p.Inner),
            Outer = Styles.BoxOf(p.Outer),
            Rotation = p.Rotation,
            Mirrored = p.Mirrored,
            Arrangement = p.Arrangement,
            Anchors = p.Anchors.ToDictionary(
                static a => a.Key,
                static a => new AnchorWire { At = [Round(a.Value.At.X), Round(a.Value.At.Y)], Direction = [a.Value.Direction.X, a.Value.Direction.Y] },
                StringComparer.Ordinal),
            LabelAt = [Round(p.LabelAt.X), Round(p.LabelAt.Y)],
            LabelBox = Styles.BoxOf(p.LabelBox),
            LabelClear = p.LabelClear,
            Source = p.Source,
            Style = styles.Of(p.ComponentId),
            Scale = scales.Of(p.ComponentId)[scales.Active].At,
            Scales = scales.Of(p.ComponentId),
        })],
        Routes = [.. scene.Routes.Select(r => new RouteWire
        {
            Id = r.ConnectionId,
            Kind = r.Kind,
            Layer = r.Layer,
            Points = [.. r.Points.SelectMany(static point => new[] { Round(point.X), Round(point.Y) })],
            Hops = [.. r.Hops.SelectMany(static point => new[] { Round(point.X), Round(point.Y) })],
            Style = styles.Of(styles.FromComponentOf(r.ConnectionId)),
            ScaleFrom = r.Kind == "pipe" ? scales.OfRoute(styles.FromComponentOf(r.ConnectionId), styles.ToComponentOf(r.ConnectionId))[scales.Active].From : null,
            ScaleTo = r.Kind == "pipe" ? scales.OfRoute(styles.FromComponentOf(r.ConnectionId), styles.ToComponentOf(r.ConnectionId))[scales.Active].To : null,
            Scales = r.Kind == "pipe" ? scales.OfRoute(styles.FromComponentOf(r.ConnectionId), styles.ToComponentOf(r.ConnectionId)) : ImmutableDictionary<string, ScalePositionWire>.Empty,
        })],
        Order = hints.Order,

        ThermalStages = [.. hints.ThermalStages.Select(static stage => new ThermalStageWire(stage.Rank, stage.Role.ToString().ToLowerInvariant(), stage.Components))],
        Flow = hints.Flow
            .OrderBy(static pair => int.Parse(pair.Key[1..], CultureInfo.InvariantCulture))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value.ToString().ToLowerInvariant(), StringComparer.Ordinal),

        Groups = [.. hints.Groups.Select(static group => new ComponentGroupWire(group.ParentComponentId, group.Children))],
        NonFlowElements = [.. hints.NonFlowElements.Select(static element => new NonFlowElementWire(
            element.ComponentId, element.PlacementAnchorId, element.MeasurementTargetId, element.ActuationTargetId, element.NavigationOrder))],
        CircuitOf = Ordered(hints.CircuitOf, static circuit => circuit),
        DistributionGroups = [.. hints.DistributionGroups.Select(static group => new DistributionGroupWire(group.ParentCircuit, group.Members))],
        Inferred = [.. hints.Inferred.Order(StringComparer.Ordinal)],

    };

    // ---- bindings and diagnostics --------------------------------------------------------------------------

    private static BindingWire BindingOf(BindingSymbol binding, ImmutableArray<Diagnostic>.Builder raised)
    {
        if (binding.Value is not { } quantity)
        {
            // Deferred: no value until the solve, but the dimension the binder typed from the expression
            // travels, so completion can filter by it (U-5). Null when the expression does not say.
            return binding.Dimension is { } typed
                ? new BindingWire(binding.Name, null, null, typed.IsNamed ? typed.Name : null, typed.IsNamed ? null : typed.SiUnit)
                : new BindingWire(binding.Name, null, null, null, null);
        }

        var (value, unit) = Canonical(quantity.SiValue, quantity.Dimension, binding.Name, "value", raised);
        var dimension = quantity.Dimension;
        return new BindingWire(
            binding.Name,
            value,
            unit,
            dimension.IsNamed && dimension.Name != "Dimensionless" ? dimension.Name : null,
            dimension.IsNamed ? null : dimension.SiUnit);
    }
}
