using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Registry;

public sealed partial class ComponentRegistry
{
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
}
