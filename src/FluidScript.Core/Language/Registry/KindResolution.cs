using System.Collections.Immutable;

namespace FluidScript.Core.Language.Registry;

/// <summary>What a written kind name resolved to.</summary>
public abstract record KindResolution
{
    private KindResolution()
    {
    }

    /// <summary>The name is a spelling the registry knows.</summary>
    /// <param name="Kind">The kind it names.</param>
    /// <remarks>Resolves silently: a known spelling is not a guess, so there is nothing to report.</remarks>
    public sealed record Exact(ComponentKindInfo Kind) : KindResolution;

    /// <summary>The name is close enough to one kind to resolve, and to nothing else.</summary>
    /// <param name="Kind">The kind it resolved to.</param>
    /// <param name="Score">How close, on <see cref="NameResolution.Score"/>'s scale.</param>
    /// <remarks>Always reported (<c>FS1512</c>, info): a resolution the user cannot see is magic.</remarks>
    public sealed record Similar(ComponentKindInfo Kind, double Score) : KindResolution;

    /// <summary>The name is close to more than one kind, and no closer to either.</summary>
    /// <param name="Candidates">The candidates, best first.</param>
    public sealed record Ambiguous(ImmutableArray<ComponentKindInfo> Candidates) : KindResolution;

    /// <summary>The name matches nothing well enough to act on.</summary>
    /// <param name="SuggestedKeyword">
    /// The closest canonical keyword, or <see langword="null"/> when nothing came within
    /// <see cref="NameResolution.SuggestionFloor"/> and a suggestion would be a guess dressed as help.
    /// </param>
    public sealed record Unknown(string? SuggestedKeyword) : KindResolution;
}
