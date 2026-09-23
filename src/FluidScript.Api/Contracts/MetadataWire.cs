using System.Collections.Immutable;
using FluidScript.Core.Model.Contract;

namespace FluidScript.Api.Contracts;

/// <summary>The static description of the language, the body of <c>GET /api/v1/metadata</c> (<c>42</c>).</summary>
/// <remarks>
/// A pure function of the deployed build: the same bytes on every call until the build changes, which
/// is what the ETag says. It is what an agent reads first (<c>R-29</c>) and what drives editor
/// completion, so a gap here shows up as missing autocomplete.
/// </remarks>
public sealed record MetadataWire
{
    /// <summary>The REST major this document describes; the <c>v1</c> in the path.</summary>
    public required int RestMajor { get; init; }

    /// <summary>The model contract version <c>compile</c> returns.</summary>
    public required string ContractVersion { get; init; }

    /// <summary>The language majors this build reads.</summary>
    public required LanguageVersionsWire Language { get; init; }

    /// <summary>Every component kind, in registry order.</summary>
    public required ImmutableArray<KindWire> Kinds { get; init; }

    /// <summary>Every dimension a parameter or property can have, with its units.</summary>
    public required ImmutableArray<DimensionWire> Dimensions { get; init; }

    /// <summary>The symbol definitions the canvas draws with (<c>D-24</c>), the same records the model contract carries.</summary>
    public required ImmutableArray<SymbolWire> Symbols { get; init; }

    /// <summary>Every live diagnostic code.</summary>
    public required ImmutableArray<DiagnosticCodeWire> Diagnostics { get; init; }

    /// <summary>Codes that were allocated and are no longer emitted, so a stale reference can be told from a typo.</summary>
    public required ImmutableArray<RetiredCodeWire> RetiredDiagnostics { get; init; }

    /// <summary>The catalogues a script may pin, with their exact versions.</summary>
    public required ImmutableArray<CatalogWire> Catalogs { get; init; }

    /// <summary>The fluid property package and its version.</summary>
    public required VersionedId PropertyBackend { get; init; }

    /// <summary>The ceilings a request may reach (<c>07</c>).</summary>
    public required LimitsWire Limits { get; init; }

    /// <summary>A URI to the generated function index (<c>61</c>).</summary>
    public required string DocsIndex { get; init; }
}

/// <summary>Which language majors are read.</summary>
/// <param name="Current">The major a new script is written in.</param>
/// <param name="Supported">Every major this build parses, including the current one.</param>
public sealed record LanguageVersionsWire(int Current, ImmutableArray<int> Supported);

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

/// <summary>One fixed port.</summary>
/// <param name="Name">The port name as written after a dot.</param>
/// <param name="Role"><c>inlet</c>, <c>outlet</c> or <c>bidirectional</c>.</param>
/// <param name="Optional">Whether inference rule I3 leaves it unconnected without a boundary node.</param>
public sealed record PortWire(string Name, string Role, bool Optional);

/// <summary>An indexed port family.</summary>
/// <param name="Prefix">The name before the index, <c>in</c> for the port id <c>in2</c>: a component's port ids and the model's keys are <c>{Prefix}{index}</c>.</param>
/// <param name="Pattern">How a script writes a member, with one <c>{index}</c> placeholder: <c>in[{index}]</c> (<c>D-120</c>). The first member is the fixed port <c>in</c>, listed under <c>ports</c>.</param>
/// <param name="MinIndex">The lowest index the family itself covers; the fixed first port sits below it.</param>
/// <param name="MaxIndex">The highest index.</param>
/// <param name="Role"><c>inlet</c>, <c>outlet</c> or <c>bidirectional</c>.</param>
/// <param name="LevelParameterSuffix">The suffix of the parameter key that places the port (<c>in2_level</c>), or <see langword="null"/>; the script spelling of that parameter is under <c>indexedParameters</c>.</param>
public sealed record PortFamilyWire(string Prefix, string Pattern, int MinIndex, int MaxIndex, string Role, string? LevelParameterSuffix);

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

/// <summary>A closed range in the parameter's own unit.</summary>
/// <param name="Min">The lowest value.</param>
/// <param name="Max">The highest value.</param>
public sealed record RangeMetaWire(double Min, double Max);

