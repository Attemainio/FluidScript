using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Topology.Construction;

public static partial class Lowering
{
    private sealed partial class Build
    {
        /// <summary>Counts how each component was connected, and by which named ports.</summary>
        /// <remarks>
        /// <para>
        /// A node's ports are unnamed and positional (<c>15</c>), so nothing in the semantic model says
        /// how many it has — only the connection list does. The count decides both the port count and
        /// whether the node carries a mass balance, so it has to come before the node is constructed.
        /// </para>
        /// <para>
        /// <strong>It counts every component and not only the nodes</strong>, because a node is no
        /// longer the only kind whose shape depends on how it was wired: a three-way valve with its
        /// optional bypass left open is a two-way valve (<c>S-14a</c>). The named ports are collected
        /// alongside the count, since a degree of two says something is unconnected and not
        /// <em>which</em>.
        /// </para>
        /// </remarks>
        public void CountConnections()
        {
            foreach (var connection in model.Connections)
            {
                Count(connection.From);
                Count(connection.To);
            }

            void Count(FluidScript.Core.Language.Binding.Symbols.EndpointSymbol endpoint)
            {
                _degree[endpoint.Component] = _degree.GetValueOrDefault(endpoint.Component) + 1;

                if (endpoint.Port.Length == 0)
                {
                    return;
                }

                if (!_namedPorts.TryGetValue(endpoint.Component, out var ports))
                {
                    ports = [];
                    _namedPorts[endpoint.Component] = ports;
                }

                ports.Add(endpoint.Port);
            }
        }

        /// <summary>How the connection list wired one component.</summary>
        /// <param name="component">The component's name.</param>
        /// <returns>Its degree and the ports connections named explicitly.</returns>
        private PortWiring Wiring(string component) =>
            new(
                _degree.GetValueOrDefault(component),
                _namedPorts.TryGetValue(component, out var ports) ? [.. ports] : []);

        /// <summary>Instantiates every component that carries flow, in declaration order.</summary>
        /// <remarks>
        /// <para>
        /// <strong>An observer and a controller are dropped by the same test, and neither needs an
        /// exemption</strong>: both are components with no ports, and a component with no ports is not
        /// in a flow graph. Invariant 9 — that adding a dozen sensors leaves the graph byte-identical —
        /// falls out of that rather than being enforced.
        /// </para>
        /// <para>
        /// A component the factory cannot build is recorded and skipped. Every connection touching it
        /// is then dropped too, which disconnects the graph; that is honest rather than convenient, and
        /// well-posedness has the disconnection to report.
        /// </para>
        /// <para>
        /// <strong>A node is built here rather than by the factory, and still carries what the script
        /// stated.</strong> Its <c>p</c>, <c>t</c> and <c>flow</c> are the model's boundary conditions,
        /// and well-posedness cannot tell a boundary from a bare junction without them — nor a stated
        /// pressure, which supplies a datum and admits an external mass flux, from an absent one.
        /// </para>
        /// </remarks>
        public void CreateComponents()
        {
            var held = ResolveSetpoints();

            foreach (var symbol in model.Components)
            {
                if (symbol.Kind is not { } kind || kind.IsObserver
                    || (kind.Ports.IsEmpty && !kind.HasUnlimitedPorts))
                {
                    continue;
                }

                if (kind.HasUnlimitedPorts)
                {
                    var degree = _degree.GetValueOrDefault(symbol.Name);

                    // Degree 3+ is a junction and degree 1 a terminal; at degree 2 the branch's own
                    // flow already makes the balance an identity.
                    Add(
                        new NodeComponent(symbol.Name, degree, degree >= 3 || degree == 1)
                        {
                            // A setpoint on this node's temperature is stated here as the design point
                            // (D-141): the row and the promotion it raises are a node temperature's.
                            StatedParameters = held.TryGetValue(symbol.Name, out var setpoint)
                                ? ComponentFactory.Stated(symbol).SetItem(setpoint.Parameter, setpoint.Value)
                                : ComponentFactory.Stated(symbol),
                            // A pressure the binder copied from a port keeps the port's spelling (D-124).
                            PressureStatedAs = symbol.Parameters.TryGetValue("p", out var stated)
                                && stated.WrittenName.Contains(' ', StringComparison.Ordinal)
                                    ? stated.WrittenName
                                    : null,
                            Boundary = Role(kind),
                            Elevation = model.Heights.Of(symbol.Name),
                        },
                        symbol.CircuitName,
                        symbol.Origin is Origin.Declared ? NodeOrigin.Declared : NodeOrigin.Inferred);
                    continue;
                }

                if (factory.Create(symbol, Wiring(symbol.Name)) is not { } component)
                {
                    _unresolved.Add(symbol.Name);
                    continue;
                }

                // A pipe's rise is what it connects, not a number of its own (D-70): the binder has
                // already propagated every stated height to the pipe's two ports.
                if (component is PipeComponent pipe)
                {
                    component = pipe.WithRise(model.Heights.Rise(symbol.Name));
                }

                Add(component, symbol.CircuitName, origin: null);
            }
        }

