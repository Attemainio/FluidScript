using FluidScript.Core;

namespace FluidScript.Api.Endpoints;

/// <summary>The one route M0 carried, kept where it was: <c>GET /api/health</c>.</summary>
public static class HealthEndpoints
{
    /// <summary>Maps the health route.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The application, for chaining.</returns>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/health", static () => Results.Ok(new HealthWire("ok", CoreAssembly.Reference.GetName().Version?.ToString())))
            .WithName("health")
            .WithSummary("The host is up, and which Core it carries.");

        return app;
    }
}
