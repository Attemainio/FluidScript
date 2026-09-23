using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Sizing.Flows;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Solvers.Seeding;

public static partial class SolutionSeed
{
    private sealed partial class Field
    {
            /// <summary>Which way a branch's estimated flow points, in the branch's own orientation.</summary>
            /// <param name="branch">The branch.</param>
            /// <param name="estimate">The magnitude estimate and its provenance.</param>
            /// <returns>+1 along <see cref="Branch.From"/> to <see cref="Branch.To"/>, -1 against it.</returns>
            /// <remarks>
            /// A pump settles the branch containing it. A stated exchanger inlet or outlet supplies the same
            /// seed-only clue when no source pump exists. A propagated branch meeting a junction next to a
            /// pumped branch starts opposite that pump at the junction. Connections with no directional
            /// evidence retain +1; none of these choices constrains the eventual signed solution.
            /// </remarks>
            private int Orientation(Branch branch, BranchFlow estimate)
            {
                var direct = PumpDirection(branch) ?? TemperatureDirection(branch);
                if (direct.HasValue)
                {
                    return direct.Value;
                }

                if (estimate.Basis is FlowBasis.Propagated or FlowBasis.Partitioned)
                {
                    foreach (var junction in new[] { branch.From.Element, branch.To.Element })
                    {
                        foreach (var candidate in graph.Branches)
                        {
                            if (candidate.Index == branch.Index
                                || !ReferenceEquals(candidate.From.Element, junction)
                                    && !ReferenceEquals(candidate.To.Element, junction))
                            {
                                continue;
                            }

                            var driven = PumpDirection(candidate);
                            if (!driven.HasValue)
                            {
                                continue;
                            }

                            var pumpEnters = ReferenceEquals(candidate.To.Element, junction)
                                ? driven.Value > 0
                                : driven.Value < 0;
                            var branchEnters = !pumpEnters;

                            return ReferenceEquals(branch.To.Element, junction)
                                ? branchEnters ? +1 : -1
                                : branchEnters ? -1 : +1;
                        }
                    }
                }

                return +1;

                int? PumpDirection(Branch candidate)
                {
                    for (var step = 0; step < candidate.Path.Length; step++)
                    {
                        if (candidate.Path[step] is Pump pump)
                        {
                            var direction = PortDirection(candidate, step, pump, sideOneOnly: false);
                            if (direction.HasValue)
                            {
                                return direction;
                            }
                        }
                    }

                    return null;
                }

                int? TemperatureDirection(Branch candidate)
                {
                    for (var step = 0; step < candidate.Path.Length; step++)
                    {
                        if (candidate.Path[step] is HeatExchanger exchanger
                            && (Ownership.Of(exchanger, "in") is ParameterState.Stated
                                || Ownership.Of(exchanger, "out") is ParameterState.Stated))
                        {
                            var direction = PortDirection(candidate, step, exchanger, sideOneOnly: true);
                            if (direction.HasValue)
                            {
                                return direction;
                            }
                        }
                    }

                    return null;
                }

                int? PortDirection(
                    Branch candidate,
                    int step,
                    IFlowComponent component,
                    bool sideOneOnly)
                {
                    if (!_componentOf.TryGetValue(component, out var element)
                        || element >= graph.Adjacency.ComponentCount)
                    {
                        return null;
                    }

                    var before = step > 0 ? candidate.Path[step - 1] : candidate.From.Element;
                    var after =
                        step + 1 < candidate.Path.Length ? candidate.Path[step + 1] : candidate.To.Element;

                    for (var port = 0; port < component.Ports.Length; port++)
                    {
                        if (sideOneOnly && component.Ports[port].Name is not ("in" or "out"))
                        {
                            continue;
                        }

                        var peer = graph.Adjacency.Peer(element, port);
                        if (!peer.Exists)
                        {
                            continue;
                        }

                        var neighbour = graph.Components[peer.Component];
                        var downstream = component.Ports[port].Role is PortRole.Outlet;

                        if (ReferenceEquals(neighbour, after))
                        {
                            return downstream ? +1 : -1;
                        }

                        if (ReferenceEquals(neighbour, before))
                        {
                            return downstream ? -1 : +1;
                        }
                    }

                    return null;
                }
            }

