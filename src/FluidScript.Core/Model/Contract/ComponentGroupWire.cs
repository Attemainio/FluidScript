using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>A pipe expansion.</summary>
/// <param name="ParentComponentId">The declared pipe.</param>
/// <param name="Children">What lowering made of it.</param>
public sealed record ComponentGroupWire(string ParentComponentId, ImmutableArray<string> Children);
