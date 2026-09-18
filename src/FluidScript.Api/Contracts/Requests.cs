namespace FluidScript.Api.Contracts;

/// <summary>The body of <c>POST /api/v1/compile</c> and <c>POST /api/v1/solve</c> (<c>42</c>).</summary>
/// <param name="SessionId">
/// The client's session id, an opaque string it chose. It keys the warm-start cache and the in-flight
/// draft that a newer request supersedes; an unknown id creates its session (<c>41</c>). Required.
/// </param>
/// <param name="Script">The whole script text. Required; over the size limit is 413.</param>
/// <param name="Solve">
/// Whether to size and solve after lowering. <see langword="null"/> or <see langword="true"/> solves;
/// <see langword="false"/> stops after lowering, for a first render of a script still being typed.
/// </param>
public sealed record CompileRequest(string? SessionId, string? Script, bool? Solve);

/// <summary>The body of <c>POST /api/v1/validate</c> (<c>42</c>): parse and bind only.</summary>
/// <param name="Script">The whole script text. Required; over the size limit is 413.</param>
public sealed record ValidateRequest(string? Script);
