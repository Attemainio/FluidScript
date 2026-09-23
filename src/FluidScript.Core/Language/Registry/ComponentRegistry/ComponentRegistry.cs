using System.Collections.Immutable;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Registry;

/// <summary>The v1 component kinds, as data.</summary>
/// <remarks>
/// <para>
/// Metadata only: keywords, aliases, ports, parameters, properties and tag codes. No physics, no
/// residuals, and no reference to <c>FluidScript.Core.Components</c> — the binder needs shape, and the
/// class implementing a kind is resolved at lowering. That separation is why this can exist, and be
/// read by the documentation gate, before a single component is written.
/// </para>
/// <para>
/// The tables here are the same tables as
/// <c>plan/20-core-domain/22-component-model.md</c>'s, and a test compares the two: without it they
/// diverge on the first component change, and the divergence is invisible until a user writes a
/// parameter the documentation promises and the binder rejects.
/// </para>
/// </remarks>
public sealed partial class ComponentRegistry : IComponentRegistry
{
    /// <summary>Gets the registry every stage shares.</summary>
    public static ComponentRegistry Default { get; } = new();

    private readonly ImmutableDictionary<string, ComponentKindInfo> _index;

    /// <summary>Builds a registry over the v1 kinds.</summary>
    /// <exception cref="InvalidOperationException">
    /// The kind data breaks one of the registry's own rules — a normalised spelling claimed by two
    /// kinds, an alias equal to a reserved word, a duplicated tag code, or a tag code that would make
    /// an equipment tag lex as a quantity.
    /// </exception>
    public ComponentRegistry()
    {
        Kinds = BuildKinds();
        _index = BuildIndex(Kinds);

        Verify(Kinds, _index);
    }

    /// <inheritdoc/>
    public ImmutableArray<ComponentKindInfo> Kinds { get; }

    /// <inheritdoc/>
    public KindResolution Resolve(string writtenKind)
    {
        ArgumentNullException.ThrowIfNull(writtenKind);

        var match = NameResolution.Match(writtenKind, _index);

        if (match.Best is null)
        {
            return new KindResolution.Unknown(null);
        }

        if (match.IsExact)
        {
            return new KindResolution.Exact(match.Best);
        }

        if (match.BestScore < NameResolution.ResolveThreshold)
        {
            return new KindResolution.Unknown(
                match.BestScore >= NameResolution.SuggestionFloor ? match.Best.Keyword : null);
        }

        // Above the threshold but not clear of the runner-up: two kinds a keystroke apart is a
        // question with a one-word answer, and guessing it is a silently wrong circuit.
        return match.IsClear
            ? new KindResolution.Similar(match.Best, match.BestScore)
            : new KindResolution.Ambiguous([match.Best, match.RunnerUp!]);
    }

    /// <summary>Finds a kind by its canonical keyword.</summary>
    /// <param name="keyword">The canonical keyword.</param>
    /// <returns>The kind, or <see langword="null"/> when no kind has that keyword.</returns>
    public ComponentKindInfo? ByKeyword(string keyword) =>
        Kinds.FirstOrDefault(kind => string.Equals(kind.Keyword, keyword, StringComparison.Ordinal));

    private static ImmutableDictionary<string, ComponentKindInfo> BuildIndex(
        ImmutableArray<ComponentKindInfo> kinds)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, ComponentKindInfo>(StringComparer.Ordinal);

