using System.Collections.Immutable;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Topology.Construction;

/// <summary>What lowering produced, and what it could not build.</summary>
/// <param name="Graph">The graph.</param>
/// <param name="Unresolved">
/// Names of components the factory could not build, in model order. Empty in the ordinary case; a
/// pipe whose bore no catalogue has resolved is the one that happens today.
/// </param>
public sealed record LoweringResult(CircuitGraph Graph, ImmutableArray<string> Unresolved);
