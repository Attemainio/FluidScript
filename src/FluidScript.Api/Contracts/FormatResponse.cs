using System.Collections.Immutable;

namespace FluidScript.Api.Contracts;

/// <summary>The body of a 200 from <c>format</c> (<c>42</c>, <c>17</c>): the edits that bring the script to the canonical layout.</summary>
/// <param name="Edits">One edit per line that changes, in document order, spans never overlapping; empty for a script already formatted.</param>
public sealed record FormatResponse(ImmutableArray<TextEditWire> Edits);
