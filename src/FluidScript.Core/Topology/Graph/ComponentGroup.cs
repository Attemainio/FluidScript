using System.Collections.Immutable;

namespace FluidScript.Core.Topology.Graph;

/// <summary>The expansion of one written component into the several the graph holds.</summary>
/// <remarks>
/// A pipe with <c>nodes=4</c> becomes five sub-pipes and four internal nodes, and nothing downstream
/// can recover that they were one line of script unless the graph says so. The canvas draws one pipe,
/// write-back edits one parameter, and a diagnostic about the third sub-pipe has to name the pipe the
/// user wrote.
/// </remarks>
public sealed record ComponentGroup
{
    /// <summary>Gets the name of the component the script wrote.</summary>
    public required string Source { get; init; }

    /// <summary>Gets every graph element the expansion produced, in order.</summary>
    /// <value>
    /// Sub-pipes and internal nodes alike. For <c>nodes=4</c> this holds nine names — five sub-pipes
    /// and four nodes — and the source pipe itself is not among them, because it is not in the graph.
    /// </value>
    public required ImmutableArray<string> Members { get; init; }
}
