using System.Collections.Immutable;

using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Registry;

/// <summary>The quantities of a fluid state the language names, by symbol and by name (<c>D-120</c>).</summary>
/// <remarks>
/// <para>
/// One table for three readers: a port's state on a declaration (<c>in[2].t=85</c>), a reference to
/// it (<c>HX1.in[2].t</c>), and the <c>show</c> directive (<c>show t</c>, <c>show temperature</c>).
/// Before <c>D-120</c> the contract builder kept its own copy for <c>show</c> and the registry rows
/// spelled the quantities by hand, which is the drift <c>L-50</c> recorded. The symbol is the
/// canonical spelling in a port's state, since the language trades on density; the name is what the
/// wire and a scale carry, since a reader of JSON does not know <c>rho</c>.
/// </para>
/// <para>
/// The symbols are lowercase and the table is case-sensitive, because the language is:
/// <c>dK</c>/<c>dC</c> already depend on it.
/// </para>
/// </remarks>
public static class PropertyTable
{
    /// <summary>Every quantity the table names, in the order <c>57</c> lists them.</summary>
    public static ImmutableArray<PropertyEntry> All { get; } =
    [
        new("t", "temperature", "Temperature", Dimension.Temperature, Diverging: false, ["temp"]),
        new("p", "pressure", "Pressure", Dimension.Pressure, Diverging: false, []),
        new("flow", "flow", "Mass flow", Dimension.MassFlow, Diverging: false, ["mflow", "mdot", "mass_flow"]),
        new("vflow", "volume_flow", "Volume flow", Dimension.VolumeFlow, Diverging: false, ["q"]),
        new("h", "enthalpy", "Enthalpy", Dimension.Enthalpy, Diverging: false, []),
        new("rho", "density", "Density", Dimension.Density, Diverging: false, []),
        new("cp", "specific_heat", "Specific heat", Dimension.SpecificHeat, Diverging: false, []),

        // `D-123`: a `d` prefix on a state symbol is that quantity's change across the component it is
        // read on. Each delta names its base and its direction word -- a pressure *drops* (in − out,
        // positive across a resistance, the datasheet's number), a temperature or an enthalpy *rises*
        // (out − in, positive across a heater) -- because no single sign rule says both.
        new("dp", "pressure_drop", "Pressure drop", Dimension.PressureDelta, Diverging: true, [], Of: "p", Drop: true),
        new("dt", "temperature_change", "Temperature change", Dimension.TemperatureDelta, Diverging: true, [], Of: "t", Drop: false),
        new("dh", "enthalpy_change", "Enthalpy change", Dimension.Enthalpy, Diverging: true, [], Of: "h", Drop: false),
    ];

    /// <summary>The state quantities: everything that is not a change of something else.</summary>
    public static IEnumerable<PropertyEntry> States => All.Where(static entry => entry.Of is null);

    /// <summary>The changes: every <c>d</c>-prefixed row, with the quantity it is a change of.</summary>
    public static IEnumerable<PropertyEntry> Deltas => All.Where(static entry => entry.Of is not null);

    private static readonly ImmutableDictionary<string, PropertyEntry> BySpelling = Index();

    /// <summary>Finds the quantity a spelling names.</summary>
    /// <param name="written">A symbol, a name, or an alias: <c>t</c>, <c>temperature</c>, <c>mdot</c>.</param>
    /// <returns>The entry, or <see langword="null"/> when the table has no such quantity.</returns>
    public static PropertyEntry? Find(string written)
    {
        ArgumentNullException.ThrowIfNull(written);

        return BySpelling.GetValueOrDefault(written);
    }

    /// <summary>Rewrites the quantity steps of a dotted name to their symbols: <c>in[2].temperature</c> reads as <c>in[2].t</c>.</summary>
    /// <param name="written">A parameter or property name as the script wrote it.</param>
    /// <returns>
    /// The same name with every step after the first, and a lone step that is a known quantity,
    /// spelled by its symbol; unchanged when nothing in it is a quantity.
    /// </returns>
    /// <remarks>
    /// The first step of a dotted name is a port and is left alone; a name with no dot is a bare
    /// quantity only when the table knows it, so <c>power</c> and <c>layers</c> pass through.
    /// </remarks>
    public static string Canonical(string written)
    {
        ArgumentNullException.ThrowIfNull(written);

        var dot = written.IndexOf('.', StringComparison.Ordinal);

        if (dot < 0)
        {
            return Find(written) is { } bare ? bare.Symbol : written;
        }

        var steps = written.Split('.');

        for (var i = 1; i < steps.Length; i++)
        {
            if (Find(steps[i]) is { } quantity)
            {
                steps[i] = quantity.Symbol;
            }
        }

        return string.Join('.', steps);
    }

    private static ImmutableDictionary<string, PropertyEntry> Index()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, PropertyEntry>(StringComparer.Ordinal);

        foreach (var entry in All)
        {
            builder[entry.Symbol] = entry;
            builder[entry.Name] = entry;

            foreach (var alias in entry.Aliases)
            {
                builder[alias] = entry;
            }
        }

        return builder.ToImmutable();
    }
}
