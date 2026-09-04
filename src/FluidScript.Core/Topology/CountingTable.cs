using System.Collections.Immutable;
using FluidScript.Core.Components;

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

/// <summary>A bare connection between two nodes, which <c>D-25</c> makes an ideal zero-drop link.</summary>
/// <param name="From">The node the branch walk reaches it from.</param>
/// <param name="To">The node it continues to.</param>
/// <remarks>
/// <strong>It is a pressure relation with no component behind it.</strong> <c>A - B</c> written between
/// two nodes puts nothing in the path, so nothing declares <c>p_A = p_B</c> and the assembler writes the
/// row itself. Naming the pair is what lets it: a count says how many such rows exist and never which
/// nodes they join (<c>S-15</c>).
/// </remarks>
public sealed record IdealLink(GraphNode From, GraphNode To);

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

    /// <summary>Gets the nodes carrying an unknown external mass flux.</summary>
    /// <value>
    /// One per pressure-stating node that balances, and per <c>return</c> boundary.
    /// <para>
    /// <strong>Named rather than counted, because an assembler has to declare these unknowns and a
    /// count cannot say which nodes get one.</strong> Recomputing the rule on the other side is how the
    /// two drift apart, and the whole value of the table is that it is a second opinion (<c>S-9</c>).
    /// </para>
    /// </value>
    public required ImmutableArray<GraphNode> FluxNodes { get; init; }

    /// <summary>Gets the number of unknown external mass fluxes.</summary>
    public int ExternalFluxes => FluxNodes.Length;

    /// <summary>Gets the sized parameters stated constraints turned into unknowns.</summary>
    public required ImmutableArray<Promotion> Promotions { get; init; }

    /// <summary>Gets the scalars components declare as their own, in graph order.</summary>
    /// <value>
    /// A control volume's own state: a tank's mixed enthalpy is one, and in v1 it is the only one.
    /// <para>
    /// <strong>Named because the layout has to allocate them and only the component knows what they
    /// are</strong> (<c>D-74</c>). <see cref="IFlowComponent.DeclareUnknowns"/> existed from the first
    /// component package and nothing consumed it, so the tank's enthalpy was a column no state vector
    /// held and a term this table did not count — and its energy balance was a row
    /// <see cref="EnergyBalances"/> did not count either, because that is <c>Nodes.Length</c> and a tank
    /// is not a node. The two omissions cancelled, <see cref="Excess"/> read zero, and the storage
    /// header reported square while being a row and a column short of the system it would assemble
    /// (<c>S-16</c>).
    /// </para>
    /// </value>
    public required ImmutableArray<UnknownDeclaration> ComponentUnknowns { get; init; }

    /// <summary>Gets the energy balances components own rather than nodes.</summary>
    /// <value>
    /// One per energy row a non-node component declares — the other half of <c>D-74</c>.
    /// <para>
    /// <strong>This term and <see cref="ComponentUnknowns"/> cancel by construction, and unlike
    /// <see cref="FluxNodes"/> against <see cref="PressureNodes"/> that buys no diagnostic.</strong>
    /// Those two come from different conditions, which is what makes their separation detect a pressure
    /// stated where no flux can enter; these two come from the same component, which is the only
    /// authority on its own state, so there is no second opinion to be had. They are counted anyway
    /// because a table that does not describe the system it is checked against is worse than one whose
    /// terms agree for a dull reason.
    /// </para>
    /// </value>
    public required int ControlVolumeBalances { get; init; }

    /// <summary>Gets the pressure relations components impose between node pressures.</summary>
    /// <value>
    /// One per two-port flow group crossed, <c>k − 1</c> at a <c>k</c>-connected junction element, and
    /// one per bare node-to-node link, which <c>D-25</c> makes an ideal zero-drop connection.
    /// </value>
    public required int PressureRelations { get; init; }

    /// <summary>Gets the bare node-to-node adjacencies, which <c>D-25</c> makes ideal zero-drop links.</summary>
    /// <value>
    /// A subset of <see cref="PressureRelations"/>, named for the same reason <see cref="FluxNodes"/> is:
    /// <strong>no component declares these rows, so the assembler has to write them, and a count cannot
    /// say between which nodes.</strong> Every other term in <see cref="PressureRelations"/> is some
    /// component's own equation and arrives through <c>DeclareEquations</c>; this one belongs to a
    /// connection with nothing on it, and there is nobody else to ask (<c>S-15</c>).
    /// </value>
    public required ImmutableArray<IdealLink> IdealLinks { get; init; }

    /// <summary>Gets the independent mass balances, after the redundant one is dropped.</summary>
    public required int MassBalances { get; init; }

    /// <summary>Gets the energy balances, one per node.</summary>
    public required int EnergyBalances { get; init; }

    /// <summary>Gets the nodes whose pressure the script states.</summary>
    public required ImmutableArray<GraphNode> PressureNodes { get; init; }

    /// <summary>Gets the number of stated pressure boundary conditions.</summary>
    public int StatedPressures => PressureNodes.Length;

    /// <summary>Gets the constraints stated parameters place on the circuit.</summary>
    public required ImmutableArray<ComponentConstraint> Constraints { get; init; }

    /// <summary>Gets the hydraulic components needing a pressure datum of their own.</summary>
    /// <value>One per component that states no pressure anywhere in it.</value>
    public required ImmutableArray<HydraulicComponent> DatumComponents { get; init; }

    /// <summary>Gets the number of pressure datums.</summary>
    public int Datums => DatumComponents.Length;

    /// <summary>Gets the hydraulic components whose enthalpy level their own relations cannot reach.</summary>
    /// <value>
    /// One per hydraulic component that is closed, solved steady, and thermally coupled to nothing.
    /// Every energy relation in such a component is a difference — <c>h_out = h_in + Q̇/ṁ</c> — so adding
    /// one offset to every enthalpy satisfies all of them at once, and the level is an unknown the block
    /// cannot determine. A stated temperature determines it, which is why this row and that constraint
    /// cancel exactly as <see cref="FluxNodes"/> and <see cref="PressureNodes"/> do.
    /// </value>
    /// <remarks>
    /// <strong>The graph cannot pick this datum for itself</strong>, unlike the pressure one. Every
    /// pressure being relative to an arbitrary node changes no result; every temperature being relative
    /// to one changes the physics. So a level nothing fills is <c>FS2211</c> and not <c>FS2201</c>. A
    /// coupled exchanger fills it without being asked: its duty reads absolute temperatures on both
    /// sides, so a uniform offset no longer satisfies its relation.
    /// </remarks>
    public required ImmutableArray<HydraulicComponent> LevelComponents { get; init; }

    /// <summary>Gets the number of enthalpy levels nothing else determines.</summary>
    public int EnthalpyLevels => LevelComponents.Length;

    /// <summary>Gets the total number of unknowns.</summary>
    public int Unknowns =>
        BranchFlows + NodePressures + NodeEnthalpies + ComponentUnknowns.Length + ExternalFluxes
        + Promotions.Length + EnthalpyLevels;

    /// <summary>Gets the total number of equations.</summary>
    public int Equations =>
        PressureRelations + MassBalances + EnergyBalances + ControlVolumeBalances + StatedPressures
        + Constraints.Length + Datums;

    /// <summary>Gets how far the system is from square.</summary>
    /// <value>
    /// Positive when over-specified (<c>FS2210</c>), negative when under-specified (<c>FS2211</c>),
    /// zero when the circuit can be solved.
    /// </value>
    public int Excess => Equations - Unknowns;
}
