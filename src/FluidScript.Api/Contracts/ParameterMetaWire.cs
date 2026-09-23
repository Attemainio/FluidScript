using System.Collections.Immutable;

namespace FluidScript.Api.Contracts;

/// <summary>One parameter of a kind.</summary>
public sealed record ParameterMetaWire
{
    /// <summary>The canonical name.</summary>
    public required string Name { get; init; }

    /// <summary>Other spellings the binder accepts.</summary>
    public required ImmutableArray<string> Aliases { get; init; }

    /// <summary><c>quantity</c>, <c>symbol</c> or <c>reference</c>.</summary>
    public required string ValueKind { get; init; }

    /// <summary>The dimension's name, an entry in <see cref="MetadataWire.Dimensions"/>; <see langword="null"/> for a synthesised dimension such as W/(m²·K), which <see cref="Unit"/> alone names.</summary>
    public required string? Dimension { get; init; }

    /// <summary>The unit a bare number means and values are reported in, or <see langword="null"/> for a dimensionless parameter.</summary>
    public required string? Unit { get; init; }

    /// <summary>The symbols a <c>symbol</c>-valued parameter accepts, such as a valve characteristic.</summary>
    public required ImmutableArray<string> AcceptedSymbols { get; init; }

    /// <summary>What omitting it means: <c>size</c>, <c>default</c> or <c>require</c> (<c>D-02</c>).</summary>
    public required string Omission { get; init; }

    /// <summary>The default as the script would write it, when the omission policy is <c>default</c>.</summary>
    public required string? Default { get; init; }

    /// <summary>Why that default, in the registry's words.</summary>
    public required string? DefaultBasis { get; init; }

    /// <summary>The range a stated value usually falls in, or <see langword="null"/>.</summary>
    public required RangeMetaWire? UsualRange { get; init; }

    /// <summary>The range outside which a stated value is an error, or <see langword="null"/>.</summary>
    public required RangeMetaWire? ValidRange { get; init; }

    /// <summary>Whether a stated value must be a whole number.</summary>
    public required bool WholeNumber { get; init; }

    /// <summary>Significant digits a display shows.</summary>
    public required int DisplayPrecision { get; init; }
}
