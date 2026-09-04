using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;

namespace FluidScript.Core.Sizing;

/// <summary>What determined a branch's flow estimate.</summary>
/// <remarks>
/// Ordered by authority, weakest first: a later basis overrides an earlier one, and the enum's order
/// is what <see cref="BranchFlows"/> compares. Extending it means deciding where the new rule ranks.
/// </remarks>
public enum FlowBasis
{
    /// <summary>Nothing determined it, so the estimate is <see cref="BranchFlows.Nominal"/>.</summary>
    /// <remarks>This is the case <c>24</c>'s <c>FS2304</c> reports once a sizer asks for the number.</remarks>
    Nominal = 0,

    /// <summary>A junction element the branch reaches shares its estimate.</summary>
    Propagated = 1,

    /// <summary>An exchanger's stated duty and terminal temperatures fix it (<c>24</c>, step 1).</summary>
    Duty = 2,

    /// <summary>The script stated a flow on the branch.</summary>
    Stated = 3,
}

/// <summary>One branch's flow estimate, and what determined it.</summary>
/// <param name="Magnitude">
/// kg/s, unsigned. Orientation is the branch decomposition's choice and means nothing to a sizing
/// rule, all of which are written on <c>|ṁ|</c>; <see cref="Solvers.SolutionSeed"/> is what turns
/// magnitudes into a signed, mass-consistent field.
/// </param>
/// <param name="Basis">What determined it.</param>
/// <param name="Source">
/// The component the estimate came from, or the empty string for <see cref="FlowBasis.Nominal"/>.
/// </param>
public readonly record struct BranchFlow(double Magnitude, FlowBasis Basis, string Source);

/// <summary>Steps 1 and 2 of <c>24</c>'s pipeline: a flow estimate for every branch, before any solve.</summary>
/// <remarks>
/// <para>
/// <strong>This is an estimate and says so in its own type.</strong> Every value carries the basis
/// that produced it, so a sizing rule can refuse to size against a guess and the seed can tell a
/// number it trusts from one it invented. A bare <c>double[]</c> would make the two
/// indistinguishable, which is the failure mode <c>24</c>'s whole "every sized value carries its
/// basis" discipline exists to prevent.
/// </para>
/// <para>
/// <strong>Propagation here is weaker than <c>24</c>'s and deliberately so.</strong> That document
/// propagates stated constraints to a fixed point over the branch graph and reports <c>FS2302</c> on a
/// conflict; what runs here spreads the largest determined estimate across a junction element's other
/// branches and reports nothing. The difference is the direction of the two errors: a seed that is
/// somewhat wrong costs Newton iterations, and a <em>diagnostic</em> that is somewhat wrong is a
/// sentence a user acts on. The exact propagation and its diagnostics arrive with the sizers, which
/// are what the diagnostics are about.
/// </para>
/// <para>
/// A junction element is degree one or degree three and up, never degree two (<c>23</c>), so there is
/// no series propagation to do: a branch already carries one flow along its whole length.
/// </para>
/// </remarks>
public static class BranchFlows
{
    /// <summary>The estimate a branch takes when nothing determines it.</summary>
    /// <value>
    /// kg/s. Roughly a small domestic circuit, and chosen only to be a plausible order of magnitude —
    /// it must be far enough from zero that a momentum relation's <c>∂(R·ṁ|ṁ|)/∂ṁ</c> is not itself
    /// zero (<c>S-21</c>), and nothing else about it is claimed.
    /// </value>
    public const double Nominal = 0.1;

    /// <summary>Estimates the flow every branch carries, from what the script stated.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <returns>One estimate per branch, indexed by <see cref="Branch.Index"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    public static ImmutableArray<BranchFlow> Estimate(CircuitGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var estimates = new BranchFlow[graph.Branches.Length];
        Array.Fill(estimates, new BranchFlow(Nominal, FlowBasis.Nominal, string.Empty));

        foreach (var branch in graph.Branches)
        {
            foreach (var part in branch.Path)
            {
                Offer(ref estimates[branch.Index], Duty(graph.Substance, part), FlowBasis.Duty, part.Name);
                Offer(
                    ref estimates[branch.Index],
                    HydraulicPartition.Stated(part, HydraulicPartition.Flow),
                    FlowBasis.Stated,
                    part.Name);
            }

            // A terminal states the flux crossing the model boundary, and a terminal has one branch, so
            // that flux *is* the branch's flow. An interior junction's stated flow is not: it splits
            // among several branches and none of them carries it alone.
            foreach (var end in new[] { branch.From, branch.To })
            {
                if (end.Element.Ports.Length == 1)
                {
                    Offer(
                        ref estimates[branch.Index],
                        HydraulicPartition.Stated(end.Element, HydraulicPartition.Flow),
                        FlowBasis.Stated,
                        end.Element.Name);
                }
            }
        }

