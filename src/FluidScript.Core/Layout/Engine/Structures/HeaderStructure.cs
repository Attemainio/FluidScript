using System.Collections.Immutable;

namespace FluidScript.Core.Layout.Engine.Structures;

/// <summary>
/// Paths in parallel that all carry the flow from one vertex to the other (<c>28</c> E2): a header, whose split feeds
/// its branches and whose merge collects them -- C14's branches, C19's open form, a plain zone (<c>C-126</c>).
/// </summary>
/// <remarks>
/// The <b>spine</b> is the branch declared last: the ring runs on through it, so it is the right side where it is the
/// last header on the ring, and every other branch hangs between the rails in script order. <c>From</c> is the
/// split and <c>To</c> the merge, whatever direction the parent read them in.
/// </remarks>
/// <param name="From">The split.</param>
/// <param name="To">The merge.</param>
/// <param name="Branches">The branches in script order, each read from the split to the merge.</param>
/// <param name="Spine">The index of the branch the ring runs on through.</param>
internal sealed record HeaderStructure(int From, int To, ImmutableArray<Structure> Branches, int Spine) : Structure(From, To);
