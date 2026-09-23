namespace FluidScript.Core.Sizing.Flows;

/// <summary>One branch's flow estimate, and what determined it.</summary>
/// <param name="Magnitude">
/// kg/s, unsigned. Orientation is the branch decomposition's choice and means nothing to a sizing
/// rule, all of which are written on <c>|ṁ|</c>; <see cref="FluidScript.Core.Solvers.Seeding.SolutionSeed"/> is what turns
/// magnitudes into a signed, mass-consistent field.
/// </param>
/// <param name="Basis">What determined it.</param>
/// <param name="Source">
/// The component the estimate came from, or the empty string for <see cref="FlowBasis.Nominal"/>.
/// </param>
public readonly record struct BranchFlow(double Magnitude, FlowBasis Basis, string Source);
