using System.Collections.Immutable;

using FluidScript.Core.Language;
using FluidScript.Core.Units;

namespace FluidScript.Core.Components;

/// <summary>Which of the three exchanger modes lowering resolved.</summary>
/// <remarks>
/// <strong>There is no script <c>mode=</c> parameter.</strong> The mode is computed from what the
/// script connected and stated, in a fixed precedence, so that adding real connections to an
/// external-profile design has one predictable meaning (<c>D-19</c>).
/// </remarks>
public enum ExchangerMode
{
    /// <summary>A stated duty crossing the model boundary. No area, effectiveness or approach claim.</summary>
    Duty,

    /// <summary>Side 2 is an external stated or sized boundary profile, not a graph branch.</summary>
    Rated,

    /// <summary>Both sides are solved hydraulic streams, coupled by ε-NTU.</summary>
    Coupled,
}

/// <summary>A heat source, a heat consumer, or a two-sided exchanger.</summary>
/// <remarks>
/// <para>
/// One kind covers all three, and <strong>a negative <c>power</c> is a consumer</strong>. Duty is
/// transferred, positive when side 1 gains heat.
/// </para>
/// <para>
/// <strong>Only Duty mode is built here.</strong> The rated and coupled modes are <c>P4.1</c>'s, and
/// deliberately so: that package builds ε-NTU <em>and</em> LMTD as two routes sharing no code, which is
/// what turns the substation's UA = 12 071 W/K into a validation rather than a regression. Written here
/// as one route with a switch, the agreement would prove nothing. A <c>Rated</c> or <c>Coupled</c>
/// instance is refused rather than silently behaving like a Duty one.
/// </para>
/// <para>
/// <strong>The sides are numbered rather than named.</strong> Not <c>hot</c>/<c>cold</c> and not
/// <c>primary</c>/<c>secondary</c>: which side is hot is a solved outcome, and a script that says
/// <c>hot_in=40</c> when the solve makes it the cold side is worse than one that says nothing.
/// </para>
/// </remarks>
public sealed class HeatExchanger : IFlowComponent
{
    private readonly ImmutableArray<EquationDeclaration> _equations;
    private readonly double _resistance;
    private readonly double _secondaryResistance;

    /// <summary>Initializes a duty-mode exchanger.</summary>
    /// <param name="name">The user's identifier.</param>
    /// <param name="power">W transferred, positive when side 1 gains heat.</param>
    /// <param name="designPressureDrop">Pa across side 1 at the design flow; 0 for an ideal block.</param>
    /// <param name="designFlow">kg/s, the flow the design drop belongs to.</param>
    /// <param name="secondaryPressureDrop">Pa across side 2 at its own design flow; 0 for an ideal side.</param>
    /// <param name="secondaryFlow">kg/s, the flow the secondary drop belongs to.</param>
    /// <param name="secondarySideConnected">
    /// Whether the script wired <c>in2</c>/<c>out2</c>. It decides how many momentum relations this
    /// component declares, and lowering is the only thing that knows it (<c>D-63</c>).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either drop is negative, or one is positive while its flow is not.
    /// </exception>
    /// <remarks>
    /// <strong>Side 2 gets a momentum relation whenever it is connected, and that is <c>S-14b</c>.</strong>
    /// Two branches crossing this component means <c>Relations()</c> credits it with two pressure
    /// equations; it declared one, so <c>m2-substation</c> counted 23 for 23, assembled 22 rows, and
    /// could not be solved at all. A real second stream has a real pressure drop, and even an ideal one
    /// has the relation <c>p_in2 = p_out2</c> -- which is a statement, where declaring nothing is a hole.
    /// </remarks>
    public HeatExchanger(
        string name,
        double power,
        double designPressureDrop = 0,
        double designFlow = 0,
        double secondaryPressureDrop = 0,
        double secondaryFlow = 0,
        bool secondarySideConnected = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(designPressureDrop);
        ArgumentOutOfRangeException.ThrowIfNegative(secondaryPressureDrop);

        if (designPressureDrop > 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(designFlow);
        }

        if (secondaryPressureDrop > 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(secondaryFlow);
        }

        Name = name;
        Power = power;
        DesignPressureDrop = designPressureDrop;
        DesignFlow = designFlow;
        SecondaryPressureDrop = secondaryPressureDrop;
        SecondaryFlow = secondaryFlow;
        SecondarySideConnected = secondarySideConnected;

        // Folded once here rather than per iteration: dp_design / mdot_design^2 is constant, and the
        // residual then costs one multiply instead of a divide inside the hot path.
        _resistance = designPressureDrop > 0 ? designPressureDrop / (designFlow * designFlow) : 0;
        _secondaryResistance =
            secondaryPressureDrop > 0 ? secondaryPressureDrop / (secondaryFlow * secondaryFlow) : 0;

        _equations = secondarySideConnected
            ?
            [
                new EquationDeclaration(0, EquationKind.Pressure, name, $"{name} side-1 drop", "Pa"),
                new EquationDeclaration(1, EquationKind.Pressure, name, $"{name} side-2 drop", "Pa"),
            ]
            :
            [
                new EquationDeclaration(0, EquationKind.Pressure, name, $"{name} side-1 drop", "Pa"),
            ];
    }

