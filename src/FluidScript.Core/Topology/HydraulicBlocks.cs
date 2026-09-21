using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Language;

namespace FluidScript.Core.Topology;

/// <summary>
/// The biconnected blocks of a graph's branch structure, and which of them something drives (<c>S-55</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Whether a loop carries flow is a property of its block, not of the loop.</strong> A loop is one
/// cycle of the branch graph; a pump on another cycle of the same block still drives it, because in a
/// biconnected block any two branches lie on a common cycle -- the pump's pressure rise reaches every
/// branch of the block, and a pumpless cycle inside it is a parallel split whose flows the resistances
/// settle. The pump-free mixing header is the case: its source valve and exchanger form a small cycle with
/// no pump, driven by the consumer pumps that draw from the supply and discharge to the return through the
/// same block. Reading the fundamental cycle alone reported <c>FS2214</c> on a circuit the equations solve.
/// </para>
/// <para>
/// <strong>Two readings, one structure.</strong> For the driver diagnostic the boundaries drive too: an open
/// path from an <c>inlet</c> to an <c>outlet</c> carries what their pressures push, so a virtual ground
/// vertex joins every boundary node and any boundary-to-boundary path becomes a cycle through ground. For
/// a valve's sizing the question is narrower -- whether a <em>free</em> pump makes its drop a choice
/// (<c>D-89</c>) -- and the ground must not join in: a pumped secondary beside a bounded primary reads as
/// two blocks, the primary's edges bridges that no pump reaches, which is the distinction the rounding
/// rule exists for.
/// </para>
/// <para>
/// Vertices are branch endpoints -- nodes and junction elements -- and edges are branches; Tarjan's
/// articulation-point walk over that graph, with each branch labelled by the block its edge falls in.
/// </para>
/// </remarks>
public sealed class HydraulicBlocks
{
    private readonly ImmutableArray<int> _blockOfBranch;
    private readonly ImmutableArray<bool> _drivenBlock;

    private HydraulicBlocks(ImmutableArray<int> blockOfBranch, ImmutableArray<bool> drivenBlock)
    {
        _blockOfBranch = blockOfBranch;
        _drivenBlock = drivenBlock;
    }

    /// <summary>Builds the blocks of a graph.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="groundBoundaries">Whether the boundary nodes are joined through a virtual ground, so that a path between two boundaries is a driven cycle.</param>
    /// <param name="drives">What counts as a driver on a branch: every kind whose registry entry drives flow, or only a pump whose head is free.</param>
    /// <returns>The blocks.</returns>
    public static HydraulicBlocks Build(CircuitGraph graph, bool groundBoundaries, Func<IFlowComponent, bool> drives)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(drives);

        // Vertices: every branch endpoint, plus the ground when asked for. Edges: every branch, plus one
        // from the ground to each boundary node.
        var index = new Dictionary<IFlowComponent, int>(ReferenceEqualityComparer.Instance);

        foreach (var branch in graph.Branches)
        {
            index.TryAdd(branch.From.Element, index.Count);
            index.TryAdd(branch.To.Element, index.Count);
        }

        var ground = groundBoundaries ? index.Count : -1;
        var vertexCount = index.Count + (groundBoundaries ? 1 : 0);
        var edges = new List<(int From, int To, int Branch, bool Drives)>();

        foreach (var branch in graph.Branches)
        {
            edges.Add((index[branch.From.Element], index[branch.To.Element], branch.Index, branch.Path.Any(drives)));
        }

        if (groundBoundaries)
        {
            foreach (var (element, vertex) in index)
            {
                if (element is CircuitNode { Boundary: not BoundaryRole.Interior })
                {
                    edges.Add((ground, vertex, -1, Drives: true));
                }
            }
        }

        var adjacency = new List<int>[vertexCount];

        for (var v = 0; v < vertexCount; v++)
        {
            adjacency[v] = [];
        }

