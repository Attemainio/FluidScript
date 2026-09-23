using System.Collections.Immutable;

using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Language.Registry;

namespace FluidScript.Core.Components.Valves;

/// <summary>A two-port throttling valve.</summary>
/// <remarks>
/// One equation: the Kv relation of <see cref="ValveLaw"/>, asserting that the flow through the valve
/// is the flow its opening and pressure drop imply.
/// </remarks>
public sealed class ValveComponent : ValveComponentBase
{
    /// <summary>Initializes a valve.</summary>
    /// <param name="name">The user's identifier.</param>
    /// <param name="kv">The rated flow coefficient, m³/h at 1 bar.</param>
    /// <param name="position">The opening, 0 to 1.</param>
    /// <param name="characteristic">Which characteristic the valve follows.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kv"/> is not positive.</exception>
    public ValveComponent(
        string name,
        double kv,
        double position = 1,
        ValveCharacteristic characteristic = ValveCharacteristic.Linear)
        : base(name, kv, position, characteristic)
    {
        Equations = [new EquationDeclaration(0, EquationKind.ComponentConstraint, name, $"{name} Kv law", "kg/s") { SteepInPressure = true }];
    }

    /// <inheritdoc/>
    public override string Kind => "valve";

    /// <inheritdoc/>
    /// <remarks>
    /// <strong>Inlet and outlet, not bidirectional</strong> — unlike <see cref="ThreeWayValveComponent"/>. The
    /// three-way valve's ports are bidirectional because mixing and diverting are two real arrangements
    /// and which one a valve is comes from the topology. A two-way valve has no such ambiguity, and the
    /// role is <em>nominal</em> in any case: a negative solved flow through it stays a legal answer
    /// (convention 2). Generalising the three-way's roles to this one was caught by the registry
    /// cross-check within the hour (<c>C-20</c>).
    /// </remarks>
    public override ImmutableArray<Port> Ports { get; } =
    [
        new Port { Name = "in", Role = PortRole.Inlet, IsOptional = false },
        new Port { Name = "out", Role = PortRole.Outlet, IsOptional = false },
    ];

    /// <inheritdoc/>
    /// <value>One group of two.</value>
    public override ImmutableArray<int> FlowGroups { get; } = [0, 0];

    /// <inheritdoc/>
    /// <value>One: the Kv relation.</value>
    public override int EquationCount => 1;

    /// <inheritdoc/>
    public override void EvaluateResiduals(in SolveContext context, Span<double> residuals)
    {
        var drop = context.Ports[0].Pressure - context.Ports[1].Pressure;
        var density = (context.Ports[0].Density + context.Ports[1].Density) / 2;

        residuals[0] = context.Flows[0]
            - ValveLaw.MassFlow(
                context.Parameter(KvIndex, Kv)
                    * ValveLaw.Opening(context.Parameter(PositionIndex, Position), Characteristic),
                drop,
                density);
    }
}
