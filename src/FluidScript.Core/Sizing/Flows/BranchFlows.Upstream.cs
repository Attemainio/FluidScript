using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Sizing.Flows;

public static partial class BranchFlows
{
    /// <summary>The temperature a stated outlet upstream delivers to an exchanger's side-1 inlet.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="exchanger">The exchanger whose <c>in</c> is not stated.</param>
    /// <returns>
    /// The stated outlet temperature of the exchanger the water last passed, or a stated node
    /// temperature met on the way; <see langword="null"/> when the walk reaches a mixing point first.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>A machine that holds its leaving temperature states it on itself, not on the load it
    /// feeds</strong> (<c>S-83</c>). <c>HPC heater out.t=45</c> with its duty unstated, feeding
    /// <c>HL load power=120 out.t=40</c> through a diverting valve, fixes the load's stream at
    /// 120 / (4.19 × 5) = 5.73 kg/s; without this walk the load had no inlet to rate against and every
    /// branch of the loop started at <see cref="Nominal"/>.
    /// </para>
    /// <para>
    /// The walk follows the written direction from the inlet port, and crosses only what does not
    /// change the water's temperature: a two-port element that is not an exchanger (pump, valve, pipe),
    /// an implicit node between two elements, a split (<see cref="SoleInflow"/>), and a three-way valve
    /// entered by <c>a</c> or <c>b</c>, whose stream arrived by <c>ab</c>. It stops at any other junction
    /// and at a three-way valve entered by <c>ab</c>: those mix streams, and a seed does not invent the mix.
    /// </para>
    /// <para>
    /// <strong>A split is crossed</strong> (<c>S-91</c>): every stream leaving a junction with one stream
    /// entering it carries that stream's temperature, so two radiators fed from the supply node after a
    /// <c>heater out.t=70</c> are rated over 70 and their own outlets -- 20 kW over 70/40 and 70/50 is 0.159
    /// and 0.239 kg/s, where the walk used to stop at the node and leave both at the nominal 0.1.
    /// </para>
    /// </remarks>
    private static Quantity? UpstreamOutlet(CircuitGraph graph, HeatExchangerComponent exchanger)
    {
        var component = graph.Components.IndexOf(exchanger);
        var port = 0;

        for (var step = 0; step < graph.Components.Length; step++)
        {
            var peer = graph.Adjacency.Peer(component, port);

            if (!peer.Exists)
            {
                return null;
            }

            var element = graph.Components[peer.Component];
            var arrival = element.Ports[peer.Port].Name;

            switch (element)
            {
                case HeatExchangerComponent source:
                    var outlet = arrival switch
                    {
                        "out" => "out",
                        "out2" => "out2",
                        _ => null,
                    };

                    return outlet is not null && source.StatedParameters.TryGetValue(outlet, out var leaving) ? leaving : null;

                case NodeComponent node:
                    if (node.StatedParameters.TryGetValue("t", out var stated))
                    {
                        return stated;
                    }

                    port = Connected(graph, peer.Component) == 2
                        ? OtherConnected(graph, peer.Component, peer.Port)
                        : SoleInflow(graph, peer.Component, peer.Port);

                    if (port < 0)
                    {
                        return null;
                    }

                    break;

                case ThreeWayValveComponent:
                    if (arrival is not ("a" or "b"))
                    {
                        return null;
                    }

                    port = PortNamed(element, "ab");
                    break;

                default:
                    if (element.Ports.Length != 2)
                    {
                        return null;
                    }

                    port = 1 - peer.Port;
                    break;
            }

            component = peer.Component;
        }

        return null;
    }

