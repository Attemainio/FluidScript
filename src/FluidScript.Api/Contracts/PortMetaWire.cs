using System.Collections.Immutable;

namespace FluidScript.Api.Contracts;

/// <summary>One fixed port.</summary>
/// <param name="Name">The port's id, as the model, the layout and the wire know it: <c>in</c>, <c>in[2]</c>, <c>ab</c>.</param>
/// <param name="Spelling">The port as a script writes it after a dot, and as a message names it: <c>secondary.in</c> for an exchanger's <c>in[2]</c> (<c>D-179</c>).</param>
/// <param name="Aliases">The other spellings a script may write it in: <c>primary.in</c> for an exchanger's <c>in</c>.</param>
/// <param name="Role"><c>inlet</c>, <c>outlet</c> or <c>bidirectional</c>.</param>
/// <param name="Optional">Whether inference rule I3 leaves it unconnected without a boundary node.</param>
public sealed record PortMetaWire(string Name, string Spelling, ImmutableArray<string> Aliases, string Role, bool Optional);
