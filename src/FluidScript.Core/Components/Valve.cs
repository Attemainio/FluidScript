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
        new ResolvedParameter("kv", Kv, "m3/h"),
        new ResolvedParameter("position", Position, "1"),
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
    /// <param name="bypassConnected">
    /// Whether a connection reaches <c>c</c>. <see langword="false"/> makes this a two-way valve: two
    /// ports, one flow group, one Kv law, and no mass balance of its own.
    /// </param>
    public ThreeWayValve(
        string name,
        double kv,
        double position = 1,
        ValveCharacteristic characteristic = ValveCharacteristic.Linear,
        bool bypassConnected = true)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kv);

        Name = name;
        Kv = kv;
        Position = position;
        Characteristic = characteristic;
        BypassConnected = bypassConnected;

        Ports = bypassConnected
            ?
            [
                new Port { Name = "a", Role = PortRole.Bidirectional, IsOptional = false },
                new Port { Name = "b", Role = PortRole.Bidirectional, IsOptional = false },
                new Port { Name = "c", Role = PortRole.Bidirectional, IsOptional = true },
            ]
            :
            [
                new Port { Name = "a", Role = PortRole.Bidirectional, IsOptional = false },
                new Port { Name = "b", Role = PortRole.Bidirectional, IsOptional = false },
            ];

        // Three ports in one group is a junction element and a branch cannot cross it; two in one
        // group is a pass-through, so the branch walks straight through and its single flow makes
        // the mass balance an identity. That is why the two-way form drops a row rather than
        // keeping one that would be zeros.
        FlowGroups = bypassConnected ? [0, 0, 0] : [0, 0];

        _equations = bypassConnected
            ?
            [
                new EquationDeclaration(0, EquationKind.Mass, name, $"{name} mass balance", "kg/s"),
                new EquationDeclaration(0, EquationKind.ComponentConstraint, name, $"{name} Kv law, a-b", "kg/s"),
                new EquationDeclaration(0, EquationKind.ComponentConstraint, name, $"{name} Kv law, a-c", "kg/s"),
            ]
            : [new EquationDeclaration(0, EquationKind.ComponentConstraint, name, $"{name} Kv law, a-b", "kg/s")];
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Kind => "three_way_valve";

    /// <inheritdoc/>
    /// <value>
    /// <c>three_way</c> or <c>two_way</c>, decided by the topology rather than by a declaration — the
    /// same way an exchanger's mode is. A user who leaves <c>c</c> open has written a two-way valve,
    /// and this is where the tool says so.
    /// </value>
    public string? Mode => BypassConnected ? "three_way" : "two_way";

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

    /// <summary>Gets whether anything is connected to the bypass port.</summary>
    /// <value>
    /// <see langword="false"/> for a valve the script wired as a two-way. The registry makes <c>c</c>
    /// optional and <c>docs/functions/three-way-valve.md</c> says leaving it open is how a two-way
    /// valve is written; before <c>S-14a</c> this class ignored that and declared a Kv law for a port
    /// with no node behind it, which made two of the shipped samples over-specified by two.
    /// </value>
    public bool BypassConnected { get; }

    /// <inheritdoc/>
    public ImmutableArray<Port> Ports { get; }

    /// <inheritdoc/>
    /// <value>
    /// <strong>One group of three</strong> when the bypass is connected, which is what makes this a
    /// junction element: the flow divides here, so its three ports carry three different flows and no
    /// branch may pass through it. Wired as a two-way it is one group of two, and a branch does.
    /// </value>
    public ImmutableArray<int> FlowGroups { get; }

    /// <inheritdoc/>
    /// <value>Three: a mass balance and one Kv relation per path. One when wired as a two-way.</value>
    public int EquationCount => BypassConnected ? 3 : 1;

    /// <inheritdoc/>
    public ImmutableArray<UnknownDeclaration> DeclareUnknowns() => [];

    /// <inheritdoc/>
    public ImmutableArray<EquationDeclaration> DeclareEquations() => _equations;

    /// <summary>The index of <c>kv</c> among this kind's resolvable parameters.</summary>
    public const int KvIndex = 0;

    /// <summary>The index of <c>position</c> among this kind's resolvable parameters.</summary>
    public const int PositionIndex = 1;

    /// <inheritdoc/>
    /// <value>
    /// The same two a two-way valve offers, in the same order. <c>position</c> is the one a mixed
    /// inlet temperature promotes (<c>23</c>): only the split can move it, and the bypass path reads
    /// <c>1 - position</c> from the same number, so one unknown moves both legs.
    /// </value>
    public ImmutableArray<ResolvedParameter> Resolvable =>
    [
        new ResolvedParameter("kv", Kv, "m3/h"),
        new ResolvedParameter("position", Position, "1"),
    ];

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

        var kv = context.Parameter(KvIndex, Kv);
        var position = context.Parameter(PositionIndex, Position);

        var controlledPath = -context.Flows[1] - ValveLaw.MassFlow(
            kv * ValveLaw.Opening(position, Characteristic),
            common.Pressure - controlled.Pressure,
            (common.Density + controlled.Density) / 2);

        if (!BypassConnected)
        {
            residuals[0] = controlledPath;
            return;
        }

        var bypass = context.Ports[2];

        residuals[0] = context.Flows[0] + context.Flows[1] + context.Flows[2];
        residuals[1] = controlledPath;

        residuals[2] = -context.Flows[2] - ValveLaw.MassFlow(
            kv * ValveLaw.Opening(1 - position, Characteristic),
            common.Pressure - bypass.Pressure,
            (common.Density + bypass.Density) / 2);
    }
}
