using System.Net;
using System.Text.Json;

using FluidScript.Api.Contracts;
using FluidScript.Core.Language;

using Microsoft.Extensions.DependencyInjection;

namespace FluidScript.Api.Tests.Endpoints;

/// <summary>
/// <c>42</c>'s script endpoints, request by request: what a 200 carries, what is a 4xx, what
/// <c>solve</c> refuses that <c>compile</c> tolerates, and where the limits bite.
/// </summary>
[Trait("Category", "Api")]
public sealed class ScriptEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Compile = "/api/v1/compile";
    private const string Solve = "/api/v1/solve";
    private const string Validate = "/api/v1/validate";

    [Fact]
    public async Task ASolvableScriptCompilesToASolvedModelWithTimings()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(Compile, new { sessionId = "a", script = Api.Sample("m2-cooling-loop.fluid") });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadAsync<CompileResponse>();

        Assert.NotNull(body.Model);
        Assert.Equal("2.0", body.Model.ContractVersion);
        Assert.True(body.Model.Circuits[0].Solved);
        Assert.NotNull(body.Model.Solve);
        Assert.True(body.Model.Solve.Converged);
        Assert.Null(body.Diagnostics);
        Assert.True(body.Timings.TotalMs >= body.Timings.SolveMs, body.Timings.ToString());
        Assert.True(body.Timings.SolveMs > 0, body.Timings.ToString());
    }

    [Fact]
    public async Task TheResponseNeverRepeatsTheModelsDiagnostics()
    {
        // 42's invariant 10: the model carries the one diagnostics collection; the envelope has none beside it.
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(Compile, new { sessionId = "a", script = Api.Sample("m1-syntax-reference.fluid") });
        using var json = await response.ReadJsonAsync();

        Assert.True(json.RootElement.TryGetProperty("model", out var model));
        Assert.True(model.TryGetProperty("diagnostics", out _));
        Assert.False(json.RootElement.TryGetProperty("diagnostics", out _), "the envelope repeats the diagnostics");
    }

    [Fact]
    public async Task AScriptWithErrorsIsStillA200WithATopologyOnlyModel()
    {
        // The syntax reference: a floating pump, two dead ends. The request succeeded -- it was asked to
        // compile a script and did -- so the canvas can still draw the circuit with the pump adrift.
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(Compile, new { sessionId = "a", script = Api.Sample("m1-syntax-reference.fluid") });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadAsync<CompileResponse>();

        Assert.NotNull(body.Model);
        Assert.False(body.Model.Circuits[0].Solved);
        Assert.Contains(body.Model.Diagnostics, static d => d.Code == "FS1507" && d.Severity == "warning");
        Assert.NotEmpty(body.Model.Components);
    }

    [Fact]
    public async Task SolveEscalatesTheUnconnectedComponentToAnErrorAndCompileDoesNot()
    {
        using var client = factory.CreateClient();
        var script = Api.Sample("m1-syntax-reference.fluid");

        using var compiled = await client.PostAsync(Compile, new { sessionId = "a", script });
        using var solved = await client.PostAsync(Solve, new { sessionId = "a", script });
        var lenient = await compiled.ReadAsync<CompileResponse>();
        var strict = await solved.ReadAsync<CompileResponse>();

        Assert.Equal("warning", lenient.Model!.Diagnostics.Single(static d => d.Code == "FS1507").Severity);
        Assert.Equal("error", strict.Model!.Diagnostics.Single(static d => d.Code == "FS1507").Severity);

        // 42's invariant 3: the two differ in strictness, never in shape.
        Assert.Equal(lenient.Model.Components.Select(static c => c.Id), strict.Model.Components.Select(static c => c.Id));
    }

    [Fact]
    public async Task SolveWithAnErrorNeverReachesTheSolver()
    {
        var solvers = new CountingSolverFactory();
        using var host = new ApiFactory { Overrides = s => s.AddSingleton<Pipeline.ISolverFactory>(solvers) };
        using var client = host.CreateClient();

        using var response = await client.PostAsync(Solve, new { sessionId = "a", script = Api.Sample("m1-syntax-reference.fluid") });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(solvers.Created, static solver => Assert.Empty(solver.Calls));
    }

    [Fact]
    public async Task SolveFalseStopsAfterLowering()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(Compile, new { sessionId = "a", script = Api.Sample("m2-cooling-loop.fluid"), solve = false });
        var body = await response.ReadAsync<CompileResponse>();

        Assert.False(body.Model!.Circuits[0].Solved);
        Assert.Null(body.Model.Solve);
        Assert.Equal(0, body.Timings.SolveMs);
        Assert.NotEmpty(body.Model.Layout.Placements);
    }

    [Fact]
    public async Task ValidateReturnsDiagnosticsAndNothingOfTheModel()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(Validate, new { script = Api.Sample("m1-syntax-reference.fluid") });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadAsync<ValidateResponse>();

        Assert.Equal("2.0", body.ContractVersion);
        Assert.Equal(1, body.LanguageMajor);
        Assert.Contains(body.Diagnostics, static d => d.Code == "FS1507");
        Assert.Equal(0, body.Timings.SizeMs);
        Assert.Equal(0, body.Timings.SolveMs);

        using var json = await response.ReadJsonAsync();
        Assert.False(json.RootElement.TryGetProperty("model", out _));
    }

    [Fact]
    public async Task AScriptThisBuildCannotReadIsA200WithDiagnosticsAndNoModel()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(Compile, new { sessionId = "a", script = "fluidscript 99\nHE1 pump\n" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadAsync<CompileResponse>();

        Assert.Null(body.Model);
        Assert.NotNull(body.Diagnostics);
        Assert.NotEmpty(body.Diagnostics.Value);
    }

    [Theory]
    [InlineData("{\"sessionId\":\"a\"}", "script")]
    [InlineData("{\"script\":\"fluidscript 1\\n\"}", "sessionId")]
    public async Task AMissingRequiredFieldIs400ProblemDetailsNamingTheField(string body, string field)
    {
        using var client = factory.CreateClient();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(Compile, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await response.ReadJsonAsync();
        Assert.Equal(field, json.RootElement.GetProperty("field").GetString());
    }

    [Fact]
    public async Task MalformedJsonIs400ProblemDetails()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(Compile, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await response.ReadJsonAsync();
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task AnUnknownPathIs404()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/nothing", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AScriptOverTheSizeLimitIs413()
    {
        using var client = factory.CreateClient();
        var script = new string('x', (1024 * 1024) + 1);
        using var response = await client.PostAsync(Compile, new { sessionId = "a", script });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        using var json = await response.ReadJsonAsync();
        Assert.Equal(1024 * 1024, json.RootElement.GetProperty("limit").GetInt64());
    }

    [Fact]
    public async Task AScriptOverTheDeclarationLimitIsReportedBeforeTheSolve()
    {
        using var host = new ApiFactory { Overrides = s => s.Configure<ApiOptions>(o => o.Limits = new InputLimits(Declarations: 2)) };
        using var client = host.CreateClient();
        using var response = await client.PostAsync(Compile, new { sessionId = "a", script = Api.Sample("m2-cooling-loop.fluid") });
        var body = await response.ReadAsync<CompileResponse>();

        var limit = Assert.Single(body.Model!.Diagnostics, static d => d.Code == "FS4601");
        Assert.Equal("error", limit.Severity);
        Assert.Contains("declarations", limit.Message, StringComparison.Ordinal);
        Assert.False(body.Model.Circuits[0].Solved);
        Assert.Equal(0, body.Timings.SolveMs);
    }

    [Fact]
    public async Task AScriptOverTheUnknownLimitIsReportedBeforeTheSolve()
    {
        using var host = new ApiFactory { Overrides = s => s.Configure<ApiOptions>(o => o.Limits = new InputLimits(Unknowns: 4)) };
        using var client = host.CreateClient();
        using var response = await client.PostAsync(Compile, new { sessionId = "a", script = Api.Sample("m2-cooling-loop.fluid") });
        var body = await response.ReadAsync<CompileResponse>();

        var limit = Assert.Single(body.Model!.Diagnostics, static d => d.Code == "FS4601");
        Assert.Contains("unknowns", limit.Message, StringComparison.Ordinal);
        Assert.False(body.Model.Circuits[0].Solved);
    }

    [Fact]
    public async Task TheSameBodyProducesTheSameModel()
    {
        // 42: every endpoint is a pure function of its body plus a cache. Two compiles of one script
        // differ only in the wall time the solve took.
        using var client = factory.CreateClient();
        var script = Api.Sample("m2-substation.fluid");

        using var first = await client.PostAsync(Compile, new { sessionId = "same-1", script });
        using var second = await client.PostAsync(Compile, new { sessionId = "same-2", script });

        Assert.Equal(await ModelJson(first), await ModelJson(second));
    }

    internal static async Task<string> ModelJson(HttpResponseMessage response)
    {
        using var json = await response.ReadJsonAsync();
        var model = json.RootElement.GetProperty("model").GetRawText();

        return System.Text.RegularExpressions.Regex.Replace(model, "\"elapsedMs\":\\d+", "\"elapsedMs\":0");
    }
}
