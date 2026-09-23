using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Topology.Construction;

public static partial class Lowering
{
    private sealed partial class Build
    {
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

            // Every link claims two fresh slots: the binder refuses a second connection to a port
            // (`FS1506`), and a node hands out a new slot per use, so no assignment here overwrites one.
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

        // A lookup, not a scan of the semantic model: this runs twice per connection, and a scan made
        // resolving the links quadratic in the script. Every node exists by the time links resolve, and
        // an expansion's own nodes carry a generated name no endpoint can spell.
        private bool IsNode(string component) =>
            _byName.TryGetValue(component, out var element) && _elements[element] is CircuitNode;

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
