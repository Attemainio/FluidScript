using FluidScript.Api;
using FluidScript.Api.Contracts;
using FluidScript.Api.Endpoints;
using FluidScript.Api.Pipeline;
using FluidScript.Api.Sessions;

var builder = WebApplication.CreateBuilder(args);

// The composition root and nothing else (41's invariant 5): what is registered, how it is served.
builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.Section));
builder.Services.ConfigureHttpJsonOptions(static options => ApiJson.Configure(options.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<InternalFaultHandler>();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<ISessionStore, SessionStore>();
builder.Services.AddSingleton<ISolverFactory, NewtonSolverFactory>();
builder.Services.AddSingleton<ScriptPipeline>();
builder.Services.AddSingleton<MetadataDocument>();
builder.Services.AddHostedService<SessionEviction>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthEndpoints();
app.MapGroup("/api/v1").MapScriptEndpoints().MapMetadataEndpoints();
app.MapOpenApi();

// Production serves the built frontend from wwwroot with an SPA fallback; development never does,
// so a stale build is never mistaken for the dev server (41's invariant 7).
if (!app.Environment.IsDevelopment())
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

app.Run();

/// <summary>Marks the API host assembly so that tests can reference it.</summary>
/// <remarks>
/// A top-level program compiles to an internal <c>Program</c> class, which a separate test assembly
/// cannot see. Declaring it explicitly is the documented way to make the host addressable from
/// <c>FluidScript.Api.Tests</c> without widening anything else.
/// </remarks>
public partial class Program;
