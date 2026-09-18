using FluidScript.Api.Contracts;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace FluidScript.Api.Endpoints;

/// <summary>The metadata endpoint of <c>42</c>: the static description of the language, with an ETag.</summary>
public static class MetadataEndpoints
{
    /// <summary>Maps the route onto a version group.</summary>
    /// <param name="group">The <c>/api/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapMetadataEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/metadata", Metadata)
            .WithName("metadata")
            .WithSummary("Every component kind, parameter, unit, symbol and diagnostic code; the versions and the limits.")
            .Produces<MetadataWire>();

        return group;
    }

    private static Results<FileContentHttpResult, StatusCodeHttpResult> Metadata(HttpContext http, MetadataDocument document)
    {
        http.Response.Headers.ETag = document.ETag;
        http.Response.Headers.CacheControl = "no-cache";

        if (http.Request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var tags)
            && tags.Any(tag => string.Equals(tag, document.ETag, StringComparison.Ordinal)))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        return TypedResults.Bytes(document.Json, "application/json; charset=utf-8");
    }
}
