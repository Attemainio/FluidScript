using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Observation;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Layout.Hints;

public static partial class LayoutHintsDerivation
{
    // ---- circuits ---------------------------------------------------------------------------------------

    /// <summary>One hint per circuit, with its attachment read from the graph when the script wrote none.</summary>
    /// <remarks>
    /// <c>supply</c>/<c>return</c> lines bind a parent and two anchors (<c>D-33</c>); a subcircuit written
    /// as connections -- which is how a mixing branch has to be written (<c>F-16</c>) -- binds nothing,
    /// and the distribution header would have no group. So a circuit with no stated parent takes the
    /// one it touches: its components connect to another circuit's <em>nodes</em>, and to no other
    /// circuit's. Touching through a node and not through a component is what separates hanging off a
    /// header from being coupled across an exchanger (<c>D-36</c>), and touching one circuit, not two,
    /// is what separates a branch from a bridge. The supply anchor is the parent node feeding one of
    /// the circuit's inlets and the return anchor the one fed by one of its outlets; with no port role
    /// to say, connection order does.
    /// </remarks>
    private static ImmutableArray<CircuitHint> Circuits(
        CircuitGraph graph, SemanticModel model, ImmutableDictionary<string, int> index)
    {
        // Every contact a circuit makes with another circuit's node, in connection order: the parent's
        // node, and whether the port on this circuit's side takes flow in, gives it out, or neither.
        var contacts = model.Circuits.ToDictionary(
            static circuit => circuit.Name,
            static _ => new List<(string Parent, string Node, PortRole Role)>(),
            StringComparer.Ordinal);

        foreach (var connection in model.Connections)
        {
            Contact(connection.From.Component, connection.To.Component);
            Contact(connection.To.Component, connection.From.Component);
        }

        void Contact(string member, string other)
        {
            if (!index.TryGetValue(member, out var at)
                || !index.TryGetValue(other, out var peer)
                || graph.Components[peer] is not NodeComponent
                || !graph.CircuitOf.TryGetValue(member, out var circuit)
                || !graph.CircuitOf.TryGetValue(other, out var parent)
                || string.Equals(circuit, parent, StringComparison.Ordinal)
                || !contacts.TryGetValue(circuit, out var list))
            {
                return;
            }

            var role = PortRole.Bidirectional;

            for (var port = 0; port < graph.Adjacency.PortCount(at); port++)
            {
                var reached = graph.Adjacency.Peer(at, port);

                if (reached.Exists && reached.Component == peer)
                {
                    role = graph.Components[at].Ports[port].Role;
                    break;
                }
            }

            list.Add((parent, other, role));
        }

        string? TouchedParent(string circuit) =>
            contacts.TryGetValue(circuit, out var list)
            && list.Select(static contact => contact.Parent).Distinct(StringComparer.Ordinal).Take(2).ToArray() is [var only]
                ? only
                : null;

        var hints = ImmutableArray.CreateBuilder<CircuitHint>(model.Circuits.Length);

        foreach (var circuit in model.Circuits)
        {
            var parent = circuit.ParentCircuit;
            var supply = circuit.Supply?.ParentComponentName;
            var returned = circuit.Return?.ParentComponentName;

            if (parent is null && supply is null && returned is null
                && TouchedParent(circuit.Name) is { } touched
                && !string.Equals(TouchedParent(touched), circuit.Name, StringComparison.Ordinal))
            {
                var list = contacts[circuit.Name];
                parent = touched;
                supply = (list.FirstOrDefault(static contact => contact.Role == PortRole.Inlet).Node ?? list[0].Node);
                returned = list.LastOrDefault(contact => contact.Role == PortRole.Outlet && !string.Equals(contact.Node, supply, StringComparison.Ordinal)).Node
                    ?? list.LastOrDefault(contact => !string.Equals(contact.Node, supply, StringComparison.Ordinal)).Node
                    ?? supply;
            }

            hints.Add(new CircuitHint
            {
                Name = circuit.Name,
                Number = circuit.Number,
                Role = circuit.Role.Stage == ThermalStageRole.Neutral && circuit.Role.CanonicalName == "neutral"
                    ? null
                    : new CircuitRoleHint(circuit.Role.CanonicalName, circuit.Role.Stage),
                ParentCircuit = parent,
                InletAnchorId = supply,
                OutletAnchorId = returned,
            });
        }

        return hints.ToImmutable();
    }

