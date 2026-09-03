using FluidScript.Core.Solvers;

namespace FluidScript.Core.Components;

/// <summary>How a valve's effective flow coefficient follows its opening.</summary>
public enum ValveCharacteristic
{
    /// <summary>φ = x. Effective Kv is proportional to the opening.</summary>
    Linear,

    /// <summary>φ = R^(x−1) with rangeability R = 50.</summary>
    EqualPercentage,

    /// <summary>φ = √x. Most of the capacity arrives in the first part of the travel.</summary>
    QuickOpen,
}

/// <summary>The Kv relation, in SI, regularised so a closed valve is differentiable.</summary>
/// <remarks>
/// <para>
/// Shared by <see cref="Valve"/> and <see cref="ThreeWayValve"/>, which are two kinds over one
/// equation.
/// </para>
/// <para>
/// <strong>Kv is not an SI quantity and the relation is only true in its own units.</strong> It is
/// defined as m³/h of water at 1 bar differential:
/// <code>
/// Q [m³/h] = Kv_eff · √( Δp [bar] / (ρ/ρ_water) )
/// </code>
/// Everything in Core is SI, so what a residual must evaluate is the converted form
/// <code>
/// ṁ [kg/s] = (ρ / 3600) · Kv_eff · √( Δp [Pa] / (10⁵ · ρ/ρ_water) )
/// </code>
/// Substituting pascals into the first form is wrong by √10⁵ ≈ 316 — a flow two and a half orders of
/// magnitude out, and entirely plausible-looking. Both forms are written here for that reason.
/// </para>
/// </remarks>
public static class ValveLaw
{
    /// <summary>The rangeability of the equal-percentage characteristic.</summary>
    public const double Rangeability = 50;

    /// <summary>The density Kv is referenced to.</summary>
    /// <value>1000 kg/m³, water.</value>
    public const double WaterDensity = 1000;

    /// <summary>The pressure drop below which the √ law is blended.</summary>
    /// <value>
    /// 100 Pa, and it is <see cref="Tolerances.ValveRegularizationDrop"/> rather than a literal: this
    /// number is <c>36</c>'s <c>valve.dp_regularization</c>, transcribed here by hand until <c>S-6</c>
    /// pointed out that a change to that table reached neither of the two components holding a copy.
    /// It stays a <see langword="const"/> because <see cref="MassFlow"/> needs it at compile time.
    /// </value>
    public const double RegularizationDrop = Tolerances.ValveRegularizationDrop;

    /// <summary>The fraction of rated Kv an opening delivers.</summary>
    /// <param name="position">The opening, 0 to 1. 1 is fully open.</param>
    /// <param name="characteristic">Which characteristic the valve follows.</param>
    /// <returns>φ, dimensionless.</returns>
    /// <remarks>
    /// <strong>Equal-percentage does not reach zero, and that is the definition rather than an
    /// oversight.</strong> φ(0) = R⁻¹ = 0.02, so a fully closed equal-percentage valve still passes
    /// 2 % of its rated Kv. A real valve's shut-off comes from its seat, which is a separate leakage
    /// class and not part of the characteristic; a model that quietly forced φ(0) = 0 would be
    /// inventing a seat. It matters for a bypass that is supposed to be shut (`C-18`).
    /// </remarks>
    public static double Opening(double position, ValveCharacteristic characteristic)
    {
        var x = Math.Clamp(position, 0, 1);

        return characteristic switch
        {
            ValveCharacteristic.Linear => x,
            ValveCharacteristic.EqualPercentage => Math.Pow(Rangeability, x - 1),
            ValveCharacteristic.QuickOpen => Math.Sqrt(x),
            _ => x,
        };
    }

    /// <summary>The mass flow the Kv relation gives at a pressure drop.</summary>
    /// <param name="effectiveKv">Kv · φ(position), in m³/h at 1 bar.</param>
    /// <param name="pressureDrop">Pa, signed along the nominal direction.</param>
    /// <param name="density">kg/m³.</param>
    /// <returns>kg/s, with the sign of <paramref name="pressureDrop"/>.</returns>
    /// <remarks>
    /// <para>
    /// <strong>It is odd, and forgetting that gives a valve that passes flow one way only</strong> —
    /// which presents as a mysterious non-convergence in any circuit with a bypass, rather than as a
    /// wrong number.
    /// </para>
    /// <para>
    /// <strong>Below <see cref="RegularizationDrop"/> the √ law is replaced by a quadratic, and it has
    /// to be curved.</strong> √Δp has infinite slope at zero, which is exactly where a closed valve
    /// sits. The obvious replacement — a straight line through the origin, <c>K·Δp/√a</c> — matches the
    /// value at the join and gets the slope wrong by exactly a factor of two, because the √ law's slope
    /// there is <c>K/(2√a)</c> and the line's is <c>K/√a</c>. Value and slope cannot both be matched by
    /// a line through the origin. The quadratic below is the lowest-order curve satisfying
    /// <c>Q(0) = 0</c>, <c>Q(a) = K√a</c> and <c>Q′(a) = K/(2√a)</c>, and it is monotone across the
    /// blend.
    /// </para>
    /// </remarks>
    public static double MassFlow(double effectiveKv, double pressureDrop, double density)
    {
        // The SI coefficient K in mdot = K sqrt(dp), folding in the m3/h-per-bar definition of Kv.
        var relativeDensity = density / WaterDensity;
        var coefficient = density * effectiveKv / (3600 * Math.Sqrt(relativeDensity * 1e5));

        var magnitude = Math.Abs(pressureDrop);
        var sign = Math.Sign(pressureDrop);

        if (magnitude >= RegularizationDrop)
        {
            return sign * coefficient * Math.Sqrt(magnitude);
        }

        const double a = RegularizationDrop;
        var blended = (3 * coefficient / (2 * Math.Sqrt(a)) * magnitude)
            - (coefficient / (2 * a * Math.Sqrt(a)) * magnitude * magnitude);

        return sign * blended;
    }
}
