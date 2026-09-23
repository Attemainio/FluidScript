namespace FluidScript.Core.Layout.Engine.Structures;

/// <summary>
/// One piece of a fragment's series-parallel reading (<c>28</c> E2): a two-terminal part of the circuit between the
/// vertices <paramref name="From"/> and <paramref name="To"/>, read in that direction.
/// </summary>
/// <remarks>
/// A vertex is a boxed element's graph index, or one of the two virtual terminals a ring is cut at (its cut member's
/// outlet side and inlet side, numbered past the graph's components). What a structure is decides how it is drawn: a
/// run is one line, a series is its parts in a row, a header's branches hang between its split and its merge, a loop
/// is a ring of its own.
/// </remarks>
/// <param name="From">The vertex it starts at.</param>
/// <param name="To">The vertex it ends at.</param>
internal abstract record Structure(int From, int To);
