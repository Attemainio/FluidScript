namespace FluidScript.Core.Topology.Graph;

/// <summary>How the graph is to be solved.</summary>
/// <remarks>
/// The graph's own enum rather than the script's <c>FluidMode</c>, which lives in the syntax tree.
/// <c>23</c>'s invariant 7 says the graph names no tier-10 type, and the two spellings are the point:
/// a user writes <c>static</c> and <c>dynamic</c>, and what the solver does is steady or transient.
/// </remarks>
public enum SolveMode
{
    /// <summary>One equilibrium, with no time derivative.</summary>
    Steady = 1,

    /// <summary>Integrated in time, with storage terms live.</summary>
    Transient,
}
