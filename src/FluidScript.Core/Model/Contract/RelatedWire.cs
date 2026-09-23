namespace FluidScript.Core.Model.Contract;

/// <summary>A related location.</summary>
/// <param name="Message">Why it is related.</param>
/// <param name="Range">Where it is.</param>
public sealed record RelatedWire(string Message, RangeWire Range);
