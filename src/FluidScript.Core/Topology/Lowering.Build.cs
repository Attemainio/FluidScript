using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Binding;
using FluidScript.Core.Components;

namespace FluidScript.Core.Topology;

public static partial class Lowering
{
    /// <summary>The mutable working state of one lowering, from bound symbols to a decomposed graph.</summary>
    /// <remarks>
    /// <para>
    /// A class rather than a chain of pure functions because every pass reads what the last one built
    /// and the shapes are index-parallel — element <c>e</c>'s ports, peers and origin all live at the
    /// same offset. Threading five arrays through five static methods would be the same state with a
    /// wider signature.
    /// </para>
    /// <para>
    /// <strong>Every pass walks the model in declaration order</strong>, and nothing here iterates a
    /// dictionary. That is what invariant 6 asks for: lowering the same model twice must yield graphs
    /// equal including node ordering, because the renderer's placement memory and the solver's variable
    /// ordering both key off it.
    /// </para>
    /// </remarks>
    private sealed class Build(SemanticModel model, IComponentFactory factory)
    {
        /// <summary>Separates an expansion's generated names from anything a script can write.</summary>
        /// <remarks>
        /// <c>#</c> is not an identifier character, so <c>P1#s2</c> cannot collide with a declared
        /// component however a script is written. The binder's own generated names use <c>__</c>,
        /// which a user can type.
        /// </remarks>
        private const char Generated = '#';

        private readonly List<IFlowComponent> _elements = [];
        private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);
        private readonly List<GraphNode> _nodes = [];
        private readonly List<(int Element, int Port, int Peer, int PeerPort)> _links = [];
        private readonly Dictionary<string, int> _degree = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _nextPort = new(StringComparer.Ordinal);
        private readonly ImmutableDictionary<string, string>.Builder _circuits =
            ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        private readonly ImmutableArray<ComponentGroup>.Builder _groups =
            ImmutableArray.CreateBuilder<ComponentGroup>();
        private readonly ImmutableArray<string>.Builder _unresolved = ImmutableArray.CreateBuilder<string>();
        private readonly HashSet<int> _replaced = [];

        private int[][] _peerElement = [];
        private int[][] _peerPort = [];

        public ImmutableArray<GraphNode> Nodes => [.. _nodes];

        public ImmutableArray<IFlowComponent> Components => [.. _elements];

        /// <summary>The port-to-port table <see cref="Connect"/> built.</summary>
        /// <value>
        /// Empty before <see cref="Connect"/> runs. It is the same data the walk in
        /// <see cref="Decompose"/> reads, published rather than kept private: the solver needs to know
        /// which node a port touches, and nothing else in the graph records it (<c>S-10</c>).
        /// </value>
        public PortAdjacency Adjacency
        {
            get
            {
                var rows = ImmutableArray.CreateBuilder<ImmutableArray<PortRef>>(_peerElement.Length);

                for (var element = 0; element < _peerElement.Length; element++)
                {
                    var row = ImmutableArray.CreateBuilder<PortRef>(_peerElement[element].Length);

                    for (var port = 0; port < _peerElement[element].Length; port++)
                    {
                        row.Add(
                            _peerElement[element][port] < 0
                                ? PortRef.None
                                : new PortRef(_peerElement[element][port], _peerPort[element][port]));
                    }

                    rows.Add(row.MoveToImmutable());
                }

                return new PortAdjacency(rows.MoveToImmutable());
            }
        }

        public ImmutableArray<ComponentGroup> Groups => _groups.ToImmutable();

        public ImmutableDictionary<string, string> CircuitOf => _circuits.ToImmutable();

        public ImmutableArray<string> Unresolved => _unresolved.ToImmutable();

