using FluidScript.Core.Units;

namespace FluidScript.Core.Fluids;

/// <summary>A pure refrigerant, measured by the property backend across every phase it has.</summary>
/// <remarks>
/// <para>
/// The working fluid of <c>D-78</c>'s refrigeration circuit. It differs from <see cref="Water"/> in
/// one way that matters: <strong>it does not refuse a state for its phase.</strong> Water's domain is
/// the liquid region and anything else there is a defect; a refrigerant's whole point is that it
/// crosses the dome twice per revolution, so two-phase and supercritical states are ordinary and
/// <c>07</c> now says so.
/// </para>
/// <para>
/// <strong>Inside the dome, three of the seven properties do not exist, and this type lets them
/// through as they are.</strong> <c>c_p</c> is infinite there — adding heat at constant pressure moves
/// quality, not temperature — and the transport properties are undefined for a mixture the backend is
/// not told the morphology of. What is always well posed is <c>(p, h)</c> and everything it fixes: the
/// temperature, the entropy and the density. A caller that reads a specific heat from a two-phase
/// state is asking a question with no answer, and gets a non-finite one rather than a plausible
/// number. Recorded as <c>C-52</c>; it is a property of the domain, not of this class.
/// </para>
/// <para>
/// <strong>Both saturation offsets must be positive, and a cycle depends on it</strong>
/// (<see cref="Cycles.VapourCompressionCycle"/>). On the saturation line a pressure and a temperature are one
/// constraint rather than two, so the backend refuses the pair — which is correct, and is why a cycle
/// states a superheat and a subcooling rather than sitting on the boundary. Real machines carry both
/// for the same reason in hardware: liquid must not reach the compressor and vapour must not reach the
/// expansion valve.
/// </para>
/// </remarks>
public sealed class Refrigerant : SubstanceBase
{
    private readonly double _freezingPoint;

    private Refrigerant(
        RefrigerantKind kind,
        string name,
        double freezingPoint,
        StateRange range)
    {
        Kind = kind;
        Name = name;
        ValidRange = range;
        _freezingPoint = freezingPoint;

        var critical = PropertyBackend.RefrigerantCriticalPoint(kind);
        CriticalTemperature = Quantity.FromSi(critical?.Temperature ?? double.NaN, Dimension.Temperature);
        CriticalPressure = Quantity.FromSi(
            critical is { } point ? point.Pressure - Atmosphere : double.NaN, Dimension.Pressure);
    }

    /// <summary>Gets the shared ammonia instance (R717).</summary>
    /// <remarks>
    /// The upper bound is 200 °C rather than the 132.25 °C critical temperature, and the difference is
    /// not slack: <strong>a range has to cover the compressor discharge, and discharge is nowhere near
    /// the saturation curve.</strong> Measured, an ammonia cycle at −7/40 °C and <c>η_is</c> = 0.7
    /// discharges at <strong>152.5 °C</strong> — superheated vapour far above the critical temperature at
    /// a pressure well below the critical one, which is an ordinary state and not a supercritical one.
    /// A bound drawn at the critical point refuses it, which is how this was found (<c>C-53</c>).
    /// The bound is 250 °C rather than 200 because discharge climbs as the compressor worsens — the same
    /// cycle at <c>η_is</c> = 0.5 discharges at 205 °C — and the backend's data runs to 427 °C, so the
    /// range is set by what is being claimed rather than by what is available.
    /// </remarks>
    public static Refrigerant Ammonia { get; } = new(
        RefrigerantKind.Ammonia, "ammonia", 195.5, new StateRange(200, 523.15, 10_000, 11_000_000));

    /// <summary>Gets the shared propane instance (R290).</summary>
    /// <remarks>
    /// Bounded at 165 °C for the reason ammonia is: a −7/75 °C cycle discharges at 107 °C, above the
    /// 96.74 °C critical temperature and below the critical pressure.
    /// </remarks>
    public static Refrigerant Propane { get; } = new(
        RefrigerantKind.Propane, "propane", 85.53, new StateRange(150, 438.15, 5_000, 4_200_000));

