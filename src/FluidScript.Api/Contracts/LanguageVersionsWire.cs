using System.Collections.Immutable;

namespace FluidScript.Api.Contracts;

/// <summary>Which language majors are read.</summary>
/// <param name="Current">The major a new script is written in.</param>
/// <param name="Supported">Every major this build parses, including the current one.</param>
public sealed record LanguageVersionsWire(int Current, ImmutableArray<int> Supported);
