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
    /// <returns>The scene; empty of placements until the compose stage exists.</returns>
    public Scene Solve()
    {
        _ = (_graph, _model, _hints);

        return new Scene
        {
            Placements = [],
            Routes = [],
            Extent = new Box(0, 0, 0, 0),
            Margin = margin,
            Provenance = [new PlacementNote("engine", "E", "the composed engine (D-153): no stage built yet")],
        };
    }
}
