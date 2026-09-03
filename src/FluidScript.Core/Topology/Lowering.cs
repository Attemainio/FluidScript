using System.Collections.Immutable;

using FluidScript.Core.Binding;
using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Syntax.Ast;

namespace FluidScript.Core.Topology;

/// <summary>What lowering produced, and what it could not build.</summary>
/// <param name="Graph">The graph.</param>
/// <param name="Unresolved">
/// Names of components the factory could not build, in model order. Empty in the ordinary case; a
/// pipe whose bore no catalogue has resolved is the one that happens today.
/// </param>
public sealed record LoweringResult(CircuitGraph Graph, ImmutableArray<string> Unresolved);

/// <summary>Turns a bound semantic model into the graph the solver runs on.</summary>
/// <remarks>
/// <para>
/// <strong>After this point nothing knows a script existed.</strong> That is what makes the solver
/// testable from a hand-constructed graph, and it is the reason <see cref="CircuitGraph"/> holds only
/// names and components: carrying a symbol to make the graph reportable would drag the binder in
/// behind it (invariant 7).
/// </para>
/// <para>
/// <strong>Nodes are built after adjacency, not before it</strong>, which reverses <c>23</c>'s step
/// order for one kind. A <see cref="CircuitNode"/> is constructed with its port count and whether it
/// carries a mass balance, and both are properties of the graph rather than of the symbol — a node
/// with two connections is interior to a branch and contributes no balance, and nothing in the
/// semantic model says how many connections it has. Flow components are instantiated first, as the
/// document says; the node pass needs a degree count that only exists once connections are walked.
/// </para>
/// <para>
/// <strong>Observers never reach here.</strong> An instrument has no ports, no <c>DrivesFlow</c> and
/// no residuals, and adding a dozen to a script leaves the graph byte-identical (<c>D-61</c>,
/// invariant 9). It is dropped by the same filter that drops a controller, and neither needs an
/// exemption: both are components with no ports, and a component with no ports is not in a flow
/// graph.
/// </para>
/// </remarks>
public static partial class Lowering
{
    /// <summary>Lowers a bound model to its graph.</summary>
    /// <param name="model">The semantic model, with inference already applied.</param>
    /// <param name="substance">The fluid every node's state is evaluated against.</param>
    /// <param name="factory">Where a bound symbol becomes a component.</param>
    /// <param name="name">The graph's name, for reporting.</param>
    /// <returns>The graph, and the components that could not be built.</returns>
    /// <remarks>
    /// <strong>It reports rather than throws, whatever the model contains.</strong> A connection
    /// naming a component that does not exist was already a binder error and is skipped here; a
    /// component the factory cannot build is named in
    /// <see cref="LoweringResult.Unresolved"/> and left out. A script under editing is malformed most
    /// of the time and lowering runs on every keystroke.
    /// </remarks>
    public static LoweringResult Lower(
        SemanticModel model, ISubstance substance, IComponentFactory factory, string name = "model")
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(substance);
        ArgumentNullException.ThrowIfNull(factory);

        var build = new Build(model, factory);

        build.CountNodePorts();
        build.CreateComponents();
        build.ResolveLinks();
        build.ExpandPipes();
        build.Prune();
        build.Connect();

        var junctions = build.JunctionElements();
        var branches = build.Decompose(junctions);
        var loops = CycleBasis(branches, junctions);

