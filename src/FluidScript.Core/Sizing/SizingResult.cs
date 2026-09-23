using System.Collections.Immutable;

namespace FluidScript.Core.Sizing;

/// <summary>What one sizer decided about one component.</summary>
public sealed record SizingResult
{
    /// <summary>Gets the values chosen, by the parameter name a script would state.</summary>
    public required ImmutableDictionary<string, SizedValue> Values { get; init; }

    /// <summary>Gets what happened on the way, in the order it happened.</summary>
    /// <value>
    /// Sentences, not codes. <c>24</c>'s <c>FS2305</c>–<c>FS2307</c> are the eventual home for these and
    /// none of them is registered yet, so they are carried as text rather than emitted as diagnostics a
    /// user could not look up (<c>C-44</c>'s reasoning: a wrong estimate costs iterations, a wrong
    /// diagnostic is a sentence somebody acts on).
    /// </value>
    public required ImmutableArray<string> Notes { get; init; }

    /// <summary>Gets what the rule has to report as a registered code rather than a sentence.</summary>
    /// <value>
    /// Empty for most rules, whose findings are notes (see <see cref="Notes"/>). The thermal rule raises
    /// <c>FS2111</c> and <c>FS4008</c>, both errors a user acts on, and an error a user cannot look up is
    /// the case <c>C-44</c> warns against -- so those two are registered and carried here, anchored to
    /// the component, for the outer loop to attach to the run.
    /// </value>
    public ImmutableArray<Diagnostics.Diagnostic> Diagnostics { get; init; } = [];
}
