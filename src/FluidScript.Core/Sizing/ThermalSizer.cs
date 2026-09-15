using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Fluids;
using FluidScript.Core.Units;

namespace FluidScript.Core.Sizing;

/// <summary>Sizes an extended exchanger's thermal size from its design point (<c>24</c>, the extended-mode rule).</summary>
/// <remarks>
/// <para>
/// <strong>The design point, not the solve, is what this rule reads.</strong> <c>24</c>'s steps 1-5:
/// each side's flow from its own energy balance, the two capacity rates, the feasibility bound, the NTU
/// that reaches the required effectiveness, and <c>UA = NTU · Cmin</c>. Every number in it follows from
/// what the script stated about the exchanger, which is why the substation's 12 071 W/K is checkable
/// by hand (<c>01</c>) -- and why a rule that sees one branch can size a component that sits on two.
/// The one thing it takes from the circuit is a Rated exchanger's side-1 flow, when the script states
/// neither that flow nor both of that side's temperatures.
/// </para>
/// <para>
/// <strong>Feasibility is checked before anything inverts.</strong> A duty above
/// <c>Cmin · (T_hot,in − T_cold,in)</c> is thermodynamically impossible, and past the inversion it would
/// surface as an enormous area that reads as an expensive design rather than a wrong one. It is
/// <c>FS2111</c>, and it stops this rule.
/// </para>
/// <para>
/// <strong>It sizes what the script left open and no more.</strong> A stated <c>ua</c>, or <c>u</c> with
/// <c>area</c>, is the size; the rule then only derives what follows -- an area from <c>u</c>, a plate
/// count from <c>plate_area</c> -- and reports the approach the size achieves. With nothing stated it
/// sizes <c>ua</c>; an area needs a <c>u</c>, and none is invented: the catalogue has no sourced
/// coefficient yet, and the basis says so. Plate geometry to <c>u</c> (<c>lamella</c>, the chevron
/// correlation) is deferred with <c>27</c>'s plate catalogue.
/// </para>
/// <para>
/// <strong>Rounding up, like the pipe rule.</strong> A plate count is <c>ceil(area / plate_area) + 2</c>:
/// the two end plates have fluid on one side only and transfer nothing, and more area means a closer
/// approach and more duty, never a shortfall. The surplus is reported when it passes
/// <see cref="SizingDefaults.ExchangerOvershootReport"/>, and the achieved approach is held to a stated
/// <c>approach</c> or <see cref="SizingDefaults.ExchangerApproachMinimum"/> (<c>FS4008</c>).
/// </para>
/// </remarks>
public sealed class ThermalSizer : ISizer
{
    /// <inheritdoc/>
    public ImmutableArray<string> Parameters { get; } = ["ua", "area", "plates"];

    /// <inheritdoc/>
    /// <value>Nothing. An exchanger without a size transfers its stated duty until one arrives.</value>
    public ImmutableDictionary<string, Quantity> Provisional => [];

    /// <inheritdoc/>
    public bool CanSize(IFlowComponent component) => component is HeatExchanger { Rating: not null };

