using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>One diagnostic (<c>44</c>).</summary>
public sealed record DiagnosticWire
{
    /// <summary>The registry code.</summary>
    public required string Code { get; init; }

    /// <summary><c>error</c>, <c>warning</c> or <c>info</c>.</summary>
    public required string Severity { get; init; }

    /// <summary>The rendered message.</summary>
    public required string Message { get; init; }

    /// <summary>Where, or <see langword="null"/> when it is about no source text.</summary>
    public required RangeWire? Range { get; init; }

    /// <summary>The component it is about, or <see langword="null"/>.</summary>
    public required string? Component { get; init; }

    /// <summary>A fix, or <see langword="null"/>.</summary>
    public required SuggestionWire? Suggestion { get; init; }

    /// <summary>Other places it is about.</summary>
    public required ImmutableArray<RelatedWire> Related { get; init; }
}
