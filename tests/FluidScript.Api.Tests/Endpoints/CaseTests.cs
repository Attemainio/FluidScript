using FluidScript.Api.Contracts;
using FluidScript.Core.Model.Contract;

namespace FluidScript.Api.Tests.Endpoints;

/// <summary>
/// A file with cases on the canvas (<c>D-143</c> step 4, <c>D-182</c>): the merged plant, drawn in the case the request
/// chose. Measured on <c>m5-scenarios</c>, whose smaller duty governs its flows.
/// </summary>
[Trait("Category", "Api")]
public sealed class CaseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Compile = "/api/v1/compile";

    private async Task<ModelContract> CompiledAsync(string? drawn)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(
            Compile,
            new { sessionId = "cases", script = Api.Sample("m5-scenarios.fluid"), @case = drawn });
        var body = await response.ReadAsync<CompileResponse>();

        Assert.NotNull(body.Model);
        Assert.True(body.Model.Solve?.Converged, string.Join("; ", body.Model.Diagnostics.Select(static d => $"{d.Code} {d.Message}")));
        return body.Model;
    }

    private static ParameterWire Parameter(ModelContract model, string component, string name) =>
        model.Components.Single(c => c.Id == component).Parameters[name];

    /// <summary>
    /// Before <c>C-148</c> the canvas drew the plant sized for winter alone: DN50 at winter's 1.197 kg/s. Summer's 40 kW over
    /// 5 K is 1.906 kg/s, and the plant that covers both has DN65.
    /// </summary>
    [Fact]
    public async Task TheCanvasDrawsTheMergedPlantInTheOperatingCase()
    {
        var model = await CompiledAsync(null);

        Assert.Equal(["winter", "summer"], model.Cases!.Names);
        Assert.Equal("winter", model.Cases.Drawn);
        Assert.Equal(65, Parameter(model, "P1", "dn").Value);
        Assert.Equal(50, Parameter(model, "HE1", "power").Value);
    }

    [Fact]
    public async Task AChosenCaseIsDrawnOnTheSamePlant()
    {
        var model = await CompiledAsync("summer");

        Assert.Equal("summer", model.Cases!.Drawn);
        Assert.Equal(65, Parameter(model, "P1", "dn").Value);
        Assert.Equal(-40, Parameter(model, "HE1", "power").Value);
    }

    [Fact]
    public async Task ACaseTheFileDoesNotDeclareDrawsTheOperatingCase() =>
        Assert.Equal("winter", (await CompiledAsync("spring")).Cases!.Drawn);

    [Fact]
    public async Task AFileWithoutCasesHasNoCases()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(Compile, new { sessionId = "no-cases", script = Api.Sample("m2-cooling-loop.fluid") });
        var body = await response.ReadAsync<CompileResponse>();

        Assert.Null(body.Model!.Cases);
    }

    /// <summary>A pipe's series reaches the panel as a word (<c>C-147</c>): the one it names, or the script's catalogue.</summary>
    [Fact]
    public async Task APipesSeriesIsOnTheContract()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(Compile, new
        {
            sessionId = "words",
            script = Api.Sample("m5-scenarios.fluid").Replace("P1  pipe  length = 20", "P1  pipe  length = 20  material = copper_en1057", StringComparison.Ordinal),
        });
        var model = (await response.ReadAsync<CompileResponse>()).Model!;

        var material = Parameter(model, "P1", "material");
        Assert.Equal("copper_en1057", material.Text);
        Assert.Equal("stated", material.Source);
        Assert.Null(material.Value);
    }
}
