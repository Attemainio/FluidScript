using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Sizing;

namespace FluidScript.Core.Topology.Construction;

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

public sealed partial class ComponentFactory(IBoreLookup bores, SizingOverlay? sizes = null, ISubstance? substance = null) : IComponentFactory
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

    /// <summary>The names whose statement is evidence of a second side (<c>D-19</c>).</summary>
    private static readonly ImmutableArray<string> SecondaryProfile = ["in2", "out2", "dt2", "flow2"];

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
    private ValveComponent? Valve(
        ComponentSymbol symbol,
        ComponentKindInfo kind,
        ImmutableDictionary<string, Quantity> stated,
        ImmutableDictionary<string, Quantity> sized,
        ImmutableDictionary<string, Quantity> defaults) =>
        Value(symbol, kind, "kv") is not { } kv
            ? null
            : new ValveComponent(symbol.Name, kv, Value(symbol, kind, "position") ?? 1, Characteristic(symbol, kind))
            {
                StatedParameters = stated,
                SizedParameters = sized,
                DefaultParameters = defaults,
            };

    /// <summary>The service a three-way valve's spelling declares it for (<c>D-136</c>).</summary>
    /// <param name="writtenKind">The kind exactly as the script wrote it.</param>
    /// <returns><see cref="ValveArrangement.Mixing"/> for <c>mixing_valve</c>, <see cref="ValveArrangement.Diverting"/> for <c>diverting_valve</c>, otherwise no claim.</returns>
    private static ValveArrangement Arrangement(string writtenKind) =>
        string.Equals(writtenKind, "mixing_valve", StringComparison.OrdinalIgnoreCase) ? ValveArrangement.Mixing
        : string.Equals(writtenKind, "diverting_valve", StringComparison.OrdinalIgnoreCase) ? ValveArrangement.Diverting
        : ValveArrangement.Unspecified;

    /// <summary>A three-way valve, or <see langword="null"/> when nothing has chosen its <c>kv</c>.</summary>
    /// <param name="symbol">The bound declaration.</param>
    /// <param name="kind">Its registry entry.</param>
    /// <param name="wiring">How many ports the script connected, and by what names.</param>
    /// <param name="stated">What the script wrote.</param>
    /// <param name="sized">What a rule chose.</param>
    /// <param name="defaults">What the registry decided.</param>
    /// <returns>The component, or <see langword="null"/> to leave it unresolved.</returns>
    /// <remarks><c>C-58</c>, for the same reason as <see cref="Valve"/>.</remarks>
    private ThreeWayValveComponent? ThreeWay(
        ComponentSymbol symbol,
        ComponentKindInfo kind,
        PortWiring wiring,
        ImmutableDictionary<string, Quantity> stated,
        ImmutableDictionary<string, Quantity> sized,
        ImmutableDictionary<string, Quantity> defaults) =>
        Value(symbol, kind, "kv") is not { } kv
            ? null
            : new ThreeWayValveComponent(
                symbol.Name,
                kv,
                Value(symbol, kind, "position") ?? 1,
                Characteristic(symbol, kind),

                // Two connections and no explicit `b` is the two-way arrangement the page describes,
                // and it is one Kv law rather than two (S-14a). `b` is the bypass port, which was spelt
                // `c` before the A/B/AB rename shifted every letter -- so this names a different port
                // than it used to, and it is the one place where getting that backwards would silently
                // turn every three-way valve in the corpus into a two-way.
                bypassConnected: wiring.Connections > 2 || wiring.Names("b"),
                leakage: Value(symbol, kind, "leakage") ?? ValveLaw.LegLeakage,
                arrangement: Arrangement(symbol.WrittenKind))
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

    private PipeComponent? Pipe(
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

        return new PipeComponent(
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

    private PumpComponent Pump(
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
            ? Components.PumpComponent.CurveThrough(metres, duty)
            : (head ?? 0, 0d);

        return new PumpComponent(symbol.Name, shutOff, curvature, efficiency: efficiency)
        {
            StatedRise = rise,
            StatedParameters = stated,
            SizedParameters = sized,
            DefaultParameters = defaults,
        };
    }

    private TankComponent Tank(
        ComponentSymbol symbol,
        ComponentKindInfo kind,
        ImmutableDictionary<string, Quantity> stated,
        ImmutableDictionary<string, Quantity> sized,
        ImmutableDictionary<string, Quantity> defaults)
    {
        var layers = Value(symbol, kind, "layers") ?? Components.TankComponent.DefaultLayers;

        return new TankComponent(
            symbol.Name,
            Levels(symbol, kind, "in"),
            Levels(symbol, kind, "out"),
            Value(symbol, kind, "volume") ?? Components.TankComponent.DefaultVolume,
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

            heights.Add(Value(symbol, kind, $"{port}_level") ?? Components.TankComponent.DefaultLevel);
        }

        return heights.ToImmutable();
    }
}
