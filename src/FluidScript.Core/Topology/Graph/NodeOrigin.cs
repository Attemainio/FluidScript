namespace FluidScript.Core.Topology.Graph;

/// <summary>How a node came to be in the graph.</summary>
/// <remarks>
/// Carried rather than derived, and separate from the semantic model's own origin: the canvas must
/// show which nodes a user wrote, and a pipe-internal node is neither written nor inferred by a
/// binder rule — it is a discretization the graph created and the renderer draws differently.
/// </remarks>
public enum NodeOrigin
{
    /// <summary>The script declared it.</summary>
    Declared = 1,

    /// <summary>An inference rule created it, to join or terminate components.</summary>
    Inferred,

    /// <summary>Lowering created it, subdividing a pipe with <c>nodes=n</c>.</summary>
    PipeInternal,
}
