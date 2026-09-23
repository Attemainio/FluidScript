using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>The script's presentation directives, resolved (<c>D-104</c>).</summary>
/// <param name="Tokens">The applied <c>style</c> tokens as written.</param>
/// <param name="Spacing">The <c>spacing</c> value in world units, or <see langword="null"/> (<c>D-37</c>).</param>
/// <param name="Default">The project-level style, applied where a circuit states none.</param>
/// <param name="Named">The named styles, <c>style name = …</c>, resolved, for an editor to list.</param>
public sealed record StyleWire(ImmutableArray<string> Tokens, double? Spacing, ResolvedStyleWire Default, IReadOnlyDictionary<string, ResolvedStyleWire> Named);
