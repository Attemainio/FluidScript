using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Language;

namespace FluidScript.Core.Topology;

/// <summary>What an actuator can physically move, for promotion (<c>23</c>, <c>D-133</c>).</summary>
/// <remarks>
/// <para>
/// Three questions, each answered once here and nowhere else. <see cref="Stream"/>: which temperatures a
/// mixing split's <c>position</c> sets -- the stream it mixes, walked along nominal flow (<c>I4</c>) until
/// that stream is next heated or cooled. <see cref="Loops"/>: which flows a pump's <c>head</c> can move --
/// every branch on a cycle through the pump, which is the block of the branch graph the pump's branch lies
/// in. <see cref="Local"/>: what shares the owner's own branch, which is what "nearest" means within a kind
/// (<c>D-130</c>).
/// </para>
/// <para>
/// <strong>Nominal flow is read from the components, not from the branch.</strong> <c>Path</c> order is
/// canonical, not directional (<c>23</c>, <c>C-25</c>); what carries the script's written direction is
/// each two-port component's <c>in</c> and <c>out</c> (the next free port in declared order, <c>12</c>),
/// so a pipe, pump or exchanger on the path orients its branch, and a boundary orients a bare link. A
/// three-way valve's ports are bidirectional; whether it mixes or diverts is read off the first oriented
/// branch at any of its ports, and a valve with no oriented neighbour is taken to mix, which is what a
/// three-way valve in a hydronic circuit almost always does.
/// </para>
/// </remarks>
public static class Reach
{
    /// <summary>The index of a three-way valve's common port among its ports.</summary>
    private const int CommonPort = 0;

    /// <summary>The temperatures a mixing split sets: every component and node on the stream it mixes, by distance.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="split">The three-way valve.</param>
    /// <returns>Each element the stream reaches, with the number of branches walked to it; a split that reaches nothing gives an empty map.</returns>
    /// <remarks>
    /// <para>
    /// A mixing valve's stream leaves its common port; a diverting valve's leaves its two legs and its
    /// temperature is whichever node they recombine at, so both legs are walked. The walk follows nominal
    /// flow: a branch is entered only at its upstream end, or at either end when nothing orients it. It
    /// passes through interior nodes and through another mixing split's legs (the stream is diluted there,
    /// not replaced), and it ends where the stream is next heated or cooled -- at an exchanger, which is
    /// reached (its inlet is the stream's temperature) but not passed -- at a boundary, or at any other
    /// junction element.
    /// </para>
    /// <para>
    /// This is what "a split feeds it" means. A header setpoint downstream of the plant's mixing valve is
    /// on that valve's stream; a consumer's valve drawing from the same header is not, whatever position
    /// it takes, and before <c>D-133</c> it was offered and taken (<c>S-45</c>).
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<IFlowComponent, int> Stream(CircuitGraph graph, ThreeWayValve split)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(split);

        var flow = new NominalFlow(graph);
        var reached = new Dictionary<IFlowComponent, int>(ReferenceEqualityComparer.Instance);
        var walked = new HashSet<Branch>(ReferenceEqualityComparer.Instance);

        foreach (var port in flow.Mixes(split) ? new[] { CommonPort } : Legs(split))
        {
            foreach (var (branch, atFrom) in flow.Attached(split, port))
            {
                Walk(branch, atFrom, 1);
            }
        }

        return reached;

