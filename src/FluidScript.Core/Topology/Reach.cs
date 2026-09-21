using FluidScript.Core.Components;

namespace FluidScript.Core.Topology;

/// <summary>What is near a component along the branches: the sets <c>D-130</c>'s "own branch" means for each constraint kind.</summary>
/// <remarks>
/// Three questions, one walk. A coil's inlet is fed by the split at either end of a branch the coil lies
/// on; a node's temperature is reached by everything on or at either end of a branch through or ending
/// at the node; a pinned flow is driven by what shares the owner's branch path. Before <c>70</c>'s R3
/// <c>WellPosedness.Candidates</c> wrote the walk three times.
/// </remarks>
public static class Reach
{
    /// <summary>The elements at either end of every branch a component lies on: the splits that feed it.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="component">The component, or <see langword="null"/> for an empty set.</param>
    /// <returns>The branch endpoints.</returns>
    public static HashSet<IFlowComponent> Feeding(CircuitGraph graph, IFlowComponent? component) =>
        Around(graph, component, atEitherEnd: false, ends: true, path: false);

    /// <summary>Everything on or at either end of every branch through or ending at a component: what reaches a node.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="component">The component, or <see langword="null"/> for an empty set.</param>
    /// <returns>The branch endpoints and path members.</returns>
    public static HashSet<IFlowComponent> Reaching(CircuitGraph graph, IFlowComponent? component) =>
        Around(graph, component, atEitherEnd: true, ends: true, path: true);

    /// <summary>Every path member of every branch a component lies on: what shares its flow.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="component">The component, or <see langword="null"/> for an empty set.</param>
    /// <returns>The branch path members.</returns>
    public static HashSet<IFlowComponent> Local(CircuitGraph graph, IFlowComponent? component) =>
        Around(graph, component, atEitherEnd: false, ends: false, path: true);

    private static HashSet<IFlowComponent> Around(
        CircuitGraph graph, IFlowComponent? component, bool atEitherEnd, bool ends, bool path)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var around = new HashSet<IFlowComponent>();

        if (component is null)
        {
            return around;
        }

        foreach (var branch in graph.Branches)
        {
            var on = branch.Path.Contains(component)
                || (atEitherEnd && (ReferenceEquals(branch.From.Element, component) || ReferenceEquals(branch.To.Element, component)));

            if (!on)
            {
                continue;
            }

            if (ends)
            {
                around.Add(branch.From.Element);
                around.Add(branch.To.Element);
            }

            if (path)
            {
                foreach (var element in branch.Path)
                {
                    around.Add(element);
                }
            }
        }

        return around;
    }
}