    /// <summary>Gets the side-2 pressure drop at its design flow.</summary>
    /// <value>Pa. Zero means an ideal side, which still carries the relation <c>p_in2 = p_out2</c>.</value>
    public double SecondaryPressureDrop { get; }

    /// <summary>Gets the flow the secondary pressure drop belongs to.</summary>
    /// <value>
    /// kg/s. <strong>Sized only when a script states it.</strong> No rule chooses it, because a rule
    /// sees one branch and this component sits on two -- the same limit that keeps parallel-set
    /// balancing out of <c>ISizer</c> (<c>C-49</c>). Until it is stated, side 2 is ideal.
    /// </value>
    public double SecondaryFlow { get; }

    /// <summary>Gets whether the script wired the secondary ports.</summary>
    /// <value><see langword="true"/> when <c>in2</c>/<c>out2</c> are connected, which adds a row.</value>
    public bool SecondarySideConnected { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Kind => "heat_exchanger";

    /// <inheritdoc/>
    /// <value>The canonical mode name. Duty is the only one this class evaluates.</value>
    public string? Mode => ExchangerMode.Duty.ToString().ToLowerInvariant();

    /// <inheritdoc/>
    public ImmutableDictionary<string, Quantity> StatedParameters { get; init; }
        = ImmutableDictionary<string, Quantity>.Empty;

    /// <inheritdoc/>
    public ImmutableDictionary<string, Quantity> SizedParameters { get; init; }
        = ImmutableDictionary<string, Quantity>.Empty;

    /// <inheritdoc/>
    public ImmutableDictionary<string, Quantity> DefaultParameters { get; init; }
        = ImmutableDictionary<string, Quantity>.Empty;

    /// <summary>Gets the duty transferred.</summary>
    /// <value>W, positive when side 1 gains heat. A negative value is a consumer.</value>
    public double Power { get; }

    /// <summary>Gets the side-1 pressure drop at the design flow.</summary>
    /// <value>Pa. Zero means an ideal block with no hydraulic resistance.</value>
    public double DesignPressureDrop { get; }

    /// <summary>Gets the flow the design pressure drop belongs to.</summary>
    /// <value>kg/s.</value>
    public double DesignFlow { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// The secondary ports are optional, so inference rule I3 skips them and a duty declaration is
    /// complete without fabricated nodes.
    /// </remarks>
    /// <inheritdoc/>
    /// <value>
    /// <strong>Two groups of two</strong>, <c>{in, out}</c> and <c>{in2, out2}</c>, whatever the mode.
    /// Nothing flows from one side to the other, so this is <em>not</em> a junction element despite
    /// having four ports — it is interior to a branch on each side. Giving it one group would assert
    /// that fluid crosses between the sides and hand it a mass balance that is false whenever the two
    /// carry different flows.
    /// </value>
    /// <remarks>
    /// <c>23</c> tabulates duty mode as one group of two, because side 2 does not exist there. The
    /// difference is in what is <em>connected</em>, not in how the ports partition, and a component
    /// that had to know its own mode to answer would need lowering to tell it (<c>D-63</c>).
    /// </remarks>
    public ImmutableArray<int> FlowGroups { get; } = [0, 0, 1, 1];

    /// <inheritdoc/>
    public ImmutableArray<Port> Ports { get; } =
    [
        new Port { Name = "in", Role = PortRole.Inlet, IsOptional = false },
        new Port { Name = "out", Role = PortRole.Outlet, IsOptional = false },
        new Port { Name = "in2", Role = PortRole.Inlet, IsOptional = true },
        new Port { Name = "out2", Role = PortRole.Outlet, IsOptional = true },
    ];

    /// <inheritdoc/>
    /// <value>
    /// One momentum relation per <em>connected</em> side: one for a duty block, two once <c>in2</c> and
    /// <c>out2</c> are wired.
    /// <para>
    /// <strong>It counts sides rather than ports because the counting table does.</strong> Two branches
    /// crossing this component earn it two pressure relations, and declaring one left
    /// <c>m2-substation</c> a row short of square -- solvable by nothing, and reported as a counting
    /// table that disagreed with its own assembly (<c>S-14b</c>).
    /// </para>
    /// <para>
    /// <strong>The duty is not a row here.</strong> It was, and it asserted the same relation as the
    /// balance of the node this exchanger discharges into — that node's balance reduces to
    /// <c>h_own = h_arriving</c>, which is <c>Q = ṁ(h_out − h_in)</c> with <c>Q</c> missing — so the
    /// assembled system carried one row too many per exchanger. The duty is now an energy injection
    /// into the node's own balance (<c>D-69</c>, <see cref="EvaluateEnergyInjection"/>).
    /// </para>
    /// </value>
    public int EquationCount => SecondarySideConnected ? 2 : 1;

    /// <inheritdoc/>
    /// <returns>Empty. Its flow belongs to its branch and its pressures to its nodes.</returns>
    public ImmutableArray<UnknownDeclaration> DeclareUnknowns() => [];

    /// <inheritdoc/>
    public ImmutableArray<EquationDeclaration> DeclareEquations() => _equations;

    /// <summary>The index of <c>power</c> among this kind's resolvable parameters.</summary>
    public const int PowerIndex = 0;

    /// <inheritdoc/>
    /// <value>
    /// The duty, W, signed: positive into the circuit. It is what a stated <c>out</c> promotes on an
    /// exchanger whose <c>power</c> the script left free, which is the duty-follows-temperature reading
    /// of a radiator sized to a room rather than to a number.
    /// </value>
    public ImmutableArray<ResolvedParameter> Resolvable => [new ResolvedParameter("power", Power, "W")];

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <code>
    /// Δp = dp_design · (ṁ/ṁ_design)²
    /// </code>
    /// </para>
    /// <para>
    /// The momentum term is <c>ṁ·|ṁ|</c> rather than <c>ṁ²</c>, so a reversed flow loses pressure in
    /// the direction it is going instead of gaining it.
    /// </para>
    /// <para>
    /// The duty is in <see cref="EvaluateEnergyInjection"/>, not here. It is still written against
    /// solved port enthalpies rather than stated terminal temperatures — convention 3 is that
    /// components consume and produce <c>(p, h)</c> and temperature is derived, and a stated
    /// <c>in=50</c> is a boundary condition on the attached node rather than this component's equation
    /// (<c>C-19</c>).
    /// </para>
    /// </remarks>
    public void EvaluateResiduals(in SolveContext context, Span<double> residuals)
    {
        var flow = context.Flows[0];

        residuals[0] = context.Ports[0].Pressure - context.Ports[1].Pressure
            - (_resistance * flow * Math.Abs(flow));

        if (!SecondarySideConnected)
        {
            return;
        }

        // Ports 2 and 3 are `in2`/`out2`, and `FlowGroups` puts them in their own group -- nothing
        // crosses between the sides, so this reads side 2's own flow rather than side 1's.
        var secondary = context.Flows[2];

        residuals[1] = context.Ports[2].Pressure - context.Ports[3].Pressure
            - (_secondaryResistance * secondary * Math.Abs(secondary));
    }

    /// <inheritdoc/>
    /// <value>Always. Injecting heat is what the kind is for, and a zero duty injects zero.</value>
    public bool InjectsEnergy => true;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The whole duty lands on the side the exchanger discharges through, and
    /// <see cref="Smoothing.ForwardShare"/> is what makes that survive a reversal: at
    /// <c>ṁ &gt; 0</c> it is all on <c>out</c>, at <c>ṁ &lt; 0</c> all on <c>in</c>, and C¹ between. The two
    /// entries sum to <see cref="Power"/> at every flow, which is the invariant worth testing — the
    /// exchanger moves a fixed amount of heat into the circuit however the fluid runs.
    /// </para>
    /// <para>
    /// <strong>When side 2 is connected it takes the same duty out, and leaving it at zero was
    /// `S-31`.</strong> This used to read "duty mode makes no claim about a second stream", deferring
    /// the <c>-Q</c> to <c>P4.1</c>'s rated model. But a script that wires <c>in2</c>/<c>out2</c> and
    /// states <c>power</c> has already made the claim: 150 kW crossing an exchanger leaves one stream
    /// and enters the other. Injecting it on one side only <strong>creates energy from nothing</strong> — on
    /// <c>m2-substation</c> the primary stayed at 85 °C end to end instead of returning at 45, because
    /// nothing ever took its heat away. What <c>P4.1</c> owns is how much duty crosses when the script
    /// does <em>not</em> say (ε-NTU, LMTD, a rated point); it does not own conservation.
    /// </para>
    /// <para>
    /// Side 2 gets its own <see cref="Smoothing.ForwardShare"/> from its own flow, because the two
    /// streams are usually counter-current and a shared share would put the heat on the wrong port the
    /// moment they were.
    /// </para>
    /// </remarks>
    public void EvaluateEnergyInjection(in SolveContext context, Span<double> injection)
    {
        injection.Clear();

        var forward = Smoothing.ForwardShare(context.Flows[0]);
        var duty = context.Parameter(PowerIndex, Power);

        injection[0] = duty * (1 - forward);
        injection[1] = duty * forward;

        if (!SecondarySideConnected)
        {
            return;
        }

        var secondary = Smoothing.ForwardShare(context.Flows[2]);

        injection[2] = -duty * (1 - secondary);
        injection[3] = -duty * secondary;
    }

    /// <summary>The flow a stated duty implies across a stated temperature rise.</summary>
    /// <param name="specificHeat">J/(kg·K), positive.</param>
    /// <param name="temperatureRise">K, the magnitude of the change across side 1.</param>
    /// <returns>kg/s, or <see cref="double.NaN"/> when the rise or the specific heat is not positive: no flow carries a duty across no temperature change.</returns>
    /// <remarks>
    /// <para>
    /// A reported relation, not an equation. <c>power</c>, <c>in</c>, <c>out</c> and <c>flow</c> are
    /// related by side 1's energy balance, so any three fix the fourth, and stating all four is
    /// <c>FS2101</c>. That diagnostic names the group and <em>not</em> this value (<c>C-21</c>): the
    /// binder raises it before any substance is resolved, so it has no specific heat to quote. Nothing
    /// in the pipeline calls this today; it states the relation the tests and a future lowering-stage
    /// message hold the exchanger to.
    /// </para>
    /// <para>
    /// It is not on the iteration path, so unlike the residual it may use a specific heat the caller
    /// looked up.
    /// </para>
    /// </remarks>
    public double ImpliedFlow(double specificHeat, double temperatureRise) =>
        specificHeat > 0 && temperatureRise > 0
            ? Math.Abs(Power) / (specificHeat * temperatureRise)
            : double.NaN;
}
