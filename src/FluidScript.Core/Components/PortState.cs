using FluidScript.Core.Physics.Fluids;

namespace FluidScript.Core.Components;

/// <summary>One port's fluid state at the current iterate, in SI.</summary>
/// <remarks>
/// <para>
/// <strong>Every property is already evaluated.</strong> A component reads them; it never fixes a
/// state itself. That is not a convenience — it is what makes <c>22</c>'s zero-allocation rule
/// achievable at all, because <see cref="FluidState"/> is a reference type and every call that
/// produced one inside a residual evaluation would allocate, N+1 times per Newton iteration
/// (<c>C-16</c>).
/// </para>
/// <para>
/// Pressure is gauge and temperature absolute, like everywhere else in the model (<c>D-26</c>). The
/// state pairing a component consumes and produces is <c>(p, h)</c>; temperature and density are
/// derived and are carried here so that no component has to invert <c>cp</c> to get one
/// (<c>22</c> convention 3).
/// </para>
/// </remarks>
public readonly record struct PortState
{
    /// <summary>Gets the gauge pressure at the port.</summary>
    /// <value>Pa, gauge.</value>
    public required double Pressure { get; init; }

    /// <summary>Gets the specific enthalpy at the port.</summary>
    /// <value>J/kg.</value>
    public required double Enthalpy { get; init; }

    /// <summary>Gets the temperature at the port.</summary>
    /// <value>K.</value>
    public required double Temperature { get; init; }

    /// <summary>Gets the density at the port.</summary>
    /// <value>kg/m³.</value>
    public required double Density { get; init; }

    /// <summary>Gets the specific heat at the port.</summary>
    /// <value>J/(kg·K).</value>
    public required double SpecificHeat { get; init; }

    /// <summary>Gets the dynamic viscosity at the port.</summary>
    /// <value>Pa·s.</value>
    /// <remarks>A pipe cannot form a Reynolds number without it, so it is carried rather than fetched.</remarks>
    public required double DynamicViscosity { get; init; }

    /// <summary>Gets the thermal conductivity at the port.</summary>
    /// <value>W/(m·K).</value>
    /// <remarks>
    /// Unused by any v1 residual: an exchanger given <c>ua</c> needs no transport property, and one
    /// sized from geometry is <c>P3.5</c>'s. It is carried because the set a port offers should be the
    /// set <see cref="FluidState"/> holds — a component reaching for the seventh property and finding
    /// six would have no way to get it, this type being the only thing it may read.
    /// </remarks>
    public required double ThermalConductivity { get; init; }
}
