using FluidScript.Core.Language.Binding;

namespace FluidScript.Core.Language.Registry;

/// <summary>What a circuit name resolved to.</summary>
/// <param name="Role">The role, or the neutral one.</param>
/// <param name="WasResolved">Whether the name matched a registered role at all.</param>
/// <param name="BySimilarity">Whether it matched by similarity rather than exactly.</param>
public readonly record struct RoleResolution(CircuitRole Role, bool WasResolved, bool BySimilarity);
