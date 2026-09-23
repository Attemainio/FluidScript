using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Physics.Fluids;

/// <summary>The two dimensions a fluid property has that the language never names.</summary>
/// <remarks>
/// Neither viscosity nor thermal conductivity can be written in a script, so neither earns an entry in
/// <c>13</c>'s closed <see cref="DimensionId"/> set — adding one would be a language change made to
/// express a value the language cannot express. They are built from their exponent vectors instead,
/// which is exactly what <see cref="Dimension.FromVector"/> is for and which keeps the dimensional
/// algebra over them correct: dividing a conductivity by a viscosity gives a real vector, not an
/// error.
/// </remarks>
public static class FluidDimensions
{
    /// <summary>Gets the dimension of a dynamic viscosity, Pa·s.</summary>
    /// <value><c>M L⁻¹ T⁻¹</c>.</value>
    public static Dimension DynamicViscosity { get; } = Dimension.FromVector(new DimensionVector(1, -1, -1, 0));

    /// <summary>Gets the dimension of a thermal conductivity, W/(m·K).</summary>
    /// <value><c>M L T⁻³ Θ⁻¹</c>.</value>
    public static Dimension ThermalConductivity { get; } = Dimension.FromVector(new DimensionVector(1, 1, -3, -1));
}