        /// <summary>Resolves every <c>control</c> line's setpoint into the design solve's constraints (<c>D-141</c>).</summary>
        /// <returns>The nodes whose temperature a setpoint states, and the value.</returns>
        /// <remarks>
        /// <para>
        /// <strong>A setpoint holds in the design solve when the actuator is the solve's to choose.</strong>
        /// <c>control actuate=3WV.position measure=N2.t by=TC1 setpoint=20</c> with no position stated
        /// on <c>3WV</c> is <c>N2 t=20</c> answered by <c>3WV.position</c>: the loop's design point. With
        /// the position stated the solve has nothing to hold the temperature with, the setpoint is not a
        /// constraint, and the run starts off setpoint by whatever the design solve lands on
        /// (<c>FS3210</c>, raised by well-posedness).
        /// </para>
        /// <para>
        /// What can be held is a plain node's temperature, read directly (<c>N2.t</c>) or through a sensor
        /// on it. A boundary's temperature is what enters the model and is not a demand; a node that
        /// states its own <c>t</c> has said what it wants; a measurement of anything else has no
        /// constraint row to become yet (<c>FS3211</c>). Each of those is recorded unapplied so the
        /// diagnostic can name it.
        /// </para>
        /// </remarks>
        private Dictionary<string, (string Parameter, Quantity Value)> ResolveSetpoints()
        {
            var symbols = new Dictionary<string, ComponentSymbol>(StringComparer.Ordinal);

            foreach (var symbol in model.Components)
            {
                symbols.TryAdd(symbol.Name, symbol);
            }

            var held = new Dictionary<string, (string Parameter, Quantity Value)>(StringComparer.Ordinal);

            // What each node is wired to, so a terminal stated on a neighbour -- `HE1 out.t=50` with the
            // sensor on HE1's outlet node -- is seen to fix the node's temperature already. A setpoint
            // applied there would be the same statement twice, square by count and singular in truth.
            var neighbours = new Dictionary<string, List<(ComponentSymbol Component, string Port)>>(StringComparer.Ordinal);

            foreach (var connection in model.Connections)
            {
                Neighbour(neighbours, symbols, connection.From, connection.To);
                Neighbour(neighbours, symbols, connection.To, connection.From);
            }

            foreach (var binding in model.ControlBindings)
            {
                if (binding.Setpoint is not { } value)
                {
                    continue;
                }

                var measured = binding.Measurement;

                // A sensor reads the node it is placed on (D-61).
                if (symbols.TryGetValue(measured.Component, out var sensor)
                    && sensor.Kind?.IsObserver == true
                    && sensor.AttachedTo is { } node)
                {
                    measured = new PropertyReference(node, measured.Property);
                }

                var holdable = symbols.TryGetValue(measured.Component, out var target)
                    && target.Kind is { HasUnlimitedPorts: true } kind
                    && Role(kind) == BoundaryRole.Interior
                    && string.Equals(measured.Property, HydraulicPartition.Temperature, StringComparison.Ordinal);

                string? reason = null;

                if (holdable)
                {
                    var fixing = neighbours.TryGetValue(measured.Component, out var wired)
                        ? wired.FirstOrDefault(wire => wire.Component.Parameters.ContainsKey(wire.Port))
                        : default;

                    reason = target!.Parameters.ContainsKey(HydraulicPartition.Temperature)
                        ? $"'{measured.Component}' states its own temperature"
                        : fixing.Component is not null
                            ? $"'{fixing.Component.Name}.{fixing.Port}.t' already fixes it"
                            : symbols.TryGetValue(binding.Actuator.Component, out var actuated)
                                && actuated.Parameters.ContainsKey(binding.Actuator.Property)
                                ? $"'{binding.Actuator.Component}.{binding.Actuator.Property}' is stated"
                                : held.ContainsKey(measured.Component)
                                    ? $"another control line already holds '{measured.Component}'"
                                    : null;
                }

                var applied = holdable && reason is null;

                if (applied)
                {
                    held[measured.Component] = (measured.Property, value);
                }

                _setpoints.Add(new Setpoint(
                    binding.Controller.Name,
                    measured.Component,
                    measured.Property,
                    binding.Actuator.Component,
                    binding.Actuator.Property,
                    value,
                    applied,
                    reason));
            }

            return held;
        }

