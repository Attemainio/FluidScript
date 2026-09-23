namespace FluidScript.Api.Contracts;

/// <summary>The body of <c>POST /api/v1/format</c> (<c>42</c>): the script to lay out.</summary>
/// <param name="Script">The whole script text. Required; over the size limit is 413.</param>
public sealed record FormatRequest(string? Script);
