namespace FluidScript.Core.Topology.Counting;

/// <summary>A sized parameter a stated constraint turned into a solver unknown (<c>D-02</c>).</summary>
/// <param name="Component">The component whose parameter moves.</param>
/// <param name="Parameter">The canonical parameter name.</param>
/// <param name="Constraint">The constraint it absorbs.</param>
/// <remarks>
/// <strong>The constraint and the unknown arrive together, which is what keeps the system square.</strong>
/// Without the pairing a stated <c>in</c> would be an extra equation and the circuit would report as
/// over-specified on the most ordinary hydronic arrangement there is.
/// </remarks>
public sealed record Promotion(string Component, string Parameter, ComponentConstraint Constraint)
{
    /// <summary>Gets the form a message names it by.</summary>
    public string Label => $"{Component}.{Parameter}";
}