    /// <summary>Gets the shared carbon-dioxide instance (R744).</summary>
    /// <remarks>
    /// Its range reaches well above the 73.77 bar critical pressure, because a CO₂ heat pump runs
    /// transcritical by design and a rectangle stopping at the critical point would refuse every useful
    /// operating state (<c>D-81</c>).
    /// </remarks>
    public static Refrigerant CarbonDioxide { get; } = new(
        RefrigerantKind.CarbonDioxide, "co2", 216.59, new StateRange(217, 450, 200_000, 20_000_000));

    /// <summary>Gets which refrigerant this is.</summary>
    public RefrigerantKind Kind { get; }

    /// <inheritdoc/>
    public override string Name { get; }

    /// <inheritdoc/>
    public override StateRange ValidRange { get; }

    /// <summary>Gets the critical temperature.</summary>
    /// <value>K absolute. The line above which there is no saturation curve and no condensing.</value>
    public Quantity CriticalTemperature { get; }

    /// <summary>Gets the critical pressure.</summary>
    /// <value>Pa gauge, like every pressure the model carries.</value>
    public Quantity CriticalPressure { get; }

    /// <inheritdoc/>
    public override Result<FluidState> FromPressureTemperature(Quantity gaugePressure, Quantity temperature)
    {
        var absolute = Absolute(gaugePressure);
        var kelvin = temperature.SiValue;

        if (OutOfRange(kelvin, absolute) is { } failure)
        {
            return Result.Failure<FluidState>(failure);
        }

        var measured = PropertyBackend.RefrigerantFromPressureTemperature(Kind, absolute, kelvin);

        // On the saturation line the pair fixes nothing, and for a refrigerant that is the common case
        // rather than a corner: it is where an evaporator ends and a condenser begins. `FS2002` says so
        // instead of a range failure, because the state exists and this pair cannot name it.
        if (measured is null && OnTheSaturationLine(kelvin, absolute))
        {
            return Result.Failure<FluidState>(ResultError.From(
                Diagnostics.FluidDiagnostics.PairDoesNotFixAState,
                ("a", "pressure"),
                ("b", "temperature")));
        }

        return Build(gaugePressure, absolute, measured);
    }

    /// <inheritdoc/>
    public override Result<FluidState> FromPressureEnthalpy(Quantity gaugePressure, Quantity enthalpy)
    {
        var absolute = Absolute(gaugePressure);

        return Build(
            gaugePressure,
            absolute,
            PropertyBackend.RefrigerantFromPressureEnthalpy(Kind, absolute, enthalpy.SiValue));
    }

    /// <inheritdoc/>
    public override Result<FluidState> FromPressureEntropy(Quantity gaugePressure, Quantity entropy)
    {
        var absolute = Absolute(gaugePressure);

        return Build(
            gaugePressure,
            absolute,
            PropertyBackend.RefrigerantFromPressureEntropy(Kind, absolute, entropy.SiValue));
    }

    /// <inheritdoc/>
    /// <remarks>The triple-point temperature, which for these fluids is far below any cycle's use.</remarks>
    public override Result<Quantity> FreezingPoint(Quantity gaugePressure) =>
        Result.Success<Quantity>(Quantity.FromSi(_freezingPoint, Dimension.Temperature));

    /// <inheritdoc/>
    public override Result<Quantity> SaturationPressure(Quantity temperature)
    {
        if (PropertyBackend.RefrigerantSaturationPressure(Kind, temperature.SiValue) is not { } absolute
            || !double.IsFinite(absolute))
        {
            return Result.Failure<Quantity>(
                NotEvaluable("a saturation pressure", Describe(temperature.SiValue, Atmosphere)));
        }

        return Result.Success<Quantity>(Quantity.FromSi(absolute - Atmosphere, Dimension.Pressure));
    }

    /// <inheritdoc/>
    public override Result<Quantity> SaturationTemperature(Quantity gaugePressure)
    {
        var absolute = Absolute(gaugePressure);

        if (PropertyBackend.RefrigerantSaturationTemperature(Kind, absolute) is not { } kelvin
            || !double.IsFinite(kelvin))
        {
            return Result.Failure<Quantity>(
                NotEvaluable("a saturation temperature", Describe(double.NaN, absolute)));
        }

        return Result.Success<Quantity>(Quantity.FromSi(kelvin, Dimension.Temperature));
    }