        for (var e = 0; e < edges.Count; e++)
        {
            adjacency[edges[e].From].Add(e);
            adjacency[edges[e].To].Add(e);
        }

        // Tarjan: an edge stack, popped as a block whenever a child's low-link reaches its parent.
        var discovery = new int[vertexCount];
        var low = new int[vertexCount];
        Array.Fill(discovery, -1);
        var blockOfEdge = new int[edges.Count];
        Array.Fill(blockOfEdge, -1);
        var stack = new Stack<int>();
        var blocks = 0;
        var time = 0;

        for (var root = 0; root < vertexCount; root++)
        {
            if (discovery[root] < 0)
            {
                Visit(root, -1);
            }
        }

        var drivenBlock = new bool[blocks];

        for (var e = 0; e < edges.Count; e++)
        {
            if (edges[e].Drives && blockOfEdge[e] >= 0)
            {
                drivenBlock[blockOfEdge[e]] = true;
            }
        }

        var blockOfBranch = new int[graph.Branches.Length];

        for (var e = 0; e < edges.Count; e++)
        {
            if (edges[e].Branch >= 0)
            {
                blockOfBranch[edges[e].Branch] = blockOfEdge[e];
            }
        }

        return new HydraulicBlocks([.. blockOfBranch], [.. drivenBlock]);

        void Visit(int v, int viaEdge)
        {
            discovery[v] = low[v] = time++;

            foreach (var e in adjacency[v])
            {
                if (e == viaEdge)
                {
                    continue;
                }

                var w = edges[e].From == v ? edges[e].To : edges[e].From;

                if (w == v)
                {
                    // A branch from a node back to itself -- a ring of one, `N1 - PU1 - HE1 - N1` -- is a
                    // cycle and a block of its own; the walk below never sees it as a tree or back edge.
                    if (blockOfEdge[e] < 0)
                    {
                        blockOfEdge[e] = blocks++;
                    }

                    continue;
                }

                if (discovery[w] < 0)
                {
                    stack.Push(e);
                    Visit(w, e);
                    low[v] = Math.Min(low[v], low[w]);

                    if (low[w] >= discovery[v])
                    {
                        // Everything pushed since e is one block, e included.
                        var block = blocks++;

                        while (true)
                        {
                            var popped = stack.Pop();
                            blockOfEdge[popped] = block;

                            if (popped == e)
                            {
                                break;
                            }
                        }
                    }
                }
                else if (discovery[w] < discovery[v])
                {
                    // A back edge to an ancestor: on the stack, in the block that closes at that ancestor.
                    stack.Push(e);
                    low[v] = Math.Min(low[v], discovery[w]);
                }
            }
        }
    }

    /// <summary>The blocks read for the driver diagnostic: boundaries drive, and every flow-driving kind counts.</summary>
    /// <param name="graph">The graph.</param>
    /// <returns>The blocks.</returns>
    public static HydraulicBlocks ForDrivers(CircuitGraph graph) =>
        Build(graph, groundBoundaries: true, static part => ComponentRegistry.Default.ByKeyword(part.Kind)?.DrivesFlow == true);

    /// <summary>The blocks read for sizing: boundaries do not join, and only a pump whose head is unstated counts (<c>D-89</c>).</summary>
    /// <param name="graph">The graph.</param>
    /// <returns>The blocks.</returns>
    public static HydraulicBlocks ForFreePumps(CircuitGraph graph) =>
        Build(graph, groundBoundaries: false, static part => part is Pump { StatedRise: null } pump && !pump.StatedParameters.ContainsKey("head"));

    /// <summary>Whether the block a branch lies in has a driver.</summary>
    /// <param name="branch">The branch.</param>
    /// <returns><see langword="true"/> when something on the block drives flow.</returns>
    public bool Drives(Branch branch)
    {
        ArgumentNullException.ThrowIfNull(branch);

        var block = _blockOfBranch[branch.Index];
        return block >= 0 && _drivenBlock[block];
    }
}
