using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Topology.Construction;

public static partial class Lowering
{
    private sealed partial class Build
    {
        /// <summary>Subdivides every pipe that asked for internal nodes.</summary>
        /// <remarks>
        /// <para>
        /// <c>nodes=n</c> becomes n internal thermodynamic nodes and n+1 hydraulic sub-pipes, each
        /// <c>length/(n+1)</c>. The internal nodes own equal shares <c>V/n</c> of the pipe's fluid
        /// volume; the endpoint nodes own none, because they are shared with whatever else connects
        /// there.
        /// </para>
        /// <para>
        /// <strong>Here rather than in the component</strong>, so the solver and the renderer see the
        /// same state nodes (<c>R-10</c>). A pipe that also knew about its own subdivision would be two
        /// models, and the second one would be invisible to every diagnostic that names a node.
        /// </para>
        /// <para>
        /// <strong>Length and rise divide; minor loss does not.</strong> A stated <c>K</c> is a
        /// fitting somewhere along the run, not a property per metre, so splitting it across the
        /// sub-pipes would invent five smaller fittings. It stays whole on the first one.
        /// </para>
        /// </remarks>
        public void ExpandPipes()
        {
            foreach (var symbol in model.Components)
            {
                if (symbol.Kind?.Keyword != "pipe"
                    || !_byName.TryGetValue(symbol.Name, out var index)
                    || _elements[index] is not PipeComponent pipe
                    || Cells(symbol) is not { } cells)
                {
                    continue;
                }

                Expand(index, pipe, cells, symbol.CircuitName, model.Heights.Of(symbol.Name, "in"));
            }
        }

        private (int Element, int Port)? Slot(EndpointSymbol endpoint)
        {
            if (IsNode(endpoint.Component))
            {
                var port = _nextPort.GetValueOrDefault(endpoint.Component);
                _nextPort[endpoint.Component] = port + 1;

                return _byName.TryGetValue(endpoint.Component, out var node) ? (node, port) : null;
            }

            if (!_byName.TryGetValue(endpoint.Component, out var element))
            {
                return null;
            }

            var ports = _elements[element].Ports;

            for (var index = 0; index < ports.Length; index++)
            {
                if (string.Equals(ports[index].Name, endpoint.Port, StringComparison.Ordinal))
                {
                    return (element, index);
                }
            }

            return null;
        }

        /// <summary>How many internal cells a pipe asked for, or null when it asked for none.</summary>
        private static int? Cells(ComponentSymbol symbol) =>
            symbol.Parameters.TryGetValue("nodes", out var stated)
            && stated.Value is { SiValue: var value }
            && double.IsInteger(value)
            && value >= 1
                ? (int)value
                : null;

        private void Expand(int index, PipeComponent pipe, int cells, string circuit, double inletHeight)
        {
            var members = ImmutableArray.CreateBuilder<string>();
            var segments = new int[cells + 1];

            for (var segment = 0; segment <= cells; segment++)
            {
                var name = $"{pipe.Name}{Generated}s{(segment + 1).ToString(CultureInfo.InvariantCulture)}";

                segments[segment] = _elements.Count;
                Add(
                    new PipeComponent(
                        name,
                        pipe.Length / (cells + 1),
                        pipe.InsideDiameter,
                        pipe.Roughness,

                        // The whole stated K on the first sub-pipe: a fitting is somewhere along the
                        // run, not a property per metre.
                        segment == 0 ? pipe.MinorLoss : 0,
                        pipe.Rise / (cells + 1))
                    {
                        Material = pipe.Material,

                        // What the script stated on the pipe it stated on every cell of it (`C-113`):
                        // a sub-pipe with an empty stated map read as a pipe nobody had sized, and
                        // `PB pipe dn=20 nodes=4` came out as five segments stepped down to DN15, with
                        // every transport figure computed from the wrong bore. Length is not copied,
                        // because the segment's is derived and a report must not call it stated.
                        StatedParameters = pipe.StatedParameters.Remove("length").Remove("nodes"),
                        SizedParameters = pipe.SizedParameters,
                        DefaultParameters = pipe.DefaultParameters,
                    },
                    circuit,
                    origin: null);
                members.Add(name);
            }

            var volume = pipe.FlowArea * pipe.Length;

            for (var cell = 0; cell < cells; cell++)
            {
                var name = $"{pipe.Name}{Generated}n{(cell + 1).ToString(CultureInfo.InvariantCulture)}";
                // Evenly up the run: the rise is shared between the sub-pipes, so each internal node
                // sits one share above the last.
                var node = new NodeComponent(name, portCount: 2, carriesMassBalance: false)
                {
                    Elevation = inletHeight + (pipe.Rise * (cell + 1) / (cells + 1)),
                };

                _byName[name] = _elements.Count;
                _links.Add((segments[cell], 1, _elements.Count, 0));
                _links.Add((_elements.Count, 1, segments[cell + 1], 0));
                _elements.Add(node);
                _circuits[name] = circuit;
                _nodes.Add(new GraphNode
                {
                    Name = name,
                    Component = node,
                    Origin = NodeOrigin.PipeInternal,
                    ThermalVolume = volume / cells,
                });

                members.Add(name);
            }

            // The original pipe's two links move to the ends of the chain. It stays in the element list
            // with no connections, which `Prune` removes once every expansion has been rewired.
            for (var link = 0; link < _links.Count; link++)
            {
                var (element, port, peer, peerPort) = _links[link];

                if (element == index)
                {
                    _links[link] = (segments[port == 0 ? 0 : cells], port, peer, peerPort);
                }
                else if (peer == index)
                {
                    _links[link] = (element, port, segments[peerPort == 0 ? 0 : cells], peerPort);
                }
            }

            _groups.Add(new ComponentGroup { Source = pipe.Name, Members = members.ToImmutable() });
            _replaced.Add(index);
        }

