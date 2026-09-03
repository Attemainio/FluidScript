using System.Collections.Immutable;

using FluidScript.Core.Units;

namespace FluidScript.Core.Components;

/// <summary>A circulating pump, as a head curve.</summary>
/// <remarks>
/// <para>
/// One equation: <c>Δp = −ρ g H(ṁ, n)</c> with <c>H(ṁ, n) = n²H₀ − k·ṁ²</c>. Negative because a
/// pressure drop is positive when pressure falls in the nominal direction, and a pump raises it.
/// </para>
/// <para>
/// <strong>The default curve is a modelling decision with real consequences.</strong> A pump given only
/// a duty point gets a quadratic through it with a shut-off head of 1.2 × the duty head, which is
/// typical for a centrifugal pump and wrong for anything else. A user comparing against a datasheet
/// needs to be told, so it is stated in <c>/docs</c> and reported in hover rather than assumed.
/// </para>
/// </remarks>
public sealed class Pump : IFlowComponent
{
    /// <summary>Standard gravity, m/s².</summary>
    private const double Gravity = 9.80665;

    /// <summary>The shut-off head of the default curve, as a multiple of the duty head.</summary>
    /// <value>1.2, typical for a centrifugal pump.</value>
    public const double DefaultShutOffFactor = 1.2;

    private readonly ImmutableArray<EquationDeclaration> _equations;

    /// <summary>Initializes a pump from its curve coefficients at full speed.</summary>
    /// <param name="name">The user's identifier.</param>
    /// <param name="shutOffHead">H₀, m, the head at zero flow and n = 1.</param>
    /// <param name="curvature">k, m per (kg/s)², the quadratic loss coefficient at n = 1.</param>
    /// <param name="speed">The relative speed n.</param>
    /// <param name="efficiency">The hydraulic efficiency, for the shaft-power property.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="shutOffHead"/> or <paramref name="curvature"/> is negative, or
    /// <paramref name="efficiency"/> is not positive.
    /// </exception>
    public Pump(
        string name,
        double shutOffHead,
        double curvature,
        double speed = 1,
        double efficiency = 0.7)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(shutOffHead);
        ArgumentOutOfRangeException.ThrowIfNegative(curvature);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(efficiency);

        Name = name;
        ShutOffHead = shutOffHead;
        Curvature = curvature;
        Speed = speed;
        Efficiency = efficiency;

        _equations = [new EquationDeclaration(0, EquationKind.Pressure, name, $"{name} curve", "Pa")];
    }

    /// <summary>Builds a pump's default quadratic curve through a duty point.</summary>
    /// <param name="name">The user's identifier.</param>
    /// <param name="dutyHead">m, the head at the duty flow.</param>
    /// <param name="dutyFlow">kg/s, the duty flow.</param>
    /// <param name="speed">The relative speed n.</param>
    /// <param name="efficiency">The hydraulic efficiency.</param>
    /// <returns>A pump whose curve passes through the duty point.</returns>
    /// <remarks>
    /// H₀ is <see cref="DefaultShutOffFactor"/> × the duty head, and k follows from the curve passing
    /// through the duty point: <c>k = (H₀ − H_duty) / ṁ_duty²</c>.
    /// </remarks>
    public static Pump FromDutyPoint(
        string name, double dutyHead, double dutyFlow, double speed = 1, double efficiency = 0.7)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dutyFlow);

        var shutOff = DefaultShutOffFactor * dutyHead;

        return new Pump(name, shutOff, (shutOff - dutyHead) / (dutyFlow * dutyFlow), speed, efficiency);
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Kind => "pump";

    /// <inheritdoc/>
    /// <value>Always <see langword="null"/>: a pump has no modes.</value>
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

    /// <summary>Gets the head at zero flow and full speed.</summary>
    /// <value>m of the pumped fluid.</value>
    public double ShutOffHead { get; }

    /// <summary>Gets the quadratic loss coefficient of the curve at full speed.</summary>
    /// <value>m per (kg/s)².</value>
    public double Curvature { get; }

    /// <summary>Gets the relative speed.</summary>
    /// <value>
    /// Dimensionless, nominally 0 to 1.2. The one parameter a controller may move on a pump
    /// (<c>D-61</c>).
    /// </value>
    public double Speed { get; init; }

    /// <summary>Gets the hydraulic efficiency.</summary>
    public double Efficiency { get; }

    /// <inheritdoc/>
    public ImmutableArray<Port> Ports { get; } =
    [
        new Port { Name = "in", Role = PortRole.Inlet, IsOptional = false },
        new Port { Name = "out", Role = PortRole.Outlet, IsOptional = false },
    ];

    /// <inheritdoc/>
    /// <value>One: the curve.</value>
    public int EquationCount => 1;

    /// <inheritdoc/>
    /// <returns>Empty. Its flow belongs to its branch and its pressures to its nodes.</returns>
    public ImmutableArray<UnknownDeclaration> DeclareUnknowns() => [];

    /// <inheritdoc/>
    public ImmutableArray<EquationDeclaration> DeclareEquations() => _equations;

    /// <summary>The head this pump develops at a flow and its current speed.</summary>
    /// <param name="massFlow">kg/s.</param>
    /// <returns>m of the pumped fluid; negative past the curve's zero-head flow.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The n² distributes over both terms, and that is the whole content of the affinity laws
    /// here.</strong> A point (Q, H) on the base curve maps to (nQ, n²H), so the head at flow ṁ and
    /// speed n is <c>n²·[H₀ − k(ṁ/n)²]</c>, which simplifies to <c>n²H₀ − k·ṁ²</c>.
    /// </para>
    /// <para>
    /// <strong>Writing it unsimplified is the error to avoid</strong>, and it is a nasty one: leaving
    /// <c>n²H₀ − k(ṁ/n)²</c> puts an extra <c>1/n²</c> on the loss term — a factor of four at half
    /// speed — and it is <em>silent at n = 1</em>, which is where every test gets written. The
    /// simplified form is also finite at n = 0, where the unsimplified one divides by zero: a stopped
    /// pump has to evaluate as a pure resistance, not as a non-finite residual.
    /// </para>
    /// </remarks>
    public double Head(double massFlow) =>
        (Speed * Speed * ShutOffHead) - (Curvature * massFlow * massFlow);

    /// <summary>The shaft power at a flow, given the solved pressure rise.</summary>
    /// <param name="massFlow">kg/s.</param>
    /// <param name="pressureRise">Pa, the magnitude of the rise across the pump.</param>
    /// <param name="density">kg/m³.</param>
    /// <returns>W.</returns>
    /// <remarks>
    /// A property, not an equation: nothing else depends on it, so it is computed after the solve
    /// rather than carried as an unknown. The magnitude is taken deliberately — <c>Δp</c> is negative
    /// for a pump by the sign convention, and shaft power is positive.
    /// </remarks>
    public double ShaftPower(double massFlow, double pressureRise, double density) =>
        Math.Abs(massFlow) * Math.Abs(pressureRise) / (density * Efficiency);

    /// <inheritdoc/>
    public void EvaluateResiduals(in SolveContext context, Span<double> residuals)
    {
        var density = (context.Ports[0].Density + context.Ports[1].Density) / 2;
        var drop = context.Ports[0].Pressure - context.Ports[1].Pressure;

        residuals[0] = drop + (density * Gravity * Head(context.Flows[0]));
    }
}
