namespace FluidScript.Api.Contracts;

/// <summary>The input ceilings (<c>07</c>).</summary>
/// <param name="SourceBytes">The largest script accepted, bytes of UTF-8; over it is 413.</param>
/// <param name="Declarations">The most component declarations; over it is <c>FS4601</c>.</param>
/// <param name="Tokens">The most tokens; over it is <c>FS4601</c>.</param>
/// <param name="Unknowns">The most solver unknowns; over it is <c>FS4601</c>.</param>
public sealed record LimitsWire(long SourceBytes, int Declarations, int Tokens, int Unknowns);