        foreach (var kind in kinds)
        {
            builder[NameResolution.Normalize(kind.Keyword)] = kind;

            foreach (var alias in kind.Aliases)
            {
                builder[NameResolution.Normalize(alias)] = kind;
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<ComponentKindInfo> BuildKinds() =>
    [
        Node(),
        Inlet(),
        Outlet(),
        WithPortPressures(Pipe()),
        WithPortPressures(HeatExchanger()),
        WithPortPressures(Valve()),
        WithPortPressures(ThreeWayValve()),
        WithPortPressures(Pump()),
        WithPortPressures(Tank()),
        Controller(),
        Sensor("t_sensor", ["temperature_sensor", "te"], "TE", "t", Dimension.Temperature),
        Sensor("p_sensor", ["pressure_sensor", "pe"], "PE", "p", Dimension.Pressure),
        Sensor("flow_sensor", ["flow_meter", "fe"], "FE", "flow", Dimension.MassFlow),
    ];

    /// <summary>Gives every port of a kind a pressure, stated as <c>port.p</c> and read as <c>Name.port.p</c> (<c>D-124</c>).</summary>
    /// <param name="kind">A kind with named ports.</param>
    /// <returns>The kind with one parameter and one property per port, and one family per port family.</returns>
    /// <remarks>
    /// <para>
    /// <strong>A port's pressure is the pressure of the node the port touches, and stating it states
    /// that node.</strong> <c>V1 valve out.p=100</c> is <c>N1 node p=100</c> on whatever node
    /// <c>V1.out</c> is wired to -- named, or the one I2 inserted -- and nothing else; the binder
    /// copies it there (<c>D-120</c> rule 3, decided as <c>D-124</c>). A pressure is never a
    /// component's own: a component has a drop, and its two ports' pressures are two nodes'.
    /// </para>
    /// <para>
    /// Generated rather than listed per kind because the rule has no per-kind content: every port
    /// has one, the key is <c>p_</c> and the port's key (<c>p_in</c>, <c>p_out2</c>, <c>p_ab</c>,
    /// <c>p_in3</c>), and <c>22</c> documents it once rather than in every table. Omission is
    /// <see cref="ParameterOmissionBehavior.Size"/> for the reason a node's own <c>p</c> is: the solve
    /// finds it. The property is the same node's solved pressure, which the wire already carries as
    /// <c>pIn</c>/<c>pOut</c>.
    /// </para>
    /// </remarks>
    private static ComponentKindInfo WithPortPressures(ComponentKindInfo kind)
    {
        var parameters = kind.Parameters.ToBuilder();
        var properties = kind.Properties.ToBuilder();

        foreach (var port in kind.Ports)
        {
            var row = Sized(port.Name + ".p", Dimension.Pressure, 0, 2500, precision: 1) with { Key = "p_" + port.Key };
            parameters[row.Key] = row;
            properties[row.Name] = Solved(port.Name + ".p", Dimension.Pressure) with { Key = "p_" + port.Key };
        }

        var parameterFamilies = kind.IndexedParameterFamilies.ToBuilder();
        var propertyFamilies = kind.IndexedPropertyFamilies.ToBuilder();

        foreach (var family in kind.PortFamilies)
        {
            parameterFamilies.Add(new IndexedParameterFamilyInfo
            {
                Pattern = family.Prefix + "[{index}].p",
                KeyPattern = "p_" + family.Prefix + "{index}",
                MinIndex = family.MinIndex,
                MaxIndex = family.MaxIndex,
                Element = Sized("p", Dimension.Pressure, 0, 2500, precision: 1),
            });
            propertyFamilies.Add(new IndexedPropertyFamilyInfo
            {
                Pattern = family.Prefix + "[{index}].p",
                KeyPattern = "p_" + family.Prefix + "{index}",
                MinIndex = family.MinIndex,
                MaxIndex = family.MaxIndex,
                Element = Solved("p", Dimension.Pressure),
            });
        }

        return kind with
        {
            Parameters = parameters.ToImmutable(),
            Properties = properties.ToImmutable(),
            IndexedParameterFamilies = parameterFamilies.ToImmutable(),
            IndexedPropertyFamilies = propertyFamilies.ToImmutable(),
        };
    }

    /// <summary>Whether a parameter key is a port's pressure: <c>p_in</c>, <c>p_out2</c>, <c>p_in3</c> (<c>D-124</c>).</summary>
    /// <param name="key">The parameter key.</param>
    /// <param name="port">The port's key when it is, else <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for a port pressure.</returns>
    public static bool IsPortPressure(string key, out string? port)
    {
        ArgumentNullException.ThrowIfNull(key);
        port = key.StartsWith("p_", StringComparison.Ordinal) && key.Length > 2 ? key[2..] : null;
        return port is not null;
    }

    /// <summary>The highest index either of a tank's port families materializes.</summary>
    private const int TankPorts = 16;
}
