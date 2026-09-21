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
    /// <returns>φ, dimensionless, and never negative for any real position.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Equal-percentage does not reach zero, and that is the definition rather than an
    /// oversight.</strong> φ(0) = R⁻¹ = 0.02, so a fully closed equal-percentage valve still passes
    /// 2 % of its rated Kv. A real valve's shut-off comes from its seat, which is a separate leakage
    /// class and not part of the characteristic; a model that quietly forced φ(0) = 0 would be
    /// inventing a seat. It matters for a bypass that is supposed to be shut (`C-18`).
    /// </para>
    /// <para>
    /// <strong>Nothing is clamped, and `S-26` is why.</strong> This used to open with
    /// <c>Math.Clamp(position, 0, 1)</c>, which looks like defensive hygiene and is a numerical trap:
    /// <see cref="Solvers.NewtonSolver"/> builds its Jacobian by <em>forward</em> differences, so at
    /// <c>position = 1</c> the perturbation clamped straight back to 1, every entry in that column came
    /// out <c>0 − 0</c>, and the system was singular naming the valve. The same happened anywhere below
    /// 0. <strong>A bound is not somewhere the iterate may not go; it is somewhere the derivative stops
    /// existing</strong> — and one step onto it was unrecoverable, because the column that would have
    /// stepped back off is exactly the one that died. Measured: `m2-distribution-header` was singular at
    /// iteration <em>zero</em> with both promoted positions sitting at 1.
    /// </para>
    /// <para>
    /// <strong>Equal-percentage needs no repair at all — the formula is already the regularisation.</strong>
    /// <c>R^(x−1)</c> is smooth, strictly positive and monotone for every real <c>x</c>, so removing the
    /// clamp is the whole fix for the default characteristic and for every valve in the corpus. Values
    /// inside <c>[0, 1]</c> are untouched to the last bit.
    /// </para>
    /// <para>
    /// <strong>The other two keep a dead column below zero, and the obstruction is real rather than a
    /// lack of imagination.</strong> Linear and quick-open have φ(0) = 0 with φ′(0) &gt; 0, and no C¹
    /// extension of such a function stays non-negative below zero: a line through the origin with
    /// positive slope goes negative, and a negative φ is a negative Kv, which makes
    /// <see cref="MassFlow"/> drive flow <em>against</em> its pressure difference. A valve that pumps is
    /// far worse than a valve whose column is dead somewhere it should never be, so they are floored at
    /// zero and the limitation is recorded rather than papered over with an invented leak — a floor at a
    /// leakage class was tried and rejected, because it changes φ(0) on a characteristic whose φ(0) = 0
    /// is deliberate and asserted. Nothing in the corpus uses either: lowering maps an unstated
    /// characteristic to equal-percentage. The fix if one ever does is to solve a transformed variable,
    /// not to bend the physics.
    /// </para>
    /// <para>
    /// Above 1 every characteristic continues linearly from its own value and slope at 1, which keeps φ
    /// positive, monotone and C¹ across the join. A position out there is not physical and is not meant
    /// to be; it is somewhere the iterate may pass through on its way back, and `FS2303` is what reports
    /// it if a converged answer stays there.
    /// </para>
    /// </remarks>
    public static double Opening(double position, ValveCharacteristic characteristic) =>
        characteristic switch
        {
            // Smooth, positive and monotone on the whole real line. The formula is its own continuation,
            // above 1 as well as below 0, so removing the clamp is the entire fix here.
            ValveCharacteristic.EqualPercentage => Math.Pow(Rangeability, position - 1),

            // sqrt is undefined below zero, and its slope at 1 is 1/2.
            ValveCharacteristic.QuickOpen => position > 1
                ? 1 + (0.5 * (position - 1))
                : Math.Sqrt(Math.Max(position, 0)),

            // Linear is its own continuation above 1, so flooring at zero is all it needs -- and all it
            // can have, since anything below would be negative.
            _ => Math.Max(position, 0),
        };

    /// <summary>What fraction of a three-way valve's Kv one of its switched legs passes at an opening.</summary>
    /// <param name="position">The leg's own opening, 0 shut to 1 fully open; the bypass leg reads <c>1 − position</c>.</param>
    /// <param name="characteristic">Which characteristic the valve follows.</param>
    /// <returns>φ, dimensionless, never below zero, and never below <see cref="LegLeakage"/> inside the travel.</returns>
    /// <remarks>
    /// <para>
    /// <strong>A three-way valve's linear leg keeps a leak at its stop, and that is what lets the
    /// solver step back off the stop</strong> (<c>D-122</c>, after <c>S-26</c>). The two-way linear
    /// law has φ(0) = 0 by definition and a dead column below it, which <c>S-26</c> recorded and left:
    /// nothing in the corpus was linear. A mixing valve's legs are linear by default now, and each leg
    /// reaches its stop whenever the other opens fully -- the first Newton step on the parallel header
    /// overshot both positions to 1, the bypass legs' φ was 0 with no slope, the position column could
    /// no longer be told from the pump head's (<c>FS3009</c>), and the run was singular at iteration one.
    /// </para>
    /// <para>
    /// So the leg carries <see cref="LegLeakage"/> at its stop and the line continues through the
    /// stop until φ reaches zero, one difference step and more beyond it. The figure is the
    /// equal-percentage law's own φ(0) = 1/R, so a closed leg passes the same 2 % whichever
    /// characteristic the script chose. Manufacturers quote less for the seat (ESBE VRG130: under
    /// 0.05 % mixing; Belimo's B port: under 2 %); the 2 % is this project's regularisation with a
    /// physical reading, not a catalogue figure. Equal-percentage and quick-open legs are
    /// <see cref="Opening"/> unchanged.
    /// </para>
    /// </remarks>
    public static double LegOpening(double position, ValveCharacteristic characteristic) =>
        characteristic is ValveCharacteristic.Linear
            ? Math.Max(0, LegLeakage + ((1 - LegLeakage) * position))
            : Opening(position, characteristic);

    /// <summary>The fraction of its Kv a three-way valve's linear leg passes at its stop.</summary>
    /// <value>Dimensionless. 1/<see cref="Rangeability"/> = 0.02, the equal-percentage law's own φ(0).</value>
    public const double LegLeakage = 1 / Rangeability;

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

    /// <summary>The pressure drop an effective Kv takes to pass a flow.</summary>
    /// <param name="effectiveKv">Kv · φ(position), in m³/h at 1 bar.</param>
    /// <param name="massFlow">kg/s. Its magnitude is used; a drop has no direction here.</param>
    /// <param name="density">kg/m³.</param>
    /// <returns>Pa, non-negative. NaN when the Kv or the density is not a usable positive number.</returns>
    /// <remarks>
    /// The √ branch of <see cref="MassFlow"/> inverted for Δp, for a report that asks what a valve
    /// drops fully open at the flow it carries. It ignores the regularised band below
    /// <see cref="RegularizationDrop"/> for the reason <see cref="RequiredKv"/> gives: no valve is
    /// sized to sit there, and a report that lands there is reporting a few pascals either way.
    /// </remarks>
    public static double PressureDrop(double effectiveKv, double massFlow, double density)
    {
        if (!double.IsFinite(effectiveKv) || effectiveKv <= 0
            || !double.IsFinite(density) || density <= 0 || !double.IsFinite(massFlow))
        {
            return double.NaN;
        }

        var relativeDensity = density / WaterDensity;
        var root = Math.Abs(massFlow) * 3600 * Math.Sqrt(relativeDensity * 1e5) / (density * effectiveKv);

        return root * root;
    }

    /// <summary>The effective Kv that would pass a flow at a pressure drop.</summary>
    /// <param name="massFlow">kg/s. Its magnitude is used; a Kv has no direction.</param>
    /// <param name="pressureDrop">Pa. Its magnitude is used, and it must be positive.</param>
    /// <param name="density">kg/m³.</param>
    /// <returns>
    /// Kv · φ(position), in m³/h at 1 bar — the same quantity <see cref="MassFlow"/> takes, so dividing
    /// by φ gives the rated Kv to look up. <see cref="double.NaN"/> when the drop is not a usable
    /// positive number, because there is no Kv that passes a flow across no pressure.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>The sizing rule must call this rather than write the relation out again.</strong> The
    /// conversion carries a √10⁵, and getting it wrong is a flow two and a half orders of magnitude out
    /// that looks entirely plausible at every step — the trap <see cref="MassFlow"/>'s remarks describe.
    /// A rule that inverts the law here is wrong only if the law is, and the round trip is testable.
    /// </para>
    /// <para>
    /// <strong>It inverts the √ branch only, and refuses below the regularisation drop.</strong> The
    /// blended branch is a quadratic in Δp that exists to keep a <em>closed</em> valve differentiable,
    /// and no valve is ever sized to sit there: a design drop under 100 Pa is a valve with no authority
    /// at all. Inverting a branch whose only purpose is numerical would put a sized valve inside the
    /// smoothing band, where its authority is not what the arithmetic says.
    /// </para>
    /// </remarks>
    public static double RequiredKv(double massFlow, double pressureDrop, double density)
    {
        var magnitude = Math.Abs(pressureDrop);

        if (!double.IsFinite(magnitude) || magnitude < RegularizationDrop
            || !double.IsFinite(density) || density <= 0 || !double.IsFinite(massFlow))
        {
            return double.NaN;
        }

        // MassFlow's coefficient solved for Kv: mdot = (rho * Kv / (3600 * sqrt(rel * 1e5))) * sqrt(dp).
        var relativeDensity = density / WaterDensity;

        return Math.Abs(massFlow) * 3600 * Math.Sqrt(relativeDensity * 1e5)
            / (density * Math.Sqrt(magnitude));
    }
}
