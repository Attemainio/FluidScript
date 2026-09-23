using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>One thermal stage.</summary>
/// <param name="Rank">The band index.</param>
/// <param name="Role"><c>source</c>, <c>conversion</c>, <c>storage</c>, <c>consumer</c> or <c>neutral</c>.</param>
/// <param name="Components">Members in graph order.</param>
public sealed record ThermalStageWire(int Rank, string Role, ImmutableArray<string> Components);
