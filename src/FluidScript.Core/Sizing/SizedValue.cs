using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Sizing;

/// <summary>One value sizing chose, and why it chose it.</summary>
/// <param name="Value">The value, carrying its own dimension so nothing downstream has to guess it.</param>
/// <param name="Basis">
/// What a user reads when they ask why. Never empty — <c>24</c>'s invariant 2, and the whole mitigation
/// for the risk that document opens with: a sized value always <em>looks</em> reasonable, so the number
/// alone is one a user has to trust and the basis is one they can argue with.
/// </param>
/// <param name="FromDefault">
/// <see langword="true"/> when the value came from the default catalogue rather than a computation.
/// Rendered differently, because a default is a placeholder and a computed size is a decision.
/// </param>
public readonly record struct SizedValue(Quantity Value, string Basis, bool FromDefault);