        /// <summary>Builds a spanning forest over the incidence lists already attached, keeping the strongest estimates as chords.</summary>
        /// <param name="estimates">One estimate per branch, whose <see cref="FlowBasis"/> is the branch's cost as a tree edge.</param>
        /// <param name="barred">Branches held back from parenting a vertex, by branch index.</param>
            /// <param name="chords">Receives every branch the forest did not use.</param>
            /// <remarks>
            /// <para>
            /// A branch is incident to its <see cref="Branch.From"/> end with sign −1 and to its
            /// <see cref="Branch.To"/> end with +1, matching <see cref="PortMap"/>'s convention so that a
            /// balance written here and a residual written there mean the same thing. A branch whose ends
            /// are the same vertex — a ring with one cut vertex is exactly this — lands twice with
            /// opposite signs and cancels, which is correct: a self-loop moves no mass across its vertex.
            /// <see cref="Solve"/> attaches them, once, before the first of possibly several spans.
            /// </para>
            /// <para>
            /// <strong>The forest is not arbitrary at a three-way valve: it has to reach one by its common
            /// port</strong> (<c>S-49</c>). <see cref="Solve"/> gives every chord a free magnitude and
            /// <em>solves</em> each vertex's parent branch to close that vertex's balance, so the parent
            /// branch is the one that ends up carrying what the others sum to. At a three-way valve that
            /// is the definition of the common port, and reaching the valve by a switched leg instead
            /// applies the same arithmetic to the wrong port: the field still balances, and it describes a
            /// valve whose <c>a</c> leg is the common one.
            /// </para>
            /// <para>
            /// <strong>Measured on the injection header</strong>, where the plain breadth-first walk
            /// reached <c>TV_AHU</c> through <c>PA2</c> and seeded it <c>ab</c> 0.2871, <c>a</c> 0.5742,
            /// <c>b</c> 0.2871 kg/s — a diverting split on a valve wired for mixing. Its Kv law then sat at
            /// a scaled residual of −3.345, an order of magnitude above everything else in the system, and
            /// the first Newton step walked a node out of the water domain. <c>C-66</c> measured the same
            /// two numbers from the other side and taught the <em>sizer</em> not to believe them.
            /// </para>
            /// <para>
            /// So an edge that would claim a valve by anything but its common port is <em>held</em> rather
            /// than taken, and used only once no preferred edge is left. A valve reachable no other way
            /// still gets attached, which keeps this a preference rather than a constraint the topology
            /// could contradict.
            /// </para>
            /// <para>
            /// <strong>A barred branch is held the same way</strong> (<c>S-51</c>), for the same reason
            /// and with the same escape: <see cref="Solve"/> bars whatever the last forest solved to a
            /// standstill, and a branch that is the only way into its vertex is attached regardless.
            /// </para>
            /// </remarks>
        private void Span(ImmutableArray<BranchFlow> estimates, bool[] barred, out List<int> chords)
        {
            _parent = new int[_vertices.Count];
            _component = new int[_vertices.Count];
            Array.Fill(_parent, -1);
            Array.Fill(_component, -1);

            var common = new int[_vertices.Count];

            for (var vertex = 0; vertex < _vertices.Count; vertex++)
            {
                common[vertex] = CommonBranch(vertex);
            }

            // What a branch costs as a tree edge. The basis is the ordinary weight; a switched leg of a
            // valve and a barred branch are penalties ordered so that either outweighs any basis, and the
            // bar outweighs the leg. The leg's penalty is charged whichever way the branch would be walked:
            // a cost that depended on direction made the greedy walk take a valve's `b` leg outward for 2
            // and then have nothing cheaper than a rated `a` leg to go on with (`S-68`), where the same
            // ring spanned by every coil and every `a` leg costs less and keeps every rating.
            int Cost(int branch) =>
                (int)estimates[branch].Basis
                + (IsSwitchedLeg(branch) ? SwitchedLegPenalty : 0)
                + (barred[branch] ? BarredPenalty : 0);

            bool IsSwitchedLeg(int branch)
            {
                var from = _vertexOf[graph.Branches[branch].From.Element];
                var to = _vertexOf[graph.Branches[branch].To.Element];

                return (common[from] >= 0 && common[from] != branch) || (common[to] >= 0 && common[to] != branch);
            }

            var used = new bool[graph.Branches.Length];
            var components = 0;

            for (var root = 0; root < _vertices.Count; root++)
            {
                if (_component[root] >= 0)
                {
                    continue;
                }

                var component = components++;
                var first = _order.Count;

                _component[root] = component;
                _order.Add(root);

                // Prim's walk: of every branch leading out of what is reached so far, take the cheapest,
                // ties to the lower index. Every vertex is added after its parent, which is the order the
                // leaves-inward solve reads back.
                while (true)
                {
                    var bestCost = int.MaxValue;
                    var bestBranch = -1;
                    var bestTarget = -1;

                    for (var at = first; at < _order.Count; at++)
                    {
                        foreach (var (branch, sign) in _incident[_order[at]])
                        {
                            var other = Other(branch, sign);

                            if (_component[other] >= 0)
                            {
                                continue;
                            }

                            var cost = Cost(branch);

                            if (cost < bestCost || (cost == bestCost && branch < bestBranch))
                            {
                                bestCost = cost;
                                bestBranch = branch;
                                bestTarget = other;
                            }
                        }
                    }

                    if (bestBranch < 0)
                    {
                        break;
                    }

                    used[bestBranch] = true;
                    _parent[bestTarget] = bestBranch;
                    _component[bestTarget] = component;
                    _order.Add(bestTarget);
                }
            }

            chords = [];

            for (var branch = 0; branch < used.Length; branch++)
            {
                if (!used[branch])
                {
                    chords.Add(branch);
                }
            }
        }

