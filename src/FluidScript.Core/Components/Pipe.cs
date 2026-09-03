using System.Collections.Immutable;

using FluidScript.Core.Language;
using FluidScript.Core.Units;

namespace FluidScript.Core.Components;

/// <summary>A pressure drop between two nodes.</summary>
/// <remarks>
/// <para>
/// One momentum equation: <c>Δp = (f·L/D + K)·ρv²/2 + ρgΔz</c>, with the Darcy friction factor from
/// Colebrook–White via the Serghide explicit approximation.
/// </para>
/// <para>
/// <strong>It takes an inside diameter, never a DN designation.</strong> DN25 steel pipe has a 27.3 mm
/// bore, and computing an area from 25 mm is a 16 % area error and roughly a factor of two in pressure
/// gradient, with nothing in the result looking wrong. Turning a designation into a bore is the
/// catalogue's job (<c>27</c>, <c>P3.5</c>); by the time a component exists the number is a length.
/// </para>
/// <para>
/// <strong>Discretization is not modelled here.</strong> <c>nodes=n</c> lowers to n internal nodes and
/// n+1 sub-pipes, each carrying <c>length/(n+1)</c> — so a discretized pipe is n+1 of these, and
/// <c>23</c> builds them. A component that also knew about its own subdivision would be two models.
/// </para>
/// </remarks>
public sealed class Pipe : IFlowComponent
{
    /// <summary>Standard gravity, m/s².</summary>
    private const double Gravity = 9.80665;

    /// <summary>Below this Reynolds number the flow is laminar.</summary>
    private const double LaminarLimit = 2300;

    /// <summary>Above this Reynolds number the flow is fully turbulent.</summary>
    private const double TurbulentLimit = 4000;

    private readonly ImmutableArray<EquationDeclaration> _equations;