        /// <summary>Drops the components an expansion replaced, keeping every index consistent.</summary>
        /// <remarks>
        /// Run after every expansion rather than during one, because <see cref="Expand"/> rewrites link
        /// endpoints by element index and removing an element mid-pass would move the ones after it.
        /// </remarks>
        /// <summary>Gives each exchanger's hold-up to the node its outlet delivers into (<c>C-114</c>).</summary>
        /// <remarks>
        /// <para>
        /// <strong>The water an exchanger holds has to sit in a control volume, and the node at its
        /// outlet already is one.</strong> A node with a thermal volume is a differential state in a run
        /// and nothing at all in a steady solve (<c>SystemLayout</c> builds the state only in
        /// <see cref="SolveMode.Transient"/>), which is exactly the hold-up's behaviour -- so the lag
        /// arrives without a column, a row, or a change to any counting table.
        /// </para>
        /// <para>
        /// The duty already lands on that node at forward flow (<c>D-69</c>'s forward share), so the
        /// node's balance becomes <c>m·dh/dt = ṁ(h_in − h) + Q̇</c>: one perfectly mixed volume per side
        /// carrying its own heat, which is what <c>D-144</c> asked for.
        /// </para>
        /// <para>
        /// <strong>Two approximations, both in a stated direction.</strong> The volume is fixed at the
        /// nominal outlet, so a reversed side holds its water on the wrong end of itself; and a node
        /// shared with other components mixes the hold-up into whatever else arrives there instead of
        /// keeping it inside the exchanger. Neither changes the total capacitance on the circuit.
        /// </para>
        /// </remarks>
        public void AttachHoldUp()
        {
            for (var element = 0; element < _elements.Count; element++)
            {
                if (_elements[element] is not HeatExchangerComponent exchanger)
                {
                    continue;
                }

                Hold(element, port: 1, exchanger.HoldUp);

                if (exchanger.SecondarySideConnected)
                {
                    Hold(element, port: 3, exchanger.HoldUp2);
                }
            }
        }

        /// <summary>Adds one side's hold-up to whatever node that port is wired to.</summary>
        /// <param name="element">The exchanger.</param>
        /// <param name="port">Its outlet port on that side.</param>
        /// <param name="volume">m³ held, ignored when it is zero or the port reaches no node.</param>
        private void Hold(int element, int port, double volume)
        {
            if (!(volume > 0) || port >= _peerElement[element].Length)
            {
                return;
            }

            var peer = _peerElement[element][port];

            if (peer < 0 || _elements[peer] is not NodeComponent node)
            {
                return;
            }

            for (var index = 0; index < _nodes.Count; index++)
            {
                if (ReferenceEquals(_nodes[index].Component, node))
                {
                    _nodes[index] = _nodes[index] with
                    {
                        ThermalVolume = _nodes[index].ThermalVolume + volume,
                    };

                    return;
                }
            }
        }
    }
}
