using System.Collections.Immutable;

namespace FluidScript.Api.Contracts;

/// <summary>One component kind as the script can write it.</summary>
public sealed record KindWire
{
    /// <summary>The canonical keyword.</summary>
    public required string Keyword { get; init; }

    /// <summary>Other spellings the binder accepts and reads as the keyword.</summary>
    public required ImmutableArray<string> Aliases { get; init; }

    /// <summary>The equipment-tag code (<c>D-34</c>), or <see langword="null"/> when the kind is not tagged.</summary>
    public required string? TagCode { get; init; }

    /// <summary>Whether the kind drives flow, as a pump does.</summary>
    public required bool DrivesFlow { get; init; }

    /// <summary>Whether the kind observes rather than carries flow: a sensor or a controller.</summary>
    public required bool IsObserver { get; init; }

    /// <summary>The symbol that draws it, an id into <see cref="MetadataWire.Symbols"/>.</summary>
    public required string SymbolId { get; init; }

    /// <summary>The fixed ports, in declaration order.</summary>
    public required ImmutableArray<PortWire> Ports { get; init; }

    /// <summary>Indexed port families such as a tank's <c>in[2]</c>..<c>in[16]</c> (<c>D-32</c>, <c>D-120</c>).</summary>
    public required ImmutableArray<PortFamilyWire> PortFamilies { get; init; }

    /// <summary>The parameters, in the registry's order.</summary>
    public required ImmutableArray<ParameterMetaWire> Parameters { get; init; }

    /// <summary>Indexed parameter families such as a tank's <c>t{index}</c>.</summary>
    public required ImmutableArray<IndexedParameterWire> IndexedParameters { get; init; }

    /// <summary>The properties an expression can read, in the registry's order.</summary>
    public required ImmutableArray<PropertyMetaWire> Properties { get; init; }

    /// <summary>Indexed property families.</summary>
    public required ImmutableArray<IndexedPropertyWire> IndexedProperties { get; init; }

    /// <summary>The parameter a <c>control</c> line may actuate, or <see langword="null"/>.</summary>
    public required string? ActuatedParameter { get; init; }

    /// <summary>The property a sensor of this kind measures, or <see langword="null"/>.</summary>
    public required string? MeasuredProperty { get; init; }
}
