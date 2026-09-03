using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Language;
using FluidScript.Core.Units;

namespace FluidScript.Core.Components;

/// <summary>A finite-volume liquid store.</summary>
/// <remarks>
/// <para>
/// A mixed junction in steady state, and a stack of equal-volume perfectly mixed layers in a
/// transient. <strong>Only the steady contract is evaluated here</strong>; the finite-volume
/// derivative, internal displacement flow, density-inversion remixing and step limits are
/// <c>33</c>'s. This class owns the state, parameter and port contract those operate on.
/// </para>
/// <para>
/// <strong>The v1 tank is adiabatic except for connected streams.</strong> It infers no ambient loss,
/// wall conduction, jet entrainment, coil heat transfer, vessel geometry or hydrostatic pressure.
/// Each of those needs parameters and validation data, and a default in their place would be physics
/// in disguise.
/// </para>
/// <para>
/// <strong>Volume, layers and elevations have no steady effect at all</strong>, and stay visible design
/// data rather than being dropped. That is what makes <c>layers=1</c> identical to the steady behaviour
/// of every larger count, and it gives the steady solve a unique equilibrium.
/// </para>
/// </remarks>
public sealed class Tank : IFlowComponent
{
    /// <summary>The volume a tank gets when the script states none.</summary>
    /// <value>0.3 m³, which is 300 dm³ — a visible decided default under <c>D-32</c>, not a sized value.</value>
    public const double DefaultVolume = 0.3;

    /// <summary>The layer count a tank gets when the script states none.</summary>
    public const int DefaultLayers = 5;

    /// <summary>The normalized height a port gets when the script states none.</summary>
    /// <value>0.5, mid-height.</value>
    public const double DefaultElevation = 0.5;

    private readonly ImmutableArray<UnknownDeclaration> _unknowns;
    private readonly ImmutableArray<EquationDeclaration> _equations;

    /// <summary>Initializes a tank from its materialized ports.</summary>
    /// <param name="name">The user's identifier.</param>
    /// <param name="inletElevations">
    /// Normalized heights of <c>in1</c> onwards, 0 at the bottom and 1 at the top. At least one.
    /// </param>
    /// <param name="outletElevations">Normalized heights of <c>out1</c> onwards. At least one.</param>
    /// <param name="volume">m³ of liquid.</param>
    /// <param name="layers">Equal-volume layers, indexed bottom to top.</param>
    /// <exception cref="ArgumentException">Either elevation list is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="volume"/> is not positive, or <paramref name="layers"/> is below one.
    /// </exception>
    public Tank(
        string name,
        ImmutableArray<double> inletElevations = default,
        ImmutableArray<double> outletElevations = default,
        double volume = DefaultVolume,
        int layers = DefaultLayers)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(volume);
        ArgumentOutOfRangeException.ThrowIfLessThan(layers, 1);

        var inlets = inletElevations.IsDefaultOrEmpty ? [DefaultElevation] : inletElevations;
        var outlets = outletElevations.IsDefaultOrEmpty ? [DefaultElevation] : outletElevations;

        Name = name;
        Volume = volume;
        Layers = layers;
        PortElevations = [.. inlets, .. outlets];

        Ports =
        [
            .. inlets.Select((elevation, index) => Indexed("in", index, elevation)),
            .. outlets.Select((elevation, index) => Indexed("out", index, elevation)),
        ];

        // K >= 3 is a junction and K == 1 a terminal; at K == 2 the branch-owned flow already makes the
        // row an identity, exactly as for a node interior to a branch.
        CarriesMassBalance = Ports.Length >= 3 || Ports.Length == 1;

        _unknowns = [new UnknownDeclaration(0, UnknownKind.NodeEnthalpy, name, $"{name}.h", "J/kg")];

        var equations = ImmutableArray.CreateBuilder<EquationDeclaration>();
        equations.Add(new EquationDeclaration(0, EquationKind.Energy, name, $"{name} energy balance", "W"));

        if (CarriesMassBalance)
        {
            equations.Add(new EquationDeclaration(0, EquationKind.Mass, name, $"{name} mass balance", "kg/s"));
        }

        for (var port = 1; port < Ports.Length; port++)
        {
            equations.Add(new EquationDeclaration(
                0, EquationKind.Pressure, name, $"{name} {Ports[port].Name} equal to {Ports[0].Name}", "Pa"));
        }

        _equations = equations.ToImmutable();