        Propagate(graph, estimates);

        return [.. estimates];
    }

    /// <summary>Takes a candidate estimate when it outranks the one already held.</summary>
    /// <param name="held">The estimate so far, replaced in place.</param>
    /// <param name="candidate">The proposed magnitude, or <see langword="null"/> when the rule did not apply.</param>
    /// <param name="basis">What produced the candidate.</param>
    /// <param name="source">The component it came from.</param>
    /// <remarks>
    /// A zero candidate is rejected rather than taken. <c>flow=0</c> on a shut-off is a legitimate
    /// statement about the answer and a useless statement about where to start looking for it, and
    /// seeding a branch at exactly zero is the singularity <c>S-21</c> is about.
    /// </remarks>
    private static void Offer(ref BranchFlow held, double? candidate, FlowBasis basis, string source)
    {
        if (candidate is not { } magnitude || Math.Abs(magnitude) <= Solvers.Tolerances.FlowZero)
        {
            return;
        }

        if (basis >= held.Basis)
        {
            held = new BranchFlow(Math.Abs(magnitude), basis, source);
        }
    }

    /// <summary>Spreads each junction element's largest determined estimate onto its undetermined branches.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="estimates">The estimates so far, written in place.</param>
    /// <remarks>
    /// Bounded by the branch count because each pass raises at least one branch's basis above
    /// <see cref="FlowBasis.Nominal"/> or changes nothing, and a basis never falls.
    /// </remarks>
    private static void Propagate(CircuitGraph graph, BranchFlow[] estimates)
    {
        for (var pass = 0; pass < graph.Branches.Length; pass++)
        {
            var moved = false;

            foreach (var junction in graph.JunctionElements)
            {
                var best = 0.0;
                var source = string.Empty;

                foreach (var branch in graph.Branches)
                {
                    if (Meets(branch, junction)
                        && estimates[branch.Index].Basis > FlowBasis.Nominal
                        && estimates[branch.Index].Magnitude > best)
                    {
                        best = estimates[branch.Index].Magnitude;
                        source = estimates[branch.Index].Source;
                    }
                }

                if (best <= 0)
                {
                    continue;
                }

                foreach (var branch in graph.Branches)
                {
                    if (!Meets(branch, junction) || estimates[branch.Index].Basis > FlowBasis.Nominal)
                    {
                        continue;
                    }

                    estimates[branch.Index] = new BranchFlow(best, FlowBasis.Propagated, source);
                    moved = true;
                }
            }

            if (!moved)
            {
                return;
            }
        }
    }

    /// <summary>Whether a branch has an end at a given junction element.</summary>
    /// <param name="branch">The branch.</param>
    /// <param name="junction">The element.</param>
    /// <returns><see langword="true"/> when either end names it.</returns>
    private static bool Meets(Branch branch, IFlowComponent junction) =>
        ReferenceEquals(branch.From.Element, junction) || ReferenceEquals(branch.To.Element, junction);

    /// <summary>The flow an exchanger's stated duty and terminal temperatures imply.</summary>
    /// <param name="substance">The circuit's fluid.</param>
    /// <param name="component">The candidate component.</param>
    /// <returns>kg/s, or <see langword="null"/> when the rule does not apply here.</returns>
    /// <remarks>
    /// <c>ṁ = |Q̇| / |h(out) − h(in)|</c>, evaluated at the substance rather than at a constant
    /// specific heat: water's <c>cp</c> moves 1 % between 20 °C and 90 °C and the whole point of this
    /// number is that a user can check it against the enthalpy table.
    /// </remarks>
    private static double? Duty(ISubstance substance, IFlowComponent component)
    {
        var stated = component.StatedParameters;

        if (!stated.TryGetValue("power", out var power)
            || !stated.TryGetValue("in", out var inlet)
            || !stated.TryGetValue("out", out var outlet))
        {
            return null;
        }

        var reference = Quantity.FromSi(0, Dimension.Pressure);

        if (!substance.FromPressureTemperature(reference, inlet).TryGetValue(out var entering)
            || !substance.FromPressureTemperature(reference, outlet).TryGetValue(out var leaving))
        {
            return null;
        }

        var rise = Math.Abs(leaving.Enthalpy.SiValue - entering.Enthalpy.SiValue);

        return rise > 0 ? Math.Abs(power.SiValue) / rise : null;
    }
}
