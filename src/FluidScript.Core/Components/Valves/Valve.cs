using System.Collections.Immutable;

using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Components.Valves;

/// <summary>A two-port throttling valve.</summary>
/// <remarks>
/// One equation: the Kv relation of <see cref="ValveLaw"/>, asserting that the flow through the valve
/// is the flow its opening and pressure drop imply.
/// </remarks>
public sealed class Valve : IFlowComponent
{
    private readonly ImmutableArray<EquationDeclaration> _equations;

    /// <summary>Initializes a valve.</summary>
    /// <param name="name">The user's identifier.</param>
    /// <param name="kv">The rated flow coefficient, m³/h at 1 bar.</param>
    /// <param name="position">The opening, 0 to 1.</param>
    /// <param name="characteristic">Which characteristic the valve follows.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kv"/> is not positive.</exception>
    public Valve(
        string name,
        double kv,
        double position = 1,
        ValveCharacteristic characteristic = ValveCharacteristic.Linear)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kv);

        Name = name;
        Kv = kv;
        Position = position;
        Characteristic = characteristic;

        _equations = [new EquationDeclaration(0, EquationKind.ComponentConstraint, name, $"{name} Kv law", "kg/s") { SteepInPressure = true }];
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Kind => "valve";

    /// <inheritdoc/>
    /// <value>Always <see langword="null"/>: a valve has no modes.</value>
    public string? Mode => null;

    /// <inheritdoc/>
    public ImmutableDictionary<string, Quantity> StatedParameters { get; init; }
        = ImmutableDictionary<string, Quantity>.Empty;

    /// <inheritdoc/>
    public ImmutableDictionary<string, Quantity> SizedParameters { get; init; }
        = ImmutableDictionary<string, Quantity>.Empty;

    /// <inheritdoc/>
    public ImmutableDictionary<string, Quantity> DefaultParameters { get; init; }
        = ImmutableDictionary<string, Quantity>.Empty;

    /// <summary>Gets the rated flow coefficient.</summary>
    /// <value>m³/h of water at 1 bar differential.</value>
    public double Kv { get; }

    /// <summary>Gets the opening.</summary>
    /// <value>0 to 1; 1 is fully open. The one parameter a controller may move (<c>D-61</c>).</value>
    public double Position { get; init; }

    /// <summary>Gets which characteristic the valve follows.</summary>
    public ValveCharacteristic Characteristic { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// <strong>Inlet and outlet, not bidirectional</strong> — unlike <see cref="ThreeWayValve"/>. The
    /// three-way valve's ports are bidirectional because mixing and diverting are two real arrangements
    /// and which one a valve is comes from the topology. A two-way valve has no such ambiguity, and the
    /// role is <em>nominal</em> in any case: a negative solved flow through it stays a legal answer
    /// (convention 2). Generalising the three-way's roles to this one was caught by the registry
    /// cross-check within the hour (<c>C-20</c>).
    /// </remarks>
    public ImmutableArray<Port> Ports { get; } =
    [
        new Port { Name = "in", Role = PortRole.Inlet, IsOptional = false },
        new Port { Name = "out", Role = PortRole.Outlet, IsOptional = false },
    ];

    /// <inheritdoc/>
    /// <value>One group of two.</value>
    public ImmutableArray<int> FlowGroups { get; } = [0, 0];

    /// <inheritdoc/>
    /// <value>One: the Kv relation.</value>
    public int EquationCount => 1;

    /// <inheritdoc/>
    /// <returns>Empty. Its flow belongs to its branch and its pressures to its nodes.</returns>
    public ImmutableArray<UnknownDeclaration> DeclareUnknowns() => [];

    /// <inheritdoc/>
    public ImmutableArray<EquationDeclaration> DeclareEquations() => _equations;

    /// <summary>The index of <c>kv</c> among this kind's resolvable parameters.</summary>
    public const int KvIndex = 0;

    /// <summary>The index of <c>position</c> among this kind's resolvable parameters.</summary>
    public const int PositionIndex = 1;

    /// <inheritdoc/>
    /// <value>
    /// <c>kv</c>, which sizing chooses from the authority target, and <c>position</c>, which a
    /// controller sets and which promotion moves when a stated temperature can only be met by
    /// throttling.
    /// </value>
    public ImmutableArray<ResolvedParameter> Resolvable =>
    [
        new ResolvedParameter("kv", Kv, "m3/h", Minimum: 0),
        new ResolvedParameter("position", Position, "1", Minimum: 0, Maximum: 1),
    ];

    /// <inheritdoc/>
    public void EvaluateResiduals(in SolveContext context, Span<double> residuals)
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
