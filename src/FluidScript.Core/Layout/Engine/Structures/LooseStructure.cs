using System.Collections.Immutable;

namespace FluidScript.Core.Layout.Engine.Structures;

/// <summary>
/// A two-terminal part that is neither series nor parallel -- a bridge (<c>28</c> E2). It is still drawn: the chain
/// rules place its members, the run builder joins them with their stubs, and the trace names it.
/// </summary>
/// <param name="From">One terminal.</param>
/// <param name="To">The other.</param>
/// <param name="Runs">Its runs' indices in the view.</param>
internal sealed record LooseStructure(int From, int To, ImmutableArray<int> Runs) : Structure(From, To);
