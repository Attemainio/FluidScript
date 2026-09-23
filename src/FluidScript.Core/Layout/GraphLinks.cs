using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Language.Binding;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Layout;

/// <summary>The links a layout draws: every model connection matched on the graph's adjacency, then the links between an expanded pipe's cells.</summary>
internal static class GraphLinks
{
    /// <summary>Each model connection as component and port indices, then the links along every pipe with <c>nodes=</c>.</summary>
    /// <remarks>
    /// A written end naming a pipe with <c>nodes=</c> stands for the cell that meets the other end, and
    /// its port is whichever meets it, since a cell's port names are the chain's and not the script's
    /// (<c>C-124</c>).
    /// </remarks>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="model">The bound model, for the connections as written.</param>
    /// <param name="index">Component name to graph index.</param>
    /// <returns>The links, written connections first in model order.</returns>
    public static ImmutableArray<GraphLink> Of(CircuitGraph graph, SemanticModel model, IReadOnlyDictionary<string, int> index)
    {
        var links = ImmutableArray.CreateBuilder<GraphLink>();
        var used = new HashSet<(int, int)>();

        for (var i = 0; i < model.Connections.Length; i++)
        {
            var connection = model.Connections[i];
            var fromName = ExpandedPipes.Drawn(graph, connection.From.Component, connection.To.Component);
            var toName = ExpandedPipes.Drawn(graph, connection.To.Component, connection.From.Component);

            if (fromName is null || toName is null || !index.TryGetValue(fromName, out var from) || !index.TryGetValue(toName, out var to))
            {
                continue;
            }

            var port = string.Equals(fromName, connection.From.Component, StringComparison.Ordinal) ? connection.From.Port : string.Empty;
            var fromPort = PortTowards(graph, from, to, port, used);

            if (fromPort < 0)
            {
                continue;
            }

            var peer = graph.Adjacency.Peer(from, fromPort);
            used.Add((from, fromPort));
            used.Add((peer.Component, peer.Port));
            links.Add(new GraphLink($"c{i.ToString(CultureInfo.InvariantCulture)}", from, fromPort, peer.Component, peer.Port));
        }

        foreach (var (id, from, fromPort, to, toPort) in ExpandedPipes.Internal(graph))
        {
            links.Add(new GraphLink(id, from, fromPort, to, toPort));
        }

        return links.ToImmutable();
    }

    /// <summary>Component name to graph index.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <returns>The index.</returns>
    public static Dictionary<string, int> Index(CircuitGraph graph)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < graph.Components.Length; i++)
        {
            index[graph.Components[i].Name] = i;
        }

        return index;
    }

    private static int PortTowards(CircuitGraph graph, int from, int to, string port, HashSet<(int, int)> used)
    {
        var flow = graph.Components[from];

        for (var p = 0; p < flow.Ports.Length; p++)
        {
            var peer = graph.Adjacency.Peer(from, p);

            if (!peer.Exists || peer.Component != to || used.Contains((from, p)))
            {
                continue;
            }

            if (port.Length == 0 || string.Equals(flow.Ports[p].Name, port, StringComparison.Ordinal))
            {
                return p;
            }
        }

        return -1;
    }
}
