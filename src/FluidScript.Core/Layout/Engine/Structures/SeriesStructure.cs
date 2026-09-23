using System.Collections.Immutable;

namespace FluidScript.Core.Layout.Engine.Structures;

/// <summary>Parts one after another: each ends where the next starts, at a vertex every path between the ends passes.</summary>
/// <param name="From">The first part's start.</param>
/// <param name="To">The last part's end.</param>
/// <param name="Parts">The parts in order from <paramref name="From"/>.</param>
internal sealed record SeriesStructure(int From, int To, ImmutableArray<Structure> Parts) : Structure(From, To);
