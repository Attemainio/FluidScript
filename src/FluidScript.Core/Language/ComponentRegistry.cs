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
        {
            foreach (var spelling in new[] { kind.Keyword }.Concat(kind.Aliases))
            {
                var normalized = NameResolution.Normalize(spelling);

                if (claimed.TryGetValue(normalized, out var owner) && owner != kind.Keyword)
                {
                    throw new InvalidOperationException(
                        $"'{spelling}' resolves to both '{owner}' and '{kind.Keyword}'.");
                }

                claimed[normalized] = kind.Keyword;

                if (ReservedWords.TryMatch(spelling, out _))
                {
                    throw new InvalidOperationException(
                        $"'{spelling}' is a reserved word, so it can never appear in kind position.");
                }
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
                    if (!kind.Parameters.ContainsKey(parameter))
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
        Pipe(),
        HeatExchanger(),
        Valve(),
        ThreeWayValve(),
        Pump(),
        Tank(),
        Controller(),
        Sensor("t_sensor", ["temperature_sensor", "te"], "TE", "t", Dimension.Temperature),
        Sensor("p_sensor", ["pressure_sensor", "pe"], "PE", "p", Dimension.Pressure),
        Sensor("flow_sensor", ["flow_meter", "fe"], "FE", "flow", Dimension.MassFlow),
    ];

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
            Sized("flow", Dimension.MassFlow, 0, 1000, precision: 3)),
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
            Defaulted("roughness", Dimension.Length, 1e-6, 5e-3, "0.045 mm", "commercial steel", precision: 4),
            Sized("nodes", Dimension.Dimensionless, 0, 100, precision: 0),
            Sized("elevation", Dimension.Length, -500, 500, precision: 2),
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
        Ports =
        [
            Port("in", PortRole.Inlet),
            Port("out", PortRole.Outlet),
            Port("in2", PortRole.Inlet, optional: true),
            Port("out2", PortRole.Outlet, optional: true),
        ],
        PortFamilies = [],
        IndexedParameterFamilies = [],
        DrivesFlow = false,
        TagCode = "HE",

        // Two relations, counted rather than solved. Q = m . cp . (out - in) makes any three of
        // power/in/out/flow fix the fourth, and UA = U . A makes any two of ua/area/u fix the third.
        ParameterGroups =
        [
            Group(BinderDiagnostics.OverDetermined, freedoms: 3, "power", "in", "out", "flow"),
            Group(BinderDiagnostics.OverDetermined, freedoms: 2, "ua", "area", "u"),
        ],
        Parameters = Parameters(
            Sized("power", Dimension.Power, -100000, 100000, precision: 1),
            Sized("in", Dimension.Temperature, -50, 300, precision: 1),
            Sized("out", Dimension.Temperature, -50, 300, precision: 1),
            Sized("in2", Dimension.Temperature, -50, 300, precision: 1),
            Sized("out2", Dimension.Temperature, -50, 300, precision: 1),
            Sized("dt", Dimension.TemperatureDelta, 0.1, 200, precision: 1),
            Sized("dt2", Dimension.TemperatureDelta, 0.1, 200, precision: 1),
            Sized("dp", Dimension.PressureDelta, 0, 1000, precision: 1),
            Sized("dp2", Dimension.PressureDelta, 0, 1000, precision: 1),
            Sized("flow", Dimension.MassFlow, 0, 1000, precision: 3),
            Sized("flow2", Dimension.MassFlow, 0, 1000, precision: 3),
            Sized("ua", ConductancePerKelvin, 1, 1e7, precision: 1),
            Sized("area", Dimension.Area, 1e-3, 1e4, precision: 3),
            Sized("u", HeatTransferCoefficient, 10, 20000, precision: 1),
            Sized("approach", Dimension.TemperatureDelta, 0.1, 100, precision: 1),
            Symbol("arrangement", ["counter", "parallel", "crossflow"], "counter", "counter-flow is the usual arrangement"),
            Sized("plates", Dimension.Dimensionless, 3, 800, precision: 0),
            Sized("lamella", Dimension.Length, 1e-3, 20e-3, precision: 4),
            Sized("plate_area", Dimension.Area, 1e-3, 5, precision: 4),
            Defaulted("fouling", FoulingResistance, 0, 1e-2, "0.00001", "clean surfaces", precision: 6)),
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
            Solved("dp", Dimension.PressureDelta),
            Solved("dp2", Dimension.PressureDelta),
            Solved("dt", Dimension.TemperatureDelta),
            Solved("dt2", Dimension.TemperatureDelta),
            Solved("flow", Dimension.MassFlow),
            Solved("flow2", Dimension.MassFlow),
            Solved("t_in", Dimension.Temperature),
            Solved("t_out", Dimension.Temperature),
            Solved("t_in2", Dimension.Temperature),
            Solved("t_out2", Dimension.Temperature)),
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

        // All three bidirectional, and `c` optional: a mixing valve takes two streams in at `b` and
        // `c`, a diverting valve splits one from `a`, and which it is comes from the topology rather
        // than from a declaration. Fixed roles made the mixing arrangement expressible only by relying
        // on reverse flow, which put `FS4009` on a correct design.
        Ports =
        [
            Port("a", PortRole.Bidirectional),
            Port("b", PortRole.Bidirectional),
            Port("c", PortRole.Bidirectional, optional: true),
        ],
        PortFamilies = [],
        IndexedParameterFamilies = [],
        DrivesFlow = false,
        TagCode = "TV",
        ActuatedParameter = "position",
        ParameterGroups = ValveGroups(),
        Parameters = ValveParameters(),
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
        Parameters = Parameters(
            Sized("head", Dimension.Head, 0.1, 500, precision: 2),
            Sized("dp", Dimension.PressureDelta, 1, 5000, precision: 1),
            Sized("flow", Dimension.MassFlow, 0, 1000, precision: 3),
            Sized("speed", Dimension.Dimensionless, 0, 1.2, precision: 2),
            Defaulted("efficiency", Dimension.Dimensionless, 0.1, 0.95, "0.7", "a typical wet-rotor circulator", precision: 2)
                with { Validity = Bounded(BinderDiagnostics.EfficiencyOutsideRange, 0, 1) },
            Defaulted("margin", Dimension.Dimensionless, 1, 2, "1.0", "size to the computed duty, with no spare", precision: 2)),
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
        Ports = [Port("in1", PortRole.Bidirectional), Port("out1", PortRole.Bidirectional)],
        PortFamilies =
        [
            new PortFamilyInfo
            {
                Prefix = "in",
                MinIndex = 1,
                MaxIndex = 16,
                Role = PortRole.Bidirectional,
                ElevationParameterSuffix = "_elevation",
            },
            new PortFamilyInfo
            {
                Prefix = "out",
                MinIndex = 1,
                MaxIndex = 16,
                Role = PortRole.Bidirectional,
                ElevationParameterSuffix = "_elevation",
            },
        ],
        IndexedParameterFamilies =
        [
            new IndexedParameterFamilyInfo
            {
                Pattern = "t{index}",
                MinIndex = 1,
                MaxIndexParameter = "layers",
                Element = Sized("t", Dimension.Temperature, -50, 300, precision: 1),
            },
            new IndexedParameterFamilyInfo
            {
                Pattern = "in{index}_elevation",
                MinIndex = 1,
                MaxIndex = 16,
                Element = Defaulted(
                    "in_elevation", Dimension.Dimensionless, 0, 1, "0.5", "mid height", precision: 2)
                    with { Validity = Bounded(BinderDiagnostics.ElevationOutsideRange, 0, 1) },
            },
            new IndexedParameterFamilyInfo
            {
                Pattern = "out{index}_elevation",
                MinIndex = 1,
                MaxIndex = 16,
                Element = Defaulted(
                    "out_elevation", Dimension.Dimensionless, 0, 1, "0.5", "mid height", precision: 2)
                    with { Validity = Bounded(BinderDiagnostics.ElevationOutsideRange, 0, 1) },
            },
        ],
        DrivesFlow = false,
        TagCode = "S",
        Parameters = Parameters(
            Defaulted("volume", Dimension.Volume, 1, 1e7, "300 dm3", "a domestic buffer vessel", precision: 1)
                with { Aliases = ["v"] },
            Defaulted("layers", Dimension.Dimensionless, 1, 100, "5", "enough to show stratification", precision: 0)
                with { Validity = Bounded(BinderDiagnostics.InvalidLayerCount, 1, 100, wholeNumber: true) },
            Sized("t", Dimension.Temperature, -50, 300, precision: 1)),
        Properties = Properties(
            Declared("volume", Dimension.Volume),
            Declared("layers", Dimension.Dimensionless),
            Solved("stored_energy", Dimension.Energy)),
    };

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

    private static ImmutableDictionary<string, ParameterInfo> ValveParameters() => Parameters(
        Sized("kv", Dimension.Kv, 0.01, 10000, precision: 2),
        Sized("position", Dimension.Dimensionless, 0, 1, precision: 3)
            with { Validity = Bounded(BinderDiagnostics.PositionOutsideRange, 0, 1) },
        Symbol(
            "characteristic",
            ["linear", "equal_percentage", "quick_open"],
            "equal_percentage",
            "the usual choice for a control valve"),
        Sized("authority", Dimension.Dimensionless, 0, 1, precision: 2),
        Sized("dp", Dimension.PressureDelta, 0, 2500, precision: 1));

    private static ImmutableDictionary<string, PropertyInfo> ValveProperties() => Properties(
        Sized("kv", Dimension.Kv),
        Solved("dp", Dimension.PressureDelta),
        Declared("position", Dimension.Dimensionless),
        Sized("authority", Dimension.Dimensionless),
        Solved("flow", Dimension.MassFlow));

    // W/K: the exchanger's thermal size, independent of how it is achieved.
    private static Dimension ConductancePerKelvin { get; } =
        Dimension.FromVector(new DimensionVector(Mass: 1, Length: 2, Time: -3, Temperature: -1));

    // W/(m²·K).
    private static Dimension HeatTransferCoefficient { get; } =
        Dimension.FromVector(new DimensionVector(Mass: 1, Length: 0, Time: -3, Temperature: -1));

    // m²·K/W.
    private static Dimension FoulingResistance { get; } =
        Dimension.FromVector(new DimensionVector(Mass: -1, Length: 0, Time: 3, Temperature: 1));

    private static PortInfo Port(string name, PortRole role, bool optional = false) =>
        new() { Name = name, Role = role, IsOptional = optional };

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

    // Kv and dp are not two constraints: the drop a valve makes follows from its Kv and the flow
    // through it. Stating both is a design intention beside its own consequence, so the group has one
    // freedom and the code is a warning rather than an error.
    private static ImmutableArray<ParameterGroupInfo> ValveGroups() =>
        [Group(BinderDiagnostics.RedundantValveDrop, freedoms: 1, "kv", "dp")];

    private static ImmutableDictionary<string, ParameterInfo> Parameters(params ParameterInfo[] parameters) =>
        parameters.ToImmutableDictionary(static parameter => parameter.Name, StringComparer.Ordinal);

    private static ImmutableDictionary<string, PropertyInfo> Properties(params PropertyInfo[] properties) =>
        properties.ToImmutableDictionary(static property => property.Name, StringComparer.Ordinal);
}
