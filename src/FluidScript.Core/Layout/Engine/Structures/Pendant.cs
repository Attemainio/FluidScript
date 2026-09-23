using System.Collections.Immutable;

namespace FluidScript.Core.Layout.Engine.Structures;

/// <summary>
/// What hangs off a fragment's body at one port and reaches nothing else of it (<c>28</c> E2): a chain to a boundary
/// or an open port (C4–C6), or a fan of them. The chain rules grow it from its port.
/// </summary>
/// <param name="At">The body element it hangs from.</param>
/// <param name="Port">The port it hangs from.</param>
/// <param name="Members">Its boxed elements.</param>
/// <param name="Runs">Its runs' indices in the view.</param>
/// <param name="Tree">Whether it is a tree; one with a cycle in it is placed by the chain rules all the same.</param>
internal sealed record Pendant(int At, int Port, ImmutableArray<int> Members, ImmutableArray<int> Runs, bool Tree);
