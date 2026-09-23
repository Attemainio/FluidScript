namespace FluidScript.Core.Solvers.Results;

/// <summary>A pump at the solution: what it passes, the rise it makes, and the head that rise is worth.</summary>
/// <param name="Flow">kg/s through the pump, positive from its inlet port to its outlet port.</param>
/// <param name="Rise">Pa, the outlet port's pressure minus the inlet port's.</param>
/// <param name="Head">
/// m of the pumped fluid: <see cref="Rise"/> over the mean of the inlet and outlet densities and g, which
/// is the convention <see cref="Components.PumpComponent.EvaluateResiduals"/> solves with, so this is the head
/// the equations were satisfied at whether a curve, a promotion or a stated rise set it.
/// </param>
/// <param name="Basis">What set the head.</param>
public sealed record SolvedPump(double Flow, double Rise, double Head, PumpHeadBasis Basis);
