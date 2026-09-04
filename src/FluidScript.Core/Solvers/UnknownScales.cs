using System.Collections.Immutable;

using FluidScript.Core.Components;

namespace FluidScript.Core.Solvers;

/// <summary>
/// The reference magnitude every unknown is divided by before the solver sees it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Scaling changes nothing about the algebra, and that is worth stating plainly</strong>
/// because it says where the bugs will be. Newton's step is invariant under it: <c>J̃ = DF⁻¹·J·Dx</c>
/// and <c>F̃ = DF⁻¹·F</c>, so <c>J̃·Δx̃ = −F̃</c> reduces to <c>J·(Dx·Δx̃) = −F</c> and the direction is
/// identical. What scaling buys is four things, and only four: a convergence test that means the same
/// for a pascal and a kg/s, pivot choices that follow physics rather than units, a finite-difference
/// step for an unknown that is legitimately zero, and a line-search merit that is not just the
/// pressure residual.
/// </para>
/// <para>
/// <strong>Mass flow is scaled per branch, not per circuit.</strong> A primary at 10 kg/s beside a
/// bypass at 0.05 shares one scale otherwise, and the convergence test stops being a statement about
/// relative error on either. The scaled Jacobian is the sharper reason: with one scale the bypass's
/// columns come out 200× smaller than the primary's, which is conditioning, and <c>newton.step_tol</c>
/// stops meaning anything on the small branch. <c>36</c> argues it from the residual test instead, and
/// that argument does not survive its own tolerance (<c>S-12</c>).
/// </para>
/// <para>
/// <strong>Computed once per solve and held fixed.</strong> Rescaling mid-solve changes what converged
/// means between iterations and makes the residual norm appear to jump.
/// </para>
/// </remarks>
public static class UnknownScales
{
    /// <summary>Builds the scale of every unknown from the seed the solve starts at.</summary>
    /// <param name="layout">The state vector's layout.</param>
    /// <param name="seed">The starting iterate, in SI, whose magnitudes the flow scales come from.</param>
    /// <returns>One positive scale per unknown, in the layout's order.</returns>
    /// <remarks>
    /// <para>
    /// Pressures and enthalpies take a fixed reference because their working range is a property of
    /// the fluid and not of the circuit; flows take the seed's own magnitude, floored, because a
    /// circuit's flows span orders of magnitude and nothing outside the model knows where.
    /// </para>
    /// <para>
    /// <strong>Every scale is positive whatever the seed contains.</strong> A seed carrying a negative
    /// flow is ordinary — reverse flow is legal — and a seed carrying a zero one is the initial guess
    /// itself, so the magnitude is taken and then floored.
    /// </para>
    /// </remarks>
    public static ImmutableArray<double> Build(SystemLayout layout, StateVector seed)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(seed);

        var scales = ImmutableArray.CreateBuilder<double>(layout.Count);

        for (var index = 0; index < layout.Count; index++)
        {
            var estimate = index < seed.Values.Length ? seed.Values[index] : 0;

            scales.Add(layout.Unknowns[index].Kind switch
            {
                UnknownKind.NodePressure => Tolerances.PressureScale,
                UnknownKind.NodeEnthalpy => Tolerances.EnthalpyScale,
                UnknownKind.BranchFlow or UnknownKind.ExternalMassFlux =>
                    Math.Max(Tolerances.FlowScaleFloor, Math.Abs(estimate)),

                // A promoted parameter is a pump head, a valve position or a duty, and 36 gives no
                // rule for it: the kinds have nothing in common but being solved for. Its own seed
                // magnitude is the only thing available that means anything, floored at 1 so a
                // parameter the seed puts at zero -- a shut valve -- does not divide by it.
                _ => Math.Max(1, Math.Abs(estimate)),
            });
        }

        return scales.MoveToImmutable();
    }
}