    /// <summary>The temperature an exchanger's outlet is held at by a stated temperature on the water it leaves into.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="exchanger">The exchanger whose outlet is not stated.</param>
    /// <param name="outlet">The outlet port's name, <c>out</c> or <c>out2</c>.</param>
    /// <returns>
    /// The stated temperature of the first node the water reaches that states one, or <see langword="null"/> when
    /// the walk reaches anything that changes or mixes it first.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="exchanger"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <see cref="UpstreamOutlet"/> read the other way (<c>S-86</c>). A controller's setpoint is written into the
    /// node it measures as that node's stated temperature (<c>D-141</c>), in place of the same temperature on the
    /// exchanger, and the seed read terminals only off the exchanger: step 5 with its controls holds <c>NS.t = 50</c>
    /// after a 30 kW exchanger stating <c>in.t = 20</c>, so its ring is 30 kW over 20/50, 0.239 kg/s -- and started at
    /// the nominal 0.1, where the load's 30 kW took its stream below freezing before the first step.
    /// </para>
    /// <para>
    /// Crosses what leaves the temperature alone: a two-port element that is not an exchanger, and a node whose only
    /// written inflow is this stream -- a node inline, or a split, whose every outflow is tried. It reads a node's
    /// stated temperature, and the stated inlet of the exchanger the water enters next: a radiator returning into a
    /// split that feeds a heat exchanger stating <c>secondary.in.t = 40</c> returns at 40, whatever else the split
    /// feeds. It stops at a three-way valve and at a node another stream also enters, whose temperature is the mix
    /// and not this outlet's; where two outflows of a split find different temperatures it answers nothing.
    /// </para>
    /// </remarks>
    public static Quantity? DownstreamTemperature(CircuitGraph graph, HeatExchangerComponent exchanger, string outlet)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(exchanger);

        var component = graph.Components.IndexOf(exchanger);
        var port = component < 0 ? -1 : PortNamed(exchanger, outlet);

