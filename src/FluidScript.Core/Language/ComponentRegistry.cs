using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Syntax;
using FluidScript.Core.Units;

namespace FluidScript.Core.Language;

/// <summary>What a written kind name resolved to.</summary>
public abstract record KindResolution
{
    private KindResolution()
    {
    }

    /// <summary>The name is a spelling the registry knows.</summary>
    /// <param name="Kind">The kind it names.</param>
    /// <remarks>Resolves silently: a known spelling is not a guess, so there is nothing to report.</remarks>
    public sealed record Exact(ComponentKindInfo Kind) : KindResolution;

    /// <summary>The name is close enough to one kind to resolve, and to nothing else.</summary>
    /// <param name="Kind">The kind it resolved to.</param>
    /// <param name="Score">How close, on <see cref="NameResolution.Score"/>'s scale.</param>
    /// <remarks>Always reported (<c>FS1512</c>, info): a resolution the user cannot see is magic.</remarks>
    public sealed record Similar(ComponentKindInfo Kind, double Score) : KindResolution;

    /// <summary>The name is close to more than one kind, and no closer to either.</summary>
    /// <param name="Candidates">The candidates, best first.</param>
    public sealed record Ambiguous(ImmutableArray<ComponentKindInfo> Candidates) : KindResolution;

    /// <summary>The name matches nothing well enough to act on.</summary>
    /// <param name="SuggestedKeyword">
    /// The closest canonical keyword, or <see langword="null"/> when nothing came within
    /// <see cref="NameResolution.SuggestionFloor"/> and a suggestion would be a guess dressed as help.
    /// </param>
    public sealed record Unknown(string? SuggestedKeyword) : KindResolution;
}

/// <summary>What the binder asks about component kinds.</summary>
public interface IComponentRegistry
{
    /// <summary>Gets every registered kind, in canonical keyword order.</summary>
    ImmutableArray<ComponentKindInfo> Kinds { get; }

