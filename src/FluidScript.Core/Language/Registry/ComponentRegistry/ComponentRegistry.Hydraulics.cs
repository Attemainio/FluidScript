using System.Collections.Immutable;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Registry;

public sealed partial class ComponentRegistry
{
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
        Aliases = ["3_way_valve", "mixing_valve", "diverting_valve", "3wv", "valve3"],
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

    // Kv and dp are not two constraints: the drop a valve makes follows from its Kv and the flow
    // through it. Stating both is a design intention beside its own consequence, so the group has one
    // freedom and the code is a warning rather than an error.
    private static ImmutableArray<ParameterGroupInfo> ValveGroups() =>
        [Group(BinderDiagnostics.RedundantValveDrop, freedoms: 1, "kv", "dp")];
}
