using System.Collections.Immutable;

using FluidScript.Core.Binding;
using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Language;
using FluidScript.Core.Units;

namespace FluidScript.Core.Topology;

/// <summary>Turns a nominal diameter designation into the bore a hydraulic calculation uses.</summary>
/// <remarks>
/// <para>
/// <strong>DN is a designation, not a diameter.</strong> DN25 steel pipe has a 27.3 mm bore, and
/// computing an area from 25 mm is a 16 % area error and roughly a factor of two in pressure gradient,
/// with nothing in the result looking wrong. The mapping is catalogue data
/// (<see href="27-component-catalog.md"/>, <c>P3.5</c>), and this interface is the seam that lets
/// lowering exist a package before the catalogue does.
/// </para>
/// <para>
/// It is not a general catalogue lookup and should not grow into one: everything else a catalogue row
/// holds — wall thickness, material, pressure class, the two public sources every row carries — is
/// wanted after lowering, not during it.
/// </para>
/// </remarks>
public interface IBoreLookup
{
    /// <summary>The inside diameter of a pipe of this nominal size.</summary>
    /// <param name="nominalDiameter">The DN designation, as a bare number.</param>
    /// <returns>m, or <see langword="null"/> when the designation is not in the catalogue.</returns>
    /// <param name="material">A catalogue id the pipe named with <c>material=</c>, or <see langword="null"/> for the script's catalogue (<c>C-36</c>).</param>
    double? BoreFor(double nominalDiameter, string? material = null);
}

/// <summary>How the script connected one component.</summary>
/// <param name="Connections">How many connection endpoints name it.</param>
/// <param name="NamedPorts">The ports named explicitly, in the order the connections wrote them.</param>
/// <remarks>
/// <strong>A count alone is not enough, and the difference matters on exactly one kind today.</strong>
/// A three-way valve with two connections has one port open, and which one decides whether it is a
/// two-way valve or something stranger: leaving <c>c</c> open is the documented two-way arrangement,
/// while a script that qualifies <c>c</c> and leaves <c>b</c> open has connected the bypass and meant
/// something else. Reading only the degree would treat the two the same (<c>S-14a</c>).
/// </remarks>
public readonly record struct PortWiring(int Connections, ImmutableArray<string> NamedPorts)
{
    /// <summary>Gets the wiring of a component no connection names.</summary>
    public static PortWiring None { get; } = new(0, []);

    /// <summary>Tells whether a connection named this port explicitly.</summary>
    /// <param name="port">The port's name, as the kind declares it.</param>
    /// <returns><see langword="true"/> when some connection qualified it.</returns>
    public bool Names(string port) => NamedPorts.Contains(port);
}

/// <summary>Builds the component that carries a symbol's equations.</summary>
/// <remarks>
/// A seam rather than a static method, because what a component is built from changes as the
/// pipeline grows: today it is the script's stated parameters, and once <c>24</c>'s outer loop exists
/// it is those plus whatever sizing most recently chose. Lowering re-runs per outer iteration and asks
/// again; a factory holding the current values is what makes that a parameter rather than a rewrite.
/// </remarks>
public interface IComponentFactory
{
    /// <summary>Builds the flow component for one bound symbol.</summary>
    /// <param name="symbol">The bound component, with its parameters evaluated to SI.</param>
    /// <param name="wiring">How the script connected it, for the kinds whose shape depends on it.</param>
    /// <returns>
    /// The component, or <see langword="null"/> when it cannot be built from what is known yet — a
    /// pipe whose bore no catalogue has resolved, most often. Null is a normal result and never an
    /// exception: a script under editing is malformed most of the time.
    /// </returns>
    IFlowComponent? Create(ComponentSymbol symbol, PortWiring wiring);

    /// <summary>Gets the labels, <c>component.parameter</c>, of sized values that are bootstrap provisionals.</summary>
    /// <value>
    /// Empty when every sized value the factory holds was a rule's choice. A provisional lets a component
    /// build and decides nothing (<c>D-96</c>); the graph carries the set so that well-posedness can treat
    /// those parameters as free.
    /// </value>
    ImmutableHashSet<string> Provisional => [];
}

