using FluidScript.Core;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// M0 carries one endpoint so that "the host starts and answers" is a checkable claim rather than an
// assumption. The compile, validate, solve and realtime contracts arrive with P5.2; see
// plan/40-api/41-api-architecture.md.
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    core = CoreAssembly.Reference.GetName().Version?.ToString(),
}));

app.Run();

/// <summary>Marks the API host assembly so that tests can reference it.</summary>
/// <remarks>
/// A top-level program compiles to an internal <c>Program</c> class, which a separate test assembly
/// cannot see. Declaring it explicitly is the documented way to make the host addressable from
/// <c>FluidScript.Api.Tests</c> without widening anything else.
/// </remarks>
public partial class Program;