    /// <summary>Finds the saturated liquid and vapour enthalpies at a pressure.</summary>
    /// <param name="gaugePressure">The pressure, gauge.</param>
    /// <returns>
    /// The two enthalpies in J/kg, or why they are unknown — including because the pressure is above
    /// the critical one, where there is no saturation line and therefore no pair.
    /// </returns>
    /// <remarks>
    /// The two numbers a condenser's zones are cut at (<c>D-80</c>): above the vapour value is
    /// desuperheat, between them is latent, below the liquid value is subcool. Deliberately not on
    /// <see cref="ISubstance"/> — it presumes a vapour dome, which most substances in this model do not
    /// have.
    /// </remarks>
    public Result<(Quantity Liquid, Quantity Vapour)> SaturationEnthalpies(Quantity gaugePressure)
    {
        var absolute = Absolute(gaugePressure);

        if (PropertyBackend.RefrigerantSaturationEnthalpies(Kind, absolute) is not { } pair
            || !double.IsFinite(pair.Liquid) || !double.IsFinite(pair.Vapour))
        {
            return Result.Failure<(Quantity, Quantity)>(
                NotEvaluable("saturation enthalpies", Describe(double.NaN, absolute)));
        }

        return Result.Success((
            Quantity.FromSi(pair.Liquid, Dimension.Enthalpy),
            Quantity.FromSi(pair.Vapour, Dimension.Enthalpy)));
    }

    private bool OnTheSaturationLine(double temperature, double absolutePressure) =>
        PropertyBackend.RefrigerantSaturationTemperature(Kind, absolutePressure) is { } boiling
        && Math.Abs(temperature - boiling) < 1e-3;

    private Result<FluidState> Build(Quantity gaugePressure, double absolute, BackendState? measured)
    {
        if (measured is not { } state)
        {
            return Result.Failure<FluidState>(NotEvaluable("a state", Describe(double.NaN, absolute)));
        }

        // Only the four that exist at every state are required. `c_p` is infinite inside the dome and
        // the transport properties belong to no single phase there, so demanding them would refuse
        // exactly the states this substance exists to carry (`C-52`).
        if (!double.IsFinite(state.Temperature) || !double.IsFinite(state.Enthalpy)
            || !double.IsFinite(state.Entropy) || !double.IsFinite(state.Density))
        {
            return Result.Failure<FluidState>(
                NotEvaluable("a property", Describe(state.Temperature, absolute)));
        }

        if (OutOfRange(state.Temperature, absolute) is { } failure)
        {
            return Result.Failure<FluidState>(failure);
        }

        // Measured on two-phase ammonia at -7 C, the backend answers with mu = 8.15e-6 Pa.s and
        // k = 0.0252 W/(m.K) -- the *vapour's*, against liquid ammonia's 1.8e-4 and 0.55, both a factor
        // of about 22 away -- and cp = 12715 J/(kg.K) where the constant-pressure value is infinite,
        // because heat added there moves quality and not temperature. Three plausible numbers under
        // labels that say the state's: the exact shape of defect this project keeps finding. They are
        // blanked rather than passed on, because nothing reads them today and a two-phase pressure-drop
        // model will ask for saturated liquid and saturated vapour explicitly rather than trust a
        // mixture state's fields (`C-52`).
        var mixture = state.Phase == Phase.TwoPhase;

        return Result.Success<FluidState>(new FluidState
        {
            Substance = this,
            Pressure = gaugePressure,
            Temperature = Quantity.FromSi(state.Temperature, Dimension.Temperature),
            Enthalpy = Quantity.FromSi(state.Enthalpy, Dimension.Enthalpy),
            Entropy = Quantity.FromSi(state.Entropy, Dimension.SpecificHeat),
            Density = Quantity.FromSi(state.Density, Dimension.Density),
            DynamicViscosity = Quantity.FromSi(
                mixture ? double.NaN : state.DynamicViscosity, FluidDimensions.DynamicViscosity),
            SpecificHeat = Quantity.FromSi(
                mixture ? double.NaN : state.SpecificHeat, Dimension.SpecificHeat),
            ThermalConductivity = Quantity.FromSi(
                mixture ? double.NaN : state.ThermalConductivity, FluidDimensions.ThermalConductivity),
            Phase = state.Phase,
        });
    }
}