        return new LoweringResult(
            new CircuitGraph
            {
                Name = name,
                Substance = substance,
                Mode = ModeOf(model),
                Nodes = build.Nodes,
                Components = build.Components,
                Branches = branches,
                JunctionElements = junctions,
                Loops = loops,
                Groups = build.Groups,
                Adjacency = build.Adjacency,
                CircuitOf = build.CircuitOf,
            },
            build.Unresolved);
    }

    /// <summary>How the model is solved, resolved across every circuit in it.</summary>
    /// <param name="model">The bound model.</param>
    /// <returns><see cref="SolveMode.Transient"/> when any circuit is dynamic.</returns>
    /// <remarks>
    /// There is one graph for every circuit (<c>D-33</c>), so there is one mode. Any dynamic circuit
    /// makes the whole solve transient, which is the conservative direction: a steady circuit solved
    /// in time reaches its equilibrium and stays there, while a transient one solved steadily loses
    /// every storage term it was written for.
    /// </remarks>
    private static SolveMode ModeOf(SemanticModel model) =>
        model.Circuits.Any(static circuit => circuit.Mode == FluidMode.Dynamic)
            ? SolveMode.Transient
            : SolveMode.Steady;

    /// <summary>One independent cycle per non-tree edge of the branch graph.</summary>
    /// <param name="branches">Every branch, in order.</param>
    /// <param name="junctions">The vertices, in order.</param>
    /// <returns>The cycle basis, one entry per independent loop.</returns>
    /// <remarks>
    /// <para>
    /// A spanning forest by breadth-first search, then one loop per edge the forest did not take: the
    /// non-tree branch plus the tree path between its two ends. Breadth-first rather than depth-first
    /// so the tree paths are the short ones, which makes a reported loop the small circuit a user
    /// recognises rather than a tour of the plant.
    /// </para>
    /// <para>
    /// <strong>The count is per connected component of the branch graph, not per graph.</strong> A
    /// model with a rated exchanger in it has more than one hydraulic component, and
    /// <c>B − V + 1</c> over the whole thing would count one loop too many for each extra component.
    /// Running the forest across all vertices and counting non-tree edges gets this right without
    /// having to identify the components first.
    /// </para>
    /// </remarks>
    private static ImmutableArray<CircuitLoop> CycleBasis(
        ImmutableArray<Branch> branches, ImmutableArray<IFlowComponent> junctions)
    {
        var index = new Dictionary<IFlowComponent, int>(junctions.Length);
        for (var i = 0; i < junctions.Length; i++)
        {
            index[junctions[i]] = i;
        }

        var adjacency = new List<(int To, Branch Branch)>[junctions.Length];
        for (var i = 0; i < junctions.Length; i++)
        {
            adjacency[i] = [];
        }

        foreach (var branch in branches)
        {
            // A branch can end somewhere that is not a vertex: Decompose stops where the graph does
            // when a component the factory could not build took its connections with it. Such a stub is
            // in no cycle, and indexing it would throw on a script that is merely incomplete -- which no
            // stage may do, since a script under editing is malformed most of the time.
            if (!index.TryGetValue(branch.From.Element, out var from)
                || !index.TryGetValue(branch.To.Element, out var to))
            {
                continue;
            }

            adjacency[from].Add((to, branch));
            adjacency[to].Add((from, branch));
        }

        var parent = new int[junctions.Length];
        var parentBranch = new Branch?[junctions.Length];
        var depth = new int[junctions.Length];
        var seen = new bool[junctions.Length];
        var tree = new HashSet<Branch>();

        for (var root = 0; root < junctions.Length; root++)
        {
            if (seen[root])
            {
                continue;
            }

            seen[root] = true;
            parent[root] = -1;
            var queue = new Queue<int>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var vertex = queue.Dequeue();

                foreach (var (next, branch) in adjacency[vertex])
                {
                    if (seen[next] || tree.Contains(branch))
                    {
                        continue;
                    }

                    seen[next] = true;
                    parent[next] = vertex;
                    parentBranch[next] = branch;
                    depth[next] = depth[vertex] + 1;
                    tree.Add(branch);
                    queue.Enqueue(next);
                }
            }
        }

        var loops = ImmutableArray.CreateBuilder<CircuitLoop>();

        foreach (var branch in branches)
        {
            // A dangling stub took no tree edge either, so it reaches here as well: the same guard,
            // for the same reason.
            if (tree.Contains(branch)
                || !index.TryGetValue(branch.To.Element, out var to)
                || !index.TryGetValue(branch.From.Element, out var from))
            {
                continue;
            }

            loops.Add(new CircuitLoop
            {
                Branches = [branch, .. PathBetween(to, from, parent, parentBranch, depth)],
            });
        }

        return loops.ToImmutable();
    }

    /// <summary>The tree path between two vertices, as the branches along it.</summary>
    /// <param name="from">Where the path starts.</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="parent">Each vertex's parent in the spanning forest.</param>
    /// <param name="parentBranch">The branch joining each vertex to its parent.</param>
    /// <param name="depth">Each vertex's depth, for climbing to the common ancestor.</param>
    /// <returns>The branches, from end to end through the lowest common ancestor.</returns>
    private static ImmutableArray<Branch> PathBetween(
        int from, int to, int[] parent, Branch?[] parentBranch, int[] depth)
    {
        var ascending = ImmutableArray.CreateBuilder<Branch>();
        var descending = new Stack<Branch>();

        while (depth[from] > depth[to])
        {
            ascending.Add(parentBranch[from]!);
            from = parent[from];
        }

        while (depth[to] > depth[from])
        {
            descending.Push(parentBranch[to]!);
            to = parent[to];
        }

        while (from != to)
        {
            ascending.Add(parentBranch[from]!);
            descending.Push(parentBranch[to]!);
            from = parent[from];
            to = parent[to];
        }

        ascending.AddRange(descending);

        return ascending.ToImmutable();
    }
}