    /// <summary>Attached circuits grouped by parent, kept only where a parent has two or more (<c>25</c> invariant 11).</summary>
    private static ImmutableArray<DistributionGroup> DistributionGroups(ImmutableArray<CircuitHint> circuits)
    {
        var groups = ImmutableArray.CreateBuilder<DistributionGroup>();

        foreach (var parent in circuits)
        {
            var members = circuits
                .Where(candidate =>
                    string.Equals(candidate.ParentCircuit, parent.Name, StringComparison.Ordinal)
                    && candidate.InletAnchorId is not null
                    && candidate.OutletAnchorId is not null)
                .Select(static candidate => candidate.Name)
                .ToImmutableArray();

            if (members.Length >= 2)
            {
                groups.Add(new DistributionGroup { ParentCircuit = parent.Name, Members = members });
            }
        }

        return groups.ToImmutable();
    }

    // ---- non-flow elements ------------------------------------------------------------------------------

    private static ImmutableArray<NonFlowElementHint> NonFlowElements(SemanticModel model, ImmutableArray<string> order)
    {
        var position = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < order.Length; i++)
        {
            position[order[i]] = i;
        }

        var observers = ModelObservers.Collect(model);
        var elements = new List<(int Anchor, int Declared, NonFlowElementHint Hint)>();
        var declaredAt = 0;

        foreach (var component in model.Components)
        {
            declaredAt++;

            if (component.Kind is { IsObserver: true } && component.AttachedTo is { } node && position.TryGetValue(node, out var at))
            {
                elements.Add((at, declaredAt, new NonFlowElementHint
                {
                    ComponentId = component.Name,
                    PlacementAnchorId = node,
                    MeasurementTargetId = node,
                    ActuationTargetId = null,
                    NavigationOrder = 0,
                }));
            }
        }

        foreach (var binding in model.ControlBindings)
        {
            var actuator = binding.Actuator.Component;
            var measured = ModelObservers.Resolve(binding.Measurement, observers).Component;

            if (!position.TryGetValue(actuator, out var anchor))
            {
                continue;
            }

            elements.Add((anchor, int.MaxValue, new NonFlowElementHint
            {
                ComponentId = binding.Controller.Name,
                PlacementAnchorId = actuator,
                MeasurementTargetId = position.ContainsKey(measured) ? measured : actuator,
                ActuationTargetId = actuator,
                NavigationOrder = 0,
            }));
        }
        // One tab order over flow components and instruments together: each element sits immediately
        // after its anchor, so a position is unique across the whole scene, and a flow component's own
        // position is its Order index plus the elements anchored before it.
        var hints = ImmutableArray.CreateBuilder<NonFlowElementHint>(elements.Count);
        var sorted = elements
            .OrderBy(static element => element.Anchor)
            .ThenBy(static element => element.Declared)
            .ThenBy(static element => element.Hint.ComponentId, StringComparer.Ordinal)
            .ToList();
        var slot = 0;

        foreach (var (anchor, _, hint) in sorted)
        {
            slot = Math.Max(slot + 1, anchor + sorted.Count(element => element.Anchor < anchor) + 1);
            hints.Add(hint with { NavigationOrder = slot });
        }

        return hints.ToImmutable();
    }

    /// <summary>The one circuit every member of a vertex belongs to, or <see langword="null"/> when they differ.</summary>
    private static string? CircuitOf(CircuitGraph graph, List<int> members)
    {
        string? circuit = null;

        foreach (var member in members)
        {
            if (!graph.CircuitOf.TryGetValue(graph.Components[member].Name, out var owner))
            {
                return null;
            }

            if (circuit is null)
            {
                circuit = owner;
            }
            else if (!string.Equals(circuit, owner, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return circuit;
    }
}
