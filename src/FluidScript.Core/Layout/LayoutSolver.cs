using FluidScript.Core.Language.Binding;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Layout;

/// <summary>Places every component and routes every connection (<c>D-103</c>) by the rule-based engine of <c>28</c> (<c>D-106</c>), built rule by rule against the ladder in <c>29</c>.</summary>
/// <remarks>
/// The entry point only; <see cref="LayoutEngine"/> does the work. Coordinates are world units with
/// <c>y</c> growing upward; the result is deterministic for a given graph, model and hints (<c>D-72</c>).
/// </remarks>
public static class LayoutSolver
{
    /// <summary>The margin when the script states no <c>spacing</c>, world units.</summary>
    public const double DefaultMargin = 0.5;

    /// <summary>Solves the layout.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="model">The bound model, for the connections as written and the non-flow elements.</param>
    /// <param name="hints">The hints derived from the same graph.</param>
    /// <param name="margin">The clearance, world units; <see cref="DefaultMargin"/> when the script states none.</param>
    /// <returns>The scene.</returns>
    public static Scene Solve(CircuitGraph graph, SemanticModel model, LayoutHints hints, double margin = DefaultMargin)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(hints);

        return new LayoutEngine(graph, model, hints, Math.Max(margin, 0.05)).Solve();
    }

    /// <summary>The margin the script asked for, or the default.</summary>
    /// <param name="model">The bound model.</param>
    /// <returns>World units.</returns>
    public static double MarginOf(SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Style.Spacing is { } spacing && spacing > 0 ? spacing : DefaultMargin;
    }
}

