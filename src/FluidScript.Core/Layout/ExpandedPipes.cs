using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Layout;

/// <summary>Where a written connection to a pipe with <c>nodes=</c> lands in the graph, and the links between its cells (<c>C-124</c>).</summary>
/// <remarks>
/// <para>
/// Lowering replaces such a pipe with a chain of sub-pipes and internal nodes and records the chain as a
/// <see cref="ComponentGroup"/> (<c>22</c>); the script's connections still name the pipe. The diagram
/// draws the cells, each with a route of its own, so the profile along the pipe shows as a gradient
/// (<c>57</c>).
/// </para>
/// <para>
/// Before this, a connection naming <c>PB</c> matched no graph component and was dropped, and the links
/// between the cells were never looked for, because the script does not write them: the demand-step
/// loop's recirculation was not drawn at all, and its nine cells stacked below the diagram.
/// </para>
/// </remarks>
internal static class ExpandedPipes
{
    /// <summary>The graph element a written connection end stands for.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="name">The component the end names.</param>
    /// <param name="neighbour">The component at the connection's other end, as written.</param>
    /// <returns>
    /// <paramref name="name"/> itself when it was not expanded; otherwise the cell of its chain that meets
    /// <paramref name="neighbour"/>, or <see langword="null"/> when none does.
    /// </returns>
    public static string? Drawn(CircuitGraph graph, string name, string neighbour)
    {
        if (GroupOf(graph, name) is not { } group)
        {
            return name;
        }

        // The neighbour may be an expanded pipe too, and then it is met by one of its own cells.
        var across = GroupOf(graph, neighbour) is { } other
            ? other.Members.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal) { neighbour };

        foreach (var member in group.Members)
        {
            var index = IndexOf(graph, member);

            for (var port = 0; index >= 0 && port < graph.Components[index].Ports.Length; port++)
            {
                var peer = graph.Adjacency.Peer(index, port);

                if (peer.Exists && across.Contains(graph.Components[peer.Component].Name))
                {
                    return member;
                }
            }
        }

        return null;
    }

    /// <summary>The links between the cells of every expanded pipe, in order along each chain.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <returns>
    /// A route id, <c>{pipe}#c{k}</c> counting from 1 at the pipe's inlet, and the two graph components
    /// and ports it joins, in the pipe's own direction.
    /// </returns>
    public static IEnumerable<(string Id, int From, int FromPort, int To, int ToPort)> Internal(CircuitGraph graph)
    {
        foreach (var group in graph.Groups)
        {
            var members = group.Members.ToHashSet(StringComparer.Ordinal);
            var (element, port) = (group.Members.IsDefaultOrEmpty ? -1 : IndexOf(graph, group.Members[0]), 1);

            // The first member is the inlet sub-pipe; from its outlet, each cell hands on to the next
            // through its other port until the chain leaves the group.
            for (var k = 1; element >= 0 && k <= members.Count; k++)
            {
                var peer = graph.Adjacency.Peer(element, port);

                if (!peer.Exists || !members.Contains(graph.Components[peer.Component].Name))
                {
                    break;
                }

                yield return ($"{group.Source}#c{k}", element, port, peer.Component, peer.Port);

                (element, port) = (peer.Component, 1 - peer.Port);
            }
        }
    }

    private static ComponentGroup? GroupOf(CircuitGraph graph, string name)
    {
        foreach (var group in graph.Groups)
        {
            if (string.Equals(group.Source, name, StringComparison.Ordinal))
            {
                return group;
            }
        }

        return null;
    }

    private static int IndexOf(CircuitGraph graph, string name)
    {
        for (var i = 0; i < graph.Components.Length; i++)
        {
            if (string.Equals(graph.Components[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
