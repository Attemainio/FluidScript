using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>One graph component.</summary>
public sealed record ComponentWire
{
    /// <summary>The stable id (<c>25</c>).</summary>
    public required string Id { get; init; }

    /// <summary>The script keyword for the kind.</summary>
    public required string Kind { get; init; }

    /// <summary>The kind's canonical mode -- an exchanger's <c>duty</c>, <c>rated</c> or <c>coupled</c> -- absent for a kind without one.</summary>
    [AbsentWhenNull]
    public string? Mode { get; init; }

    /// <summary>Which entry in <c>symbols</c> draws it.</summary>
    public required string SymbolId { get; init; }

    /// <summary><c>declared</c>, or <c>inferred:I1</c>, <c>inferred:I2</c>, <c>inferred:I3</c>, <c>inferred:I7</c> (a pipe a connection line's properties made, <c>D-110</c>).</summary>
    public required string Origin { get; init; }

    /// <summary>Where the declaration sits in the source: the component's line, or for an implicit pipe (I7) the connection line that made it; <see langword="null"/> for an inferred node, which has no text.</summary>
    public required SpanWire? SourceSpan { get; init; }

    /// <summary>The owning circuit (<c>D-33</c>; the losing side's under <c>D-36</c>).</summary>
    public required string Circuit { get; init; }

    /// <summary>The equipment tag, display metadata only; <see langword="null"/> when the kind has no code or the component is inferred (<c>D-34</c>).</summary>
    public required string? Tag { get; init; }

    /// <summary>The design specification, by canonical parameter name, in declaration order of the kind's parameters.</summary>
    public required IReadOnlyDictionary<string, ParameterWire> Parameters { get; init; }

    /// <summary>The solved operating point, or <see langword="null"/> when the circuit is unsolved or states are omitted.</summary>
    public required ComponentStateWire? State { get; init; }

    /// <summary>Every port, in declaration order; a tank lists only its materialized ports.</summary>
    public required ImmutableArray<PortWire> Ports { get; init; }
}
