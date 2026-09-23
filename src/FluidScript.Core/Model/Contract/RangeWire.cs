namespace FluidScript.Core.Model.Contract;

/// <summary>A source range in both forms, from one line index.</summary>
public sealed record RangeWire
{
    /// <summary>Where it starts.</summary>
    public required PositionWire Start { get; init; }

    /// <summary>Where it ends, exclusive.</summary>
    public required PositionWire End { get; init; }

    /// <summary>The zero-based character offset.</summary>
    public required int Offset { get; init; }

    /// <summary>The length in UTF-16 code units.</summary>
    public required int Length { get; init; }
}