    /// <summary>Resolves a kind name as the user wrote it.</summary>
    /// <param name="writtenKind">The name in <c>kind-name</c> position.</param>
    /// <returns>What it resolved to, which is never an exception and never a fabricated kind.</returns>
    KindResolution Resolve(string writtenKind);
}

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
public sealed class ComponentRegistry : IComponentRegistry
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

    // Everything asserted here is a rule the data can break silently. A duplicated normalised spelling
    // would make one kind unreachable depending on registration order; an alias equal to a reserved
    // word would be unwriteable, because a reserved word never reaches kind position (`D-40` did
    // exactly this to `control`); a tag code that lexes as a unit would produce equipment tags the
    // language reads as numbers.
    private static void Verify(
        ImmutableArray<ComponentKindInfo> kinds,
        ImmutableDictionary<string, ComponentKindInfo> index)
    {
        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var kind in kinds)
            foreach (var spelling in new[] { kind.Keyword }.Concat(kind.Aliases))
            {
                var normalized = NameResolution.Normalize(spelling);

                if (claimed.TryGetValue(normalized, out var owner) && owner != kind.Keyword)
                {
                    throw new InvalidOperationException(
                        $"'{spelling}' resolves to both '{owner}' and '{kind.Keyword}'.");
                }

                claimed[normalized] = kind.Keyword;

                // A kind's own keyword MAY be a reserved word. It could not be until `D-64` made
                // `S1 supply t=5` a declaration: statement classification reads the *first* token, so a
                // reserved word in kind position is unambiguous, and `supply N3` still attaches a
                // subcircuit because that line starts with the keyword.
                //
                // An alias may not, and the difference is worth keeping. An alias is a convenience
                // spelling, so one that collides with a reserved word buys a second way to write
                // something already writable and costs a reader the question of which they are looking
                // at. Only a kind the decision log sanctions should be reachable by a reserved word.
                if (!string.Equals(spelling, kind.Keyword, StringComparison.Ordinal)
                    && ReservedWords.TryMatch(spelling, out _))
                {
                    throw new InvalidOperationException(
                        $"'{spelling}' is a reserved word, so it may not be an alias for '{kind.Keyword}'.");
                }
            }

        var codes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var kind in kinds.Where(static kind => kind.TagCode is not null))
        {
            var code = kind.TagCode!;

            if (codes.TryGetValue(code, out var owner))
            {
                throw new InvalidOperationException($"'{owner}' and '{kind.Keyword}' share tag code '{code}'.");
            }

            codes[code] = kind.Keyword;

            // The tag itself, lexed. Checking against the unit table would miss the real failure: it
            // is the whole tag that must not read as a number and a unit, not the code alone.
            var tag = $"100{code}01";
            var tokens = Lexer.Lex(new SourceText(tag)).Tokens;

            if (tokens[0].Kind != TokenKind.Identifier || tokens[0].Text != tag)
            {
                throw new InvalidOperationException(
                    $"Tag code '{code}' makes '{tag}' lex as {tokens[0].Kind}, not one identifier.");
            }
        }

        // A marker naming a parameter or property the kind does not have would make the short control
        // form resolve to nothing at bind time, with a message about a name the registry itself
        // invented. Asserted here, where the fix is one row away.
        foreach (var kind in kinds)
        {
            if (kind.ActuatedParameter is { } actuated && !kind.Parameters.ContainsKey(actuated))
            {
                throw new InvalidOperationException(
                    $"'{kind.Keyword}' actuates '{actuated}', which is not one of its parameters.");
            }

            if (kind.MeasuredProperty is { } measured && !kind.Properties.ContainsKey(measured))
            {
                throw new InvalidOperationException(
                    $"'{kind.Keyword}' measures '{measured}', which is not one of its properties.");
            }

            if (kind.IsObserver && !kind.Ports.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"'{kind.Keyword}' observes a node, so it may carry no ports.");
            }

            // A group naming a parameter the kind does not have would never fill up, so the code it
            // carries could not fire and nothing would say why. A group with as many freedoms as
            // members is the same failure spelled differently.
            foreach (var group in kind.ParameterGroups)
            {
                foreach (var parameter in group.Parameters)
                {
                    // Groups name keys, since they are read against what a component stated.
                    if (!kind.Parameters.Values.Any(row => string.Equals(row.Key, parameter, StringComparison.Ordinal)))
                    {
                        throw new InvalidOperationException(
                            $"'{kind.Keyword}' groups '{parameter}', which is not one of its parameters.");
                    }
                }

                if (group.Freedoms < 1 || group.Freedoms >= group.Parameters.Length)
                {
                    throw new InvalidOperationException(
                        $"'{kind.Keyword}' has a group of {group.Parameters.Length} with "
                        + $"{group.Freedoms} freedoms, which can never be over-determined.");
                }

                // A lower bound with no code to raise would reject a script and say nothing, and one
                // above the freedoms would reject every script including the ones it is there to allow.
                if (group.Minimum > 0 && group.MinimumDescriptor is null)
                {
                    throw new InvalidOperationException(
                        $"'{kind.Keyword}' has a group with a minimum of {group.Minimum} and no code "
                        + "to raise when it is not met.");
                }

                if (group.Minimum < 0 || group.Minimum > group.Freedoms)
                {
                    throw new InvalidOperationException(
                        $"'{kind.Keyword}' has a group needing at least {group.Minimum} of its members "
                        + $"stated and at most {group.Freedoms}, which nothing can satisfy.");
                }
            }

            // A family whose pattern carries no placeholder matches nothing, and one whose bound names
            // a parameter the kind lacks has no maximum at all. Both leave a name the registry
            // advertises and no reference can reach.
            foreach (var (pattern, bound) in Families(kind))
            {
                if (!pattern.Contains("{index}", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"'{kind.Keyword}' has an indexed family '{pattern}' with no '{{index}}' in it.");
                }

                if (bound is { } parameter && !kind.Parameters.ContainsKey(parameter))
                {
                    throw new InvalidOperationException(
                        $"'{kind.Keyword}' bounds '{pattern}' by '{parameter}', which is not one of its "
                        + "parameters.");
                }
            }
        }

        if (index.Count < kinds.Length)
        {
            throw new InvalidOperationException("Every kind must be reachable by at least its keyword.");
        }
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

    private static ComponentKindInfo Node() => new()
    {
        Keyword = "node",
        Aliases = ["point", "junction"],
        Ports = [],
        HasUnlimitedPorts = true,
        PortFamilies = [],
        IndexedParameterFamilies = [],
        DrivesFlow = false,
        TagCode = null,
        Parameters = Parameters(
            Sized("t", Dimension.Temperature, -50, 300, precision: 1),
            Sized("p", Dimension.Pressure, 0, 2500, precision: 1),
            Sized("flow", Dimension.MassFlow, 0, 1000, precision: 3),
            Elevation()),
        Properties = Properties(
            Solved("t", Dimension.Temperature),
            Solved("p", Dimension.Pressure),
            Solved("h", Dimension.Enthalpy),
            Solved("flow", Dimension.MassFlow),
            Solved("rho", Dimension.Density)),
    };

    /// <summary>Where fluid enters the model (<c>D-64</c>).</summary>
    /// <remarks>
    /// A node that states what a boundary has to state: the thermal condition of what arrives, and one
    /// hydraulic condition. Which hydraulic one depends on what feeds it — a pumped feed states
    /// <c>flow</c> and its pressure is solved; a district connection states <c>p</c> and its flow is
    /// solved — so the group has one freedom and one minimum, and stating both or neither is an error.
    /// </remarks>
    private static ComponentKindInfo Inlet() => new()
    {
        Keyword = "inlet",
        Aliases = ["source"],
        Ports = [],
        HasUnlimitedPorts = true,
        PortFamilies = [],
        IndexedParameterFamilies = [],
        DrivesFlow = false,
        TagCode = null,
        Parameters = Parameters(
            Required("t", Dimension.Temperature, -50, 300, precision: 1),
            Sized("p", Dimension.Pressure, 0, 2500, precision: 1),
            Sized("flow", Dimension.MassFlow, 0, 1000, precision: 3),
            Elevation()),
        ParameterGroups =
        [
            Exactly(
                BinderDiagnostics.OverDetermined,
                BinderDiagnostics.UnderDetermined,
                freedoms: 1,
                "flow",
                "p"),
        ],
        Properties = Properties(
            Solved("t", Dimension.Temperature),
            Solved("p", Dimension.Pressure),
            Solved("h", Dimension.Enthalpy),
            Solved("flow", Dimension.MassFlow),
            Solved("rho", Dimension.Density)),
    };

    /// <summary>Where fluid leaves the model (<c>D-64</c>).</summary>
    /// <remarks>
    /// <strong>It requires nothing, and is still not the same as a bare node.</strong> What it carries is
    /// intent no parameter can: mass leaves here. That is what gives its balance an unknown external
    /// flux rather than a zero-flow closure, and what lets a circuit that fills up with no way out be
    /// reported instead of solved. Stating <c>p</c> on one makes it a pressure boundary as well.
    /// </remarks>
    private static ComponentKindInfo Outlet() => new()
    {
        Keyword = "outlet",
        Aliases = ["sink"],
        Ports = [],
        HasUnlimitedPorts = true,
        PortFamilies = [],
        IndexedParameterFamilies = [],
        DrivesFlow = false,
        TagCode = null,
        Parameters = Parameters(
            Sized("t", Dimension.Temperature, -50, 300, precision: 1),
            Sized("p", Dimension.Pressure, 0, 2500, precision: 1),
            Sized("flow", Dimension.MassFlow, 0, 1000, precision: 3),
            Elevation()),
        Properties = Properties(
            Solved("t", Dimension.Temperature),
            Solved("p", Dimension.Pressure),
            Solved("h", Dimension.Enthalpy),
            Solved("flow", Dimension.MassFlow),
            Solved("rho", Dimension.Density)),
    };

    private static ComponentKindInfo Pipe() => new()
    {
        Keyword = "pipe",
        Aliases = ["tube"],
        Ports = [Port("in", PortRole.Inlet), Port("out", PortRole.Outlet)],
        PortFamilies = [],
        IndexedParameterFamilies = [],
        DrivesFlow = false,
        TagCode = null,
        Parameters = Parameters(
            Sized("length", Dimension.Length, 0.01, 10000, precision: 2),
            Sized("dn", Dimension.NominalDiameter, 6, 2000, precision: 0),
            // Which series `dn` is read in (C-36): a catalogue id, defaulting to the script's `catalog`
            // line. The list is the shipped catalogues, pinned to `PipeCatalogs.All` by a test.
            new ParameterInfo
            {
                Name = "material",
                ValueKind = ParameterValueKind.Symbol,
                Dimension = Dimension.Dimensionless,
                AcceptedSymbols = ["steel_en10255", "steel_en10220", "copper_en1057"],
                OmissionBehavior = ParameterOmissionBehavior.Default,
                DefaultLiteral = "steel_en10255",
                DefaultBasis = "the script's `catalog` line when one is written; otherwise the shipped default, which this is",
                DisplayPrecision = 0,
            },
            Defaulted("roughness", Dimension.Length, 1e-6, 5e-3, "0.045 mm", "commercial steel", precision: 4),
            Sized("nodes", Dimension.Dimensionless, 0, 100, precision: 0),
            // No `elevation` here, deliberately: a pipe is the one kind that spans two heights, so
            // its rise is z(out) - z(in) from what it connects, never a number of its own (D-70).
            Defaulted("minor_loss", Dimension.Dimensionless, 0, 10000, "0", "no fittings stated", precision: 2)),
        Properties = Properties(
            Solved("dp", Dimension.PressureDelta),
            Solved("velocity", Dimension.Velocity),
            Solved("re", Dimension.Dimensionless),
            Sized("dn", Dimension.NominalDiameter),
            Sized("diameter", Dimension.Length),
            Solved("flow", Dimension.MassFlow),
            Sized("volume", Dimension.Volume)),
    };

    private static ComponentKindInfo HeatExchanger() => new()
    {
        Keyword = "heat_exchanger",
        Aliases = ["exchanger", "hx", "heater", "cooler", "radiator", "load", "boiler", "chiller"],
        // `D-120`: side 2's ports are `in[2]`/`out[2]` to the script and `in2`/`out2` to the model.
        Ports =
        [
            Port("in", PortRole.Inlet),
            Port("out", PortRole.Outlet),
            Keyed(Port("in[2]", PortRole.Inlet, optional: true), "in2"),
            Keyed(Port("out[2]", PortRole.Outlet, optional: true), "out2"),
        ],
        PortFamilies = [],
        IndexedParameterFamilies = [],
        DrivesFlow = false,
        TagCode = "HE",

        // Three relations, counted rather than solved. Q = m . cp . (out - in) makes any three of
        // power/in/out/flow fix the fourth, side 2 has the same relation with its own terminals and
        // flow (S-32), and UA = U . A makes any two of ua/area/u fix the third. Groups name keys.
        // A side's flow is stated once, as a mass flow or as a volume flow at the side's inlet state
        // (`D-120`, P5.13b): `vflow` is `flow` divided by a density the solve knows and the binder does
        // not, so the two are one freedom.
        ParameterGroups =
        [
            Group(BinderDiagnostics.OverDetermined, freedoms: 3, "power", "in", "out", "flow", "vflow"),
            Group(BinderDiagnostics.OverDetermined, freedoms: 3, "power", "in2", "out2", "flow2", "vflow2"),
            Group(BinderDiagnostics.OverDetermined, freedoms: 1, "flow", "vflow"),
            Group(BinderDiagnostics.OverDetermined, freedoms: 1, "flow2", "vflow2"),
            Group(BinderDiagnostics.OverDetermined, freedoms: 2, "ua", "area", "u"),
        ],

        // A port's state is `port.quantity` (`D-120`): `in.t` is the side-1 inlet temperature the
        // script once wrote as `in`, and `in[2].flow`, `in[2].dp`, `in[2].dt` are side 2's stream --
        // the one entering at `in[2]` -- where the bare `flow`, `dp`, `dt` are side 1's, as a bare
        // quantity is always the first port's. Each is stored under the key the physics reads.
        Parameters = Parameters(
            Sized("power", Dimension.Power, -100000, 100000, precision: 1),
            Keyed(Sized("in.t", Dimension.Temperature, -50, 300, precision: 1), "in"),
            Keyed(Sized("out.t", Dimension.Temperature, -50, 300, precision: 1), "out"),
            Keyed(Sized("in[2].t", Dimension.Temperature, -50, 300, precision: 1), "in2"),
            Keyed(Sized("out[2].t", Dimension.Temperature, -50, 300, precision: 1), "out2"),
            Defaulted(
                "dp",
                Dimension.PressureDelta,
                0,
                1000,
                "20 kPa",
                "a plate exchanger at its design flow; write dp=0 for an ideal block",
                precision: 1) with { Aliases = ["in.dp"] },
            Keyed(
                Defaulted(
                    "in[2].dp",
                    Dimension.PressureDelta,
                    0,
                    1000,
                    "20 kPa",
                    "the secondary side, on the same basis as dp",
                    precision: 1),
                "dp2"),
            Sized("dt", Dimension.TemperatureDelta, 0.1, 200, precision: 1) with { Aliases = ["in.dt"] },
            Keyed(Sized("in[2].dt", Dimension.TemperatureDelta, 0.1, 200, precision: 1), "dt2"),
            Sized("flow", Dimension.MassFlow, 0, 1000, precision: 3) with { Aliases = ["in.flow"] },
            Keyed(Sized("in[2].flow", Dimension.MassFlow, 0, 1000, precision: 3), "flow2"),
            Sized("vflow", Dimension.VolumeFlow, 0, 1000, precision: 2) with { Aliases = ["in.vflow"] },
            Keyed(Sized("in[2].vflow", Dimension.VolumeFlow, 0, 1000, precision: 2), "vflow2"),
            Sized("ua", ConductancePerKelvin, 1, 1e7, precision: 1),
            Sized("area", Dimension.Area, 1e-3, 1e4, precision: 3),
            Sized("u", HeatTransferCoefficient, 10, 20000, precision: 1),
            Sized("approach", Dimension.TemperatureDelta, 0.1, 100, precision: 1),
            Symbol("arrangement", ["counter", "parallel", "crossflow"], "counter", "counter-flow is the usual arrangement"),
            Sized("plates", Dimension.Dimensionless, 3, 800, precision: 0),
            Sized("lamella", Dimension.Length, 1e-3, 20e-3, precision: 4),
            Sized("plate_area", Dimension.Area, 1e-3, 5, precision: 4),
            // The fluid each side holds (`D-144`, `D-145`). Stated when a datasheet gives it, and
            // otherwise sized from the plate area the same pass decided -- never solved for: a volume
            // is geometry, and a circuit that could pick one would be sizing from its own transient.
            Sized("volume", Dimension.Volume, 0.01, 2000, precision: 3),
            Keyed(Sized("volume[2]", Dimension.Volume, 0.01, 2000, precision: 3), "volume2"),
            Defaulted("fouling", FoulingResistance, 0, 1e-2, "0.00001", "clean surfaces", precision: 6),
            Elevation()),
        Properties = Properties(
            Sized("power", Dimension.Power),
            Sized("ua", ConductancePerKelvin),
            Sized("area", Dimension.Area),
            Sized("u", HeatTransferCoefficient),
            Sized("ntu", Dimension.Dimensionless),
            Solved("effectiveness", Dimension.Dimensionless),
            Solved("lmtd", Dimension.TemperatureDelta),
            Solved("approach", Dimension.TemperatureDelta),
            Sized("plates", Dimension.Dimensionless),
            Sized("volume", Dimension.Volume),
            Keyed(Sized("volume[2]", Dimension.Volume), "volume2"),
            Solved("dp", Dimension.PressureDelta),
            Keyed(Solved("in[2].dp", Dimension.PressureDelta), "dp2"),
            Solved("dt", Dimension.TemperatureDelta),
            Keyed(Solved("in[2].dt", Dimension.TemperatureDelta), "dt2"),
            Solved("flow", Dimension.MassFlow),
            Keyed(Solved("in[2].flow", Dimension.MassFlow), "flow2"),
            Keyed(Solved("in.t", Dimension.Temperature), "t_in"),
            Keyed(Solved("out.t", Dimension.Temperature), "t_out"),
            Keyed(Solved("in[2].t", Dimension.Temperature), "t_in2"),
            Keyed(Solved("out[2].t", Dimension.Temperature), "t_out2")),
    };

    private static ComponentKindInfo Valve() => new()
    {
        Keyword = "valve",
        Aliases = ["control_valve", "balancing_valve", "two_way_valve", "2_way_valve"],
        Ports = [Port("in", PortRole.Inlet), Port("out", PortRole.Outlet)],
        PortFamilies = [],
        IndexedParameterFamilies = [],
        DrivesFlow = false,
        TagCode = "V",
        ActuatedParameter = "position",
        ParameterGroups = ValveGroups(),
        Parameters = ValveParameters(),
        Properties = ValveProperties(),
    };

    private static ComponentKindInfo ThreeWayValve() => new()
    {
        Keyword = "three_way_valve",
        Aliases = ["3_way_valve", "mixing_valve", "diverting_valve", "3wv"],
        // `ab` is the common port and `a`/`b` the two switched ones, which is how a valve body is
        // labelled: mixing is A + B -> AB, diverting is AB -> A + B. `b` is optional -- a three-way
        // used as a two-way leaves it open, and inference rule I3 terminates it.
        //
        // All three stay bidirectional. Typing them inlet/outlet/outlet describes a *diverting* valve
        // only, and doing so made a mixing arrangement expressible just by relying on reverse flow,
        // which put `FS4009` on a correct design. Which arrangement it is comes from the topology.
        Ports =
        [
            Port("ab", PortRole.Bidirectional),
            Port("a", PortRole.Bidirectional),
            Port("b", PortRole.Bidirectional, optional: true),
        ],
        PortFamilies = [],
        IndexedParameterFamilies = [],
        DrivesFlow = false,
        TagCode = "TV",
        ActuatedParameter = "position",
        ParameterGroups = ValveGroups(),
        Parameters = ValveParameters(
            characteristic: "linear",
            characteristicBasis: "a mixing valve's legs open complementarily, so the total flow holds",
            leakage: true),
        Properties = ValveProperties(),
    };

    private static ComponentKindInfo Pump() => new()
    {
        Keyword = "pump",
        Aliases = ["circulator"],
        Ports = [Port("in", PortRole.Inlet), Port("out", PortRole.Outlet)],
        PortFamilies = [],
        IndexedParameterFamilies = [],
        DrivesFlow = true,
        TagCode = "PU",
        ActuatedParameter = "speed",
        // One flow statement: a mass flow, or a volume flow at the pump's inlet state (P5.13b); and
        // one rise statement, a head or a pressure rise (C-109).
        ParameterGroups =
        [
            Group(BinderDiagnostics.OverDetermined, freedoms: 1, "flow", "vflow"),
            Group(BinderDiagnostics.OverDetermined, freedoms: 1, "head", "dp"),
        ],
        Parameters = Parameters(
            Sized("head", Dimension.Head, 0.1, 500, precision: 2),
            Sized("dp", Dimension.PressureDelta, 1, 5000, precision: 1),
            Sized("flow", Dimension.MassFlow, 0, 1000, precision: 3),
            Sized("vflow", Dimension.VolumeFlow, 0, 1000, precision: 2),
            Sized("speed", Dimension.Dimensionless, 0, 1.2, precision: 2),
            Defaulted("efficiency", Dimension.Dimensionless, 0.1, 0.95, "0.7", "a typical wet-rotor circulator", precision: 2)
                with { Validity = Bounded(BinderDiagnostics.EfficiencyOutsideRange, 0, 1) },
            Defaulted("margin", Dimension.Dimensionless, 1, 2, "1.0", "size to the computed duty, with no spare", precision: 2),
            Elevation()),
        Properties = Properties(
            Sized("head", Dimension.Head),
            Solved("dp", Dimension.PressureDelta),
            Solved("flow", Dimension.MassFlow),
            Solved("power", Dimension.Power),
            Solved("speed", Dimension.Dimensionless),
            Sized("efficiency", Dimension.Dimensionless)),
    };

    private static ComponentKindInfo Tank() => new()
    {
        Keyword = "tank",
        Aliases = ["container"],
        // `D-120`: the first inlet and outlet are the bare `in` and `out` (keys `in1`, `out1`), the
        // rest a family written `in[n]` and keyed `in{n}`; a port's height is `in[n].level`, a layer's
        // initial temperature `layer[n].t`. Each family starts at 2 because its first member is the
        // fixed row beside it, which is how a bare word and `[1]` come to be one port.
        Ports =
        [
            Keyed(Port("in", PortRole.Bidirectional), "in1"),
            Keyed(Port("out", PortRole.Bidirectional), "out1"),
        ],
        PortFamilies =
        [
            new PortFamilyInfo
            {
                Prefix = "in",
                MinIndex = 2,
                MaxIndex = TankPorts,
                Role = PortRole.Bidirectional,
                LevelParameterSuffix = "_level",
            },
            new PortFamilyInfo
            {
                Prefix = "out",
                MinIndex = 2,
                MaxIndex = TankPorts,
                Role = PortRole.Bidirectional,
                LevelParameterSuffix = "_level",
            },
        ],
        IndexedParameterFamilies =
        [
            new IndexedParameterFamilyInfo
            {
                Pattern = "layer[{index}].t",
                KeyPattern = "t{index}",
                LegacyPattern = "t{index}",
                MinIndex = 1,
                MaxIndexParameter = "layers",
                Element = Sized("t", Dimension.Temperature, -50, 300, precision: 1),
            },
            new IndexedParameterFamilyInfo
            {
                Pattern = "in[{index}].level",
                KeyPattern = "in{index}_level",
                LegacyPattern = "in{index}_level",
                MinIndex = 2,
                MaxIndex = TankPorts,
                Element = LevelParameter("in_level"),
            },
            new IndexedParameterFamilyInfo
            {
                Pattern = "out[{index}].level",
                KeyPattern = "out{index}_level",
                LegacyPattern = "out{index}_level",
                MinIndex = 2,
                MaxIndex = TankPorts,
                Element = LevelParameter("out_level"),
            },
        ],
        DrivesFlow = false,
        TagCode = "S",
        Parameters = Parameters(
            Defaulted("volume", Dimension.Volume, 1, 1e7, "300 dm3", "a domestic buffer vessel", precision: 1)
                with { Aliases = ["v"] },
            Defaulted("layers", Dimension.Dimensionless, 1, 100, "5", "enough to show stratification", precision: 0)
                with { Validity = Bounded(BinderDiagnostics.InvalidLayerCount, 1, 100, wholeNumber: true) },
            Sized("t", Dimension.Temperature, -50, 300, precision: 1),
            Keyed(LevelParameter("in.level"), "in1_level"),
            Keyed(LevelParameter("out.level"), "out1_level"),
            // One height for the whole vessel and every port on it. `D-70`'s z_port = z_tank + f·H
            // waits for the tank to have a height, which is P6.2's geometry, not this registry's.
            Elevation()),
        Properties = Properties(
            Declared("volume", Dimension.Volume),
            Declared("layers", Dimension.Dimensionless),
            Solved("stored_energy", Dimension.Energy),
            Keyed(Solved("in.t", Dimension.Temperature), "in1_t"),
            Keyed(Solved("out.t", Dimension.Temperature), "out1_t")),

        // layer[n].t is on both sides of the registry and means two things: the parameter is an
        // initial condition, the property is the solved layer temperature. in[n].t and out[n].t have
        // no parameter behind them at all -- a port's temperature is read, never stated.
        IndexedPropertyFamilies =
        [
            PropertyFamily("layer[{index}].t", "t{index}", Dimension.Temperature, maxIndexParameter: "layers"),
            PropertyFamily("in[{index}].t", "in{index}_t", Dimension.Temperature, minIndex: 2, maxIndex: TankPorts),
            PropertyFamily("out[{index}].t", "out{index}_t", Dimension.Temperature, minIndex: 2, maxIndex: TankPorts),
        ],
    };

    private static ParameterInfo LevelParameter(string name) =>
        Defaulted(name, Dimension.Dimensionless, 0, 1, "0.5", "mid height", precision: 2)
            with { Validity = Bounded(BinderDiagnostics.LevelOutsideRange, 0, 1) };

    private static ComponentKindInfo Controller() => new()
    {
        Keyword = "controller",
        Aliases = ["pi", "pid", "p", "thermostat"],

        // No ports, and that is not an omission: a controller is excluded from the flow graph. It is a
        // registry kind so that `PID1 pid kp=3` needs no new grammar.
        Ports = [],
        PortFamilies = [],
        IndexedParameterFamilies = [],
        DrivesFlow = false,
        TagCode = "PID",
        Parameters = Parameters(
            Sized("kp", Dimension.Dimensionless, -1e6, 1e6, precision: 4),
            Sized("ki", Dimension.Dimensionless, -1e6, 1e6, precision: 6),
            Sized("kd", Dimension.Dimensionless, -1e6, 1e6, precision: 4)),
        Properties = Properties(),
    };

    /// <summary>Builds one instrument kind: a placed observer with a single measured property.</summary>
    /// <remarks>
    /// One kind per instrument rather than one <c>sensor</c> kind with a <c>measures=</c> parameter,
    /// because the tag then falls out of the kind for free: TE, PE and FE are what an instrument index
    /// already calls a temperature, pressure and flow element.
    /// </remarks>
    private static ComponentKindInfo Sensor(
        string keyword,
        ImmutableArray<string> aliases,
        string tagCode,
        string property,
        Dimension dimension) => new()
    {
        Keyword = keyword,
        Aliases = aliases,

        // No ports, like a controller and for a stronger reason: an instrument is attached to a node
        // with `at`, reads that node's state, and holds none of its own. It writes no residuals.
        Ports = [],
        PortFamilies = [],
        IndexedParameterFamilies = [],
        DrivesFlow = false,
        TagCode = tagCode,
        IsObserver = true,
        MeasuredProperty = property,
        Parameters = Parameters(),
        Properties = Properties(Solved(property, dimension)),
    };

    /// <summary>The absolute height every single-height kind carries (<c>D-70</c>).</summary>
    /// <returns>The parameter, in metres above the project datum.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Not a sizing candidate, ever.</strong> A height is where the plant is, not something
    /// equipment selection decides (<c>C-41</c>), so an omitted one is defaulted rather than sized.
    /// The default is stated as 0 m for the registry's sake; the binder's height propagation is what
    /// decides the height a silent component actually sits at (<c>D-95</c>), and it reads only the
    /// heights the script wrote.
    /// </para>
    /// <para>
    /// A pipe is the one flow kind without it: it spans two heights and its rise is derived from
    /// what it connects. A sensor has none because it observes a node and carries no port.
    /// </para>
    /// </remarks>
    private static ParameterInfo Elevation() =>
        Defaulted("elevation", Dimension.Length, -500, 500, "0 m", "no elevation stated", precision: 2);

    // The characteristic is the one parameter the two valve kinds default differently (`D-122`). A
    // two-way control valve is equal-percentage by long convention; a three-way valve in a mixing
    // circuit is a constant-flow device, its two legs opening complementarily so that what one closes
    // the other opens -- which is what a linear pair does (Σφ = 1) and an equal-percentage pair does not
    // (Σφ = 0.28 at mid-travel, Johnson Controls VM-12 fig. 2). Rotary mixing valves are linear (ESBE
    // VRG130: rangeability 100, A-AB), and Siemens' VXG44 seat valve is linear in the body with
    // equal-percentage as an actuator option. `characteristic=equal_percentage` still states the other.
    private static ImmutableDictionary<string, ParameterInfo> ValveParameters(
        string characteristic = "equal_percentage",
        string characteristicBasis = "the usual choice for a control valve",
        bool leakage = false) => Parameters(
        [
            Sized("kv", Dimension.Kv, 0.01, 10000, precision: 2),
            Sized("position", Dimension.Dimensionless, 0, 1, precision: 3)
                with { Validity = Bounded(BinderDiagnostics.PositionOutsideRange, 0, 1) },
            Symbol(
                "characteristic",
                ["linear", "equal_percentage", "quick_open"],
                characteristic,
                characteristicBasis),
            Sized("authority", Dimension.Dimensionless, 0, 1, precision: 2),
            Sized("dp", Dimension.PressureDelta, 0, 2500, precision: 1),
            Elevation(),

            // A three-way body's legs are never quite shut: what a leg passes at its stop is the body's
            // rated leakage, a catalogue figure and not the characteristic's floor (`D-135`, `C-71`).
            // Belimo's characterised three-way bodies rate B-AB at leakage class I, 1-2 % of Kvs, with
            // A-AB bubble-tight; ESBE's VRG130 rotary bodies are under 0.05 %. The default is the
            // leakier published body; a script modelling a rotary body states `leakage=0.05%`.
            .. leakage
                ? new[]
                {
                    Defaulted(
                        "leakage",
                        Dimension.Dimensionless,
                        0,
                        0.05,
                        "2 %",
                        "Belimo's bypass at leakage class I; a rotary body is under 0.05 %",
                        precision: 4),
                }
                : [],
        ]);

    private static ImmutableDictionary<string, PropertyInfo> ValveProperties() => Properties(
        Sized("kv", Dimension.Kv),
        Solved("dp", Dimension.PressureDelta),
        Declared("position", Dimension.Dimensionless),
        Sized("authority", Dimension.Dimensionless),
        Solved("flow", Dimension.MassFlow));

    // Expression-bodied on purpose (C-99). `Default` is initialised at the top of the class, before a
    // static field declared below it would be, so a stored value here was still `default(Dimension)` --
    // unnamed, with no vector -- when the shared registry read it, and u, ua and fouling were
    // dimensionless in every model. Computing on access has no order to get wrong.

    // W/K: the exchanger's thermal size, independent of how it is achieved. Unnamed: `13` names no
    // conductance, and `ToSiUnitString` spells the vector W/K.
    private static Dimension ConductancePerKelvin =>
        Dimension.FromVector(new DimensionVector(Mass: 1, Length: 2, Time: -3, Temperature: -1));

    // W/(m²·K) and m²·K/W are named since D-119 (L-54), so a script can write the unit and the wire reports it.
    private static Dimension HeatTransferCoefficient => Dimension.HeatTransferCoefficient;

    private static Dimension FoulingResistance => Dimension.ThermalResistance;
    private static PortInfo Port(string name, PortRole role, bool optional = false) =>
        new() { Name = name, Role = role, IsOptional = optional };

    // `D-120` respelled the surface and not the model: a row written `in[2].t` is stored as `in2`,
    // which every reader of StatedParameters, every port key and every published property has used
    // since before the spelling changed, and the old spelling is exactly that key -- so one word
    // says all three. The suggestion `FS1536` offers is the row's Name.
    private static ParameterInfo Keyed(ParameterInfo row, string key) =>
        row with { Key = key, LegacySpellings = [key] };

    private static PropertyInfo Keyed(PropertyInfo row, string key) =>
        row with { Key = key, LegacySpellings = [key] };

    private static PortInfo Keyed(PortInfo row, string key) =>
        row with { Key = key, LegacySpellings = [key] };

    private static ParameterInfo Sized(
        string name, Dimension dimension, double min, double max, int precision) => new()
        {
            Name = name,
            ValueKind = ParameterValueKind.Quantity,
            Dimension = dimension,
            OmissionBehavior = ParameterOmissionBehavior.Size,
            UsualRange = SiRange(min, max, dimension),
            DisplayPrecision = precision,
        };

    private static ParameterInfo Defaulted(
        string name,
        Dimension dimension,
        double min,
        double max,
        string literal,
        string basis,
        int precision) => new()
        {
            Name = name,
            ValueKind = ParameterValueKind.Quantity,
            Dimension = dimension,
            OmissionBehavior = ParameterOmissionBehavior.Default,
            DefaultLiteral = literal,
            DefaultBasis = basis,
            UsualRange = SiRange(min, max, dimension),
            DisplayPrecision = precision,
        };

    private static ParameterInfo Symbol(
        string name, ImmutableArray<string> accepted, string literal, string basis) => new()
        {
            Name = name,
            ValueKind = ParameterValueKind.Symbol,
            Dimension = Dimension.Dimensionless,
            AcceptedSymbols = accepted,
            OmissionBehavior = ParameterOmissionBehavior.Default,
            DefaultLiteral = literal,
            DefaultBasis = basis,
            DisplayPrecision = 0,
        };

    // The ranges in `22`'s tables are written the way a user writes a value: a bare number in the
    // dimension's canonical unit. Converting here rather than hand-writing SI numbers is what keeps
    // `-50 … 300` for a temperature from being transcribed as -50 K.
    private static Range<double> SiRange(double min, double max, Dimension dimension) =>
        new(
            Quantity.FromBareNumber(min, dimension).SiValue,
            Quantity.FromBareNumber(max, dimension).SiValue);

    private static PropertyInfo Declared(string name, Dimension dimension) =>
        Property(name, dimension, PropertyAvailability.Declared);

    private static PropertyInfo Sized(string name, Dimension dimension) =>
        Property(name, dimension, PropertyAvailability.Sized);

    private static PropertyInfo Solved(string name, Dimension dimension) =>
        Property(name, dimension, PropertyAvailability.Solved);

    private static PropertyInfo Property(string name, Dimension dimension, PropertyAvailability availability) =>
        new()
        {
            Name = name,
            Dimension = dimension,
            Availability = availability,
            CanonicalUnit = UnitTable.CanonicalUnitFor(dimension)?.Text ?? dimension.SiUnit,
        };

    /// <summary>The bounds outside which a parameter's value is an error, with the code that says so.</summary>
    /// <param name="descriptor">The code raised for a value outside the range.</param>
    /// <param name="low">The lowest accepted value, in the dimension's canonical unit.</param>
    /// <param name="high">The highest accepted value, in the dimension's canonical unit.</param>
    /// <param name="wholeNumber">Whether a fractional value is an error as well.</param>
    /// <returns>The validity rule to hang on a parameter.</returns>
    /// <remarks>
    /// Every bounded parameter in v1 is dimensionless, so no conversion is involved yet. It goes
    /// through <see cref="SiRange"/> anyway, for the reason that method exists: a bound written the way
    /// a user writes a value is the only form the tables in <c>22</c> can be checked against by eye.
    /// </remarks>
    private static ParameterValidity Bounded(
        DiagnosticDescriptor descriptor, double low, double high, bool wholeNumber = false) => new()
        {
            Range = SiRange(low, high, Dimension.Dimensionless),
            Descriptor = descriptor,
            RequiresWholeNumber = wholeNumber,
        };

    /// <summary>One relation over a kind's parameters, for the over-determination count.</summary>
    /// <param name="descriptor">The code raised when too many members are stated.</param>
    /// <param name="freedoms">How many members may be stated before the group is over-determined.</param>
    /// <param name="parameters">The canonical parameter names the relation ties together.</param>
    /// <returns>The group to hang on a kind.</returns>
    private static ParameterGroupInfo Group(
        DiagnosticDescriptor descriptor, int freedoms, params ReadOnlySpan<string> parameters) => new()
        {
            Parameters = [.. parameters],
            Freedoms = freedoms,
            Descriptor = descriptor,
        };

    /// <summary>A relation whose members must be stated a fixed number of times, no more and no fewer.</summary>
    /// <param name="over">The code raised when too many members are stated.</param>
    /// <param name="under">The code raised when too few are.</param>
    /// <param name="freedoms">How many members must be stated.</param>
    /// <param name="parameters">The canonical parameter names the relation ties together.</param>
    /// <returns>The group to hang on a kind.</returns>
    /// <remarks>
    /// A boundary's <c>flow</c> and <c>p</c> are the case: exactly one, because a stream is fixed by one
    /// hydraulic condition and over-determined by two. <see cref="Group"/> is the same thing with no
    /// lower bound, which is what every other relation in the registry wants — an exchanger with none of
    /// <c>power</c>, <c>in</c>, <c>out</c> and <c>flow</c> stated is a component sizing has yet to reach,
    /// not an error.
    /// </remarks>
    private static ParameterGroupInfo Exactly(
        DiagnosticDescriptor over,
        DiagnosticDescriptor under,
        int freedoms,
        params ReadOnlySpan<string> parameters) => new()
        {
            Parameters = [.. parameters],
            Freedoms = freedoms,
            Minimum = freedoms,
            Descriptor = over,
            MinimumDescriptor = under,
        };

    /// <summary>A parameter the kind has no answer without (<c>D-64</c>).</summary>
    /// <param name="name">The canonical parameter name.</param>
    /// <param name="dimension">Its dimension.</param>
    /// <param name="min">The low end of its usual range, in the canonical unit.</param>
    /// <param name="max">The high end.</param>
    /// <param name="precision">Decimal places when the value is displayed.</param>
    /// <returns>The parameter to hang on a kind.</returns>
    /// <remarks>
    /// Deliberately rare. Use it only where every substitute would be a guess about the plant rather
    /// than about the model — which, so far, is a boundary's temperature and nothing else.
    /// </remarks>
    private static ParameterInfo Required(
        string name, Dimension dimension, double min, double max, int precision) => new()
        {
            Name = name,
            ValueKind = ParameterValueKind.Quantity,
            Dimension = dimension,
            OmissionBehavior = ParameterOmissionBehavior.Require,
            UsualRange = SiRange(min, max, dimension),
            DisplayPrecision = precision,
        };

    // Kv and dp are not two constraints: the drop a valve makes follows from its Kv and the flow
    // through it. Stating both is a design intention beside its own consequence, so the group has one
    // freedom and the code is a warning rather than an error.
    private static ImmutableArray<ParameterGroupInfo> ValveGroups() =>
        [Group(BinderDiagnostics.RedundantValveDrop, freedoms: 1, "kv", "dp")];

    /// <summary>The highest index either of a tank's port families materializes.</summary>
    private const int TankPorts = 16;

    /// <summary>One indexed property family, for a value that exists once per layer or per port.</summary>
    /// <param name="pattern">The pattern a reference writes, with one <c>{index}</c> placeholder.</param>
    /// <param name="keyPattern">The pattern the value is published under, which is also the pre-<c>D-120</c> spelling.</param>
    /// <param name="dimension">The dimension of the value read back.</param>
    /// <param name="minIndex">The lowest member; 2 for a port family whose first member is a fixed row.</param>
    /// <param name="maxIndex">The fixed highest index, when the family has one.</param>
    /// <param name="maxIndexParameter">The parameter supplying the highest index instead.</param>
    /// <returns>The family to hang on a kind.</returns>
    /// <remarks>
    /// Always <see cref="PropertyAvailability.Solved"/>. Every indexed property in v1 is a solved
    /// layer or port state; a <em>stated</em> <c>t3</c> is readable through the parameter path, which
    /// the property lookup tries first for exactly that reason.
    /// </remarks>
    private static IndexedPropertyFamilyInfo PropertyFamily(
        string pattern,
        string keyPattern,
        Dimension dimension,
        int minIndex = 1,
        int? maxIndex = null,
        string? maxIndexParameter = null) => new()
        {
            Pattern = pattern,
            KeyPattern = keyPattern,
            LegacyPattern = keyPattern,
            MinIndex = minIndex,
            MaxIndex = maxIndex,
            MaxIndexParameter = maxIndexParameter,
            Element = Keyed(Solved(pattern, dimension), keyPattern),
        };

    /// <summary>Every indexed family a kind declares, parameter and property alike.</summary>
    /// <param name="kind">The kind to enumerate.</param>
    /// <returns>Each family's pattern and the parameter bounding it, if one does.</returns>
    private static IEnumerable<(string Pattern, string? MaxIndexParameter)> Families(ComponentKindInfo kind) =>
        kind.IndexedParameterFamilies
            .Select(static family => (family.Pattern, family.MaxIndexParameter))
            .Concat(kind.IndexedPropertyFamilies
                .Select(static family => (family.Pattern, family.MaxIndexParameter)));

    private static ImmutableDictionary<string, ParameterInfo> Parameters(params ParameterInfo[] parameters) =>
        parameters.ToImmutableDictionary(static parameter => parameter.Key, StringComparer.Ordinal);

    private static ImmutableDictionary<string, PropertyInfo> Properties(params PropertyInfo[] properties) =>
        properties.ToImmutableDictionary(static property => property.Name, StringComparer.Ordinal);
}
