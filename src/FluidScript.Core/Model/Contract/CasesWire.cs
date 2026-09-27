using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>The cases a file declares, and which one this contract draws (<c>D-143</c>, <c>D-182</c>).</summary>
/// <param name="Names">Every declared case, in declaration order.</param>
/// <param name="Drawn">
/// The case whose state the model carries: the one the request chose, or the operating case, the first, when it chose
/// none or one the file does not declare.
/// </param>
/// <remarks>
/// The sizes are the merged plant's whichever case is drawn: one plant covers every case, so choosing a case changes the
/// state on the canvas and the values that case states, never a size.
/// </remarks>
public sealed record CasesWire(ImmutableArray<string> Names, string Drawn);