            /// <summary>The branch meeting a three-way valve's common port, or -1 for any other vertex.</summary>
            /// <param name="vertex">The vertex to classify.</param>
            /// <returns>A branch index, or -1 when the vertex is not a three-way valve with a named common port.</returns>
            /// <remarks>
            /// The port name is read directly, which <c>D-88</c> settles for the common port specifically:
            /// <c>ab</c> is port 0, so positional binding gives it to the first connection written, and that
            /// is the common leg for a mixing and a diverting arrangement alike. The switched legs are the
            /// ones whose inferred letters mean nothing, and nothing here reads them.
            /// </remarks>
            private int CommonBranch(int vertex)
            {
                if (_vertices[vertex] is not ThreeWayValve { BypassConnected: true } valve)
                {
                    return -1;
                }

                foreach (var (branch, _) in _incident[vertex])
                {
                    if (string.Equals(
                        ValveLegs.PortName(graph.Branches[branch], valve), "ab", StringComparison.Ordinal))
                    {
                        return branch;
                    }
                }

                return -1;
            }

        /// <summary>Records one end of a branch against the vertex it meets.</summary>
        /// <param name="element">The junction element at that end.</param>
        /// <param name="branch">The branch's index.</param>
        /// <param name="sign">−1 where the branch leaves, +1 where it arrives.</param>
        private void Attach(IFlowComponent element, int branch, int sign)
        {
            if (!_vertexOf.TryGetValue(element, out var vertex))
            {
                vertex = _vertices.Count;

                _vertexOf[element] = vertex;
                _vertices.Add(element);
                _incident.Add([]);
            }

            _incident[vertex].Add((branch, sign));
        }

        /// <summary>The vertex at a branch's other end, seen from one of its incidence entries.</summary>
        /// <param name="branch">The branch's index.</param>
        /// <param name="sign">The sign this end carries.</param>
        /// <returns>The vertex index at the far end, which is this one for a self-loop.</returns>
        private int Other(int branch, int sign) =>
            _vertexOf[sign < 0 ? graph.Branches[branch].To.Element : graph.Branches[branch].From.Element];
    }
}
