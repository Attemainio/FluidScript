using System.Collections.Immutable;

using FluidScript.Core.Binding;
using FluidScript.Core.Units;

namespace FluidScript.Core.Language;

/// <summary>The drivers a curve can depend on by name (<c>D-59</c>).</summary>
/// <remarks>
/// <para>
/// <c>tout</c> means the outdoor temperature. It is not a reserved word and not a special case in the
/// binder: it is an entry here, resolved by <c>D-15</c>'s three stages — normalise, exact match
/// against canonical names and curated aliases, then similarity — exactly as a circuit's name
/// resolves to a role through <see cref="CircuitRoleRegistry"/>. That buys <c>tout</c>, <c>t_out</c>,
/// <c>outdoor</c> and <c>outdoorTemperature</c> as one driver for free, and keeps the set extensible
/// without touching the grammar.
/// </para>
/// <para>
/// <strong>A role's dimension is the only place a curve meets one.</strong> The table itself is bare
/// (<c>D-57</c>): <c>heating</c> maps −26 to 50, and what 50 <em>is</em> comes from the parameter it
/// is assigned to. What the role adds is a check on the <em>design</em> value — <c>design tout=-26</c>
/// and <c>design tout=-26 C</c> agree, and <c>design tout=3 bar</c> is caught. A role with no
/// dimension takes its design value bare and checks nothing.
/// </para>
/// <para>
/// <strong>The entries below are implementation-defined.</strong> <c>D-59</c> names <c>tout</c> and
/// nothing else, and no document enumerates the set; this is the v1 list, recorded in
/// <c>plan/10-language/defects.md</c> as a gap filled here. Unlike a circuit role, an unregistered
/// name is not silently neutral: a driver has to supply a number, so one that names no curve, no role
/// and no <c>design</c> entry is <c>FS1527</c>.
/// </para>
/// </remarks>
public static class ScheduleRoleRegistry
{
    /// <summary>Gets every registered driver, in canonical name order.</summary>
    /// <value>
    /// Seven roles. The dimension is the one a <c>design</c> value for that driver is read in, and
    /// <see langword="null"/> where the language has no dimension for the quantity — solar irradiance
    /// is W/m², which <c>13</c>'s closed set does not name.
    /// </value>
    public static ImmutableArray<ScheduleRole> All { get; } =
    [
        new("demand", Dimension.Power),
        new("humidity", Dimension.Dimensionless),
        new("solar", null),
        new("tground", Dimension.Temperature),
        new("tout", Dimension.Temperature),
        new("troom", Dimension.Temperature),
        new("wind", Dimension.Velocity),
    ];

    private static readonly ImmutableDictionary<string, ScheduleRole> Index = BuildIndex();

    /// <summary>Resolves a driver name to a role.</summary>
    /// <param name="written">The name in the curve header, as written.</param>
    /// <returns>The role, or <see langword="null"/> when the name matches none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="written"/> is <see langword="null"/>.</exception>
    public static ScheduleRole? Resolve(string written)
    {
        ArgumentNullException.ThrowIfNull(written);

        var match = NameResolution.Match(written, Index);

        if (match.Best is null)
        {
            return null;
        }

        // The ambiguity margin is not applied, for the same reason it is not applied to a circuit
        // role: the cost of choosing between two near matches is a design value read under the wrong
        // name, which the dimension check then catches. A kind picked wrongly is a different plant.
        return match.IsExact || match.BestScore >= NameResolution.ResolveThreshold ? match.Best : null;
    }

    /// <summary>Gets the canonical names, for a diagnostic that lists what is known.</summary>
    /// <returns>Every registered driver's canonical name, in order.</returns>
    public static string Names() => string.Join(", ", All.Select(static role => role.CanonicalName));

    private static ImmutableDictionary<string, ScheduleRole> BuildIndex()
    {
        var aliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["demand"] = ["heat_demand", "heating_demand", "load"],
            ["humidity"] = ["rh", "relative_humidity", "outdoor_humidity"],
            ["solar"] = ["irradiance", "solar_radiation", "insolation"],
            ["tground"] = ["t_ground", "ground_temperature", "brine_temperature", "soil_temperature"],
            ["tout"] = ["t_out", "outdoor", "outdoor_temperature", "outside_temperature", "oat"],
            ["troom"] = ["t_room", "room_temperature", "indoor_temperature", "zone_temperature"],
            ["wind"] = ["wind_speed", "windspeed"],
        };

        var builder = ImmutableDictionary.CreateBuilder<string, ScheduleRole>(StringComparer.Ordinal);

        foreach (var role in All)
        {
            builder[NameResolution.Normalize(role.CanonicalName)] = role;

            foreach (var alias in aliases[role.CanonicalName])
            {
                builder[NameResolution.Normalize(alias)] = role;
            }
        }

        return builder.ToImmutable();
    }
}
