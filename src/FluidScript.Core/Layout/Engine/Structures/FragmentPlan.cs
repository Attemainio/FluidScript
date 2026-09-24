using System.Collections.Immutable;

namespace FluidScript.Core.Layout.Engine.Structures;

/// <summary>
/// A fragment read as structures before any geometry (<c>28</c> E2): what kind it is, the member it starts from, its
/// body between two terminals, and what hangs off the body.
/// </summary>
/// <param name="Kind">How the fragment is read.</param>
/// <param name="Head">C1's head: the source, the inlet, or the member the chain grows from.</param>
/// <param name="Cut">The member a ring is cut at -- the source (C2) or the consumer (C18) -- and -1 otherwise.</param>
/// <param name="Body">The body between the two terminals, or null for a chain or a member alone.</param>
/// <param name="Pendants">What hangs off the body, each at one port.</param>
/// <param name="Plus">The virtual vertex standing for the cut member's outlet side, or -1.</param>
/// <param name="Minus">The virtual vertex standing for the cut member's inlet side, or -1.</param>
internal sealed record FragmentPlan(FragmentKind Kind, int Head, int Cut, Structure? Body, ImmutableArray<Pendant> Pendants, int Plus, int Minus)
{
    /// <summary>Gets the rings that share one element with the body and are laid from it (<c>D-157</c>); none by default.</summary>
    public ImmutableArray<AttachedRing> Rings { get; init; } = [];
}
