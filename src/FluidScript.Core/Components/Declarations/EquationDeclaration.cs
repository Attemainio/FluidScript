namespace FluidScript.Core.Components.Declarations;

/// <summary>One equation the solver drives to zero.</summary>
/// <param name="Index">Its position in the residual vector, assigned at assembly.</param>
/// <param name="Kind">What it asserts.</param>
/// <param name="OwnerComponentId">The component that contributes it.</param>
/// <param name="Name">A human-readable name, for diagnostics.</param>
/// <param name="ResidualSiUnit">
/// The SI unit of the residual itself, which is what makes <c>"HX1 energy balance off by 4.2 kW"</c>
/// possible instead of <c>"residual[17] = 4200"</c>.
/// </param>
public sealed record EquationDeclaration(
    int Index,
    EquationKind Kind,
    string OwnerComponentId,
    string Name,
    string ResidualSiUnit)
{
    /// <summary>Gets whether the row's dependence on a node pressure is a √Δp law that may sit at a sub-pascal drop.</summary>
    /// <value>
    /// <see langword="true"/> for a valve's Kv law, whose derivative in pressure is <c>C / (2√Δp)</c>
    /// and whose Δp is whatever the circuit leaves it -- 0.19 Pa across a bootstrap Kv 630 passing
    /// 0.24 kg/s. A Jacobian column perturbed by a few pascals measures a secant across ten times that
    /// valve's whole operating drop and the Newton direction is wrong from the seed (measured on the
    /// dead-leg script: the full step's residual rises from 1.6 to 87 and the line search never
    /// recovers). Such a row keeps <see cref="Solvers.Tolerances.NewtonFiniteDifferenceStep"/> on the
    /// pressure columns; every other row takes <see cref="Solvers.Tolerances.NewtonFiniteDifferenceStateStep"/>,
    /// which clears the property flash's noise (<c>S-74</c>). Default <see langword="false"/>.
    /// </value>
    public bool SteepInPressure { get; init; }
}
