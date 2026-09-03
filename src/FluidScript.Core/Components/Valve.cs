using System.Collections.Immutable;

using FluidScript.Core.Language;
using FluidScript.Core.Units;

namespace FluidScript.Core.Components;

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

        _equations = [new EquationDeclaration(0, EquationKind.ComponentConstraint, name, $"{name} Kv law", "kg/s")];
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
    /// <value>One: the Kv relation.</value>
    public int EquationCount => 1;

    /// <inheritdoc/>
    /// <returns>Empty. Its flow belongs to its branch and its pressures to its nodes.</returns>
    public ImmutableArray<UnknownDeclaration> DeclareUnknowns() => [];

    /// <inheritdoc/>
    public ImmutableArray<EquationDeclaration> DeclareEquations() => _equations;

    /// <inheritdoc/>
    public void EvaluateResiduals(in SolveContext context, Span<double> residuals)
    {
        var drop = context.Ports[0].Pressure - context.Ports[1].Pressure;
        var density = (context.Ports[0].Density + context.Ports[1].Density) / 2;

        residuals[0] = context.Flows[0]
            - ValveLaw.MassFlow(Kv * ValveLaw.Opening(Position, Characteristic), drop, density);
    }
}

/// <summary>A three-port mixing or diverting valve.</summary>
/// <remarks>
/// <para>
/// <strong>All three ports are bidirectional, because both arrangements are real.</strong> Typing them
/// inlet/outlet/outlet describes a <em>diverting</em> valve — one stream in at <c>a</c>, split between
/// <c>b</c> and <c>c</c>. The commonest three-way valve in hydronics is a <em>mixing</em> valve: two
/// streams in at <c>b</c> and <c>c</c>, one out at <c>a</c>, which is how every weather-compensated
/// heating circuit is built. Fixed roles made that expressible only by leaning on reverse flow being
/// legal, which left the port roles wrong, the canvas arrows wrong, and <c>FS4009</c> firing on a
/// correct design.
/// </para>
/// <para>
/// <strong>The mass balance is the valve's own equation, not a node's.</strong> A three-way valve is
/// the only element in the graph where a flow divides without a node.
/// </para>
/// </remarks>
public sealed class ThreeWayValve : IFlowComponent
{
    private readonly ImmutableArray<EquationDeclaration> _equations;

    /// <summary>Initializes a three-way valve.</summary>
    /// <param name="name">The user's identifier.</param>
    /// <param name="kv">The rated flow coefficient, m³/h at 1 bar.</param>
    /// <param name="position">The opening between <c>a</c> and <c>b</c>, 0 to 1.</param>
    /// <param name="characteristic">Which characteristic the controlled path follows.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kv"/> is not positive.</exception>
    public ThreeWayValve(
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

        _equations =
        [
            new EquationDeclaration(0, EquationKind.Mass, name, $"{name} mass balance", "kg/s"),
            new EquationDeclaration(0, EquationKind.ComponentConstraint, name, $"{name} Kv law, a-b", "kg/s"),
            new EquationDeclaration(0, EquationKind.ComponentConstraint, name, $"{name} Kv law, a-c", "kg/s"),
        ];
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Kind => "three_way_valve";

    /// <inheritdoc/>
    /// <value>Always <see langword="null"/>.</value>
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

    /// <summary>Gets the opening between <c>a</c> and <c>b</c>.</summary>
    /// <value>
    /// 0 to 1. <strong>1 is fully open between <c>a</c> and <c>b</c></strong>, whichever way the fluid
    /// moves through them — the meaning does not change between a mixing and a diverting arrangement.
    /// </value>
    public double Position { get; init; }

    /// <summary>Gets which characteristic the controlled path follows.</summary>
    public ValveCharacteristic Characteristic { get; }

    /// <inheritdoc/>
    public ImmutableArray<Port> Ports { get; } =
    [
        new Port { Name = "a", Role = PortRole.Bidirectional, IsOptional = false },
        new Port { Name = "b", Role = PortRole.Bidirectional, IsOptional = false },
        new Port { Name = "c", Role = PortRole.Bidirectional, IsOptional = true },
    ];

    /// <inheritdoc/>
    /// <value>Three: a mass balance and one Kv relation per path.</value>
    public int EquationCount => 3;

    /// <inheritdoc/>
    public ImmutableArray<UnknownDeclaration> DeclareUnknowns() => [];

    /// <inheritdoc/>
    public ImmutableArray<EquationDeclaration> DeclareEquations() => _equations;

    /// <inheritdoc/>
    /// <remarks>
    /// The bypass path takes the <em>complementary</em> opening: as <c>a-b</c> opens, <c>a-c</c>
    /// closes. With signed flows the single balance <c>ṁ_a + ṁ_b + ṁ_c = 0</c> covers mixing and
    /// diverting alike, which is why the arrangement is read from the topology rather than declared.
    /// </remarks>
    public void EvaluateResiduals(in SolveContext context, Span<double> residuals)
    {
        var common = context.Ports[0];
        var controlled = context.Ports[1];
        var bypass = context.Ports[2];

        residuals[0] = context.Flows[0] + context.Flows[1] + context.Flows[2];

        residuals[1] = -context.Flows[1] - ValveLaw.MassFlow(
            Kv * ValveLaw.Opening(Position, Characteristic),
            common.Pressure - controlled.Pressure,
            (common.Density + controlled.Density) / 2);

        residuals[2] = -context.Flows[2] - ValveLaw.MassFlow(
            Kv * ValveLaw.Opening(1 - Position, Characteristic),
            common.Pressure - bypass.Pressure,
            (common.Density + bypass.Density) / 2);
    }
}
