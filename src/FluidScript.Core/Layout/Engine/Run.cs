using System.Collections.Immutable;

namespace FluidScript.Core.Layout.Engine;

/// <summary>
/// The chain of links between two boxed elements' ports, through the inline elements between them (<c>28</c> A5):
/// one polyline on the drawing. Oriented with the flow where the ports say which way it runs (A3), else as written.
/// </summary>
/// <param name="Index">Its position among the view's runs.</param>
/// <param name="Start">The end the flow leaves.</param>
/// <param name="End">The end the flow reaches; the same component as <paramref name="Start"/> for a member joined to itself (C20).</param>
/// <param name="Links">The links from start to end, each with whether it is written in that direction.</param>
/// <param name="Inline">The inline elements from start to end.</param>
internal sealed record Run(int Index, RunEnd Start, RunEnd End, ImmutableArray<(int Link, bool Forward)> Links, ImmutableArray<InlinePoint> Inline)
{
    /// <summary>The end of this run at a boxed element's port, and the other end.</summary>
    /// <param name="component">The boxed element.</param>
    /// <param name="port">Its port.</param>
    /// <returns>The far end seen from that port, or null when the run does not end there.</returns>
    public RunEnd? From(int component, int port) =>
        Start.Component == component && Start.Port == port ? End
        : End.Component == component && End.Port == port ? Start
        : null;
}