        /// <summary>Counts how many connections each node has, which is how many ports it gets.</summary>
        /// <remarks>
        /// A node's ports are unnamed and positional (<c>15</c>), so nothing in the semantic model says
        /// how many it has — only the connection list does. The count decides both the port count and
        /// whether the node carries a mass balance, so it has to come before the node is constructed.
        /// </remarks>
        public void CountNodePorts()
        {
            foreach (var connection in model.Connections)
            {
                Count(connection.From.Component);
                Count(connection.To.Component);
            }

            void Count(string component)
            {
                if (IsNode(component))
                {
                    _degree[component] = _degree.GetValueOrDefault(component) + 1;
                }
            }
        }

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
                        new CircuitNode(symbol.Name, degree, degree >= 3 || degree == 1)
                        {
                            StatedParameters = ComponentFactory.Stated(symbol),
                            Boundary = Role(kind),
                        },
                        symbol.CircuitName,
                        symbol.Origin is Origin.Declared ? NodeOrigin.Declared : NodeOrigin.Inferred);
                    continue;
                }

                if (factory.Create(symbol) is not { } component)
                {
                    _unresolved.Add(symbol.Name);
                    continue;
                }

                Add(component, symbol.CircuitName, origin: null);
            }
        }

        /// <summary>Which end of an open circuit a node kind declares itself to be.</summary>
        /// <param name="kind">The registry entry the script named.</param>
        /// <returns>The role, or <see cref="BoundaryRole.Interior"/> for a plain node.</returns>
        /// <remarks>
        /// Read from the keyword because that is the only place it exists: a <c>return</c> and a bare
        /// terminal <c>node</c> have identical parameters and opposite mass balances (<c>D-64</c>). This
        /// is the one point where lowering reads a kind's spelling, and it reads it from the registry
        /// entry rather than from the script, so an alias resolves before it gets here.
        /// </remarks>
        private static BoundaryRole Role(Language.ComponentKindInfo kind) => kind.Keyword switch
        {
            "supply" => BoundaryRole.Supply,
            "return" => BoundaryRole.Return,
            _ => BoundaryRole.Interior,
        };

        /// <summary>Turns each connection into a link between two port slots.</summary>
        /// <remarks>
        /// A node's port index comes from its position in this walk, which is why the walk order is the
        /// connection order and not anything derived. A link touching a component that was not built is
        /// dropped, but the node counter still advances — so a node's ports keep the indices the degree
        /// count gave them, and one is simply left with no peer.
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
                }
            }
        }

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
        /// <strong>Length and elevation divide; minor loss does not.</strong> A stated <c>K</c> is a
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
                    || _elements[index] is not Pipe pipe
                    || Cells(symbol) is not { } cells)
                {
                    continue;
                }

                Expand(index, pipe, cells, symbol.CircuitName);
            }
        }

        /// <summary>Builds the port-slot adjacency every later pass walks.</summary>
        public void Connect()
        {
            _peerElement = new int[_elements.Count][];
            _peerPort = new int[_elements.Count][];

            for (var element = 0; element < _elements.Count; element++)
            {
                var ports = _elements[element].Ports.Length;
                _peerElement[element] = new int[ports];
                _peerPort[element] = new int[ports];

                Array.Fill(_peerElement[element], -1);
                Array.Fill(_peerPort[element], -1);
            }

            foreach (var (element, port, peer, peerPort) in _links)
            {
                if (port >= _peerElement[element].Length || peerPort >= _peerElement[peer].Length)
                {
                    continue;
                }

                _peerElement[element][port] = peer;
                _peerPort[element][port] = peerPort;
                _peerElement[peer][peerPort] = element;
                _peerPort[peer][peerPort] = port;
            }
        }

        /// <summary>The vertices of the branch graph, in element order.</summary>
        /// <returns>Every junction element, and one cut vertex per component that has none.</returns>
        /// <remarks>
        /// <para>
        /// <strong>A ring of pass-throughs has no vertex of its own</strong>, and a branch graph with no
        /// vertices has no branches — so <c>N1 - PU1 - N2 - HE1 - N3 - CV1 - N4 - P1 - N1</c>, which is
        /// the whole of <c>samples/m2-simple-loop.fluid</c>, would otherwise lower to a graph with no
        /// flow unknown at all and no loop for <c>FS2214</c> to name. Every node on such a ring has
        /// degree two, and <see cref="CircuitGraph.IsJunctionElement"/> is right that none of them is a
        /// junction; what is wrong is concluding from that there is nothing to solve.
        /// </para>
        /// <para>
        /// The ring is cut at one node, which becomes a vertex with a branch leaving and re-entering it.
        /// <c>B − V + 1 = 1 − 1 + 1</c> is then the one loop the user wrote. Where the cut falls changes
        /// no unknown and no equation — the branch and the cycle are the same wherever it is — so it only
        /// has to be deterministic, and the lowest-indexed node of the component is that.
        /// </para>
        /// <para>
        /// The cut node keeps the <c>carriesMassBalance: false</c> its degree of two gave it, which is
        /// correct rather than an oversight: the branch's single flow already makes that balance an
        /// identity, and a closed ring is exactly the case where an auto-picked datum supplies the
        /// equation the redundant balance would have.
        /// </para>
        /// </remarks>
        public ImmutableArray<IFlowComponent> JunctionElements()
        {
            var isVertex = new bool[_elements.Count];
            var visited = new bool[_elements.Count][];

            for (var element = 0; element < _elements.Count; element++)
            {
                visited[element] = new bool[_elements[element].Ports.Length];
                isVertex[element] = CircuitGraph.IsJunctionElement(_elements[element]);
            }

            for (var element = 0; element < _elements.Count; element++)
            {
                if (isVertex[element])
                {
                    Spread(element, port: -1, visited, members: null);
                }
            }

            // A slot at a time, not an element at a time. Seeding every port of a component would
            // enter both flow groups of a coupled exchanger at once and spread across it, merging the
            // two circuits it separates into one -- and one cut vertex would then serve a ring that is
            // really two. A port with no peer is skipped: a duty exchanger's unwired second side is not
            // a hydraulic component waiting for a vertex.
            for (var element = 0; element < _elements.Count; element++)
            {
                for (var port = 0; port < visited[element].Length; port++)
                {
                    if (visited[element][port] || _peerElement[element][port] < 0)
                    {
                        continue;
                    }

                    var members = new List<int>();
                    Spread(element, port, visited, members);
                    members.Sort();

                    var cut = members.FirstOrDefault(member => _elements[member] is CircuitNode, -1);
                    isVertex[cut < 0 ? members[0] : cut] = true;
                }
            }

            var junctions = ImmutableArray.CreateBuilder<IFlowComponent>();

            for (var element = 0; element < _elements.Count; element++)
            {
                if (isVertex[element])
                {
                    junctions.Add(_elements[element]);
                }
            }

            return junctions.ToImmutable();
        }

        /// <summary>Marks every port slot flow reaches from one, and optionally lists the elements.</summary>
        /// <param name="element">Where to start.</param>
        /// <param name="port">The slot to enter, or a negative number to enter every slot.</param>
        /// <param name="visited">Slot marks, read and written.</param>
        /// <param name="members">Collects the elements reached, or <see langword="null"/> for marks alone.</param>
        /// <remarks>
        /// <strong>Slots rather than elements, because one component can belong to two hydraulic
        /// components.</strong> A coupled exchanger's side-1 ports are reachable from one circuit and its
        /// side-2 ports from the other; marking the exchanger itself would make the second side look
        /// already covered, and the substation's secondary would vanish. That is the case <c>D-17</c>
        /// exists for, and the reason this walks a port at a time — including at the seed, where
        /// entering every port of a two-sided component would join the very things it separates.
        /// </remarks>
        private void Spread(int element, int port, bool[][] visited, List<int>? members)
        {
            var queue = new Queue<(int Element, int Port)>();

            for (var slot = 0; slot < visited[element].Length; slot++)
            {
                if ((port >= 0 && slot != port) || visited[element][slot])
                {
                    continue;
                }

                visited[element][slot] = true;
                queue.Enqueue((element, slot));
            }

            members?.Add(element);

            members?.Add(element);

            while (queue.Count > 0)
            {
                var (current, slot) = queue.Dequeue();
                var groups = _elements[current].FlowGroups;

                // Across the component, but only within this port's flow group: fluid crosses a pump
                // from inlet to outlet and never crosses an exchanger from one side to the other.
                for (var other = 0; other < groups.Length; other++)
                {
                    if (other != slot && groups[other] == groups[slot] && !visited[current][other])
                    {
                        visited[current][other] = true;
                        queue.Enqueue((current, other));
                    }
                }

                var peer = _peerElement[current][slot];

                if (peer < 0)
                {
                    continue;
                }

                var peerPort = _peerPort[current][slot];

                if (visited[peer][peerPort])
                {
                    continue;
                }

                visited[peer][peerPort] = true;
                members?.Add(peer);
                queue.Enqueue((peer, peerPort));
            }
        }

        /// <summary>Walks every maximal path between junction elements.</summary>
        /// <param name="junctions">The vertices, as <see cref="JunctionElements"/> found them.</param>
        /// <returns>One branch per path, each carrying one flow unknown.</returns>
        /// <remarks>
        /// <para>
        /// The walk crosses a pass-through component by <em>flow group</em>, not by port order: leaving
        /// a port means entering the other port of that port's group. A coupled exchanger is two groups
        /// of two, so a walk entering at <c>in</c> leaves at <c>out</c> and never crosses to side 2 —
        /// which is why the same component appears in two branches and <c>Path</c> is not a partition.
        /// </para>
        /// <para>
        /// Each slot is marked as it is crossed, so the branch found walking from one end is not found
        /// again from the other.
        /// </para>
        /// </remarks>
        public ImmutableArray<Branch> Decompose(ImmutableArray<IFlowComponent> junctions)
        {
            var isJunction = new bool[_elements.Count];
            foreach (var junction in junctions)
            {
                isJunction[_byName[junction.Name]] = true;
            }

            var walked = new bool[_elements.Count][];
            for (var element = 0; element < _elements.Count; element++)
            {
                walked[element] = new bool[_elements[element].Ports.Length];
            }

            var branches = ImmutableArray.CreateBuilder<Branch>();
            var path = ImmutableArray.CreateBuilder<IFlowComponent>();

            for (var element = 0; element < _elements.Count; element++)
            {
                if (!isJunction[element])
                {
                    continue;
                }

                for (var port = 0; port < _elements[element].Ports.Length; port++)
                {
                    if (walked[element][port] || _peerElement[element][port] < 0)
                    {
                        continue;
                    }

                    path.Clear();
                    walked[element][port] = true;

                    var current = element;
                    var exit = port;

                    while (true)
                    {
                        var next = _peerElement[current][exit];
                        var entry = _peerPort[current][exit];

                        walked[next][entry] = true;

                        if (isJunction[next])
                        {
                            branches.Add(new Branch
                            {
                                From = End(element, port),
                                To = End(next, entry),
                                Path = path.ToImmutable(),
                                Index = branches.Count,
                            });
                            break;
                        }

                        path.Add(_elements[next]);

                        // The other port of this port's flow group. A pass-through has exactly two per
                        // group by construction, which is what makes it one.
                        var groups = _elements[next].FlowGroups;
                        var partner = -1;

                        for (var candidate = 0; candidate < groups.Length; candidate++)
                        {
                            if (candidate != entry && groups[candidate] == groups[entry])
                            {
                                partner = candidate;
                                break;
                            }
                        }

                        if (partner < 0 || _peerElement[next][partner] < 0)
                        {
                            // A pass-through with nothing on its far side. Inference rule I3 normally
                            // terminates these, so reaching it means the far port was dropped with an
                            // unbuilt component; the branch ends where the graph does.
                            branches.Add(new Branch
                            {
                                From = End(element, port),
                                To = End(next, partner < 0 ? entry : partner),
                                Path = path.ToImmutable(),
                                Index = branches.Count,
                            });
                            break;
                        }

                        walked[next][partner] = true;
                        current = next;
                        exit = partner;
                    }
                }
            }

            return branches.ToImmutable();
        }

        private BranchEnd End(int element, int port)
        {
            var component = _elements[element];

            return new BranchEnd
            {
                Element = component,
                Port = port,

                // A node's ports are unnamed and interchangeable, so there is nothing to report but the
                // node; a three-way valve's `a`, `b` and `c` are the whole content of a branch row.
                PortName = component is CircuitNode ? null : component.Ports[port].Name,
            };
        }

        private bool IsNode(string component) =>
            model.Components.Any(symbol =>
                string.Equals(symbol.Name, component, StringComparison.Ordinal)
                && symbol.Kind?.HasUnlimitedPorts == true);

        private void Add(IFlowComponent component, string circuit, NodeOrigin? origin)
        {
            _byName[component.Name] = _elements.Count;
            _elements.Add(component);
            _circuits[component.Name] = circuit;

            if (origin is { } kind && component is CircuitNode node)
            {
                _nodes.Add(new GraphNode { Name = node.Name, Component = node, Origin = kind });
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

        private void Expand(int index, Pipe pipe, int cells, string circuit)
        {
            var members = ImmutableArray.CreateBuilder<string>();
            var segments = new int[cells + 1];

            for (var segment = 0; segment <= cells; segment++)
            {
                var name = $"{pipe.Name}{Generated}s{(segment + 1).ToString(CultureInfo.InvariantCulture)}";

                segments[segment] = _elements.Count;
                Add(
                    new Pipe(
                        name,
                        pipe.Length / (cells + 1),
                        pipe.InsideDiameter,
                        pipe.Roughness,

                        // The whole stated K on the first sub-pipe: a fitting is somewhere along the
                        // run, not a property per metre.
                        segment == 0 ? pipe.MinorLoss : 0,
                        pipe.Elevation / (cells + 1)),
                    circuit,
                    origin: null);
                members.Add(name);
            }

            var volume = pipe.FlowArea * pipe.Length;

            for (var cell = 0; cell < cells; cell++)
            {
                var name = $"{pipe.Name}{Generated}n{(cell + 1).ToString(CultureInfo.InvariantCulture)}";
                var node = new CircuitNode(name, portCount: 2, carriesMassBalance: false);

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
        public void Prune()
        {
            if (_replaced.Count == 0)
            {
                return;
            }

            var moved = new int[_elements.Count];
            var kept = new List<IFlowComponent>(_elements.Count - _replaced.Count);

            for (var element = 0; element < _elements.Count; element++)
            {
                if (_replaced.Contains(element))
                {
                    moved[element] = -1;
                    _byName.Remove(_elements[element].Name);
                    continue;
                }

                moved[element] = kept.Count;
                _byName[_elements[element].Name] = kept.Count;
                kept.Add(_elements[element]);
            }

            _elements.Clear();
            _elements.AddRange(kept);

            for (var link = _links.Count - 1; link >= 0; link--)
            {
                var (element, port, peer, peerPort) = _links[link];

                if (moved[element] < 0 || moved[peer] < 0)
                {
                    _links.RemoveAt(link);
                    continue;
                }

                _links[link] = (moved[element], port, moved[peer], peerPort);
            }
        }
    }
}