/// <summary>Builds components from a bound symbol's stated parameters and the registry's defaults.</summary>
/// <remarks>
/// <para>
/// <strong>A parameter absent from the symbol is absent, not null</strong> (<c>D-02</c>). What fills
/// its place is the kind's omission policy: a visible default where the registry declares one, and the
/// component's own default where sizing would otherwise choose. Nothing here invents a value the
/// registry does not name.
/// </para>
/// <para>
/// <strong>Geometry is the one thing it cannot always supply.</strong> A pipe needs a bore, the script
/// states a DN designation, and turning one into the other is the catalogue's — so a pipe whose
/// <see cref="IBoreLookup"/> returns nothing produces no component. That is a real ordering
/// consequence of building lowering before the catalogue rather than a shortcut (<c>C-24</c>).
/// </para>
/// </remarks>
/// <param name="bores">Where a DN designation becomes a bore.</param>
/// <param name="sizes">
/// What the outer loop chose on an earlier pass, or <see langword="null"/> on the first lowering.
/// This is the whole of how a sized value re-enters the model: lowering is re-run against it rather
/// than a component being mutated, which is what keeps a solve a pure function of its graph
/// (<c>31</c>'s invariant 6) and what <c>08</c> means by lowering having to be re-runnable.
/// </param>
/// <param name="substance">
/// The fluid, for the one thing a component is built <em>from</em> a property of: a Rated exchanger's
/// side-2 capacity rate, <c>ṁ₂ cp₂</c>, which is a boundary condition fixed at lowering rather than a
/// solved quantity (<c>D-19</c>). <see langword="null"/> leaves a Rated exchanger unable to rate, and it
/// then transfers its stated duty.
/// </param>

public sealed class ComponentFactory(IBoreLookup bores, SizingOverlay? sizes = null, ISubstance? substance = null) : IComponentFactory
{
    private readonly SizingOverlay _sizes = sizes ?? SizingOverlay.Empty;

    /// <inheritdoc/>
    public ImmutableHashSet<string> Provisional => _sizes.Provisional;

    /// <inheritdoc/>
    public IFlowComponent? Create(ComponentSymbol symbol, PortWiring wiring)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        if (symbol.Kind is not { } kind)
        {
            return null;
        }

        var stated = Stated(symbol);
        var defaults = Defaults(symbol, kind);
        var sized = Sized(symbol, kind, stated, defaults);

