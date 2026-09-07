using System.Collections.Immutable;

using FluidScript.Core.Binding;
using FluidScript.Core.Components;
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
    double? BoreFor(double nominalDiameter);
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
public sealed class ComponentFactory(IBoreLookup bores, SizingOverlay? sizes = null) : IComponentFactory
{
    private readonly SizingOverlay _sizes = sizes ?? SizingOverlay.Empty;

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

        // `S-14b`. Side 2's row exists whenever the script wired it, and its *resistance* only when a
        // script also states `flow2` -- no rule chooses that, because a rule sees one branch and this
        // component sits on two. An ideal side still carries `p_in2 = p_out2`, which is the row the
        // counting table was crediting and the component was not declaring.
        var secondary = wiring.Names("in2") || wiring.Names("out2");
        var secondaryFlow = Value(symbol, kind, "flow2") ?? 0;
        var secondaryDrop = secondaryFlow > 0 ? Value(symbol, kind, "dp2") ?? 0 : 0;

        return new HeatExchanger(
            symbol.Name,
            Value(symbol, kind, "power") ?? 0,
            drop,
            design,
            secondaryDrop,
            secondaryFlow,
            secondary)
        {
            StatedParameters = stated,
            SizedParameters = sized,
            DefaultParameters = defaults,
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
            : new Valve(symbol.Name, kv, Value(symbol, kind, "position") ?? 1, Characteristic(symbol))
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
                Characteristic(symbol),

                // Two connections and no explicit `c` is the two-way arrangement the page describes,
                // and it is one Kv law rather than two (S-14a).
                bypassConnected: wiring.Connections > 2 || wiring.Names("c"))
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

    private static ValveCharacteristic Characteristic(ComponentSymbol symbol) =>
        symbol.Parameters.TryGetValue("characteristic", out var stated) && stated.Symbol is { } name
            ? name switch
            {
                "linear" => ValveCharacteristic.Linear,
                "quick_open" => ValveCharacteristic.QuickOpen,
                _ => ValveCharacteristic.EqualPercentage,
            }
            : ValveCharacteristic.EqualPercentage;

    private Pipe? Pipe(
        ComponentSymbol symbol,
        ImmutableDictionary<string, Quantity> stated,
        ImmutableDictionary<string, Quantity> sized,
        ImmutableDictionary<string, Quantity> defaults)
    {
        var kind = symbol.Kind!;
        var length = Value(symbol, kind, "length");
        var nominal = Value(symbol, kind, "dn");

        if (length is not { } metres || nominal is not { } dn || bores.BoreFor(dn) is not { } bore)
        {
            return null;
        }

        return new Pipe(
            symbol.Name,
            metres,
            bore,
            Value(symbol, kind, "roughness") ?? 0.045e-3,
            Value(symbol, kind, "minor_loss") ?? 0,
            Value(symbol, kind, "elevation") ?? 0)
        {
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
        var flow = Value(symbol, kind, "flow");
        var efficiency = Value(symbol, kind, "efficiency") ?? 0.7;

        // A duty point gives the default quadratic its curvature; a head with no flow beside it is a
        // shut-off head and nothing more, which is the flat curve a pump with one stated number has.
        var (shutOff, curvature) = head is { } metres && flow is { } duty and > 0
            ? Components.Pump.CurveThrough(metres, duty)
            : (head ?? 0, 0d);

        return new Pump(symbol.Name, shutOff, curvature, efficiency: efficiency)
        {
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
            Elevations(symbol, kind, "in"),
            Elevations(symbol, kind, "out"),
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
    /// Driven by <see cref="ComponentSymbol.Ports"/> rather than by which elevations were stated: the
    /// binder already decided which ports exist, and a port evidenced by a connection has a height
    /// whether or not the script wrote one (<c>D-32</c>).
    /// </remarks>
    private ImmutableArray<double> Elevations(
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

            heights.Add(Value(symbol, kind, $"{port}_elevation") ?? Components.Tank.DefaultElevation);
        }

        return heights.ToImmutable();
    }
}
