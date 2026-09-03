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
    /// <returns>
    /// The component, or <see langword="null"/> when it cannot be built from what is known yet — a
    /// pipe whose bore no catalogue has resolved, most often. Null is a normal result and never an
    /// exception: a script under editing is malformed most of the time.
    /// </returns>
    IFlowComponent? Create(ComponentSymbol symbol);
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
public sealed class ComponentFactory(IBoreLookup bores) : IComponentFactory
{
    /// <inheritdoc/>
    public IFlowComponent? Create(ComponentSymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        if (symbol.Kind is not { } kind)
        {
            return null;
        }

        return kind.Keyword switch
        {
            "pipe" => Pipe(symbol),
            "valve" => new Valve(
                symbol.Name,
                Value(symbol, kind, "kv") ?? 1,
                Value(symbol, kind, "position") ?? 1,
                Characteristic(symbol)),
            "three_way_valve" => new ThreeWayValve(
                symbol.Name,
                Value(symbol, kind, "kv") ?? 1,
                Value(symbol, kind, "position") ?? 1,
                Characteristic(symbol)),
            "pump" => Pump(symbol, kind),
            "heat_exchanger" => new HeatExchanger(symbol.Name, Value(symbol, kind, "power") ?? 0),
            "tank" => Tank(symbol, kind),
            _ => null,
        };
    }

    /// <summary>The SI value of a parameter, from the script or from the kind's visible default.</summary>
    /// <param name="symbol">The bound component.</param>
    /// <param name="kind">Its registry entry.</param>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <returns>
    /// The value in SI, or <see langword="null"/> when the script stated none and the kind declares no
    /// default — which is a parameter sizing chooses, and sizing has not run.
    /// </returns>
    private static double? Value(ComponentSymbol symbol, ComponentKindInfo kind, string parameter)
    {
        if (symbol.Parameters.TryGetValue(parameter, out var stated) && stated.Value is { } quantity)
        {
            return quantity.SiValue;
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

    private Pipe? Pipe(ComponentSymbol symbol)
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
            Value(symbol, kind, "elevation") ?? 0);
    }

    private static Pump Pump(ComponentSymbol symbol, ComponentKindInfo kind)
    {
        var head = Value(symbol, kind, "head");
        var flow = Value(symbol, kind, "flow");
        var efficiency = Value(symbol, kind, "efficiency") ?? 0.7;

        // A duty point gives the default quadratic its curvature; a head with no flow beside it is a
        // shut-off head and nothing more, which is the flat curve a pump with one stated number has.
        return head is { } metres && flow is { } duty and > 0
            ? Components.Pump.FromDutyPoint(symbol.Name, metres, duty, efficiency: efficiency)
            : new Pump(symbol.Name, head ?? 0, curvature: 0, efficiency: efficiency);
    }

    private static Tank Tank(ComponentSymbol symbol, ComponentKindInfo kind)
    {
        var layers = Value(symbol, kind, "layers") ?? Components.Tank.DefaultLayers;

        return new Tank(
            symbol.Name,
            Elevations(symbol, kind, "in"),
            Elevations(symbol, kind, "out"),
            Value(symbol, kind, "volume") ?? Components.Tank.DefaultVolume,
            (int)layers);
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
    private static ImmutableArray<double> Elevations(
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
