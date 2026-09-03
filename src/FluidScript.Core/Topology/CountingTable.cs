using System.Collections.Immutable;

namespace FluidScript.Core.Topology;

/// <summary>What a stated parameter asks the circuit to do, beyond supplying a coefficient.</summary>
/// <remarks>
/// The distinction decides what can absorb it. A mixed inlet temperature can only be met by moving a
/// mixing split; a fixed flow can only be met by moving whatever sets the flow. Collapsing the two
/// into "a constraint" lets a valve's <c>kv</c> be offered as the fix for a temperature it cannot
/// change, and the circuit is then reported well-posed when it has no solution.
/// </remarks>
public enum ConstraintKind
{
    /// <summary>A heat exchanger's stated inlet temperature, met by a mixing split.</summary>
    MixedInlet = 1,

    /// <summary>A stated duty or outlet that pins a branch's mass flow.</summary>
    FixedFlow,

    /// <summary>A temperature stated on a node that is not a boundary.</summary>
    NodeTemperature,
}

/// <summary>One stated parameter the circuit must satisfy rather than merely read.</summary>
/// <param name="Component">The component that states it.</param>
/// <param name="Parameter">The canonical parameter name.</param>
/// <param name="Kind">What has to move to satisfy it.</param>
/// <param name="Hydraulic">The hydraulic component it constrains.</param>
public sealed record ComponentConstraint(
    string Component, string Parameter, ConstraintKind Kind, int Hydraulic)
{
    /// <summary>Gets the form a message names it by.</summary>
    public string Label => $"{Component}.{Parameter}";
}

/// <summary>A sized parameter a stated constraint turned into a solver unknown (<c>D-02</c>).</summary>
/// <param name="Component">The component whose parameter moves.</param>
/// <param name="Parameter">The canonical parameter name.</param>
/// <param name="Constraint">The constraint it absorbs.</param>
/// <remarks>
/// <strong>The constraint and the unknown arrive together, which is what keeps the system square.</strong>
/// Without the pairing a stated <c>in</c> would be an extra equation and the circuit would report as
/// over-specified on the most ordinary hydronic arrangement there is.
/// </remarks>
public sealed record Promotion(string Component, string Parameter, ComponentConstraint Constraint)
{
    /// <summary>Gets the form a message names it by.</summary>
    public string Label => $"{Component}.{Parameter}";
}

/// <summary>The counting argument: what the solver must find, against what it has to find it with.</summary>
/// <remarks>
/// <para>
/// <strong>A count that always balances is not a check.</strong> The obvious version — branch flows
/// plus node pressures and enthalpies against mass balances, pressure relations, energy balances and a
/// datum — comes to <c>2N + B</c> on both sides for <em>every</em> graph, and so can never detect
/// anything. The rows that make this a test are the ones a user actually varies:
/// <see cref="ExternalFluxes"/>, <see cref="Constraints"/> and <see cref="Promotions"/>.
/// </para>
/// <para>
/// <strong><see cref="ExternalFluxes"/> and <see cref="StatedPressures"/> are equal by construction and
/// cancel.</strong> That is not redundancy: a stated pressure admits an unknown flux and supplies the
/// equation fixing the pressure, and separating them is what makes a pressure stated on a node with no
/// mass balance — a node interior to a branch, which cannot accept external mass — come out as the
/// over-specification it is.
/// </para>
/// </remarks>
public sealed record CountingTable
{
    /// <summary>Gets the number of branch flows, one per branch.</summary>
    public required int BranchFlows { get; init; }

    /// <summary>Gets the number of node pressures.</summary>
    public required int NodePressures { get; init; }

    /// <summary>Gets the number of node enthalpies.</summary>
    public required int NodeEnthalpies { get; init; }

    /// <summary>Gets the unknown external mass fluxes, one per pressure-stating node that balances.</summary>
    public required int ExternalFluxes { get; init; }

    /// <summary>Gets the sized parameters stated constraints turned into unknowns.</summary>
    public required ImmutableArray<Promotion> Promotions { get; init; }

    /// <summary>Gets the pressure relations components impose between node pressures.</summary>
    /// <value>
    /// One per two-port flow group crossed, <c>k − 1</c> at a <c>k</c>-connected junction element, and
    /// one per bare node-to-node link, which <c>D-25</c> makes an ideal zero-drop connection.
    /// </value>
    public required int PressureRelations { get; init; }

    /// <summary>Gets the independent mass balances, after the redundant one is dropped.</summary>
    public required int MassBalances { get; init; }

    /// <summary>Gets the energy balances, one per node.</summary>
    public required int EnergyBalances { get; init; }

    /// <summary>Gets the stated pressure boundary conditions.</summary>
    public required int StatedPressures { get; init; }

    /// <summary>Gets the constraints stated parameters place on the circuit.</summary>
    public required ImmutableArray<ComponentConstraint> Constraints { get; init; }

    /// <summary>Gets the pressure datums, one per component that states no pressure of its own.</summary>
    public required int Datums { get; init; }

    /// <summary>Gets the total number of unknowns.</summary>
    public int Unknowns =>
        BranchFlows + NodePressures + NodeEnthalpies + ExternalFluxes + Promotions.Length;

    /// <summary>Gets the total number of equations.</summary>
    public int Equations =>
        PressureRelations + MassBalances + EnergyBalances + StatedPressures + Constraints.Length + Datums;

    /// <summary>Gets how far the system is from square.</summary>
    /// <value>
    /// Positive when over-specified (<c>FS2210</c>), negative when under-specified (<c>FS2211</c>),
    /// zero when the circuit can be solved.
    /// </value>
    public int Excess => Equations - Unknowns;
}
