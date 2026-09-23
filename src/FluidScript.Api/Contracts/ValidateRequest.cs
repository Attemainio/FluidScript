namespace FluidScript.Api.Contracts;

/// <summary>The body of <c>POST /api/v1/validate</c> (<c>42</c>): parse and bind only.</summary>
/// <param name="Script">The whole script text. Required; over the size limit is 413.</param>
public sealed record ValidateRequest(string? Script);
