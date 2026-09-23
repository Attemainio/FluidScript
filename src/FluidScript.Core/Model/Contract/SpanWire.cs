namespace FluidScript.Core.Model.Contract;

/// <summary>A character span in the source.</summary>
/// <param name="Start">The zero-based offset.</param>
/// <param name="Length">The length in UTF-16 code units.</param>
public sealed record SpanWire(int Start, int Length);
