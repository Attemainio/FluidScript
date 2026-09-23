using System.Collections.Immutable;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Registry;

public sealed partial class ComponentRegistry
{
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
}
