using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Sizing.Flows;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Solvers.Seeding;

/// <summary>The iterate a solve starts from: <c>31</c>'s <c>seedFromStatedDuties</c>.</summary>
/// <remarks>
/// <para>
/// <strong>A seed is not a convenience, and zero is not a neutral one.</strong> A pipe's momentum
/// relation is <c>Δp = R·ṁ|ṁ|</c> and a pump curve is <c>H₀ − kṁ²</c>; both have a derivative of
/// exactly zero at <c>ṁ = 0</c>, so a zero-flow start is a genuinely singular Jacobian rather than a
/// poor guess, and every solve from one reports <c>FS3002</c> however well-posed the circuit is
/// (<c>S-21</c>).
/// </para>
/// <para>
/// <strong>Non-zero is not enough either, and this is the half that is easy to miss.</strong> A
/// branch's orientation is the decomposition's choice, so seeding every flow to the same positive
/// number leaves some node with every port an inflow — and a node nothing leaves is a node whose own
/// enthalpy enters no equation, which is a zero column and a singular Jacobian again. The fix is not a
/// sign heuristic: it is that the seed <em>satisfies the mass balances</em>, at which point every node
/// with an inflow has an outflow by construction.
/// </para>
/// <para>
/// <strong>How the field is made mass-consistent.</strong> The branch graph is spanned by a forest.
/// Every non-tree branch — one per independent loop (<c>23</c>) — takes its own estimate outright, and
/// every boundary flux is chosen so the fluxes of a hydraulic component sum to zero. The tree branches
/// are then <em>solved</em>, leaves inward: at each vertex all but the parent branch is known, so the
/// parent's flow is whatever closes that vertex's balance. The last vertex closes identically because
/// its component's fluxes were made to sum to zero, which is the same statement one level up.
/// </para>
/// <para>
/// This is exactly the decomposition <c>23</c>'s cycle basis describes, used for its other purpose: a
/// divergence-free field on a graph is a particular solution plus the cycle space, so choosing the
/// chords and the boundary freely and solving for the tree reaches every such field and nothing else.
/// </para>
/// </remarks>
public static partial class SolutionSeed
{
    /// <summary>The temperature a node's state falls back to when the script fixes none.</summary>
    /// <value>K. 20 °C — room temperature, valid for every substance the catalogue carries.</value>
    public const double ReferenceTemperature = 293.15;

    /// <summary>Metres of head used only to keep a promoted bare pump inside a driven seed.</summary>
    private const double NominalPumpHead = 2.2;

    /// <summary>The temperature step the seed puts between one node and the next along a branch.</summary>
    /// <value>
    /// K. Two degrees — small enough that <see cref="Band"/> steps stay well inside any fluid's
    /// validated range, large enough that an enthalpy difference is far from the noise floor of a
    /// finite-difference derivative.
    /// </value>
    /// <remarks>
    /// <strong>Two degrees is load-bearing at both ends, and reducing it was tried and reverted.</strong>
    /// The spread is chosen in kelvin and paid for in watts: an interior node's energy balance reads
    /// <c>m*(h_out - h_in)</c>, so a step of <c>dT</c> costs <c>m*cp*dT</c> of residual, and the header's
    /// 1.65 kg/s primary turns the eight-degree wrap into 55 kW against a plant whose whole duty is 54 kW.
    /// That looked like the reason its first Newton step leaves the property domain, and it is not.
    /// Measured at 0.02 K: the scaled residual stayed at 1.96 — unchanged by a hundredfold reduction, so
    /// the energy rows never dominated it — the header variant that had converged at 2.85e-10 turned
    /// <c>NonFinite</c>, and the shipped header's measured rank fell from 44 to 43. The last of those is
    /// the point: a smaller spread brings back the very column degeneracy the spread exists to prevent
    /// (<c>S-21</c>). The noise-floor bound is on the <em>derivative</em>; what actually binds is that the
    /// enthalpy differences keep the flow columns distinguishable, and that needs far more than
    /// resolvability. See <c>S-44</c>.
    /// </remarks>
    public const double NominalRise = 2;

