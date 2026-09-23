using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>The one serialized shape every consumer receives (<c>26</c>).</summary>
/// <remarks>
/// <para>
/// <strong>No Core type is on the wire.</strong> Every record here is a copy, so a rename in Core cannot
/// reshape the API. Every number is in the canonical script unit for its dimension (<c>D-14</c>), never
/// SI, and sits beside the unit it is in. <see langword="null"/> means <em>not computed</em>; a field
/// that is not applicable is absent, which is what <see cref="AbsentWhenNullAttribute"/> marks.
/// </para>
/// <para>
/// Property order is the wire order: the Api's serializer writes members in declaration order and
/// reads them back the same way, which is what makes a round trip byte-identical (invariant 10). No
/// serialization type is named here (<c>D-47</c>); the serializer lives in <c>FluidScript.Api</c>.
/// </para>
/// </remarks>
public sealed record ModelContract
{
    /// <summary>The contract version the producing Core implements, <c>major.minor</c>.</summary>
    public required string ContractVersion { get; init; }

    /// <summary>What produced this: the source, the language, the catalogue, the property backend.</summary>
    public required Provenance Provenance { get; init; }

    /// <summary>The <c>project</c> line, absent when the script has none (<c>D-37</c>).</summary>
    [AbsentWhenNull]
    public ProjectWire? Project { get; init; }

    /// <summary>Presentation Core carries and never interprets.</summary>
    public required StyleWire Style { get; init; }

    /// <summary>Every circuit, in declaration order; never empty (<c>D-33</c>).</summary>
    public required ImmutableArray<CircuitWire> Circuits { get; init; }

    /// <summary>One pressure datum per hydraulically connected part, not per circuit.</summary>
    public required ImmutableArray<string> PressureDatums { get; init; }

    /// <summary>Every graph component, in graph order.</summary>
    public required ImmutableArray<ComponentWire> Components { get; init; }

    /// <summary>The symbol definitions the components reference (<c>D-20</c>, <c>D-24</c>).</summary>
    public required ImmutableArray<SymbolWire> Symbols { get; init; }

    /// <summary>Every adjacency, in the model's connection order, keyed <c>c{n}</c>.</summary>
    public required ImmutableArray<ConnectionWire> Connections { get; init; }

    /// <summary>The layout hints, serialized from <c>25</c>'s contract field for field.</summary>
    public required LayoutWire Layout { get; init; }

    /// <summary>The <c>show</c> directive's resolution (<c>57</c>).</summary>
    public required VisualizationWire Visualization { get; init; }

    /// <summary>Evaluated <c>let</c> values.</summary>
    public required ImmutableArray<BindingWire> Bindings { get; init; }

    /// <summary>Every diagnostic the pipeline produced, ordered by severity then offset (<c>44</c>).</summary>
    public required ImmutableArray<DiagnosticWire> Diagnostics { get; init; }

    /// <summary>What the solve did, or <see langword="null"/> when nothing was solved.</summary>
    public required SolveWire? Solve { get; init; }
}
