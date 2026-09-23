namespace FluidScript.Core.Layout.Engine.Structures;

/// <summary>
/// Paths in parallel whose flows run opposite ways (<c>28</c> E2): the fluid circulates, so this is a loop -- an
/// injection or mixing block inside a ring (C11), or the ring itself where the fragment is fed from outside (C18).
/// </summary>
/// <param name="From">Where the loop meets its parent first.</param>
/// <param name="To">Where it meets its parent second.</param>
/// <param name="Forward">The part whose flow goes from <paramref name="From"/> to <paramref name="To"/>.</param>
/// <param name="Back">The part whose flow comes back, read from <paramref name="To"/> to <paramref name="From"/>.</param>
internal sealed record LoopStructure(int From, int To, Structure Forward, Structure Back) : Structure(From, To);
