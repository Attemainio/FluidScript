using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Components.Valves;
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
    /// an implicit node between two elements, and a three-way valve entered by <c>a</c> or <c>b</c>,
    /// whose stream arrived by <c>ab</c>. It stops at a junction of three or more branches and at a
    /// three-way valve entered by <c>ab</c>: those mix streams, and a seed does not invent the mix.
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

                    if (Connected(graph, peer.Component) != 2)
                    {
                        return null;
                    }

                    port = OtherConnected(graph, peer.Component, peer.Port);
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
