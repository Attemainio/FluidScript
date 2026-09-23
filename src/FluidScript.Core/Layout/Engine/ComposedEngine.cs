using FluidScript.Core.Language.Binding;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Layout.Engine;

/// <summary>
/// The rebuilt layout engine (<c>D-153</c>, <c>28</c> part E): a fragment's topology is decomposed into structures
/// before any geometry, the structures are composed bottom-up against one occupancy, and every run is drawn by one
/// builder that keeps its stubs.
/// </summary>
/// <remarks>
/// Built beside the ladder engine until every ladder step and sample reaches parity (<c>28</c> E5); until a stage
/// exists the scene it returns is what the stages so far can say, and the parity report shows the rest as missing.
/// </remarks>
/// <param name="graph">The lowered graph.</param>
/// <param name="model">The bound model.</param>
/// <param name="hints">The hints derived from the same graph.</param>
/// <param name="margin">The clearance, world units.</param>
internal sealed class ComposedEngine(CircuitGraph graph, SemanticModel model, LayoutHints hints, double margin)
{
    private readonly CircuitGraph _graph = graph;
    private readonly SemanticModel _model = model;
    private readonly LayoutHints _hints = hints;

    /// <summary>Solves the layout.</summary>
    /// <returns>The scene; empty of placements until the compose stage exists, its trace listing what the stages so far found.</returns>
    public Scene Solve()
    {
        var view = new CircuitView(_graph, _model, _hints);
        var trace = new List<PlacementNote>();

        for (var f = 0; f < view.Fragments.Length; f++)
        {
            trace.Add(new PlacementNote($"fragment {f + 1}", "E1", "members " + string.Join(", ", view.Fragments[f].Select(view.Name))));
        }

        foreach (var run in view.Runs)
        {
            trace.Add(new PlacementNote($"run {run.Index + 1}", "E1", StructureText.Run(view, run)));
        }

        for (var f = 0; f < view.Fragments.Length; f++)
        {
            var plan = Decomposition.Plan(view, view.Fragments[f]);

            foreach (var line in StructureText.Lines(view, plan))
            {
                trace.Add(new PlacementNote($"fragment {f + 1}", "E2", line));
            }
        }

        return new Scene
        {
            Placements = [],
            Routes = [],
            Extent = new Box(0, 0, 0, 0),
            Margin = margin,
            Provenance = [.. trace],
        };
    }
}