/// <summary>An indexed parameter family.</summary>
/// <param name="Pattern">The name with <c>{index}</c> where the index goes.</param>
/// <param name="MinIndex">The lowest index.</param>
/// <param name="MaxIndex">The highest index, or <see langword="null"/> when another parameter sets it.</param>
/// <param name="MaxIndexParameter">The parameter that sets the highest index, or <see langword="null"/>.</param>
/// <param name="Element">What each member of the family is.</param>
public sealed record IndexedParameterWire(string Pattern, int MinIndex, int? MaxIndex, string? MaxIndexParameter, ParameterMetaWire Element);

/// <summary>One readable property of a kind.</summary>
/// <param name="Name">The name after the dot in an expression.</param>
/// <param name="Dimension">The dimension's name.</param>
/// <param name="Unit">The unit it is reported in.</param>
/// <param name="Availability">When it has a value: <c>declared</c>, <c>sized</c> or <c>solved</c>.</param>
public sealed record PropertyMetaWire(string Name, string? Dimension, string Unit, string Availability);

/// <summary>An indexed property family.</summary>
/// <param name="Pattern">The name with <c>{index}</c> where the index goes.</param>
/// <param name="MinIndex">The lowest index.</param>
/// <param name="MaxIndex">The highest index, or <see langword="null"/> when another parameter sets it.</param>
/// <param name="MaxIndexParameter">The parameter that sets the highest index, or <see langword="null"/>.</param>
/// <param name="Element">What each member of the family is.</param>
public sealed record IndexedPropertyWire(string Pattern, int MinIndex, int? MaxIndex, string? MaxIndexParameter, PropertyMetaWire Element);

/// <summary>One dimension and the units the script may write for it.</summary>
/// <param name="Name">The dimension's name, as parameters refer to it.</param>
/// <param name="SiUnit">The unit Core computes in.</param>
/// <param name="CanonicalUnit">The unit a bare number means and the wire reports in, or <see langword="null"/>.</param>
/// <param name="Units">Every unit symbol accepted for this dimension.</param>
/// <param name="Conversions">How each of <paramref name="Units"/> converts to <paramref name="SiUnit"/>, in the same order (<c>A-6</c>): the editor's quantity hover shows a value in SI and in the alternative units from this, so no second unit table exists on the client.</param>
public sealed record DimensionWire(string Name, string SiUnit, string? CanonicalUnit, ImmutableArray<string> Units, ImmutableArray<UnitConversionWire> Conversions);

/// <summary>One unit symbol's conversion to its dimension's SI base unit: <c>si = value × Factor + Offset</c> (<c>13</c>).</summary>
/// <param name="Symbol">The symbol as a script writes it.</param>
/// <param name="Factor">The multiplier to the SI base unit: 1000 for <c>kW</c>.</param>
/// <param name="Offset">Added after scaling, in the SI base unit: 273.15 for <c>°C</c>; zero for every ratio unit.</param>
public sealed record UnitConversionWire(string Symbol, double Factor, double Offset);

/// <summary>One diagnostic code.</summary>
/// <param name="Code">The code, <c>FS1302</c>.</param>
/// <param name="Severity"><c>error</c>, <c>warning</c> or <c>info</c>.</param>
/// <param name="Area">The subject the code belongs to.</param>
/// <param name="Message">The message with its <c>{placeholders}</c> unfilled.</param>
/// <param name="Arguments">The placeholder names, in order.</param>
public sealed record DiagnosticCodeWire(string Code, string Severity, string Area, string Message, ImmutableArray<string> Arguments);

/// <summary>A code no longer emitted.</summary>
/// <param name="Code">The code.</param>
/// <param name="Reason">Why it was withdrawn and what replaced it.</param>
public sealed record RetiredCodeWire(string Code, string Reason);

/// <summary>One catalogue a script may pin (<c>27</c>).</summary>
/// <param name="Id">The id as written after <c>catalog</c>.</param>
/// <param name="Version">The exact version.</param>
/// <param name="Standard">The standard the rows are drawn from, or <see langword="null"/>.</param>
public sealed record CatalogWire(string Id, string Version, string? Standard);

/// <summary>The input ceilings (<c>07</c>).</summary>
/// <param name="SourceBytes">The largest script accepted, bytes of UTF-8; over it is 413.</param>
/// <param name="Declarations">The most component declarations; over it is <c>FS4601</c>.</param>
/// <param name="Tokens">The most tokens; over it is <c>FS4601</c>.</param>
/// <param name="Unknowns">The most solver unknowns; over it is <c>FS4601</c>.</param>
public sealed record LimitsWire(long SourceBytes, int Declarations, int Tokens, int Unknowns);
