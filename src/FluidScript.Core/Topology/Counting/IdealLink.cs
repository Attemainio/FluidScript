using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Topology.Counting;

/// <summary>A bare connection between two nodes, which <c>D-25</c> makes an ideal zero-drop link.</summary>
/// <param name="From">The node the branch walk reaches it from.</param>
/// <param name="To">The node it continues to.</param>
/// <remarks>
/// <strong>It is a pressure relation with no component behind it.</strong> <c>A - B</c> written between
/// two nodes puts nothing in the path, so nothing declares <c>p_A = p_B</c> and the assembler writes the
/// row itself. Naming the pair is what lets it: a count says how many such rows exist and never which
/// nodes they join (<c>S-15</c>).
/// </remarks>
public sealed record IdealLink(GraphNode From, GraphNode To);
