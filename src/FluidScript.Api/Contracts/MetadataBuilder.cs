using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using FluidScript.Core.Catalogs;
using FluidScript.Core.Compatibility;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Fluids;
using FluidScript.Core.Language;
using FluidScript.Core.Model;
using FluidScript.Core.Units;

using Microsoft.Extensions.Options;

namespace FluidScript.Api.Contracts;

/// <summary>The metadata document, built once from the registries and held with its bytes and ETag.</summary>
/// <remarks>
/// Built lazily on the first request and never again: every source is an immutable registry, so the
/// document is a pure function of the build and the options (<c>42</c>'s invariant 5). The ETag is
/// the SHA-256 of the bytes, so a client that cached it keeps it until the build changes.
/// </remarks>
public sealed class MetadataDocument
{
    private readonly Lazy<(MetadataWire Value, byte[] Json, string ETag)> _document;

    /// <summary>Creates the document for the configured limits and docs index.</summary>
    /// <param name="options">The host options.</param>
    public MetadataDocument(IOptions<ApiOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _document = new Lazy<(MetadataWire, byte[], string)>(() =>
        {
            var value = Build(options.Value);
            var json = JsonSerializer.SerializeToUtf8Bytes(value, ModelContractJson.Options);
            var etag = "\"" + Convert.ToHexStringLower(SHA256.HashData(json))[..32] + "\"";
            return (value, json, etag);
        });
    }

    /// <summary>The document.</summary>
    public MetadataWire Value => _document.Value.Value;

    /// <summary>The document's compact JSON, the bytes the endpoint writes.</summary>
    public ReadOnlyMemory<byte> Json => _document.Value.Json;

    /// <summary>The entity tag, a quoted string as the header carries it.</summary>
    public string ETag => _document.Value.ETag;

