using System.Net;
using System.Net.Http.Headers;

using FluidScript.Api.Contracts;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Registry;

namespace FluidScript.Api.Tests.Endpoints;

/// <summary>
/// <c>42</c>'s metadata: every kind and every code, the tank's families, the limits, and an ETag that
/// makes the second fetch free. And the OpenAPI document the routes describe themselves with.
/// </summary>
[Trait("Category", "Api")]
public sealed class MetadataTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Metadata = "/api/v1/metadata";

    private static readonly System.Text.Json.JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task TheDocumentIsCommittedForTheEditorsTests()
    {
        // The frontend's completion tests read the same document the host serves, from the goldens
        // directory, so an editor test never depends on a running host and a metadata change is a
        // reviewed diff on both sides. Regenerated with FLUIDSCRIPT_UPDATE_GOLDENS=1 like the rest.
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(Metadata, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var indented = System.Text.Json.JsonSerializer.Serialize(
            System.Text.Json.JsonDocument.Parse(body).RootElement,
            Indented) + "\n";
        var path = Path.Combine(Contracts.Goldens.Directory, "metadata.json");

        if (Environment.GetEnvironmentVariable("FLUIDSCRIPT_UPDATE_GOLDENS") == "1")
        {
            await File.WriteAllTextAsync(path, indented, TestContext.Current.CancellationToken);
            return;
        }

        Assert.True(File.Exists(path), "No committed metadata.json. Run once with FLUIDSCRIPT_UPDATE_GOLDENS=1 to create it.");
        var expected = (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");
        Assert.True(string.Equals(expected, indented, StringComparison.Ordinal), "The metadata document differs from the committed metadata.json; if intended, regenerate with FLUIDSCRIPT_UPDATE_GOLDENS=1 and review the diff.");
    }

    [Fact]
    public async Task EveryRegisteredKindAndEveryDiagnosticCodeIsDescribed()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(Metadata, TestContext.Current.CancellationToken);
        var body = await response.ReadAsync<MetadataWire>();

        Assert.Equal(1, body.RestMajor);
        Assert.Equal("2.3", body.ContractVersion);
        Assert.Equal(1, body.Language.Current);
        Assert.Equal(ComponentRegistry.Default.Kinds.Select(static k => k.Keyword), body.Kinds.Select(static k => k.Keyword));
        Assert.Equal(DiagnosticRegistry.All.Select(static d => d.Code), body.Diagnostics.Select(static d => d.Code));
        Assert.Equal(DiagnosticRegistry.Retired.Select(static d => d.Code), body.RetiredDiagnostics.Select(static d => d.Code));
        Assert.Contains(body.Diagnostics, static d => d.Code == "FS4601" && d.Area == "Request");
        Assert.NotEmpty(body.Symbols);
        Assert.All(body.Kinds, kind => Assert.Contains(body.Symbols, symbol => symbol.Id == kind.SymbolId));
        Assert.Equal(InputLimits.Default.Unknowns, body.Limits.Unknowns);
        Assert.Equal(InputLimits.Default.SourceBytes, body.Limits.SourceBytes);
        Assert.NotEmpty(body.DocsIndex);
        Assert.Contains(body.Catalogs, static c => c.Id == "steel_en10255");
    }

    [Fact]
    public async Task TheTankExposesItsAliasesFamiliesAndIndexedPatterns()
    {
        // 42's acceptance row for the tank (D-32): canonical tank/volume, the container/v aliases, the
        // 2..16 families beyond the fixed `in`/`out`, written `in[n]` and keyed `in{n}` (D-120).
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(Metadata, TestContext.Current.CancellationToken);
        var body = await response.ReadAsync<MetadataWire>();

        var tank = Assert.Single(body.Kinds, static k => k.Keyword == "tank");
        Assert.Contains("container", tank.Aliases);
        var volume = Assert.Single(tank.Parameters, static p => p.Name == "volume");
        Assert.Contains("v", volume.Aliases);
        Assert.Contains(tank.Ports, static p => p.Name == "in");
        Assert.Contains(tank.PortFamilies, static f => f.Prefix == "in" && f.Pattern == "in[{index}]" && f.MinIndex == 2 && f.MaxIndex == 16 && f.Role == "bidirectional");
        Assert.Contains(tank.PortFamilies, static f => f.Prefix == "out" && f.Pattern == "out[{index}]" && f.MinIndex == 2 && f.MaxIndex == 16 && f.Role == "bidirectional");
        Assert.Contains(tank.IndexedParameters, static f => f.Pattern.Contains("{index}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryParameterNamesADimensionTheDocumentDescribes()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(Metadata, TestContext.Current.CancellationToken);
        var body = await response.ReadAsync<MetadataWire>();
        var dimensions = body.Dimensions.Select(static d => d.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var parameter in body.Kinds.SelectMany(static k => k.Parameters))
        {
            if (parameter.Dimension is null)
            {
                // A synthesised dimension -- an exchanger's u in W/(m²·K) -- has no name of its own, so the unit must say what it is.
                Assert.True(parameter.Unit is not null, $"{parameter.Name} has neither a dimension name nor a unit");
                continue;
            }

            Assert.Contains(parameter.Dimension, dimensions);
        }
    }

    [Fact]
    public async Task EveryUnitCarriesItsConversionToSi()
    {
        // A-6: the quantity hover converts on the client from these, so every unit symbol has a factor and
        // an offset, in the order the symbols are listed, and they are 13's numbers: kW is a thousand
        // watts, °C is offset by 273.15, and a ratio unit has no offset.
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(Metadata, TestContext.Current.CancellationToken);
        var body = await response.ReadAsync<MetadataWire>();

        foreach (var dimension in body.Dimensions)
        {
            Assert.Equal(dimension.Units, dimension.Conversions.Select(static c => c.Symbol));
            Assert.All(dimension.Conversions, static c => Assert.True(c.Factor > 0, $"{c.Symbol} has factor {c.Factor}"));
        }

        var power = body.Dimensions.Single(static d => d.Name == "Power");
        Assert.Equal((1000.0, 0.0), power.Conversions.Single(static c => c.Symbol == "kW") is var kw ? (kw.Factor, kw.Offset) : default);

        var temperature = body.Dimensions.Single(static d => d.Name == "Temperature");
        Assert.Equal(273.15, temperature.Conversions.Single(static c => c.Symbol == "°C").Offset, 6);
    }

    [Fact]
    public async Task TheDocumentCarriesAnETagAndAMatchingFetchIs304()
    {
        using var client = factory.CreateClient();
        using var first = await client.GetAsync(Metadata, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);

        using var request = new HttpRequestMessage(HttpMethod.Get, Metadata);
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag.Tag));
        using var second = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task TheOpenApiDocumentListsEveryRoute()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await response.ReadJsonAsync();
        var paths = json.RootElement.GetProperty("paths").EnumerateObject().Select(static p => p.Name).ToList();

        foreach (var path in new[] { "/api/v1/compile", "/api/v1/solve", "/api/v1/validate", "/api/v1/format", "/api/v1/metadata", "/api/health" })
        {
            Assert.Contains(path, paths);
        }
    }

    [Fact]
    public async Task HealthStillAnswersWhereM0PutIt()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);
        using var json = await response.ReadJsonAsync();

        Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
    }
}
