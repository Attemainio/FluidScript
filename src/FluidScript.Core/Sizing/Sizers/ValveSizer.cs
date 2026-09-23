using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Catalogs;
using FluidScript.Core.Catalogs.Valves;
using FluidScript.Core.Components;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Primitives;

namespace FluidScript.Core.Sizing.Sizers;

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
    ICatalog<ValveSpec> catalog, double authorityTarget = SizingDefaults.ValveAuthorityTarget) : SizerBase<ValveComponentBase>
{
    /// <inheritdoc/>
    /// <remarks>
    /// Both, because the achieved authority is an output rather than an input. A script that states
    /// <c>authority</c> has set the target and keeps it; one that does not gets the value the chosen
    /// row actually delivers, which after rounding down is not the number that was asked for.
    /// </remarks>
    public override ImmutableArray<string> Parameters { get; } = ["kv", "authority"];

    /// <inheritdoc/>
    /// <remarks>
    /// <strong>The largest row, because a provisional must disturb the bootstrap as little as
    /// possible.</strong> A valve has no component at all without a <c>kv</c>, so something has to go
    /// in before flows can be estimated — and the smallest row is the most restrictive, which would
    /// hand every other rule on the loop a branch drop dominated by a valve nobody has sized yet. The
    /// largest row is the closest thing the series has to an open port.
    /// </remarks>
    public override ImmutableDictionary<string, Quantity> Provisional { get; } =
        ImmutableDictionary<string, Quantity>.Empty.Add(
            "kv",
            Quantity.FromSi(
                catalog is { Entries.Count: > 0 } ? catalog.Entries[^1].Spec.Kvs : 1, Dimension.Kv));

    /// <inheritdoc/>
    protected override (string Property, string State) Refusal =>
        ("a Kv", "a two-way valve, or a three-way valve with its bypass unconnected");

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
    /// <strong>A three-way valve with its bypass <em>connected</em> is accepted here too, and what keeps
    /// it out of the ordinary pass is the context rather than the type</strong> (<c>C-63</c>). It is a
    /// junction element, so it sits in no branch's <c>Path</c> and <c>OuterLoop.Context</c> can build it
    /// nothing -- the loop skips it on that alone. <c>OuterLoop</c>'s three-way pass builds the context
    /// <em>its</em> way, from the leg that actually varies, and then this arithmetic is the arithmetic:
    /// same Kv law, same authority definition, same catalogue. Refusing by type as well would mean
    /// duplicating all of it to change which two numbers go in.
    /// </para>
    /// </remarks>
    protected override Result<SizingResult> Size(ValveComponentBase valve, in SizingContext context)
    {
        var target = Target(valve);
        var density = context.State.Density.SiValue;

        if (!double.IsFinite(density) || density <= 0 || target is <= 0 or >= 1)
        {
            return Result.Failure<SizingResult>(ResultError.From(
                FluidScript.Core.Diagnostics.Descriptors.FluidDiagnostics.PropertyNotEvaluable,
                ("property", "a Kv"),
                ("name", valve.Name),
                ("state", "the branch's resistance and a target authority between 0 and 1")));
        }

        // A valve nothing flows through has no design point (`S-56`): the band rule would take the
        // smallest row for a common-port flow of 1e-27 and the control-valve rule a Kv of nothing at
        // all. A mixing valve's design flow is its common port's -- the legs still pass the leakage
        // cross-flow of a stopped consumer. It keeps the Kv it has, and the note says why.
        if (Math.Abs(context.CommonFlow ?? context.MassFlow) <= Solvers.Tolerances.FlowZero)
        {
            return Result.Success(new SizingResult
            {
                Values = ImmutableDictionary<string, SizedValue>.Empty,
                Notes =
                [
                    $"{valve.Name} carries no flow at this operating point, so there is no design flow to "
                    + "size it on; it keeps the Kv it has. A consumer that is off (power=0) has no design "
                    + "point this run.",
                ],
            });
        }

        // A stated `dp` is the design drop the Kv is chosen at (C-109): the flow coefficient that
        // takes that drop at the design flow, rounded to the next larger catalogue row so the valve
        // drops no more than the script asked at that flow -- Belimo's and Siemens' selection rule
        // for a calculated Kv between two Kvs values. Authority is then reported, not targeted.
        if (valve.StatedParameters.TryGetValue("dp", out var statedDrop)
            && Ownership.Of(valve, "kv") is not ParameterState.Stated)
        {
            return AtStatedDrop(valve, context, statedDrop.SiValue, density);
        }

        // `D-122`. A three-way valve with its bypass connected is a mixing valve and is sized on its
        // common-port flow to a drop band -- unless the script states an authority, which asks for the
        // control-valve rule by name.
        if (valve is ThreeWayValveComponent { BypassConnected: true }
            && context.CommonFlow is { } common
            && double.IsFinite(common)
            && common > 0
            && Ownership.Of(valve, "authority") is not ParameterState.Stated)
        {
            return MixingBand(valve, context, common, density);
        }

        // `24` step 2, in whichever of its two shapes the circuit allows. On a pump-driven circuit the
        // valve's share `a` of the branch total means a * (rest + valve) = valve, so valve = a * rest /
        // (1 - a); at a = 0.5 that is exactly the rest of the branch. On a bounded one there is nothing
        // to choose: the boundaries fix the driving pressure and the valve takes what the rest leaves.
        var rest = Math.Max(0, context.BranchDrop);
        var bounded = context.AvailableDrop is { } offered && double.IsFinite(offered) && offered > 0;
        var wanted = bounded
            ? Math.Max(0, context.AvailableDrop!.Value - rest)
            : rest * target / (1 - target);

        var required = ValveLaw.RequiredKv(context.MassFlow, wanted, density);
        var notes = ImmutableArray.CreateBuilder<string>();

        if (!double.IsFinite(required) || required <= 0)
        {
            // Below the regularisation drop there is no honest Kv, so the rule declines rather than
            // inventing one. `Provisional` still stands, so the valve exists and the model still builds.
            notes.Add(bounded
                ? $"{valve.Name} could not be sized: the circuit offers "
                    + $"{context.AvailableDrop!.Value / 1000:0.##} kPa and the rest of it already takes "
                    + $"{rest / 1000:0.##} kPa, so nothing is left for the valve to drop. Reduce the "
                    + "resistance in its path, widen the boundary pressures, or state a `kv`."
                : $"{valve.Name} could not be sized: its branch drops {rest / 1000:0.##} kPa excluding the "
                    + "valve, so a valve taking its target share would sit inside the solver's smoothing "
                    + "band. Add resistance to the branch, or state a `kv`.");

            return Result.Success(new SizingResult
            {
                Values = ImmutableDictionary<string, SizedValue>.Empty,
                Notes = notes.ToImmutable(),
            });
        }

        // Up on a bounded circuit and down on a pump-driven one, and the asymmetry is the whole reason
        // the two cases are distinguished: at a fixed differential a Kv below the required one cannot
        // pass the design flow at any position, so rounding down there does not make the valve safer,
        // it makes the design point unreachable.
        var chosen = bounded
            ? catalog.SmallestSatisfying(spec => spec.Kvs >= required)
            : catalog.NearestBelow(required, static spec => spec.Kvs);

        var kvs = chosen.Entry.Spec.Kvs;

        // The drop the chosen row actually produces, from the same law the solver runs, and the
        // authority that follows from it. Not the target -- `24`'s invariant is that the reported
        // authority is the achieved one, and after rounding down it is higher.
        var achievedDrop = Drop(context.MassFlow, kvs, density);
        var achieved = achievedDrop / (rest + achievedDrop);

        Report(valve, chosen, kvs, achieved, achievedDrop, rest, required, notes);

        var litresPerSecond = context.LitresPerSecond(density);
            // Which of the two shapes chose the drop is not recoverable from the Kv alone -- the same
            // catalogue row means different things on a bounded and a pump-driven circuit -- and the rule
            // is the only thing that knows. It used to be `OuterLoop.ThreeWay`'s to say, which meant a
            // two-way valve's basis said nothing about it (`C-62`).
            var available = context.AvailableDrop ?? 0;
            var shape = " — chosen against the branch's own resistance, which a free pump absorbs, so "
                + "the selection rounds down";

            if (bounded)
            {
                shape = string.Create(
                    CultureInfo.InvariantCulture,
                    $" — determined by the {available / 1000:0.#} kPa the boundaries offer, so the "
                    + $"selection rounds up");
            }

            // Two bases, because a report line reads "CV1.authority = ..." and a Kv designation in that
            // position is a value that does not belong to the parameter it is explaining.
            var kvBasis = string.Create(
                CultureInfo.InvariantCulture,
                $"{chosen.Entry.Designation} ({chosen.Entry.Spec.Series}) — authority {achieved:0.##} at "
                + $"{litresPerSecond:0.###} l/s, {achievedDrop / 1000:0.#} kPa{shape}");

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
        ValveComponentBase valve,
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

        Poor(valve.Name, achieved, notes);
    }

    /// <summary>The Kv that takes a stated drop at the design flow, from the next larger catalogue row (<c>C-109</c>).</summary>
    /// <param name="valve">The valve.</param>
    /// <param name="context">The design flow and the branch it sits on.</param>
    /// <param name="statedDrop">Pa, the script's <c>dp</c>.</param>
    /// <param name="density">kg/m³, the design state's.</param>
    /// <returns>The Kv and the authority it achieves, both with a basis naming the stated drop.</returns>
    private Result<SizingResult> AtStatedDrop(ValveComponentBase valve, in SizingContext context, double statedDrop, double density)
    {
        var flow = Math.Abs(context.CommonFlow ?? context.MassFlow);
        var required = ValveLaw.RequiredKv(flow, statedDrop, density);

        if (!double.IsFinite(required) || required <= 0 || statedDrop <= 0)
        {
            return Result.Success(new SizingResult
            {
                Values = ImmutableDictionary<string, SizedValue>.Empty,
                Notes =
                [
                    $"{valve.Name} could not be sized at its stated {statedDrop / 1000:0.##} kPa: no Kv takes "
                    + $"{SizingContext.LitresPerSecond(flow, density):0.###} l/s at that drop. State a `kv`, or a drop above zero.",
                ],
            });
        }

        var chosen = catalog.SmallestSatisfying(spec => spec.Kvs >= required);
        var kvs = chosen.Entry.Spec.Kvs;
        var achievedDrop = Drop(flow, kvs, density);
        var rest = Math.Max(0, context.BranchDrop);
        var achieved = achievedDrop / (rest + achievedDrop);
        var litresPerSecond = SizingContext.LitresPerSecond(flow, density);

        var kvBasis = string.Create(
            CultureInfo.InvariantCulture,
            $"{chosen.Entry.Designation} ({chosen.Entry.Spec.Series}) — the stated {statedDrop / 1000:0.#} kPa at "
            + $"{litresPerSecond:0.###} l/s asks Kv {required:0.##}; the next larger row drops {achievedDrop / 1000:0.#} kPa");

        var authorityBasis = string.Create(
            CultureInfo.InvariantCulture,
            $"{achieved:0.##} achieved at the stated drop — {chosen.Entry.Designation} drops "
            + $"{achievedDrop / 1000:0.#} kPa of the branch's {(rest + achievedDrop) / 1000:0.#} kPa");

        var values = ImmutableDictionary<string, SizedValue>.Empty
            .Add("kv", new SizedValue(Quantity.FromSi(kvs, Dimension.Kv), kvBasis, FromDefault: false))
            .Add("authority", new SizedValue(Quantity.FromSi(achieved, Dimension.Dimensionless), authorityBasis, FromDefault: false));

        return Result.Success(new SizingResult { Values = values, Notes = [] });
    }

    /// <summary>The target authority for one valve.</summary>
    /// <param name="valve">The valve.</param>
    /// <returns>Dimensionless. A stated <c>authority</c> is a constraint; otherwise the rule's target.</returns>
    private double Target(ValveComponentBase valve) =>
        valve.StatedParameters.TryGetValue("authority", out var stated) ? stated.SiValue : authorityTarget;

    /// <summary>Sizes a three-way valve to the drop band a mixing valve is selected in (<c>D-122</c>).</summary>
    /// <param name="valve">The valve, its bypass connected.</param>
    /// <param name="context">Its context: <see cref="SizingContext.MassFlow"/> is the variable leg's flow and <see cref="SizingContext.BranchDrop"/> the variable circuit's resistance.</param>
    /// <param name="common">kg/s through the common port.</param>
    /// <param name="density">kg/m³ at the valve's inlet.</param>
    /// <returns>The Kv, and the authority it achieves.</returns>
    /// <remarks>
    /// <para>
    /// <strong>ESBE's rule, verbatim from the VRG130, VRG140 and 3F datasheets:</strong> <em>"Start with
    /// the heat demand in kW and move vertically to the chosen Δt. Move horizontally to the shaded field
    /// (pressure drop of 3–15 kPa) and select the smaller Kvs-value."</em> The heat demand at the mixed
    /// circuit's Δt is the flow through the common port, and "the smaller Kvs" is the smallest catalogue
    /// row whose drop at that flow is under <see cref="SizingDefaults.ThreeWayDropMaximum"/>. The
    /// catalogue's R5 series is ESBE's own (0.4, 0.63, 1 … 40).
    /// </para>
    /// <para>
    /// <strong>Why not the authority rule</strong> (<c>C-104</c>): that rule presumes the controlled leg
    /// carries its design flow fully open, and a load whose stated inlet lies between the feed and its
    /// own return sits mid-travel at design by construction. Sized for authority 0.5 with an
    /// equal-percentage leg, the ladder's series header got Kv 1.6, which at mid-travel passes 14 % of
    /// that and asked 15 bar of its pump. Sized to the band with linear legs, the drop across the valve
    /// is the band's whatever the mixing ratio, because the leg's opening and its share of the flow
    /// move together.
    /// </para>
    /// <para>
    /// The authority is the same figure the control-valve rule reports -- the variable leg fully open
    /// against its circuit -- so that the two rules' figures compare like with like. It is reported,
    /// not targeted, and <c>FS4006</c>'s note still fires below the minimum: Spirax's band for three-port
    /// valves starts at 0.2, while Johnson Controls' VM-12 shows a constant-flow three-way valve doing
    /// its job at 0.1, so a low figure is worth a line and not a refusal.
    /// </para>
    /// </remarks>
    private Result<SizingResult> MixingBand(
        ValveComponentBase valve, in SizingContext context, double common, double density)
    {
        var chosen = catalog.SmallestSatisfying(
            spec => Drop(common, spec.Kvs, density) <= SizingDefaults.ThreeWayDropMaximum);
        var kvs = chosen.Entry.Spec.Kvs;
        var commonDrop = Drop(common, kvs, density);
        var rest = Math.Max(0, context.BranchDrop);
        var legDrop = Drop(context.MassFlow, kvs, density);
        var achieved = legDrop / (rest + legDrop);
        var litresPerSecond = SizingContext.LitresPerSecond(common, density);
        var notes = ImmutableArray.CreateBuilder<string>();

        if (chosen.Fit == CatalogFit.ClampedToLargest)
        {
            notes.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{valve.Name} wanted a drop under {SizingDefaults.ThreeWayDropMaximum / 1000:0.#} kPa at "
                + $"{litresPerSecond:0.###} l/s and the series stops at Kv {kvs:0.##}, which drops "
                + $"{commonDrop / 1000:0.#} kPa. Nothing in the catalogue passes this flow inside the band."));
        }
        else if (commonDrop < SizingDefaults.ThreeWayDropMinimum)
        {
            notes.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{valve.Name} drops only {commonDrop / 1000:0.#} kPa at {litresPerSecond:0.###} l/s: the "
                + $"smallest row in the series, Kv {kvs:0.##}, is still larger than this flow wants, so the "
                + $"valve will control near its stops."));
        }

        Poor(valve.Name, achieved, notes);

        var kvBasis = string.Create(
            CultureInfo.InvariantCulture,
            $"{chosen.Entry.Designation} ({chosen.Entry.Spec.Series}) — {commonDrop / 1000:0.#} kPa at "
            + $"{litresPerSecond:0.###} l/s through the common port, inside the "
            + $"{SizingDefaults.ThreeWayDropMinimum / 1000:0.#}–{SizingDefaults.ThreeWayDropMaximum / 1000:0.#} kPa "
            + $"a mixing valve is sized to; authority {achieved:0.##} against the variable circuit");

        var authorityBasis = string.Create(
            CultureInfo.InvariantCulture,
            $"{achieved:0.##} against the variable circuit, fully open — {chosen.Entry.Designation} drops "
            + $"{legDrop / 1000:0.#} kPa of its {(rest + legDrop) / 1000:0.#} kPa; reported, not targeted: a "
            + $"mixing valve is sized to its drop band");

        var values = ImmutableDictionary<string, SizedValue>.Empty
            .Add("kv", new SizedValue(Quantity.FromSi(kvs, Dimension.Kv), kvBasis, FromDefault: false))
            .Add(
                "authority",
                new SizedValue(
                    Quantity.FromSi(achieved, Dimension.Dimensionless), authorityBasis, FromDefault: false));

        return Result.Success(new SizingResult { Values = values, Notes = notes.ToImmutable() });
    }

    /// <summary>The authority a rated Kv achieves over its branch at one operating point (<c>24</c>, <c>C-121</c>).</summary>
    /// <param name="context">The operating point: <see cref="SizingContext.MassFlow"/> through the controlled path and <see cref="SizingContext.BranchDrop"/> the rest of it.</param>
    /// <param name="kvs">The rated coefficient, m³/h at 1 bar.</param>
    /// <returns>
    /// The authority, dimensionless, and the valve's own drop fully open, Pa. Both NaN when the state
    /// has no density or no flow to take a drop at.
    /// </returns>
    /// <remarks>
    /// The figure every rule here reports, taken out so that a plant whose Kv was given rather than
    /// chosen reads the same number the same way. Both drops are at one flow and both go as ṁ², so
    /// the flow cancels to first order: this is a property of the Kv against the branch's geometry,
    /// which is why a merged plant has one per valve rather than one per case.
    /// </remarks>
    public static (double Authority, double ValveDrop) Achieved(in SizingContext context, double kvs)
    {
        var density = context.State.Density.SiValue;

        if (!double.IsFinite(density) || density <= 0 || kvs <= 0)
        {
            return (double.NaN, double.NaN);
        }

        var drop = Drop(context.MassFlow, kvs, density);

        return (drop / (Math.Max(0, context.BranchDrop) + drop), drop);
    }

    /// <summary><c>FS4006</c>'s note: the valve will behave as a switch.</summary>
    /// <param name="name">The valve's name.</param>
    /// <param name="achieved">The authority it achieves, dimensionless.</param>
    /// <param name="notes">Where the note goes.</param>
    /// <param name="scenario">The case the figure was read in, when a plant has several; otherwise <see langword="null"/>.</param>
    internal static void Poor(string name, double achieved, ImmutableArray<string>.Builder notes, string? scenario = null)
    {
        if (achieved < SizingDefaults.ValveAuthorityMinimum)
        {
            var where = scenario is null ? string.Empty : $" in {scenario}";

            notes.Add(
                $"{name} has authority {achieved:0.##}{where}, below {SizingDefaults.ValveAuthorityMinimum:0.##}. "
                + "It will behave as a switch rather than a control valve: the branch's own resistance "
                + "dominates until the valve is nearly shut.");
        }
    }
}
