namespace FluidScript.Api.Contracts;

/// <summary>A code no longer emitted.</summary>
/// <param name="Code">The code.</param>
/// <param name="Reason">Why it was withdrawn and what replaced it.</param>
public sealed record RetiredCodeWire(string Code, string Reason);
