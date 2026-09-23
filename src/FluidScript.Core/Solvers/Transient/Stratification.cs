using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Solvers.Transient;

/// <summary>Keeps a tank's layers in density order after a step (<c>33</c> §Stratified tank).</summary>
/// <remarks>
/// <para>
/// A stack of perfectly mixed layers can integrate itself into an unstable state: a warm inflow into
/// a low layer leaves lighter water under heavier water, which no real vessel holds. Natural
/// convection turns it over in seconds, far faster than any step this integrator takes, so the
/// overturn is applied as an instantaneous correction after each accepted step rather than modelled
/// as a flux.
/// </para>
/// <para>
/// <strong>Density, never temperature.</strong> Water is densest at about 4 °C, so below it colder
/// water is <em>lighter</em> and a stack that is cold at the bottom is the stable one. A rule written
/// as "hotter floats" inverts there and would stir a chilled store that was resting correctly. The
/// comparison is on the property backend's density at each block's own enthalpy.
/// </para>
/// </remarks>
public static class Stratification
{
    /// <summary>Pools adjacent violators until density is non-increasing from the bottom up.</summary>
    /// <param name="masses">Each layer's reference mass, bottom to top. kg.</param>
    /// <param name="enthalpies">Each layer's enthalpy, bottom to top, rewritten in place. J/kg.</param>
    /// <param name="substance">The fluid, for density.</param>
    /// <param name="pressure">The vessel's gauge pressure, shared by every layer. Pa.</param>
    /// <param name="pooled">How many layers ended up inside a pool of more than one. 0 when the stack was already stable.</param>
    /// <returns><see langword="false"/> when a layer's state left the property domain, leaving <paramref name="enthalpies"/> untouched.</returns>
    /// <remarks>
    /// <para>
    /// Pool-adjacent-violators, one pass bottom to top: each layer joins the stack, and while the
    /// block below it is lighter than it, the two merge to their mass-weighted mean enthalpy and the
    /// test repeats against the next block down. A merge can only create a violation below, never
    /// above, which is what makes one pass enough and what bounds the work at one density evaluation
    /// per layer plus one per merge.
    /// </para>
    /// <para>
    /// <strong>It conserves mass and energy exactly</strong> (invariant 12). Mass is untouched, since
    /// no layer changes its reference mass, and <c>Σ mₖhₖ</c> is preserved because a mass-weighted
    /// mean is what preserves it. The operation is therefore invisible to the drift accumulator, which
    /// is the check that it is doing what it claims.
    /// </para>
    /// <para>
    /// <strong>Only the minimal violating block moves.</strong> A single inversion in the middle of a
    /// stack pools two layers and leaves the rest where they were, because the scan stops as soon as
    /// the block below is denser.
    /// </para>
    /// </remarks>
    public static bool Remix(
        ReadOnlySpan<double> masses,
        Span<double> enthalpies,
        ISubstance substance,
        double pressure,
        out int pooled)
    {
        ArgumentNullException.ThrowIfNull(substance);

        pooled = 0;

        if (enthalpies.Length != masses.Length)
        {
            throw new ArgumentException($"Expected {masses.Length} enthalpies, got {enthalpies.Length}.", nameof(enthalpies));
        }

        if (enthalpies.Length < 2)
        {
            return true;
        }

        // One block per stack entry: the layers it spans, its total mass, its mean enthalpy and the
        // density of that mean. Stack-allocated, because a tank has a hundred layers at the most.
        Span<int> bottom = stackalloc int[enthalpies.Length];
        Span<double> mass = stackalloc double[enthalpies.Length];
        Span<double> enthalpy = stackalloc double[enthalpies.Length];
        Span<double> density = stackalloc double[enthalpies.Length];
        var top = 0;

        for (var layer = 0; layer < enthalpies.Length; layer++)
        {
            if (Density(substance, pressure, enthalpies[layer]) is not { } arriving)
            {
                return false;
            }

            bottom[top] = layer;
            mass[top] = masses[layer];
            enthalpy[top] = enthalpies[layer];
            density[top] = arriving;

            // Unstable while the block below is lighter than the one above it.
            while (top > 0 && density[top - 1] < density[top])
            {
                var total = mass[top - 1] + mass[top];
                var mixed = ((mass[top - 1] * enthalpy[top - 1]) + (mass[top] * enthalpy[top])) / total;

                if (Density(substance, pressure, mixed) is not { } merged)
                {
                    return false;
                }

                top--;
                mass[top] = total;
                enthalpy[top] = mixed;
                density[top] = merged;
            }

            top++;
        }

        for (var block = 0; block < top; block++)
        {
            var last = block + 1 < top ? bottom[block + 1] - 1 : enthalpies.Length - 1;

            if (last == bottom[block])
            {
                continue;
            }

            for (var layer = bottom[block]; layer <= last; layer++)
            {
                enthalpies[layer] = enthalpy[block];
            }

            pooled += last - bottom[block] + 1;
        }

        return true;
    }

    private static double? Density(ISubstance substance, double pressure, double enthalpy)
    {
        var state = substance.FromPressureEnthalpy(
            Quantity.FromSi(pressure, Dimension.Pressure),
            Quantity.FromSi(enthalpy, Dimension.Enthalpy));

        return state.IsSuccess ? state.Value.Density.SiValue : null;
    }
}