        /// <summary>Records one end of a connection as the other end's neighbour, when that end is a node.</summary>
        private static void Neighbour(
            Dictionary<string, List<(ComponentSymbol Component, string Port)>> neighbours,
            Dictionary<string, ComponentSymbol> symbols,
            EndpointSymbol node,
            EndpointSymbol other)
        {
            if (!symbols.TryGetValue(node.Component, out var symbol)
                || symbol.Kind is not { HasUnlimitedPorts: true }
                || !symbols.TryGetValue(other.Component, out var component))
            {
                return;
            }

            if (!neighbours.TryGetValue(node.Component, out var list))
            {
                neighbours[node.Component] = list = [];
            }

            list.Add((component, other.Port));
        }

        /// <summary>Which end of an open circuit a node kind declares itself to be.</summary>
        /// <param name="kind">The registry entry the script named.</param>
        /// <returns>The role, or <see cref="BoundaryRole.Interior"/> for a plain node.</returns>
        /// <remarks>
        /// Read from the keyword because that is the only place it exists: an <c>outlet</c> and a bare
        /// terminal <c>node</c> have identical parameters and opposite mass balances (<c>D-64</c>). This
        /// is the one point where lowering reads a kind's spelling, and it reads it from the registry
        /// entry rather than from the script, so an alias resolves before it gets here.
        /// </remarks>
        private static BoundaryRole Role(FluidScript.Core.Language.Registry.ComponentKindInfo kind) => kind.Keyword switch
        {
            "inlet" => BoundaryRole.Inlet,
            "outlet" => BoundaryRole.Outlet,
            _ => BoundaryRole.Interior,
        };

        /// <summary>Turns each connection into a link between two port slots.</summary>
        /// <remarks>
        /// <para>
        /// A node's port index comes from its position in this walk, which is why the walk order is the
        /// connection order and not anything derived. A link touching a component that was not built is
        /// dropped, but the node counter still advances — so a node's ports keep the indices the degree
        /// count gave them, and one is simply left with no peer.
        /// </para>
        /// <para>
        /// It is also where the ports the script <em>named</em> are collected (<c>D-88</c>). The slot is
        /// resolved either way, so nothing downstream could recover the difference from the graph alone.
        /// </para>
        /// </remarks>
        public void ResolveLinks()
        {
            foreach (var connection in model.Connections)
            {
                var from = Slot(connection.From);
                var to = Slot(connection.To);

                if (from is { } a && to is { } b)
                {
                    _links.Add((a.Element, a.Port, b.Element, b.Port));
                    State(connection.From);
                    State(connection.To);
                }
            }
        }

        /// <summary>Remembers a port name the script wrote, so an inferred one can be told from it.</summary>
        /// <param name="endpoint">One end of a connection, resolved.</param>
        private void State(EndpointSymbol endpoint)
        {
            if (endpoint.PortStated && endpoint.Port.Length > 0)
            {
                _stated.Add($"{endpoint.Component}.{endpoint.Port}");
            }
        }
    }
}