        void Walk(Branch branch, bool fromEnd, int depth)
        {
            if (!walked.Add(branch))
            {
                return;
            }

            // Only downstream: a branch oriented toward the entry end carries the stream away from it.
            var direction = flow.Direction(branch);

            if ((fromEnd && direction < 0) || (!fromEnd && direction > 0))
            {
                return;
            }

            IEnumerable<IFlowComponent> path = fromEnd ? branch.Path : branch.Path.Reverse();

            foreach (var element in path)
            {
                reached.TryAdd(element, depth);

                if (element is HeatExchanger)
                {
                    return;
                }
            }

            var far = fromEnd ? branch.To : branch.From;
            reached.TryAdd(far.Element, depth);

            switch (far.Element)
            {
                case CircuitNode { Boundary: BoundaryRole.Interior } node:
                    foreach (var (next, atFrom) in flow.Attached(node))
                    {
                        Walk(next, atFrom, depth + 1);
                    }

                    break;

                case ThreeWayValve valve when flow.Mixes(valve) && far.Port != CommonPort:
                    foreach (var (next, atFrom) in flow.Attached(valve, CommonPort))
                    {
                        Walk(next, atFrom, depth + 1);
                    }

                    break;

                case ThreeWayValve valve when !flow.Mixes(valve) && far.Port == CommonPort:
                    foreach (var leg in Legs(valve))
                    {
                        foreach (var (next, atFrom) in flow.Attached(valve, leg))
                        {
                            Walk(next, atFrom, depth + 1);
                        }
                    }

                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>The blocks of the branch graph, boundaries grounded: two branches share a block exactly when a cycle runs through both.</summary>
    /// <param name="graph">The graph.</param>
    /// <returns>The partition, whose <see cref="HydraulicBlocks.Share"/> answers whether a pump can move a flow.</returns>
    /// <remarks>
    /// A pump's head appears in the pressure balance of every loop through the pump and in no other, so it
    /// can move a branch's flow only when some loop holds both. Grounding the boundaries makes an open
    /// path a loop through the ground, so a pump anywhere between an inlet and an outlet moves the flow
    /// along it. Two rings joined at one node share no cycle, and a pump on one cannot move the other's
    /// flow however the header pressure is set.
    /// </remarks>
    public static HydraulicBlocks Loops(CircuitGraph graph) =>
        HydraulicBlocks.Build(graph, groundBoundaries: true, static _ => false);

    /// <summary>The elements on the owner's own branches: what "nearest" means within a kind (<c>D-130</c>).</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="component">The constraint's owner, or <see langword="null"/> for an empty set.</param>
    /// <returns>Every element on the path of a branch the owner is on.</returns>
    public static HashSet<IFlowComponent> Local(CircuitGraph graph, IFlowComponent? component)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var local = new HashSet<IFlowComponent>(ReferenceEqualityComparer.Instance);

        if (component is null)
        {
            return local;
        }

        foreach (var branch in graph.Branches)
        {
            if (!branch.Path.Contains(component))
            {
                continue;
            }

            foreach (var element in branch.Path)
            {
                local.Add(element);
            }
        }

        return local;
    }

    /// <summary>The splits at whose legs a branch through the owner ends: their <c>position</c> is the leg's flow.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="component">The constraint's owner.</param>
    /// <returns>Each three-way valve a branch through the owner ends at through <c>a</c> or <c>b</c>.</returns>
    /// <remarks>
    /// A three-way valve's position is the ratio of its two legs' flows, so it is a flow actuator for a
    /// branch ending at a leg -- the source's flow on a pumpless header whose main valve mixes the source's
    /// outlet with the return is set by nothing else. The common port carries the sum, which the position
    /// does not move.
    /// </remarks>
    public static IEnumerable<ThreeWayValve> LegSplits(CircuitGraph graph, IFlowComponent? component)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (component is null)
        {
            yield break;
        }

        foreach (var branch in graph.Branches)
        {
            if (!branch.Path.Contains(component))
            {
                continue;
            }

            if (branch.From is { Element: ThreeWayValve fromSplit, Port: not CommonPort })
            {
                yield return fromSplit;
            }

            if (branch.To is { Element: ThreeWayValve toSplit, Port: not CommonPort })
            {
                yield return toSplit;
            }
        }
    }

    private static int[] Legs(ThreeWayValve split) =>
        split.Ports.Length > 2 ? [1, 2] : [1];

    /// <summary>The written flow direction of every branch, as far as the components state it.</summary>
    private sealed class NominalFlow
    {
        private readonly CircuitGraph _graph;
        private readonly Dictionary<IFlowComponent, int> _index;
        private readonly Dictionary<Branch, int> _direction = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<ThreeWayValve, bool> _mixes = new(ReferenceEqualityComparer.Instance);

        public NominalFlow(CircuitGraph graph)
        {
            _graph = graph;
            _index = new Dictionary<IFlowComponent, int>(ReferenceEqualityComparer.Instance);

            for (var i = 0; i < graph.Components.Length; i++)
            {
                _index.TryAdd(graph.Components[i], i);
            }
        }