    /// <summary>Projects the registries into the wire shape.</summary>
    /// <param name="options">The host options, for the limits and the docs index.</param>
    /// <returns>The document.</returns>
    public static MetadataWire Build(ApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var versions = SupportedVersions.Default;

        return new MetadataWire
        {
            RestMajor = 1,
            ContractVersion = ModelContractBuilder.ContractVersion,
            Language = new LanguageVersionsWire(versions.Current.Value, [.. versions.Supported.Select(static major => major.Value)]),
            Kinds = [.. ComponentRegistry.Default.Kinds.Select(Kind)],
            Dimensions = [.. Dimension.All.Select(DimensionOf)],
            Symbols = SymbolCatalog.All,
            Diagnostics = [.. DiagnosticRegistry.All.Select(static descriptor => new DiagnosticCodeWire(
                descriptor.Code,
                descriptor.Severity.ToString().ToLowerInvariant(),
                descriptor.Area.ToString(),
                descriptor.MessageTemplate,
                descriptor.ArgumentNames))],
            RetiredDiagnostics = [.. DiagnosticRegistry.Retired.Select(static retired => new RetiredCodeWire(retired.Code, retired.Reason))],
            Catalogs =
            [
                .. PipeCatalogs.All.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => new CatalogWire(pair.Key, pair.Value.Version, pair.Value.Standard)),
                new CatalogWire(ValveKvR5.Instance.Name, ValveKvR5.Instance.Version, ValveKvR5.Instance.Standard),
            ],
            PropertyBackend = ModelContractBuilder.PropertyBackend,
            Limits = new LimitsWire(options.Limits.SourceBytes, options.Limits.Declarations, options.Limits.Tokens, options.Limits.Unknowns),
            DocsIndex = options.DocsIndex,
        };
    }

    private static KindWire Kind(ComponentKindInfo kind) => new()
    {
        Keyword = kind.Keyword,
        Aliases = kind.Aliases,
        TagCode = kind.TagCode,
        DrivesFlow = kind.DrivesFlow,
        IsObserver = kind.IsObserver,
        SymbolId = SymbolCatalog.IdFor(kind.Keyword),
        Ports = [.. kind.Ports.Select(static port => new PortWire(port.Name, Role(port.Role), port.IsOptional))],
        PortFamilies = [.. kind.PortFamilies.Select(static family => new PortFamilyWire(
            family.Prefix, family.Pattern, family.MinIndex, family.MaxIndex, Role(family.Role), family.LevelParameterSuffix))],
        // The registry holds parameters and properties in dictionaries, whose order is the process's
        // string hashing and differs between runs; the document must be the same bytes on every host
        // (42: a pure function of the deployed build, and the ETag depends on it), so both are ordered
        // by name. A-5.
        Parameters = [.. kind.Parameters.Values.OrderBy(static p => p.Name, StringComparer.Ordinal).Select(Parameter)],
        IndexedParameters = [.. kind.IndexedParameterFamilies.Select(static family => new IndexedParameterWire(
            family.Pattern, family.MinIndex, family.MaxIndex, family.MaxIndexParameter, Parameter(family.Element)))],
        Properties = [.. kind.Properties.Values.OrderBy(static p => p.Name, StringComparer.Ordinal).Select(Property)],
        IndexedProperties = [.. kind.IndexedPropertyFamilies.Select(static family => new IndexedPropertyWire(
            family.Pattern, family.MinIndex, family.MaxIndex, family.MaxIndexParameter, Property(family.Element)))],
        ActuatedParameter = kind.ActuatedParameter,
        MeasuredProperty = kind.MeasuredProperty,
    };

    private static ParameterMetaWire Parameter(ParameterInfo parameter) => new()
    {
        Name = parameter.Name,
        Aliases = parameter.Aliases,
        ValueKind = parameter.ValueKind.ToString().ToLowerInvariant(),
        Dimension = parameter.Dimension.IsNamed ? parameter.Dimension.Name : null,
        Unit = parameter.Dimension.CanonicalUnit
            ?? UnitTable.CanonicalUnitFor(parameter.Dimension)?.Text
            ?? (parameter.Dimension.SiUnit.Length == 0 ? null : parameter.Dimension.SiUnit),
        AcceptedSymbols = parameter.AcceptedSymbols,
        Omission = parameter.OmissionBehavior.ToString().ToLowerInvariant(),
        Default = parameter.DefaultLiteral,
        DefaultBasis = parameter.DefaultBasis,
        UsualRange = RangeIn(parameter.UsualRange, parameter.Dimension),
        ValidRange = RangeIn(parameter.Validity?.Range, parameter.Dimension),
        WholeNumber = parameter.Validity?.RequiresWholeNumber ?? false,
        DisplayPrecision = parameter.DisplayPrecision,
    };

    /// <summary>A registry range, held in SI, in the unit the parameter is reported in, as <see cref="RangeMetaWire"/> promises.</summary>
    /// <param name="range">The range in SI, or <see langword="null"/>.</param>
    /// <param name="dimension">The parameter's dimension, whose canonical unit converts it.</param>
    /// <returns>The range in the canonical unit; in SI when the dimension has no canonical unit.</returns>
    private static RangeMetaWire? RangeIn(Range<double>? range, Dimension dimension)
    {
        if (range is not { } si)
        {
            return null;
        }

        var unit = UnitTable.CanonicalUnitFor(dimension);
        return unit is null
            ? new RangeMetaWire(si.Min, si.Max)
            : new RangeMetaWire(unit.FromSi(si.Min), unit.FromSi(si.Max));
    }

    private static PropertyMetaWire Property(PropertyInfo property) =>
        new(property.Name, property.Dimension.IsNamed ? property.Dimension.Name : null, property.CanonicalUnit, property.Availability.ToString().ToLowerInvariant());

    private static DimensionWire DimensionOf(Dimension dimension)
    {
        var units = UnitTable.All.Where(unit => unit.Dimension == dimension).ToImmutableArray();

        return new DimensionWire(
            dimension.Name,
            dimension.SiUnit,
            dimension.CanonicalUnit,
            [.. units.Select(static unit => unit.Text)],
            [.. units.Select(static unit => new UnitConversionWire(unit.Text, unit.Factor, unit.Offset))]);
    }

    private static string Role(PortRole role) => role.ToString().ToLowerInvariant();
}