        return kind.Keyword switch
        {
            "pipe" => Pipe(symbol, stated, sized, defaults),
            "valve" => Valve(symbol, kind, stated, sized, defaults),
            "three_way_valve" => ThreeWay(symbol, kind, wiring, stated, sized, defaults),
            "pump" => Pump(symbol, kind, stated, sized, defaults),
            "heat_exchanger" => Exchanger(symbol, kind, wiring, stated, sized, defaults),
            "tank" => Tank(symbol, kind, stated, sized, defaults),
            _ => null,
        };
    }

    /// <summary>A duty-mode exchanger, with the resistance its design point implies.</summary>
    /// <param name="symbol">The bound declaration.</param>
    /// <param name="kind">Its registry entry.</param>
    /// <param name="stated">What the script wrote.</param>
    /// <param name="sized">What a rule chose.</param>
    /// <param name="defaults">What the registry decided.</param>
    /// <returns>The component. An exchanger always builds; only its resistance waits.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The drop and the flow arrive from different places and neither is sufficient alone.</strong>
    /// <see cref="HeatExchanger"/> holds <c>Δp = dp·(ṁ/ṁ_design)²</c>, so a <c>dp</c> with no design flow
    /// is half a law. <c>dp</c> is stated or carries the registry's decided 20 kPa; the design flow is
    /// sized by <c>ExchangerSizer</c> from the flow the circuit actually runs at.
    /// </para>
    /// <para>
    /// <strong>Before the first flow estimate there is no design point, so the block is ideal.</strong>
    /// Passing a positive <c>dp</c> with a zero flow throws by contract, and rightly -- the implied
    /// resistance is infinite. The bootstrap pass therefore builds a resistance-free exchanger and the
    /// first real pass replaces it, which is the same shape as every other provisional.
    /// </para>
    /// <para>
    /// Role aliases state a positive capacity while the physics core receives signed heat flow:
    /// <c>load</c>, <c>cooler</c>, <c>radiator</c> and <c>chiller</c> remove heat; <c>heater</c> and
    /// <c>boiler</c> add it. The neutral <c>heat_exchanger</c>, <c>exchanger</c> and <c>hx</c>
    /// spellings retain an explicitly signed <c>power</c>.
    /// </para>
    /// </remarks>
    /// <param name="wiring">How many ports the script connected, and by what names.</param>
    private HeatExchanger Exchanger(
        ComponentSymbol symbol,
        ComponentKindInfo kind,
        PortWiring wiring,
        ImmutableDictionary<string, Quantity> stated,
        ImmutableDictionary<string, Quantity> sized,
        ImmutableDictionary<string, Quantity> defaults)
    {
        var design = Value(symbol, kind, "flow") ?? 0;
        var drop = design > 0 ? Value(symbol, kind, "dp") ?? 0 : 0;
        var power = Value(symbol, kind, "power") ?? 0;
        var writtenKind = NameResolution.Normalize(symbol.WrittenKind);

        power = writtenKind switch
        {
            "load" or "cooler" or "radiator" or "chiller" => -Math.Abs(power),
            "heater" or "boiler" => Math.Abs(power),
            _ => power,
        };

        // `S-14b`. Side 2's row exists whenever the script wired it, and its *resistance* only when a
        // script also states `flow2` -- no rule chooses that, because a rule sees one branch and this
        // component sits on two. An ideal side still carries `p_in2 = p_out2`, which is the row the
        // counting table was crediting and the component was not declaring.
        var secondary = wiring.Names("in2") || wiring.Names("out2");
        var secondaryFlow = Value(symbol, kind, "flow2") ?? 0;
        var secondaryDrop = secondaryFlow > 0 ? Value(symbol, kind, "dp2") ?? 0 : 0;

        return new HeatExchanger(
            symbol.Name,
            power,
            drop,
            design,
            secondaryDrop,
            secondaryFlow,
            secondary)
        {
            StatedParameters = stated,
            SizedParameters = sized,
            DefaultParameters = defaults,
            Rating = Rating(symbol, kind, secondary, power),
        };
    }

    /// <summary>The names whose statement is evidence of a second side (<c>D-19</c>).</summary>
    private static readonly ImmutableArray<string> SecondaryProfile = ["in2", "out2", "dt2", "flow2"];

    /// <summary>Whether a declaration states any of the side-2 profile, which is what promotes Duty to Rated.</summary>
    /// <param name="symbol">The bound declaration.</param>
    /// <returns><see langword="true"/> when <c>in2</c>, <c>out2</c>, <c>dt2</c> or <c>flow2</c> is written.</returns>
    public static bool StatesSecondaryProfile(ComponentSymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        foreach (var name in SecondaryProfile)
        {
            if (symbol.Parameters.ContainsKey(name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What an exchanger rates against, or <see langword="null"/> for a duty block.</summary>
    /// <param name="symbol">The bound declaration.</param>
    /// <param name="kind">Its registry entry.</param>
    /// <param name="coupled">Whether the secondary ports are wired.</param>
    /// <param name="power">W, the signed duty the script stated, for the direction a side-2 profile runs.</param>
    /// <returns>The rating, whose <see cref="ExchangerRating.CanRate"/> says whether it is complete.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The mode is evidence of a second side, in <c>D-19</c>'s precedence.</strong> Wired secondary
    /// ports are Coupled whatever else is stated; a stated <c>in2</c>, <c>out2</c>, <c>dt2</c> or
    /// <c>flow2</c> with open ports is Rated; and <c>ua</c>, <c>u</c>, <c>area</c> or geometry alone promote
    /// nothing, because they say how heat crosses and not what it crosses to.
    /// </para>
    /// <para>
    /// <strong>The size is whatever is known first:</strong> a stated or sized <c>ua</c>, else
    /// <c>u · area</c> when both are known. Nothing here sizes; the thermal rule does, and this reads its
    /// answer back through the overlay like every other sized value.
    /// </para>
    /// <para>
    /// <strong>A Rated profile is resolved to the two numbers ε-NTU needs</strong>: the temperature side 2
    /// enters at, and its capacity rate. <c>in2</c> gives the first outright; <c>out2</c> with <c>dt2</c>
    /// gives it in the direction the duty runs -- a side that gives heat enters hotter. The flow is
    /// <c>flow2</c> when stated, else what the duty implies across the side's temperature change, at
    /// the fluid's specific heat at the side's mean temperature (<c>S-32</c>).
    /// </para>
    /// </remarks>
    private ExchangerRating? Rating(ComponentSymbol symbol, ComponentKindInfo kind, bool coupled, double power)
    {
        if (!coupled && !StatesSecondaryProfile(symbol))
        {
            return null;
        }

        var conductance = Value(symbol, kind, "ua")
            ?? (Value(symbol, kind, "u") is { } u && Value(symbol, kind, "area") is { } area ? u * area : 0);

        var arrangement = symbol.Parameters.TryGetValue("arrangement", out var stated) && stated.Symbol is { } name
            ? name switch
            {
                "parallel" => ExchangerArrangement.Parallel,
                "crossflow" => ExchangerArrangement.Crossflow,
                _ => ExchangerArrangement.Counter,
            }
            : ExchangerArrangement.Counter;

        if (coupled)
        {
            return new ExchangerRating
            {
                Mode = ExchangerMode.Coupled,
                Arrangement = arrangement,
                Conductance = conductance,
            };
        }

        var inlet = Value(symbol, kind, "in2");
        var outlet = Value(symbol, kind, "out2");
        var change = Value(symbol, kind, "dt2");

        // Side 2 loses what side 1 gains: with power > 0 it enters hotter than it leaves.
        var direction = power >= 0 ? 1 : -1;

        inlet ??= outlet is { } leaving && change is { } across ? leaving + (direction * across) : null;
        outlet ??= inlet is { } entering && change is { } across2 ? entering - (direction * across2) : null;

        var capacity = 0.0;

        if (inlet is { } entering2 && substance is not null)
        {
            var mean = outlet is { } leaving2 ? 0.5 * (entering2 + leaving2) : entering2;
            var state = substance.FromPressureTemperature(
                Quantity.FromSi(0, Dimension.Pressure), Quantity.FromSi(mean, Dimension.Temperature));

            if (state.IsSuccess)
            {
                var heat = state.Value.SpecificHeat.SiValue;
                var flow = Value(symbol, kind, "flow2")
                    ?? (outlet is { } leaving3 && Math.Abs(entering2 - leaving3) > 0
                        ? Math.Abs(power) / (heat * Math.Abs(entering2 - leaving3))
                        : null);

                capacity = flow is { } rate ? rate * heat : 0;
            }
        }

        return new ExchangerRating
        {
            Mode = ExchangerMode.Rated,
            Arrangement = arrangement,
            Conductance = conductance,
            SecondaryInletTemperature = inlet ?? double.NaN,
            SecondaryCapacityRate = capacity,
        };
    }

    /// <summary>A two-way valve, or <see langword="null"/> when nothing has chosen its <c>kv</c>.</summary>
    /// <param name="symbol">The bound declaration.</param>
    /// <param name="kind">Its registry entry.</param>
    /// <param name="stated">What the script wrote.</param>
    /// <param name="sized">What a rule chose.</param>
    /// <param name="defaults">What the registry decided.</param>
    /// <returns>The component, or <see langword="null"/> to leave it unresolved.</returns>
    /// <remarks>
    /// <strong>The absent <c>kv</c> used to be a literal <c>1</c>, and that was <c>C-58</c>.</strong>
    /// The fallback reached the component without reaching any of the three parameter maps, so
    /// <c>WellPosedness.IsFree</c> called <c>kv</c> free and promotable while lowering had already
    /// picked it -- and at Kv 1 the simple loop's valve dropped 74.3 kPa where sizing gives 29.3, which
    /// is most of the gap between its pump head and <c>24</c>'s worked example. <c>D-02</c> allows a
    /// parameter to be sized or to carry a visible decided default and allows nothing else, so this now
    /// follows <see cref="Pipe"/>'s rule: no bore, no pipe; no Kv, no valve.
    /// </remarks>
    private Valve? Valve(
        ComponentSymbol symbol,
        ComponentKindInfo kind,
        ImmutableDictionary<string, Quantity> stated,
        ImmutableDictionary<string, Quantity> sized,
        ImmutableDictionary<string, Quantity> defaults) =>
        Value(symbol, kind, "kv") is not { } kv
            ? null
            : new Valve(symbol.Name, kv, Value(symbol, kind, "position") ?? 1, Characteristic(symbol, kind))
            {
                StatedParameters = stated,
                SizedParameters = sized,
                DefaultParameters = defaults,
            };

    /// <summary>A three-way valve, or <see langword="null"/> when nothing has chosen its <c>kv</c>.</summary>
    /// <param name="symbol">The bound declaration.</param>
    /// <param name="kind">Its registry entry.</param>
    /// <param name="wiring">How many ports the script connected, and by what names.</param>
    /// <param name="stated">What the script wrote.</param>
    /// <param name="sized">What a rule chose.</param>
    /// <param name="defaults">What the registry decided.</param>
    /// <returns>The component, or <see langword="null"/> to leave it unresolved.</returns>
    /// <remarks><c>C-58</c>, for the same reason as <see cref="Valve"/>.</remarks>
    private ThreeWayValve? ThreeWay(
        ComponentSymbol symbol,
        ComponentKindInfo kind,
        PortWiring wiring,
        ImmutableDictionary<string, Quantity> stated,
        ImmutableDictionary<string, Quantity> sized,
        ImmutableDictionary<string, Quantity> defaults) =>
        Value(symbol, kind, "kv") is not { } kv
            ? null
            : new ThreeWayValve(
                symbol.Name,
                kv,
                Value(symbol, kind, "position") ?? 1,
                Characteristic(symbol, kind),

                // Two connections and no explicit `b` is the two-way arrangement the page describes,
                // and it is one Kv law rather than two (S-14a). `b` is the bypass port, which was spelt
                // `c` before the A/B/AB rename shifted every letter -- so this names a different port
                // than it used to, and it is the one place where getting that backwards would silently
                // turn every three-way valve in the corpus into a two-way.
                bypassConnected: wiring.Connections > 2 || wiring.Names("b"))
            {
                StatedParameters = stated,
                SizedParameters = sized,
                DefaultParameters = defaults,
            };

    /// <summary>Every parameter the outer loop chose for this component.</summary>
    /// <param name="symbol">The bound component.</param>
    /// <param name="kind">Its registry entry.</param>
    /// <param name="stated">What the script stated, which sizing may never override.</param>
    /// <param name="defaults">What the registry defaulted, which sizing does not touch either.</param>
    /// <returns>Canonical parameter name to value; empty before the loop has run.</returns>
    /// <remarks>
    /// <strong>A stated value wins outright</strong> (<c>24</c>'s invariant 1), and a defaulted one is
    /// already claimed, so an overlay entry for either is dropped rather than applied. That is a guard
    /// against a rule that did not check, not an expectation that one will not: the loop already skips
    /// both, and a sizer reaching past a user's own number is the worst thing this subsystem could do
    /// quietly.
    /// </remarks>
    private ImmutableDictionary<string, Quantity> Sized(
        ComponentSymbol symbol,
        ComponentKindInfo kind,
        ImmutableDictionary<string, Quantity> stated,
        ImmutableDictionary<string, Quantity> defaults)
    {
        var chosen = _sizes.For(symbol.Name);

        if (chosen.IsEmpty)
        {
            return [];
        }

        var sized = ImmutableDictionary.CreateBuilder<string, Quantity>(StringComparer.Ordinal);

        foreach (var (name, value) in chosen)
        {
            if (!stated.ContainsKey(name) && !defaults.ContainsKey(name) && kind.Parameters.ContainsKey(name))
            {
                sized[name] = value;
            }
        }

        return sized.ToImmutable();
    }

    /// <summary>A pump's duty point stated as a volume flow, as the mass flow a curve is published for.</summary>
    /// <param name="symbol">The pump.</param>
    /// <param name="kind">Its kind.</param>
    /// <returns>kg/s at 20 °C, or <see langword="null"/> when no <c>vflow</c> is stated.</returns>
    /// <remarks>
    /// A pump curve is published against volume flow of cold water (Grundfos and Wilo datasheets state
    /// their curves for water at 20 °C), so the duty point's density is the datasheet's, not the
    /// circuit's; the operating point the circuit reaches is the solve's business (P5.13b).
    /// </remarks>
    private double? DutyVolume(ComponentSymbol symbol, ComponentKindInfo kind) =>
        Value(symbol, kind, "vflow") is { } volume ? volume * ReferenceDensity() : null;

    /// <summary>The fluid's density at 20 °C, kg/m³: the state pump curves are published at.</summary>
    private double ReferenceDensity() =>
        substance?.FromPressureTemperature(
            Quantity.FromSi(0, Dimension.Pressure), Quantity.FromSi(293.15, Dimension.Temperature)) is { IsSuccess: true } state
            ? state.Value.Density.SiValue
            : 998.2;

    /// <summary>Every parameter the script stated, in SI.</summary>
    /// <param name="symbol">The bound component.</param>
    /// <returns>Canonical parameter name to value; empty when the script stated none.</returns>
    /// <remarks>
    /// <para>
    /// <strong>What separates a constraint from a coefficient is that the user wrote it</strong>
    /// (<c>D-02</c>), and no downstream pass can recover that from the value: a stated
    /// <c>position=1</c> and the registry's own <c>position</c> default are the same number and mean
    /// opposite things to well-posedness. Carrying the distinction is the whole reason
    /// <see cref="IComponent.StatedParameters"/> is separate from
    /// <see cref="IComponent.DefaultParameters"/>.
    /// </para>
    /// <para>
    /// A parameter holding a symbol rather than a quantity — <c>characteristic=linear</c> — is absent
    /// here. It selects a mode and constrains no unknown, and a dimensionless placeholder standing in
    /// for it would be counted as a constraint by everything that walks this map.
    /// </para>
    /// </remarks>
    internal static ImmutableDictionary<string, Quantity> Stated(ComponentSymbol symbol)
    {
        var stated = ImmutableDictionary.CreateBuilder<string, Quantity>(StringComparer.Ordinal);

        foreach (var (name, parameter) in symbol.Parameters)
        {
            if (parameter.Value is { } quantity)
            {
                stated[name] = quantity;
            }
        }

        return stated.ToImmutable();
    }

    /// <summary>Every parameter the script omitted that the registry decides a visible default for.</summary>
    /// <param name="symbol">The bound component.</param>
    /// <param name="kind">Its registry entry.</param>
    /// <returns>Canonical parameter name to the default's value in SI.</returns>
    /// <remarks>
    /// Only the <c>Default</c> omission policy appears. A parameter sizing chooses is absent from all
    /// three maps until <c>24</c>'s outer loop has run and filled
    /// <see cref="IComponent.SizedParameters"/> — which is exactly what makes it promotable
    /// (<c>D-02</c>): well-posedness looks for a parameter no map claims.
    /// </remarks>
    private static ImmutableDictionary<string, Quantity> Defaults(
        ComponentSymbol symbol, ComponentKindInfo kind)
    {
        var defaults = ImmutableDictionary.CreateBuilder<string, Quantity>(StringComparer.Ordinal);

        foreach (var (name, info) in kind.Parameters)
        {
            if (symbol.Parameters.ContainsKey(name)
                || info is not { OmissionBehavior: ParameterOmissionBehavior.Default, DefaultLiteral: { } literal }
                || DefaultOf(literal, info.Dimension) is not { } value)
            {
                continue;
            }

            defaults[name] = Quantity.FromSi(value, info.Dimension);
        }

        // D-110: an implicit pipe (rule I7) states no length unless the line does; its length is then zero, a
        // decided default the report shows as one, so nothing tries to size it.
        if (symbol.Origin is Origin.Inferred { Rule: "I7" }
            && !symbol.Parameters.ContainsKey("length")
            && kind.Parameters.TryGetValue("length", out var length))
        {
            defaults["length"] = Quantity.FromSi(0, length.Dimension);
        }

        return defaults.ToImmutable();
    }

    /// <summary>The SI value of a parameter, from the script or from the kind's visible default.</summary>
    /// <param name="symbol">The bound component.</param>
    /// <param name="kind">Its registry entry.</param>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <returns>
    /// The value in SI, or <see langword="null"/> when the script stated none and the kind declares no
    /// default — which is a parameter sizing chooses, and sizing has not run.
    /// </returns>
    private double? Value(ComponentSymbol symbol, ComponentKindInfo kind, string parameter)
    {
        if (symbol.Parameters.TryGetValue(parameter, out var stated) && stated.Value is { } quantity)
        {
            return quantity.SiValue;
        }

        // Between the two, because a stated value outranks a chosen one and a chosen one outranks the
        // registry's default -- which for a `Size` parameter does not exist anyway, so in practice this
        // is the only thing that ever fills one in.
        if (_sizes.For(symbol.Name, parameter) is { } chosen)
        {
            return chosen;
        }

        return kind.Parameters.TryGetValue(parameter, out var info)
            && info is { OmissionBehavior: ParameterOmissionBehavior.Default, DefaultLiteral: { } literal }
                ? DefaultOf(literal, info.Dimension)
                : null;
    }

    /// <summary>Evaluates a registry default literal, which is written the way a user would write it.</summary>
    /// <param name="literal">The canonical source form, such as <c>300 dm3</c> or <c>0.045 mm</c>.</param>
    /// <param name="dimension">The parameter's dimension, for a bare number.</param>
    /// <returns>The value in SI, or <see langword="null"/> when the literal does not parse.</returns>
    /// <remarks>
    /// Parsed rather than held as a number, because <c>D-32</c> makes the literal the thing the model
    /// contract reports and hover shows. Two spellings of the same default is the drift this avoids.
    /// </remarks>
    private static double? DefaultOf(string literal, Dimension dimension)
    {
        var parts = literal.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0
            || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var magnitude))
        {
            return null;
        }

        if (parts.Length == 1)
        {
            return Quantity.FromBareNumber(magnitude, dimension).SiValue;
        }

        return UnitTable.Resolve(parts[1], dimension) is { } unit
            ? Quantity.FromUnit(magnitude, unit).SiValue
            : null;
    }

    // The registry's default is read here rather than hard-coded, because the two valve kinds default
    // differently (`D-122`): a two-way control valve is equal-percentage and a three-way mixing valve
    // is linear. Until `D-122` the fallback was a literal `EqualPercentage` that no parameter map
    // recorded -- `C-58`'s shape for the characteristic -- and the registry's own default was never read.
    private static ValveCharacteristic Characteristic(ComponentSymbol symbol, ComponentKindInfo kind)
    {
        var name = symbol.Parameters.TryGetValue("characteristic", out var stated) && stated.Symbol is { } written
            ? written
            : kind.Parameters.TryGetValue("characteristic", out var info) ? info.DefaultLiteral : null;

        return name switch
        {
            "linear" => ValveCharacteristic.Linear,
            "quick_open" => ValveCharacteristic.QuickOpen,
            _ => ValveCharacteristic.EqualPercentage,
        };
    }

    private Pipe? Pipe(
        ComponentSymbol symbol,
        ImmutableDictionary<string, Quantity> stated,
        ImmutableDictionary<string, Quantity> sized,
        ImmutableDictionary<string, Quantity> defaults)
    {
        var kind = symbol.Kind!;
        // An implicit pipe (I7, D-110) with no length runs at its decided default of zero: `dn=25` on a connection line marks the drawing and the bore and adds no friction until a length is written.
        var length = Value(symbol, kind, "length") ?? (defaults.TryGetValue("length", out var decided) ? decided.SiValue : null);
        var nominal = Value(symbol, kind, "dn");

        var material = symbol.Parameters.TryGetValue("material", out var series) ? series.Symbol : null;

        if (length is not { } metres || nominal is not { } dn || bores.BoreFor(dn, material) is not { } bore)
        {
            return null;
        }

        return new Pipe(
            symbol.Name,
            metres,
            bore,
            Value(symbol, kind, "roughness") ?? 0.045e-3,
            Value(symbol, kind, "minor_loss") ?? 0)
        {
            Material = material,
            StatedParameters = stated,
            SizedParameters = sized,
            DefaultParameters = defaults,
        };
    }

    private Pump Pump(
        ComponentSymbol symbol,
        ComponentKindInfo kind,
        ImmutableDictionary<string, Quantity> stated,
        ImmutableDictionary<string, Quantity> sized,
        ImmutableDictionary<string, Quantity> defaults)
    {
        var head = Value(symbol, kind, "head");
        var flow = Value(symbol, kind, "flow") ?? DutyVolume(symbol, kind);
        var efficiency = Value(symbol, kind, "efficiency") ?? 0.7;

        // A stated `dp` with no `head` is the rise itself (C-109); the head here is what that rise is
        // worth at 20 °C water, for the seed's walk and the curve's shape. The equation holds the
        // rise, not this number. Both stated is FS2101's, and the head wins for the build.
        var rise = head is null && stated.TryGetValue("dp", out var pascals) ? pascals.SiValue : (double?)null;
        head ??= rise is { } stated2 ? Hydrostatic.Head(stated2, ReferenceDensity()) : null;

        // A duty point gives the default quadratic its curvature; a head with no flow beside it is a
        // shut-off head and nothing more, which is the flat curve a pump with one stated number has.
        var (shutOff, curvature) = head is { } metres && flow is { } duty and > 0
            ? Components.Pump.CurveThrough(metres, duty)
            : (head ?? 0, 0d);

        return new Pump(symbol.Name, shutOff, curvature, efficiency: efficiency)
        {
            StatedRise = rise,
            StatedParameters = stated,
            SizedParameters = sized,
            DefaultParameters = defaults,
        };
    }

    private Tank Tank(
        ComponentSymbol symbol,
        ComponentKindInfo kind,
        ImmutableDictionary<string, Quantity> stated,
        ImmutableDictionary<string, Quantity> sized,
        ImmutableDictionary<string, Quantity> defaults)
    {
        var layers = Value(symbol, kind, "layers") ?? Components.Tank.DefaultLayers;

        return new Tank(
            symbol.Name,
            Levels(symbol, kind, "in"),
            Levels(symbol, kind, "out"),
            Value(symbol, kind, "volume") ?? Components.Tank.DefaultVolume,
            (int)layers)
        {
            StatedParameters = stated,
            SizedParameters = sized,
            DefaultParameters = defaults,
        };
    }

    /// <summary>The normalized heights of one of a tank's port families, in port order.</summary>
    /// <param name="symbol">The bound tank.</param>
    /// <param name="kind">Its registry entry.</param>
    /// <param name="prefix"><c>in</c> or <c>out</c>.</param>
    /// <returns>One height per materialized port of that family, mid-height where none was stated.</returns>
    /// <remarks>
    /// Driven by <see cref="ComponentSymbol.Ports"/> rather than by which levels were stated: the
    /// binder already decided which ports exist, and a port evidenced by a connection has a height
    /// whether or not the script wrote one (<c>D-32</c>).
    /// </remarks>
    private ImmutableArray<double> Levels(
        ComponentSymbol symbol, ComponentKindInfo kind, string prefix)
    {
        var heights = ImmutableArray.CreateBuilder<double>();

        for (var index = 1; ; index++)
        {
            var port = prefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (!symbol.Ports.Contains(port, StringComparer.Ordinal))
            {
                break;
            }

            heights.Add(Value(symbol, kind, $"{port}_level") ?? Components.Tank.DefaultLevel);
        }

        return heights.ToImmutable();
    }
}
