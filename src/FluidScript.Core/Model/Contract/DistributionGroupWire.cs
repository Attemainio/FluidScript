using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>A distribution group.</summary>
/// <param name="ParentCircuit">The circuit owning the rails.</param>
/// <param name="Members">The branches, at least two.</param>
public sealed record DistributionGroupWire(string ParentCircuit, ImmutableArray<string> Members);
