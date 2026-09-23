using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Topology.Construction;

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
    private sealed partial class Build(SemanticModel model, IComponentFactory factory)
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
        private readonly Dictionary<string, List<string>> _namedPorts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _nextPort = new(StringComparer.Ordinal);
        private readonly ImmutableDictionary<string, string>.Builder _circuits =
            ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        private readonly ImmutableArray<ComponentGroup>.Builder _groups =
            ImmutableArray.CreateBuilder<ComponentGroup>();
        private readonly ImmutableArray<string>.Builder _unresolved = ImmutableArray.CreateBuilder<string>();
        private readonly HashSet<int> _replaced = [];
        private readonly HashSet<string> _stated = new(StringComparer.Ordinal);
        private readonly List<Setpoint> _setpoints = [];

        private int[][] _peerElement = [];
        private int[][] _peerPort = [];
        public ImmutableArray<GraphNode> Nodes => [.. _nodes];

        public ImmutableArray<IFlowComponent> Components => [.. _elements];

        /// <summary>Every <c>control</c> line's setpoint, applied to the design solve or not (<c>D-141</c>).</summary>
        public ImmutableArray<Setpoint> Setpoints => [.. _setpoints];

        /// <summary>The ports the script itself named, as <c>component.port</c> (<c>D-88</c>).</summary>
        /// <value>
        /// One entry per qualified endpoint. An unqualified one is absent even though it still resolved
        /// to a port, which is the whole distinction: positional binding hands out real port names that
        /// carry no user intent.
        /// </value>
        public ImmutableHashSet<string> StatedPorts => [.. _stated];

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
    }
}