    /// <summary>How many steps the seed takes before wrapping back to the start of its band.</summary>
    /// <value>
    /// Five. A cumulative walk down a long branch leaves the fluid's validated range; wrapping bounds
    /// the excursion at four steps while still giving every pair of adjacent nodes different values,
    /// which is the only property the seed needs from this.
    /// </value>
    public const int Band = 5;

    /// <summary>Builds the starting iterate for a graph.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">The state vector's layout, which fixes where each value goes.</param>
    /// <returns>One value per unknown, in SI, in the layout's order.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static StateVector Build(CircuitGraph graph, SystemLayout layout)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(layout);

        var values = new double[layout.Count];
        var estimates = BranchFlows.Estimate(graph);
        var field = new Field(graph);

        field.Solve(estimates);

        // A block whose coil has no duty learns its feed only from the field's closure -- the ring's flow
        // arrives at its `a` leg by mass balance, not by any estimate -- and only then can its stated
        // temperatures say how much the coil circulates (`S-69`). One more round: the closed field hands
        // the estimate what the balance found, the three-way rule partitions from it, and the field is
        // solved again with those legs as its chords.
        if (BranchFlows.Refine(graph, estimates, field.Flows) is { } refined)
        {
            field = new Field(graph);
            field.Solve(refined);
        }

        for (var branch = 0; branch < graph.Branches.Length; branch++)
        {
            values[layout.BranchFlow(branch)] = field.Flows[branch];
        }

        for (var index = 0; index < layout.FluxNodes.Length; index++)
        {
            values[layout.ExternalFluxOffset + index] = field.Injection(layout.FluxNodes[index].Component);
        }

        Thermal(graph, layout, values);

        return new StateVector([.. values]);
    }

    /// <summary>Water's density at the reference state, kg/m³, for a seed that needs one before any state is fixed.</summary>
    private const double ReferenceDensity = 1000;

    /// <summary>The fluid's specific heat at the seed's level and datum, or water's when it cannot be fixed there.</summary>
    private static double SpecificHeat(CircuitGraph graph, double level, double datum) =>
        graph.Substance.FromPressureTemperature(
            Quantity.FromSi(level, Dimension.Pressure), Quantity.FromSi(datum, Dimension.Temperature))
            .TryGetValue(out var state)
            ? state.SpecificHeat.SiValue
            : 4180;

    /// <summary>The temperature every unstated node is seeded at.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <returns>K.</returns>
    /// <remarks>
    /// A stated boundary temperature first, then an exchanger's stated inlet — which is the enthalpy
    /// datum of a closed circuit (<c>D-65</c>), so it is the one absolute temperature such a circuit
    /// has — and <see cref="ReferenceTemperature"/> only when the script names neither.
    /// </remarks>
    private static double Datum(CircuitGraph graph) =>
        graph.Nodes
            .Select(static node => HydraulicPartition.Stated(node.Component, HydraulicPartition.Temperature))
            .FirstOrDefault(static stated => stated is not null)
        ?? graph.Components
            .Select(static component =>
                component.StatedParameters.TryGetValue("in", out var inlet) ? inlet.SiValue : (double?)null)
            .FirstOrDefault(static stated => stated is not null)
        ?? ReferenceTemperature;

    /// <summary>The specific enthalpy of a state, or zero when the substance cannot evaluate it.</summary>
    /// <param name="substance">The circuit's fluid.</param>
    /// <param name="pressure">Gauge pressure, Pa.</param>
    /// <param name="temperature">K.</param>
    /// <returns>J/kg.</returns>
    /// <remarks>
    /// Zero rather than a throw or a diagnostic: a state the substance refuses has already been
    /// reported by well-posedness (<c>FS2205</c>), and a seed is allowed to be wrong. Nothing here is
    /// the right place to tell the user about it a second time.
    /// </remarks>
    private static double Enthalpy(ISubstance substance, double pressure, double temperature) =>
        substance.FromPressureTemperature(
            Quantity.FromSi(pressure, Dimension.Pressure),
            Quantity.FromSi(temperature, Dimension.Temperature)).TryGetValue(out var state)
            ? state.Enthalpy.SiValue
            : 0;
}
