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

    /// <summary>Initializes a duty-mode exchanger.</summary>
    /// <param name="name">The user's identifier.</param>
    /// <param name="power">W transferred, positive when side 1 gains heat.</param>
    /// <param name="designPressureDrop">Pa across side 1 at the design flow; 0 for an ideal block.</param>
    /// <param name="designFlow">kg/s, the flow the design drop belongs to.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="designPressureDrop"/> is negative, or it is positive while
    /// <paramref name="designFlow"/> is not.
    /// </exception>
    public HeatExchanger(
        string name, double power, double designPressureDrop = 0, double designFlow = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(designPressureDrop);

        if (designPressureDrop > 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(designFlow);
        }

        Name = name;
        Power = power;
        DesignPressureDrop = designPressureDrop;
        DesignFlow = designFlow;

        // Folded once here rather than per iteration: dp_design / mdot_design^2 is constant, and the
        // residual then costs one multiply instead of a divide inside the hot path.
        _resistance = designPressureDrop > 0 ? designPressureDrop / (designFlow * designFlow) : 0;

        _equations =
        [
            new EquationDeclaration(0, EquationKind.Energy, name, $"{name} duty", "W"),
            new EquationDeclaration(0, EquationKind.Pressure, name, $"{name} side-1 drop", "Pa"),
        ];
    }

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
    /// <value>Two: side 1's energy relation and its momentum relation.</value>
    public int EquationCount => 2;

    /// <inheritdoc/>
    /// <returns>Empty. Its flow belongs to its branch and its pressures to its nodes.</returns>
    public ImmutableArray<UnknownDeclaration> DeclareUnknowns() => [];

    /// <inheritdoc/>
    public ImmutableArray<EquationDeclaration> DeclareEquations() => _equations;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <code>
    /// Q̇  = ṁ (h_out − h_in)
    /// Δp = dp_design · (ṁ/ṁ_design)²
    /// </code>
    /// </para>
    /// <para>
    /// <strong>The energy relation is written against solved port enthalpies, not against stated
    /// terminal temperatures.</strong> Convention 3 is that components consume and produce
    /// <c>(p, h)</c> and temperature is derived — an energy balance produces an enthalpy, and going via
    /// a temperature means inverting <c>cp</c>. A stated <c>in=50</c> is a boundary condition on the
    /// attached node, which lowering handles; it is not this component's equation (<c>C-19</c>).
    /// </para>
    /// <para>
    /// The momentum term is <c>ṁ·|ṁ|</c> rather than <c>ṁ²</c>, so a reversed flow loses pressure in
    /// the direction it is going instead of gaining it.
    /// </para>
    /// </remarks>
    public void EvaluateResiduals(in SolveContext context, Span<double> residuals)
    {
        var flow = context.Flows[0];
        var inlet = context.Ports[0];
        var outlet = context.Ports[1];

        residuals[0] = Power - (flow * (outlet.Enthalpy - inlet.Enthalpy));
        residuals[1] = inlet.Pressure - outlet.Pressure - (_resistance * flow * Math.Abs(flow));
    }

    /// <summary>The flow a stated duty implies across a stated temperature rise.</summary>
    /// <param name="specificHeat">J/(kg·K).</param>
    /// <param name="temperatureRise">K, the magnitude of the change across side 1.</param>
    /// <returns>kg/s.</returns>
    /// <remarks>
    /// <para>
    /// A reported relation, not an equation. <c>power</c>, <c>in</c>, <c>out</c> and <c>flow</c> are
    /// related by side 1's energy balance, so any three fix the fourth — and stating all four is
    /// <c>FS2101</c>, which reports the value the other three imply. This computes that value.
    /// </para>
    /// <para>
    /// It is not on the iteration path, so unlike the residual it may use a specific heat the caller
    /// looked up.
    /// </para>
    /// </remarks>
    public double ImpliedFlow(double specificHeat, double temperatureRise) =>
        Math.Abs(Power) / (specificHeat * temperatureRise);
}