        static Port Indexed(string prefix, int index, double elevation) => new()
        {
            Name = prefix + (index + 1).ToString(CultureInfo.InvariantCulture),
            Role = PortRole.Bidirectional,
            IsOptional = index > 0,
            NormalizedElevation = elevation,
        };
    }

    /// <summary>The index of the tank's mixed enthalpy among its own unknowns.</summary>
    public const int EnthalpyIndex = 0;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    /// <value><c>tank</c>. The alias <c>container</c> resolves to it and is never emitted (<c>D-32</c>).</value>
    public string Kind => "tank";

    /// <inheritdoc/>
    /// <value>Always <see langword="null"/>: a tank has no modes.</value>
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

    /// <summary>Gets the liquid volume.</summary>
    /// <value>
    /// m³. <strong>Held in m³ and exposed in dm³ on the model contract</strong>, which is the one place
    /// the two differ.
    /// </value>
    public double Volume { get; }

    /// <summary>Gets the number of equal-volume layers, indexed bottom to top.</summary>
    public int Layers { get; }

    /// <summary>Gets each port's normalized height, in port order.</summary>
    /// <value>0 at the bottom and 1 at the top.</value>
    public ImmutableArray<double> PortElevations { get; }

    /// <summary>Gets whether this tank contributes a mass balance.</summary>
    public bool CarriesMassBalance { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// <strong>All bidirectional, whatever they are called.</strong> The solved flow sign decides
    /// whether fluid enters or leaves, so an <c>in</c> port with reverse flow draws from its layer.
    /// <c>in1</c> and <c>out1</c> always exist; higher ports materialize only when a qualified
    /// connection or an elevation parameter names them.
    /// </remarks>
    public ImmutableArray<Port> Ports { get; }

    /// <inheritdoc/>
    /// <value>
    /// One energy balance, a mass balance when this tank is a junction or a terminal, and K−1 pressure
    /// equalities against the first port.
    /// </value>
    public int EquationCount => _equations.Length;

    /// <inheritdoc/>
    /// <returns>The tank's mixed enthalpy. Its pressure is its first port's, not a separate unknown.</returns>
    public ImmutableArray<UnknownDeclaration> DeclareUnknowns() => _unknowns;

    /// <inheritdoc/>
    public ImmutableArray<EquationDeclaration> DeclareEquations() => _equations;

    /// <summary>The layer a normalized height selects.</summary>
    /// <param name="elevation">0 at the bottom, 1 at the top.</param>
    /// <param name="layers">The layer count.</param>
    /// <returns>A one-based layer index, bottom to top.</returns>
    /// <remarks>
    /// <c>min(floor(elevation × layers) + 1, layers)</c>. <strong>The boundary rule is explicit so that
    /// a port sitting exactly on a layer boundary is not placed differently by two implementations</strong>
    /// — in a five-layer tank, 0 is layer 1, 0.30 is layer 2, 0.90 is layer 5, and 1 is the top layer
    /// rather than a sixth that does not exist.
    /// </remarks>
    public static int LayerFor(double elevation, int layers) =>
        Math.Min((int)Math.Floor(elevation * layers) + 1, layers);

    /// <summary>The layer a port's height selects in this tank.</summary>
    /// <param name="port">The port's index in <see cref="Ports"/>.</param>
    /// <returns>A one-based layer index.</returns>
    public int LayerForPort(int port) => LayerFor(PortElevations[port], Layers);

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <code>
    /// Σᵢ ṁᵢ · h(ṁᵢ) = 0                 every tank
    /// Σᵢ ṁᵢ         = 0                 junction or terminal only
    /// pᵢ − p₁       = 0                 i = 2…K
    /// </code>
    /// </para>
    /// <para>
    /// <strong>No hydrostatic term.</strong> A normalized port height is thermal metadata rather than
    /// physical metres, and the script never states a vessel height to compute <c>ρgΔz</c> from.
    /// Inventing one would be a different model wearing the same parameters.
    /// </para>
    /// <para>
    /// In steady state every layer collapses to one perfectly mixed enthalpy and every outflow carries
    /// it, which the same upwinding a node uses produces without a special case.
    /// </para>
    /// </remarks>
    public void EvaluateResiduals(in SolveContext context, Span<double> residuals)
    {
        var mixed = context.Unknowns[EnthalpyIndex];
        var mass = 0.0;
        var energy = 0.0;

        for (var port = 0; port < context.PortCount; port++)
        {
            var flow = context.Flows[port];
            var arriving = context.HasPortStates ? context.Ports[port].Enthalpy : mixed;

            mass += flow;
            energy += flow * Smoothing.Upwind(flow, arriving, mixed);
        }

        var next = 0;
        residuals[next++] = energy;

        if (CarriesMassBalance)
        {
            residuals[next++] = mass;
        }

        for (var port = 1; port < context.PortCount; port++)
        {
            residuals[next++] = context.Ports[port].Pressure - context.Ports[0].Pressure;
        }
    }
}
