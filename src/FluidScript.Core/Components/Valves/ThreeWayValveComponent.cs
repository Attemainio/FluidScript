using System.Collections.Immutable;

using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Components.Valves;

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
public sealed class ThreeWayValveComponent : ValveComponentBase
{
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
    /// <param name="leakage">The fraction of <paramref name="kv"/> a leg passes at its stop, 0 to 1. The registry's default is 2 %, Belimo's B–AB leakage class I (<c>D-135</c>).</param>
    /// <param name="arrangement">The service the script declares the body for, from its spelling; <see cref="ValveArrangement.Unspecified"/> for a bare <c>three_way_valve</c> (<c>D-136</c>).</param>
    public ThreeWayValveComponent(
        string name,
        double kv,
        double position = 1,
        ValveCharacteristic characteristic = ValveCharacteristic.Linear,
        bool bypassConnected = true,
        double leakage = ValveLaw.LegLeakage,
        ValveArrangement arrangement = ValveArrangement.Unspecified)
        : base(name, kv, position, characteristic)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(leakage);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(leakage, 1);

        BypassConnected = bypassConnected;
        Leakage = leakage;
        Arrangement = arrangement;

        // `ab` is the common port and `a`/`b` are the two switched ones, which is how valve bodies are
        // labelled: a mixing valve is A + B -> AB and a diverting valve is AB -> A + B. The names used
        // to be `a` common, `b` controlled, `c` bypass, which called the *common* port `a` and so meant
        // the opposite of the label cast into the iron.
        Ports = bypassConnected
            ?
            [
                new Port { Name = "ab", Role = PortRole.Bidirectional, IsOptional = false },
                new Port { Name = "a", Role = PortRole.Bidirectional, IsOptional = false },
                new Port { Name = "b", Role = PortRole.Bidirectional, IsOptional = true },
            ]
            :
            [
                new Port { Name = "ab", Role = PortRole.Bidirectional, IsOptional = false },
                new Port { Name = "a", Role = PortRole.Bidirectional, IsOptional = false },
            ];

        // Three ports in one group is a junction element and a branch cannot cross it; two in one
        // group is a pass-through, so the branch walks straight through and its single flow makes
        // the mass balance an identity. That is why the two-way form drops a row rather than
        // keeping one that would be zeros.
        FlowGroups = bypassConnected ? [0, 0, 0] : [0, 0];

        Equations = bypassConnected
            ?
            [
                new EquationDeclaration(0, EquationKind.Mass, name, $"{name} mass balance", "kg/s"),
                new EquationDeclaration(0, EquationKind.ComponentConstraint, name, $"{name} Kv law, ab-a", "kg/s") { SteepInPressure = true },
                new EquationDeclaration(0, EquationKind.ComponentConstraint, name, $"{name} Kv law, ab-b", "kg/s") { SteepInPressure = true },
            ]
            : [new EquationDeclaration(0, EquationKind.ComponentConstraint, name, $"{name} Kv law, ab-a", "kg/s") { SteepInPressure = true }];
    }

    /// <inheritdoc/>
    public override string Kind => "three_way_valve";

    /// <inheritdoc/>
    /// <value>
    /// <c>three_way</c> or <c>two_way</c>, decided by the topology rather than by a declaration — the
    /// same way an exchanger's mode is. A user who leaves <c>c</c> open has written a two-way valve,
    /// and this is where the tool says so.
    /// </value>
    public override string? Mode => BypassConnected ? "three_way" : "two_way";

    /// <summary>Gets whether anything is connected to the bypass port.</summary>
    /// <value>
    /// <see langword="false"/> for a valve the script wired as a two-way. The registry makes <c>c</c>
    /// optional and <c>docs/functions/three-way-valve.md</c> says leaving it open is how a two-way
    /// valve is written; before <c>S-14a</c> this class ignored that and declared a Kv law for a port
    /// with no node behind it, which made two of the shipped samples over-specified by two.
    /// </value>
    public bool BypassConnected { get; }

    /// <summary>Gets the fraction of <see cref="ValveComponentBase.Kv"/> a switched leg passes at its stop.</summary>
    /// <value>Dimensionless, 0 to 1. The body's rated leakage, not the characteristic's: <c>D-135</c>.</value>
    public double Leakage { get; }

    /// <summary>Gets the service the script declared this body for, or <see cref="ValveArrangement.Unspecified"/>.</summary>
    /// <value>From the kind as written: <c>mixing_valve</c>, <c>diverting_valve</c>, or neither. The arrangement it runs in is the solve's to find; <c>FS4012</c> compares the two (<c>C-65</c>, <c>D-136</c>).</value>
    public ValveArrangement Arrangement { get; }

    /// <inheritdoc/>
    public override ImmutableArray<Port> Ports { get; }

    /// <inheritdoc/>
    /// <value>
    /// <strong>One group of three</strong> when the bypass is connected, which is what makes this a
    /// junction element: the flow divides here, so its three ports carry three different flows and no
    /// branch may pass through it. Wired as a two-way it is one group of two, and a branch does.
    /// </value>
    public override ImmutableArray<int> FlowGroups { get; }

    /// <inheritdoc/>
    /// <value>Three: a mass balance and one Kv relation per path. One when wired as a two-way.</value>
    public override int EquationCount => BypassConnected ? 3 : 1;

    /// <inheritdoc/>
    /// <remarks>
    /// The bypass path takes the <em>complementary</em> opening: as <c>a-b</c> opens, <c>a-c</c>
    /// closes. With signed flows the single balance <c>ṁ_a + ṁ_b + ṁ_c = 0</c> covers mixing and
    /// diverting alike, which is why the arrangement is read from the topology rather than declared.
    /// </remarks>
    public override void EvaluateResiduals(in SolveContext context, Span<double> residuals)
    {
        var common = context.Ports[0];
        var controlled = context.Ports[1];

        var kv = context.Parameter(KvIndex, Kv);
        var position = context.Parameter(PositionIndex, Position);

        // `LegOpening`, not `Opening`: a leg keeps the body's leakage at the stop so the position column
        // survives the other leg opening fully (`D-122`, `D-135`).
        var controlledPath = -context.Flows[1] - ValveLaw.MassFlow(
            kv * ValveLaw.LegOpening(position, Characteristic, Leakage),
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
            kv * ValveLaw.LegOpening(1 - position, Characteristic, Leakage),
            common.Pressure - bypass.Pressure,
            (common.Density + bypass.Density) / 2);
    }
}
