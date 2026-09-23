using System.Collections.Immutable;

namespace FluidScript.Core.Layout.Hints;

/// <summary>The expansion of one written component into the graph elements it became.</summary>
public sealed record ComponentGroupHint
{
    /// <summary>The stable id of the component the script wrote.</summary>
    public required string ParentComponentId { get; init; }

    /// <summary>Every lowered child, in deterministic local order.</summary>
    public required ImmutableArray<string> Children { get; init; }
}
