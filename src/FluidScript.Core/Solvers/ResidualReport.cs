namespace FluidScript.Core.Solvers;

/// <summary>One equation and how far from satisfied it is.</summary>
/// <param name="OwnerComponentId">The component that contributes it.</param>
/// <param name="EquationName">Its name, as the component declared it.</param>
/// <param name="Residual">How far off, in <paramref name="ResidualSiUnit"/>.</param>
/// <param name="ResidualSiUnit">The SI unit the residual is measured in.</param>
/// <param name="ScaledResidual">The same miss divided by the row's reference magnitude.</param>
/// <remarks>
/// <strong>Both numbers, because each answers a different question.</strong> The scaled one says which
/// row is worst — that is what a norm ranks by, and comparing a pascal to a kg/s any other way is
/// meaningless. The unscaled one is what a sentence can carry: "off by 4.2 kW" is actionable and
/// "off by 0.042" is not.
/// </remarks>
public sealed record ResidualReport(
    string OwnerComponentId,
    string EquationName,
    double Residual,
    string ResidualSiUnit,
    double ScaledResidual);