    /// <inheritdoc/>
    public Result<SizingResult> Size(IFlowComponent component, in SizingContext context)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component is not HeatExchanger { Rating: { } rating } exchanger)
        {
            return Result.Failure<SizingResult>(ResultError.From(
                FluidDiagnostics.PropertyNotEvaluable,
                ("property", "a thermal size"),
                ("name", component.Name),
                ("state", "a component that is not an extended-mode heat exchanger")));
        }

        var notes = ImmutableArray.CreateBuilder<string>();
        var duty = Math.Abs(exchanger.Power);

        if (!(duty > 0))
        {
            return Nothing(notes, $"{exchanger.Name}: no duty is stated, so there is no design point to size a thermal size from");
        }

        var substance = context.State.Substance;
        var stated = exchanger.StatedParameters;

        // Side 1 gains heat when the duty is positive; side 2 then gives it. A Rated exchanger's side 1
        // is the one branch the rule sees, so its flow can stand in for a missing temperature.
        var side1 = Side.Resolve(
            substance, stated, "in", "out", "dt", "flow", duty, gains: exchanger.Power > 0,
            fallbackFlow: rating.Mode == ExchangerMode.Rated ? Math.Abs(context.MassFlow) : null);
        var side2 = Side.Resolve(substance, stated, "in2", "out2", "dt2", "flow2", duty, gains: exchanger.Power < 0, fallbackFlow: null);

        if (side1 is not { } one || side2 is not { } two)
        {
            return Nothing(
                notes,
                $"{exchanger.Name}: the design point does not fix both sides -- each needs its entering "
                + "temperature and its capacity rate, from power with in/out (in2/out2), dt (dt2) or flow (flow2)");
        }

        var minimum = Math.Min(one.Capacity, two.Capacity);
        var maximum = Math.Max(one.Capacity, two.Capacity);
        var ratio = minimum / maximum;
        var hotIn = Math.Max(one.Inlet, two.Inlet);
        var coldIn = Math.Min(one.Inlet, two.Inlet);
        var span = hotIn - coldIn;

        if (!(span > 0))
        {
            return Nothing(notes, $"{exchanger.Name}: both sides enter at the same temperature, so nothing drives heat across");
        }

        var most = minimum * span;
        var effectiveness = duty / most;

        if (effectiveness >= Effectiveness.Maximum(ratio, rating.Arrangement))
        {
            var infeasible = Diagnostic.Create(
                BinderDiagnostics.DutyBeyondInlets,
                span: null,
                new DiagnosticArgument("name", exchanger.Name),
                new DiagnosticArgument("power", Kilo(duty)),
                new DiagnosticArgument("t_hot", Celsius(hotIn)),
                new DiagnosticArgument("t_cold", Celsius(coldIn)),
                new DiagnosticArgument("qmax", Kilo(most)))
                with
            { ComponentName = exchanger.Name };

            return Result.Success(new SizingResult
            {
                Values = ImmutableDictionary<string, SizedValue>.Empty,
                Notes = [infeasible.Message],
                Diagnostics = [infeasible],
            });
        }

        var required = Effectiveness.Ntu(effectiveness, ratio, rating.Arrangement) * minimum;
        var values = ImmutableDictionary.CreateBuilder<string, SizedValue>(StringComparer.Ordinal);

        var u = Si(stated, "u");
        var statedArea = Si(stated, "area");
        var statedSize = Si(stated, "ua") ?? (u is { } coefficient && statedArea is { } given ? coefficient * given : null);
        var conductance = statedSize ?? required;

        // What the size becomes in metal: an area when a coefficient is known, a plate count when a
        // plate area is. Nothing here rounds until the plate count, which is the one discrete step.
        var area = statedArea ?? (u is { } known ? conductance / known : (double?)null);
        var plateArea = Si(stated, "plate_area");
        var plates = Si(stated, "plates") is { } written ? (int)Math.Round(written) : (int?)null;

        if (plates is null && area is { } wanted && plateArea is { } each && each > 0)
        {
            plates = (int)Math.Ceiling(wanted / each - 1e-9) + 2;
        }

        var installedArea = plates is { } count && plateArea is { } per ? (count - 2) * per : area;
        var installed = installedArea is { } metal && u is { } coefficient2 ? metal * coefficient2 : conductance;

        // The size as built, held to what it achieves: duty, terminals, approach.
        var achievedEffectiveness = Effectiveness.Of(installed / minimum, ratio, rating.Arrangement);
        var achievedDuty = achievedEffectiveness * minimum * span;
        var hotCapacity = one.Inlet >= two.Inlet ? one.Capacity : two.Capacity;
        var coldCapacity = one.Inlet >= two.Inlet ? two.Capacity : one.Capacity;
        var hotOut = hotIn - (achievedDuty / hotCapacity);
        var coldOut = coldIn + (achievedDuty / coldCapacity);
        var approach = rating.Arrangement == ExchangerArrangement.Parallel
            ? hotOut - coldOut
            : Math.Min(hotIn - coldOut, hotOut - coldIn);

        var basis = string.Create(
            CultureInfo.InvariantCulture,
            $"UA {conductance / 1000:0.###} kW/K — NTU {conductance / minimum:0.###}, ε {effectiveness:0.####}, Cr {ratio:0.###}, approach {approach:0.##} K");

        if (statedSize is null)
        {
            values["ua"] = new SizedValue(Quantity.FromSi(required, ConductancePerKelvin), basis, FromDefault: false);
        }

        if (statedArea is null && area is { } sizedArea)
        {
            values["area"] = new SizedValue(
                Quantity.FromSi(sizedArea, Dimension.Area),
                string.Create(CultureInfo.InvariantCulture, $"{sizedArea:0.###} m² — UA {conductance / 1000:0.###} kW/K over u {u:0} W/(m²·K)"),
                FromDefault: false);
        }
        else if (statedArea is null && statedSize is null)
        {
            notes.Add($"{exchanger.Name}: UA {conductance / 1000:0.###} kW/K is sized, but no area follows from it without a u; state u=, or area= directly");
        }

        if (plates is { } chosen && Si(stated, "plates") is null && installedArea is { } built)
        {
            values["plates"] = new SizedValue(
                Quantity.FromSi(chosen, Dimension.Dimensionless),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{chosen} plates, {built:0.###} m² — UA {installed / 1000:0.###} kW/K, approach {approach:0.##} K"),
                FromDefault: false);
        }

        var surplus = (achievedDuty - duty) / duty;

        if (surplus > SizingDefaults.ExchangerOvershootReport && installedArea is { } finalArea && area is { } needed)
        {
            notes.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"'{exchanger.Name}' sized to {plates} plates ({finalArea:0.###} m²); {needed:0.###} m² was needed, so it delivers {achievedDuty / 1000:0.#} kW against {duty / 1000:0.#} kW"));
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var floor = Si(stated, "approach") ?? SizingDefaults.ExchangerApproachMinimum;

        if (approach < floor - 1e-9)
        {
            var close = Diagnostic.Create(
                DesignDiagnostics.ApproachBelowMinimum,
                span: null,
                new DiagnosticArgument("name", exchanger.Name),
                new DiagnosticArgument("approach", approach.ToString("0.##", CultureInfo.InvariantCulture)),
                new DiagnosticArgument("minimum", floor.ToString("0.##", CultureInfo.InvariantCulture)))
                with
            { ComponentName = exchanger.Name };

            diagnostics.Add(close);
            notes.Add(close.Message);
        }

        return Result.Success(new SizingResult
        {
            Values = values.ToImmutable(),
            Notes = notes.ToImmutable(),
            Diagnostics = diagnostics.ToImmutable(),
        });
    }

    /// <summary>W/K, the dimension the registry gives <c>ua</c>.</summary>
    private static Dimension ConductancePerKelvin { get; } =
        Dimension.FromVector(new DimensionVector(Mass: 1, Length: 2, Time: -3, Temperature: -1));

    private static Result<SizingResult> Nothing(ImmutableArray<string>.Builder notes, string reason)
    {
        notes.Add(reason);

        return Result.Success(new SizingResult
        {
            Values = ImmutableDictionary<string, SizedValue>.Empty,
            Notes = notes.ToImmutable(),
        });
    }

    private static double? Si(ImmutableDictionary<string, Quantity> stated, string name) =>
        stated.TryGetValue(name, out var quantity) ? quantity.SiValue : null;

    private static string Kilo(double watts) => (watts / 1000).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Celsius(double kelvin) => (kelvin - 273.15).ToString("0.#", CultureInfo.InvariantCulture) + " °C";

    /// <summary>One side of the design point: where it enters and how much it carries.</summary>
    /// <param name="Inlet">K, the entering temperature.</param>
    /// <param name="Capacity">W/K, <c>ṁ · cp</c>.</param>
    private readonly record struct Side(double Inlet, double Capacity)
    {
        /// <summary>Resolves a side from whatever the script stated about it.</summary>
        /// <remarks>
        /// The energy balance <c>Q = C · ΔT</c> is used directly wherever it can be: a stated <c>dt</c>, or
        /// two stated temperatures, give the capacity rate without a specific heat at all, which is the
        /// number a user checks against a table. A stated flow needs <c>cp</c>, taken at the side's mean
        /// temperature, or at its one known temperature. A missing inlet follows from the outlet and the
        /// capacity rate, in the direction the duty runs.
        /// </remarks>
        public static Side? Resolve(
            ISubstance substance,
            ImmutableDictionary<string, Quantity> stated,
            string inlet,
            string outlet,
            string change,
            string flow,
            double duty,
            bool gains,
            double? fallbackFlow)
        {
            var entering = Si(stated, inlet);
            var leaving = Si(stated, outlet);
            var across = Si(stated, change);
            var rate = Si(stated, flow) ?? fallbackFlow;
            var direction = gains ? 1 : -1;

            if (across is null && entering is { } a && leaving is { } b)
            {
                across = Math.Abs(b - a);
            }

            entering ??= leaving is { } c && across is { } d ? c - (direction * d) : null;
            leaving ??= entering is { } e && across is { } f ? e + (direction * f) : null;

            double? capacity = across is { } delta && delta > 0 ? duty / delta : null;

            if (capacity is null && rate is { } massFlow && massFlow > 0 && (entering ?? leaving) is { } known)
            {
                var mean = entering is { } g && leaving is { } h ? 0.5 * (g + h) : known;
                var state = substance.FromPressureTemperature(
                    Quantity.FromSi(0, Dimension.Pressure), Quantity.FromSi(mean, Dimension.Temperature));

                if (state.IsSuccess)
                {
                    capacity = massFlow * state.Value.SpecificHeat.SiValue;
                }
            }

            entering ??= leaving is { } i && capacity is { } j && j > 0 ? i - (direction * duty / j) : null;

            return entering is { } t && capacity is { } w && w > 0 && double.IsFinite(t) && double.IsFinite(w)
                ? new Side(t, w)
                : null;
        }
    }
}
