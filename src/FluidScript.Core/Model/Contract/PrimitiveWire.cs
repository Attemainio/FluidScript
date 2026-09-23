using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>One drawing primitive; the fields a kind does not use are absent.</summary>
public sealed record PrimitiveWire
{
    /// <summary><c>rect</c>, <c>line</c>, <c>circle</c>, <c>polyline</c> or <c>polygon</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>A rectangle's or circle's origin.</summary>
    [AbsentWhenNull]
    public double? X { get; init; }

    /// <summary>A rectangle's or circle's origin.</summary>
    [AbsentWhenNull]
    public double? Y { get; init; }

    /// <summary>A rectangle's width.</summary>
    [AbsentWhenNull]
    public double? Width { get; init; }

    /// <summary>A rectangle's height.</summary>
    [AbsentWhenNull]
    public double? Height { get; init; }

    /// <summary>A circle's radius.</summary>
    [AbsentWhenNull]
    public double? R { get; init; }

    /// <summary>A line's start.</summary>
    [AbsentWhenNull]
    public ImmutableArray<double>? From { get; init; }

    /// <summary>A line's end.</summary>
    [AbsentWhenNull]
    public ImmutableArray<double>? To { get; init; }

    /// <summary>A polyline's or polygon's points, flattened <c>[x0, y0, x1, y1, …]</c>.</summary>
    [AbsentWhenNull]
    public ImmutableArray<double>? Points { get; init; }

    /// <summary>
    /// What fills a closed shape: <c>state</c> for the active colour scale's slot (<c>57</c>), <c>stroke</c>
    /// for a solid mark in the line colour; absent for an outline.
    /// </summary>
    [AbsentWhenNull]
    public string? Fill { get; init; }

    /// <summary><see langword="true"/> for a dashed stroke; absent for a solid one.</summary>
    [AbsentWhenNull]
    public bool? Dashed { get; init; }
}
