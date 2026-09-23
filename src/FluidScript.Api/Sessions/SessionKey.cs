namespace FluidScript.Api.Sessions;

/// <summary>What a session is keyed by: the REST major and the client's id, so warm starts never cross majors (<c>42</c>).</summary>
/// <param name="ApiMajor">The REST major the request came in on.</param>
/// <param name="SessionId">The client's opaque id.</param>
public readonly record struct SessionKey(int ApiMajor, string SessionId);
