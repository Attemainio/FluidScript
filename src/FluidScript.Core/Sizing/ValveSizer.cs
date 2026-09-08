using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Catalogs;
using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Units;

namespace FluidScript.Core.Sizing;

/// <summary>Chooses a control valve's <c>kv</c> from the authority it needs over its branch (<c>24</c>).</summary>
/// <param name="catalog">The Kv series to select from.</param>
/// <param name="authorityTarget">
/// The share of the branch's total drop the valve should take. <c>24</c>'s
/// <see cref="SizingDefaults.ValveAuthorityTarget"/> unless a script states <c>authority</c> on the
/// valve, which is a constraint rather than a preference.
/// </param>
/// <remarks>
/// <para>
/// <strong>Authority is the criterion, and it is a <em>control</em>-valve criterion.</strong> The rule
/// asks what drop gives the valve its target share of the branch, inverts the Kv relation at the
/// design flow to get the coefficient that produces it, and takes the catalogue row at or below.
/// A balancing valve is a different job with a different rule and this one does not claim it
/// (<c>C-49</c>); it also does not attempt a parallel set, which needs sibling branches a rule cannot
/// see.
/// </para>
/// <para>
/// <strong>Rounding is down, and that is the direction that is safe.</strong> A smaller Kv drops more,
/// which raises authority above the target — so the achieved value is reported and is always at or
/// above what was asked for. Rounding up would produce a valve quietly below its target authority,
/// which is a valve that controls over less of its travel than the report claims. The R5 step is 1.6×
/// in Kv and 2.56× in drop, so the overshoot is not small: <c>24</c>'s own example asks for 0.5 and
/// achieves 0.57.
/// </para>
/// <para>
/// <strong>The Kv relation is inverted by <see cref="ValveLaw.RequiredKv"/> rather than written out
/// here.</strong> The conversion carries a √10⁵ and getting it wrong is a flow two and a half orders
/// of magnitude out that looks plausible at every step. A rule that calls the law's own inverse is
/// wrong only if the law is.
/// </para>
/// </remarks>
public sealed class ValveSizer(
    ICatalog<ValveSpec> catalog, double authorityTarget = SizingDefaults.ValveAuthorityTarget) : ISizer
{
    /// <inheritdoc/>
    /// <remarks>
    /// Both, because the achieved authority is an output rather than an input. A script that states
    /// <c>authority</c> has set the target and keeps it; one that does not gets the value the chosen
    /// row actually delivers, which after rounding down is not the number that was asked for.
    /// </remarks>
    public ImmutableArray<string> Parameters { get; } = ["kv", "authority"];

    /// <inheritdoc/>
    /// <remarks>
    /// <strong>The largest row, because a provisional must disturb the bootstrap as little as
    /// possible.</strong> A valve has no component at all without a <c>kv</c>, so something has to go
    /// in before flows can be estimated — and the smallest row is the most restrictive, which would
    /// hand every other rule on the loop a branch drop dominated by a valve nobody has sized yet. The
    /// largest row is the closest thing the series has to an open port.
    /// </remarks>
    public ImmutableDictionary<string, Quantity> Provisional { get; } =
        ImmutableDictionary<string, Quantity>.Empty.Add(
            "kv",
            Quantity.FromSi(
                catalog is { Entries.Count: > 0 } ? catalog.Entries[^1].Spec.Kvs : 1, Dimension.Kv));

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <strong>A three-way valve whose bypass is unconnected is a two-way valve, and this rule is right
    /// for it</strong> (<c>C-61</c>). <c>22</c> says as much -- "a three-way valve used as a two-way
    /// leaves [<c>c</c>] open, and inference rule I3 terminates it" -- and the topology agrees: measured
    /// on <c>m2-distribution-header</c>, <c>TV_AHU</c> and <c>TV_RAD</c> are <em>not</em> junction
    /// elements and sit <em>inside</em> a branch's <c>Path</c>, so <c>OuterLoop.Context</c> builds them
    /// the same single-branch context a <c>valve</c> gets. Excluding them by type left both holding the
    /// bootstrap Kv 630 for the life of the run (<c>C-60</c>).
    /// </para>
    /// <para>
    /// A three-way valve with its bypass <em>connected</em> is a different matter and is still refused:
    /// it is a junction element standing on three branches at once, so no single-branch context
    /// describes it, and its rule is the one <c>24</c> states separately.
    /// </para>
    /// </remarks>
    public bool CanSize(IFlowComponent component) =>
        component is Valve or ThreeWayValve { BypassConnected: false };

    /// <inheritdoc/>
    public Result<SizingResult> Size(IFlowComponent component, in SizingContext context)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (!CanSize(component))
        {
            return Result.Failure<SizingResult>(ResultError.From(
                Diagnostics.FluidDiagnostics.PropertyNotEvaluable,
                ("property", "a Kv"),
                ("name", component.Name),
                ("state", "a two-way valve, or a three-way valve with its bypass unconnected")));
        }

        var valve = component;

        var target = Target(valve);
        var density = context.State.Density.SiValue;

        if (!double.IsFinite(density) || density <= 0 || target is <= 0 or >= 1)
        {
            return Result.Failure<SizingResult>(ResultError.From(
                Diagnostics.FluidDiagnostics.PropertyNotEvaluable,
                ("property", "a Kv"),
                ("name", valve.Name),
                ("state", "the branch's resistance and a target authority between 0 and 1")));
        }

        // `24` step 2. The valve's share `a` of the branch total means a * (rest + valve) = valve, so
        // valve = a * rest / (1 - a). At a = 0.5 that is exactly the rest of the branch.
        var rest = Math.Max(0, context.BranchDrop);
        var wanted = rest * target / (1 - target);
        var required = ValveLaw.RequiredKv(context.MassFlow, wanted, density);
        var notes = ImmutableArray.CreateBuilder<string>();

        if (!double.IsFinite(required) || required <= 0)
        {
            // Below the regularisation drop there is no honest Kv, so the rule declines rather than
            // inventing one. `Provisional` still stands, so the valve exists and the model still builds.
            notes.Add(
                $"{valve.Name} could not be sized: its branch drops {rest / 1000:0.##} kPa excluding the "
                + "valve, so a valve taking its target share would sit inside the solver's smoothing "
                + "band. Add resistance to the branch, or state a `kv`.");

            return Result.Success(new SizingResult
            {
                Values = ImmutableDictionary<string, SizedValue>.Empty,
                Notes = notes.ToImmutable(),
            });
        }

        var chosen = catalog.NearestBelow(required, static spec => spec.Kvs);
        var kvs = chosen.Entry.Spec.Kvs;

        // The drop the chosen row actually produces, from the same law the solver runs, and the
        // authority that follows from it. Not the target -- `24`'s invariant is that the reported
        // authority is the achieved one, and after rounding down it is higher.
        var achievedDrop = Drop(context.MassFlow, kvs, density);
        var achieved = achievedDrop / (rest + achievedDrop);

        Report(valve, chosen, kvs, achieved, achievedDrop, rest, required, notes);

        var litresPerSecond = Math.Abs(context.MassFlow) / density * 1000;

        // Two bases, because a report line reads "CV1.authority = ..." and a Kv designation in that
        // position is a value that does not belong to the parameter it is explaining.
        var kvBasis = string.Create(
            CultureInfo.InvariantCulture,
            $"{chosen.Entry.Designation} ({chosen.Entry.Spec.Series}) — authority {achieved:0.##} at "
            + $"{litresPerSecond:0.###} l/s, {achievedDrop / 1000:0.#} kPa");

        var authorityBasis = string.Create(
            CultureInfo.InvariantCulture,
            $"{achieved:0.##} achieved against a target of {target:0.##} — {chosen.Entry.Designation} "
            + $"drops {achievedDrop / 1000:0.#} kPa of the branch's {(rest + achievedDrop) / 1000:0.#} kPa");

        var values = ImmutableDictionary<string, SizedValue>.Empty
            .Add("kv", new SizedValue(Quantity.FromSi(kvs, Dimension.Kv), kvBasis, FromDefault: false))
            .Add(
                "authority",
                new SizedValue(
                    Quantity.FromSi(achieved, Dimension.Dimensionless), authorityBasis, FromDefault: false));
        return Result.Success(new SizingResult { Values = values, Notes = notes.ToImmutable() });
    }

    /// <summary>The drop a rated Kv produces at a flow, from the law the solver runs.</summary>
    /// <param name="massFlow">kg/s.</param>
    /// <param name="kvs">The rated coefficient, m³/h at 1 bar.</param>
    /// <param name="density">kg/m³.</param>
    /// <returns>Pa, positive.</returns>
    /// <remarks>
    /// Inverting <see cref="ValveLaw.RequiredKv"/> rather than <see cref="ValveLaw.MassFlow"/> because
    /// what is wanted is Δp given Kv, and the two are the same relation rearranged: the required Kv at
    /// a reference drop scales as 1/√Δp, so Δp = reference · (required / actual)².
    /// </remarks>
    private static double Drop(double massFlow, double kvs, double density)
    {
        const double reference = 100_000;
        var atReference = ValveLaw.RequiredKv(massFlow, reference, density);

        return double.IsFinite(atReference) ? reference * atReference / kvs * (atReference / kvs) : double.NaN;
    }

    /// <summary>Adds whatever is worth telling the user about the row that was chosen.</summary>
    private static void Report(
        IFlowComponent valve,
        CatalogSelection<ValveSpec> chosen,
        double kvs,
        double achieved,
        double achievedDrop,
        double rest,
        double required,
        ImmutableArray<string>.Builder notes)
    {
        // `FS2601`/`FS2602`'s case: the series ran out. Clamping low means the valve is bigger than the
        // branch wanted and takes less than its share; clamping high means the opposite.
        if (chosen.Fit == CatalogFit.ClampedToSmallest)
        {
            notes.Add(
                $"{valve.Name} wanted Kv {required:0.##} and the series stops at {kvs:0.##}, so it drops "
                + $"{achievedDrop / 1000:0.#} kPa against the branch's {rest / 1000:0.#} kPa. The branch "
                + "resists too little for a valve this size to control it.");
        }
        else if (chosen.Fit == CatalogFit.ClampedToLargest)
        {
            notes.Add(
                $"{valve.Name} wanted Kv {required:0.##} and the series stops at {kvs:0.##}, so it takes "
                + $"authority {achieved:0.##} rather than the target. Nothing in the catalogue is large "
                + "enough for this flow.");
        }

        // `FS4006`. Rounding down can only raise authority, so reaching here means the *target* was
        // already unreachable -- the branch resists far more than the valve can.
        if (achieved < SizingDefaults.ValveAuthorityMinimum)
        {
            notes.Add(
                $"{valve.Name} has authority {achieved:0.##}, below {SizingDefaults.ValveAuthorityMinimum:0.##}. "
                + "It will behave as a switch rather than a control valve: the branch's own resistance "
                + "dominates until the valve is nearly shut.");
        }
    }

    /// <summary>The target authority for one valve.</summary>
    /// <param name="valve">The valve.</param>
    /// <returns>Dimensionless. A stated <c>authority</c> is a constraint; otherwise the rule's target.</returns>
    private double Target(IFlowComponent valve) =>
        valve.StatedParameters.TryGetValue("authority", out var stated) ? stated.SiValue : authorityTarget;
}
