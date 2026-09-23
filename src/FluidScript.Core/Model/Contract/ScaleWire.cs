namespace FluidScript.Core.Model.Contract;

/// <summary>A colour scale.</summary>
public sealed record ScaleWire
{
    /// <summary>The property.</summary>
    public required string Property { get; init; }

    /// <summary>The legend title.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The unit the domain is in.</summary>
    public required string Unit { get; init; }

    /// <summary><c>sequential</c> or <c>diverging</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The range mapped to the scale's ends, or <see langword="null"/> before anything is solved.</summary>
    public required DomainWire? Domain { get; init; }

    /// <summary>Whether every element has the same value at the legend's precision; a plant on its pressure datum is degenerate at 0 (<c>C-112</c>).</summary>
    public required bool Degenerate { get; init; }
}
