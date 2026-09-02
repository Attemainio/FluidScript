using System.Collections.Immutable;

using FluidScript.Core.Binding;

namespace FluidScript.Core.Language;

/// <summary>The circuit roles a header name can resolve to (<c>D-35</c>).</summary>
/// <remarks>
/// <para>
/// <c>circuit AHU 101</c> classifies the circuit by role, and the name resolves through the same three
/// stages a component kind does — normalise, exact match against canonical names and curated aliases,
/// then similarity. Reusing <c>D-15</c>'s stages rather than writing a second matcher is deliberate:
/// two similarity implementations drift, and a user who learns that <c>3WayValve</c> finds
/// <c>three_way_valve</c> reasonably expects <c>AirHandlingUnit</c> to find <c>ahu</c>.
/// </para>
/// <para>
/// <strong>An unresolved name is not an error.</strong> The circuit gets
/// <see cref="ThermalStageRole.Neutral"/> and <c>FS1519</c>, exactly as an unresolved component kind
/// still produces a component. A plant is full of circuits whose function has no registry entry.
/// </para>
/// <para>
/// <strong>The entries below are implementation-defined.</strong> <c>D-35</c> names <c>AHU</c>,
/// <c>radiator</c>, <c>hot_water</c> and <c>ground_loop</c> as examples and no document enumerates the
/// set or its stage mapping; this is the v1 list, recorded in <c>10-language/defects.md</c> as a gap
/// filled here. A role is a hint: <c>25</c>'s staging overrules it when the solved duty sign
/// disagrees, which is what keeps a wrong guess here cheap.
/// </para>
/// </remarks>
public static class CircuitRoleRegistry
{
    /// <summary>The role a name that matches nothing resolves to.</summary>
    public static CircuitRole Neutral { get; } = new("neutral", ThermalStageRole.Neutral);

    /// <summary>Gets every registered role, in canonical name order.</summary>
    public static ImmutableArray<CircuitRole> All { get; } =
    [
        new("ahu", ThermalStageRole.Consumer),
        new("cooling", ThermalStageRole.Neutral),
        new("district", ThermalStageRole.Source),
        new("distribution", ThermalStageRole.Neutral),
        new("ground_loop", ThermalStageRole.Source),
        new("heat_pump", ThermalStageRole.Conversion),
        new("heating", ThermalStageRole.Neutral),
        new("hot_water", ThermalStageRole.Consumer),
        new("radiator", ThermalStageRole.Consumer),
        new("solar", ThermalStageRole.Source),
        new("storage", ThermalStageRole.Storage),
        new("underfloor", ThermalStageRole.Consumer),
    ];

    private static readonly ImmutableDictionary<string, CircuitRole> Index = BuildIndex();

    /// <summary>Resolves a circuit's name to a role.</summary>
    /// <param name="written">The header name, as written.</param>
    /// <returns>
    /// The role, whether it was a near miss worth reporting, and the canonical name it matched. A name
    /// matching nothing resolves to <see cref="Neutral"/> with <c>WasResolved</c> false.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="written"/> is <see langword="null"/>.</exception>
    public static RoleResolution Resolve(string written)
    {
        ArgumentNullException.ThrowIfNull(written);

        var match = NameResolution.Match(written, Index);

        if (match.Best is null)
        {
            return new RoleResolution(Neutral, WasResolved: false, BySimilarity: false);
        }

        if (match.IsExact)
        {
            return new RoleResolution(match.Best, WasResolved: true, BySimilarity: false);
        }

        // The ambiguity margin is not applied here, and that is the difference from kind resolution: a
        // role is a placement hint that physics overrules, so the cost of guessing between two is a
        // component drawn one column left. A kind picked wrongly is a different circuit.
        return match.BestScore >= NameResolution.ResolveThreshold
            ? new RoleResolution(match.Best, WasResolved: true, BySimilarity: true)
            : new RoleResolution(Neutral, WasResolved: false, BySimilarity: false);
    }

    /// <summary>Gets the canonical names, for a diagnostic that lists what is known.</summary>
    /// <returns>Every registered role's canonical name, in order.</returns>
    public static string Names() => string.Join(", ", All.Select(static role => role.CanonicalName));

    private static ImmutableDictionary<string, CircuitRole> BuildIndex()
    {
        var aliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ahu"] = ["air_handling_unit", "ventilation", "air_handler"],
            ["cooling"] = ["chilled_water", "cooling_circuit"],
            ["district"] = ["district_heating", "district_loop"],
            ["distribution"] = ["primary", "header", "distribution_header"],
            ["ground_loop"] = ["ground_source", "borehole", "brine"],
            ["heat_pump"] = ["heatpump", "hp"],
            ["heating"] = ["heating_circuit", "secondary"],
            ["hot_water"] = ["dhw", "domestic_hot_water", "tap_water"],
            ["radiator"] = ["radiators", "radiator_circuit"],
            ["solar"] = ["solar_collector", "solar_loop"],
            ["storage"] = ["buffer", "accumulator", "storage_circuit"],
            ["underfloor"] = ["floor_heating", "ufh", "underfloor_heating"],
        };

        var builder = ImmutableDictionary.CreateBuilder<string, CircuitRole>(StringComparer.Ordinal);

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

/// <summary>What a circuit name resolved to.</summary>
/// <param name="Role">The role, or the neutral one.</param>
/// <param name="WasResolved">Whether the name matched a registered role at all.</param>
/// <param name="BySimilarity">Whether it matched by similarity rather than exactly.</param>
public readonly record struct RoleResolution(CircuitRole Role, bool WasResolved, bool BySimilarity);