    /// <summary>Initializes a pipe from its resolved geometry.</summary>
    /// <param name="name">The user's identifier, or the generated name of an inferred pipe.</param>
    /// <param name="length">m, along the pipe.</param>
    /// <param name="insideDiameter">m, the catalogue bore — not the DN designation.</param>
    /// <param name="roughness">m, absolute wall roughness.</param>
    /// <param name="minorLoss">The sum of explicit fitting coefficients K, dimensionless.</param>
    /// <param name="elevation">m, outlet height minus inlet height.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="length"/> or <paramref name="insideDiameter"/> is not positive, or
    /// <paramref name="roughness"/> or <paramref name="minorLoss"/> is negative.
    /// </exception>
    public Pipe(
        string name,
        double length,
        double insideDiameter,
        double roughness = 0.045e-3,
        double minorLoss = 0,
        double elevation = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(insideDiameter);
        ArgumentOutOfRangeException.ThrowIfNegative(roughness);
        ArgumentOutOfRangeException.ThrowIfNegative(minorLoss);

        Name = name;
        Length = length;
        InsideDiameter = insideDiameter;
        Roughness = roughness;
        MinorLoss = minorLoss;
        Elevation = elevation;
        FlowArea = Math.PI * insideDiameter * insideDiameter / 4;

        _equations =
            [new EquationDeclaration(0, EquationKind.Pressure, name, $"{name} momentum", "Pa")];
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Kind => "pipe";

    /// <inheritdoc/>
    /// <value>Always <see langword="null"/>: a pipe has no modes.</value>
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

    /// <summary>Gets the pipe's length.</summary>
    /// <value>m.</value>
    public double Length { get; }

    /// <summary>Gets the inside diameter every hydraulic calculation uses.</summary>
    /// <value>m, the catalogue bore.</value>
    public double InsideDiameter { get; }

    /// <summary>Gets the absolute wall roughness.</summary>
    /// <value>m. The default is 0.045 mm, commercial steel.</value>
    public double Roughness { get; }

    /// <summary>Gets the sum of explicit fitting coefficients.</summary>
    /// <value>
    /// Dimensionless K. Zero when omitted: this is the design's stated fittings, and no elbow is ever
    /// inferred from a diagram bend, because auto-layout geometry is not physical routing.
    /// </value>
    public double MinorLoss { get; }

    /// <summary>Gets the outlet height minus the inlet height.</summary>
    /// <value>m, positive when the outlet is higher.</value>
    public double Elevation { get; }

    /// <summary>Gets the cross-sectional flow area.</summary>
    /// <value>m².</value>
    public double FlowArea { get; }

    /// <inheritdoc/>
    public ImmutableArray<Port> Ports { get; } =
    [
        new Port { Name = "in", Role = PortRole.Inlet, IsOptional = false },
        new Port { Name = "out", Role = PortRole.Outlet, IsOptional = false },
    ];

    /// <inheritdoc/>
    /// <value>One group of two: everything entering a pipe leaves it.</value>
    public ImmutableArray<int> FlowGroups { get; } = [0, 0];

    /// <inheritdoc/>
    /// <value>One: the momentum equation.</value>
    public int EquationCount => 1;

    /// <inheritdoc/>
    /// <returns>Empty. A pipe's flow belongs to its branch and its pressures to its nodes.</returns>
    public ImmutableArray<UnknownDeclaration> DeclareUnknowns() => [];

    /// <inheritdoc/>
    public ImmutableArray<EquationDeclaration> DeclareEquations() => _equations;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>(p_in − p_out) − Δp_friction − ρgΔz = 0</c>, with the friction term written in <c>v·|v|</c>
    /// so that a reversed flow opposes itself rather than driving itself.
    /// </remarks>
    public void EvaluateResiduals(in SolveContext context, Span<double> residuals)
    {
        var inlet = context.Ports[0];
        var outlet = context.Ports[1];

        // Mean properties across the pipe rather than the upstream port's. The upstream choice is a
        // switch on flow direction, and a switch is the thing this whole file is written to avoid; the
        // mean is smooth through a reversal and differs by nothing worth having over one pipe.
        var density = (inlet.Density + outlet.Density) / 2;
        var viscosity = (inlet.DynamicViscosity + outlet.DynamicViscosity) / 2;

        var velocity = context.Flows[0] / (density * FlowArea);

        residuals[0] = inlet.Pressure - outlet.Pressure
            - PressureDrop(velocity, density, viscosity)
            - (density * Gravity * Elevation);
    }

    /// <summary>The pressure lost to friction and fittings at a velocity.</summary>
    /// <param name="velocity">m/s, signed along the nominal direction.</param>
    /// <param name="density">kg/m³.</param>
    /// <param name="viscosity">Pa·s.</param>
    /// <returns>Pa, signed: positive when pressure falls in the direction of flow.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The laminar branch is written as a closed form, not as <c>f = 64/Re</c>.</strong> The
    /// two are algebraically the same, but <c>64/Re</c> diverges as the velocity goes to zero while the
    /// term it multiplies goes to zero with it — evaluated literally that is <c>∞ × 0</c>, and a
    /// residual that returns <c>NaN</c> at zero flow poisons the whole Newton step. Substituting Re
    /// gives <c>32·μ·L·v/D²</c>, which is linear in velocity, exactly zero at rest, and has a finite
    /// derivative there.
    /// </para>
    /// <para>
    /// The turbulent branch and the minor losses are quadratic and vanish at rest on their own.
    /// </para>
    /// </remarks>
    public double PressureDrop(double velocity, double density, double viscosity)
    {
        var reynolds = density * Math.Abs(velocity) * InsideDiameter / viscosity;
        var dynamic = density * velocity * Math.Abs(velocity) / 2;

        var laminar = 32 * viscosity * Length * velocity / (InsideDiameter * InsideDiameter);
        var turbulent = FrictionFactor(Math.Max(reynolds, TurbulentLimit)) * Length / InsideDiameter * dynamic;

        var friction = Smoothing.Blend(reynolds, LaminarLimit, TurbulentLimit, laminar, turbulent);

        return friction + (MinorLoss * dynamic);
    }

    /// <summary>The Darcy friction factor for turbulent flow in this pipe.</summary>
    /// <param name="reynolds">The Reynolds number, at least <see cref="TurbulentLimit"/>.</param>
    /// <returns>The dimensionless Darcy factor.</returns>
    /// <remarks>
    /// Serghide's explicit approximation to Colebrook–White: three nested logarithms instead of a
    /// fixed-point iteration, within 0.003 % of the implicit form. Explicit matters twice over — it is
    /// differentiable, which an iteration-to-tolerance is not, and it allocates nothing and takes a
    /// fixed time, which a residual on the Newton path must.
    /// </remarks>
    public double FrictionFactor(double reynolds)
    {
        var relative = Roughness / (3.7 * InsideDiameter);

        var a = -2 * Math.Log10(relative + (12 / reynolds));
        var b = -2 * Math.Log10(relative + (2.51 * a / reynolds));
        var c = -2 * Math.Log10(relative + (2.51 * b / reynolds));

        var denominator = c - (2 * b) + a;
        var root = a - ((b - a) * (b - a) / denominator);

        return 1 / (root * root);
    }
}