        return port < 0 ? null : Downstream(graph, component, port, [component]);
    }

    /// <summary>The temperature held on the water leaving a component's port, walking the way it runs (<see cref="DownstreamTemperature"/>).</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="component">The component the water leaves.</param>
    /// <param name="port">The port it leaves by.</param>
    /// <param name="visited">Components already walked, so a ring ends.</param>
    /// <returns>The temperature, or <see langword="null"/>.</returns>
    private static Quantity? Downstream(CircuitGraph graph, int component, int port, HashSet<int> visited)
    {
        for (var step = 0; step < graph.Components.Length; step++)
        {
            var peer = graph.Adjacency.Peer(component, port);

            if (!peer.Exists || !visited.Add(peer.Component))
            {
                return null;
            }

            var element = graph.Components[peer.Component];

            switch (element)
            {
                case NodeComponent node:
                    var connected = Connected(graph, peer.Component);

                    if (connected > 2 && !OnlyInflow(graph, peer.Component, peer.Port))
                    {
                        return null;
                    }

                    if (node.StatedParameters.TryGetValue("t", out var stated))
                    {
                        return stated;
                    }

                    if (connected > 2)
                    {
                        return Split(graph, peer.Component, peer.Port, visited);
                    }

                    port = OtherConnected(graph, peer.Component, peer.Port);
                    break;

                case HeatExchangerComponent next:
                    var entered = element.Ports[peer.Port].Name;

                    return entered is "in" or "in2" && next.StatedParameters.TryGetValue(entered, out var inlet) ? inlet : null;

                case ThreeWayValveComponent:
                    return null;

                default:
                    if (element.Ports.Length != 2)
                    {
                        return null;
                    }

                    port = 1 - peer.Port;
                    break;
            }

            component = peer.Component;
        }

        return null;
    }

    /// <summary>The one temperature a split's outflows find, walking each (<see cref="Downstream"/>).</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="node">The split's index; the stream arriving is its only inflow.</param>
    /// <param name="arrived">Its port the stream arrived by.</param>
    /// <param name="visited">Components already walked.</param>
    /// <returns>The temperature every outflow that finds one agrees on, or <see langword="null"/>.</returns>
    private static Quantity? Split(CircuitGraph graph, int node, int arrived, HashSet<int> visited)
    {
        Quantity? found = null;

        for (var port = 0; port < graph.Adjacency.PortCount(node); port++)
        {
            if (port == arrived || !graph.Adjacency.Peer(node, port).Exists
                || Downstream(graph, node, port, visited) is not { } temperature)
            {
                continue;
            }

            if (found is { } earlier && !earlier.IsCloseTo(temperature))
            {
                return null;
            }

            found = temperature;
        }

        return found;
    }

    /// <summary>The one connection of a junction node the script wrote water arriving by, when the node is a split.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="node">The junction node's index in <see cref="CircuitGraph.Components"/>.</param>
    /// <param name="arrived">The node's port the walk came in by, which draws from it.</param>
    /// <returns>The node's port on that connection, or -1 when the node is not a split.</returns>
    /// <remarks>
    /// A junction's own ports are unnamed and carry no direction, so each connection is read by the component it
    /// reaches past any node inline (<see cref="Written"/>): its outlet feeds the junction, its inlet draws from it.
    /// Exactly one feeding connection is a split, whose every outflow is that stream. Two, or a connection whose
    /// direction cannot be told, and the walk stops rather than guess.
    /// </remarks>
    private static int SoleInflow(CircuitGraph graph, int node, int arrived)
    {
        var inflow = -1;

        for (var port = 0; port < graph.Adjacency.PortCount(node); port++)
        {
            if (port == arrived || !graph.Adjacency.Peer(node, port).Exists)
            {
                continue;
            }

            switch (Written(graph, node, port))
            {
                case PortRole.Outlet when inflow < 0:
                    inflow = port;
                    break;

                case PortRole.Inlet:
                    break;

                default:
                    return -1;
            }
        }

        return inflow;
    }

    /// <summary>Whether every connection of a junction node but the one arrived by draws from it.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="node">The junction node's index.</param>
    /// <param name="arrived">The node's port the stream came in by.</param>
    /// <returns><see langword="true"/> when that stream is the node's only written inflow.</returns>
    private static bool OnlyInflow(CircuitGraph graph, int node, int arrived)
    {
        for (var port = 0; port < graph.Adjacency.PortCount(node); port++)
        {
            if (port != arrived && graph.Adjacency.Peer(node, port).Exists && Written(graph, node, port) != PortRole.Inlet)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The written role of the component port a node's connection reaches, past any node inline.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="node">The node's index.</param>
    /// <param name="port">The node's port on the connection.</param>
    /// <param name="visited">Junctions already being read, so a ring of bare links ends rather than recurses.</param>
    /// <returns>The role, or <see langword="null"/> when the direction cannot be told.</returns>
    /// <remarks>
    /// A bare link to another junction -- a header, <c>NA1 - NA2</c> -- reaches no port with a role. Its direction is
    /// that junction's mass balance: when every other connection of the far junction draws from it, this link is what
    /// feeds it, and from this side it draws like an inlet; when every other one feeds it, this link drains it, and
    /// from this side it feeds like an outlet. Anything mixed, or a far junction reached again, cannot be told.
    /// </remarks>
    private static PortRole? Written(CircuitGraph graph, int node, int port, HashSet<int>? visited = null)
    {
        for (var step = 0; step < graph.Components.Length; step++)
        {
            var peer = graph.Adjacency.Peer(node, port);

            if (!peer.Exists)
            {
                return null;
            }

            if (graph.Components[peer.Component] is ThreeWayValveComponent)
            {
                return ValvePort(graph, peer.Component, peer.Port, visited ?? [node]);
            }

            if (graph.Components[peer.Component] is not NodeComponent)
            {
                return graph.Components[peer.Component].Ports[peer.Port].Role;
            }

            if (Connected(graph, peer.Component) != 2)
            {
                return FarJunction(graph, peer.Component, peer.Port, visited ?? [node]);
            }

            node = peer.Component;
            port = OtherConnected(graph, peer.Component, peer.Port);
        }

        return null;
    }

    /// <summary>The role a three-way valve's port plays, from the service it runs in (<see cref="Written"/>).</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="valve">The valve's index.</param>
    /// <param name="port">The port reached.</param>
    /// <param name="visited">Junctions and valves already being read.</param>
    /// <returns>The port's role, or <see langword="null"/> when the service cannot be told.</returns>
    /// <remarks>
    /// A three-way valve's ports are all <see cref="PortRole.Bidirectional"/>: the same body mixes or diverts. A
    /// mixing valve draws at <c>a</c> and <c>b</c> and feeds at <c>ab</c>; a diverting one the other way. The service is
    /// the one its spelling declares (<c>D-136</c>), else the one its common port implies -- a common port drawn from
    /// by an inlet, a pump's, is mixing. The syntax tour's radiator returns to a node feeding its mixing valve's
    /// <c>b</c> and the exchanger's <c>secondary.in</c>, and a <c>b</c> that might have fed the node made the return
    /// look like a mix.
    /// </remarks>
    private static PortRole? ValvePort(CircuitGraph graph, int valve, int port, HashSet<int> visited)
    {
        if (!visited.Add(valve))
        {
            return null;
        }

        var element = (ThreeWayValveComponent)graph.Components[valve];
        var common = PortNamed(element, "ab");
        var mixes = element.Arrangement switch
        {
            ValveArrangement.Mixing => true,
            ValveArrangement.Diverting => false,
            _ when common < 0 || common == port => null,
            _ => Written(graph, valve, common, visited) switch
            {
                PortRole.Inlet => true,
                PortRole.Outlet => false,
                _ => (bool?)null,
            },
        };

        return mixes is not { } mixing ? null
            : mixing == (port != common) ? PortRole.Inlet
            : PortRole.Outlet;
    }

    /// <summary>What a bare link to another junction is, read from that junction's other connections (<see cref="Written"/>).</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="junction">The far junction's index.</param>
    /// <param name="arrived">Its port on the link.</param>
    /// <param name="visited">Junctions already being read.</param>
    /// <returns><see cref="PortRole.Inlet"/> when the link feeds it, <see cref="PortRole.Outlet"/> when it drains it, else <see langword="null"/>.</returns>
    private static PortRole? FarJunction(CircuitGraph graph, int junction, int arrived, HashSet<int> visited)
    {
        if (!visited.Add(junction))
        {
            return null;
        }

        PortRole? all = null;

        for (var port = 0; port < graph.Adjacency.PortCount(junction); port++)
        {
            if (port == arrived || !graph.Adjacency.Peer(junction, port).Exists)
            {
                continue;
            }

            var role = Written(graph, junction, port, visited);

            if (role is not (PortRole.Inlet or PortRole.Outlet) || (all is not null && all != role))
            {
                return null;
            }

            all = role;
        }

        return all;
    }

    /// <summary>How many of a component's ports are wired.</summary>
    private static int Connected(CircuitGraph graph, int component)
    {
        var count = 0;

        for (var port = 0; port < graph.Adjacency.PortCount(component); port++)
        {
            if (graph.Adjacency.Peer(component, port).Exists)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The wired port of a two-way connection that is not the one arrived by.</summary>
    private static int OtherConnected(CircuitGraph graph, int component, int arrived)
    {
        for (var port = 0; port < graph.Adjacency.PortCount(component); port++)
        {
            if (port != arrived && graph.Adjacency.Peer(component, port).Exists)
            {
                return port;
            }
        }

        return -1;
    }

    /// <summary>The index of the port with a given name.</summary>
    private static int PortNamed(IFlowComponent element, string name)
    {
        for (var port = 0; port < element.Ports.Length; port++)
        {
            if (string.Equals(element.Ports[port].Name, name, StringComparison.Ordinal))
            {
                return port;
            }
        }

        return -1;
    }
}