        /// <summary>Whether the valve mixes (its stream leaves the common port) rather than diverts.</summary>
        public bool Mixes(ThreeWayValve split)
        {
            if (_mixes.TryGetValue(split, out var mixes))
            {
                return mixes;
            }

            // Assumed to mix until an oriented branch says otherwise; written before the lookups so that a
            // ring of splits joined by bare links terminates.
            _mixes[split] = true;

            for (var port = 0; port < split.Ports.Length; port++)
            {
                foreach (var (branch, atFrom) in Attached(split, port))
                {
                    var direction = Oriented(branch);

                    if (direction == 0)
                    {
                        continue;
                    }

                    var leaves = atFrom ? direction > 0 : direction < 0;
                    _mixes[split] = port == CommonPort ? leaves : !leaves;
                    return _mixes[split];
                }
            }

            return true;
        }

        /// <summary>+1 when nominal flow runs <c>From</c> to <c>To</c>, −1 when the reverse, 0 when nothing says.</summary>
        public int Direction(Branch branch)
        {
            if (_direction.TryGetValue(branch, out var known))
            {
                return known;
            }

            var direction = Oriented(branch);

            // A bare link at a split's port takes the split's mode: a mixing valve's common port sends, its
            // legs receive.
            if (direction == 0 && branch.From.Element is ThreeWayValve from)
            {
                direction = (Mixes(from) == (branch.From.Port == CommonPort)) ? 1 : -1;
            }

            if (direction == 0 && branch.To.Element is ThreeWayValve to)
            {
                direction = (Mixes(to) == (branch.To.Port == CommonPort)) ? -1 : 1;
            }

            _direction[branch] = direction;
            return direction;
        }

        /// <summary>The branches at one port of an element, and whether the element is their <c>From</c> end.</summary>
        public IEnumerable<(Branch Branch, bool AtFrom)> Attached(IFlowComponent element, int port)
        {
            foreach (var branch in _graph.Branches)
            {
                if (ReferenceEquals(branch.From.Element, element) && branch.From.Port == port)
                {
                    yield return (branch, true);
                }

                if (ReferenceEquals(branch.To.Element, element) && branch.To.Port == port)
                {
                    yield return (branch, false);
                }
            }
        }

        /// <summary>Every branch at a node, whichever port.</summary>
        public IEnumerable<(Branch Branch, bool AtFrom)> Attached(CircuitNode node)
        {
            foreach (var branch in _graph.Branches)
            {
                if (ReferenceEquals(branch.From.Element, node))
                {
                    yield return (branch, true);
                }

                if (ReferenceEquals(branch.To.Element, node))
                {
                    yield return (branch, false);
                }
            }
        }

        /// <summary>What the branch's own contents say: the first oriented component on the path, else a boundary at an end.</summary>
        private int Oriented(Branch branch)
        {
            var previous = branch.From.Element;

            foreach (var element in branch.Path)
            {
                var facing = Facing(element, previous);

                if (facing != 0)
                {
                    return facing;
                }

                previous = element;
            }

            return (branch.From.Element, branch.To.Element) switch
            {
                (CircuitNode { Boundary: BoundaryRole.Inlet }, _) => 1,
                (CircuitNode { Boundary: BoundaryRole.Outlet }, _) => -1,
                (_, CircuitNode { Boundary: BoundaryRole.Inlet }) => -1,
                (_, CircuitNode { Boundary: BoundaryRole.Outlet }) => 1,
                _ => 0,
            };
        }

        /// <summary>+1 when the element's inlet faces its predecessor on the path, −1 when its outlet does, 0 for a bidirectional element.</summary>
        private int Facing(IFlowComponent element, IFlowComponent previous)
        {
            if (!_index.TryGetValue(element, out var self) || !_index.TryGetValue(previous, out var before))
            {
                return 0;
            }

            for (var port = 0; port < element.Ports.Length; port++)
            {
                if (_graph.Adjacency.Peer(self, port).Component != before)
                {
                    continue;
                }

                switch (element.Ports[port].Role)
                {
                    case PortRole.Inlet:
                        return 1;
                    case PortRole.Outlet:
                        return -1;
                    default:
                        break;
                }
            }

            return 0;
        }
    }
}
