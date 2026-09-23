using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Topology.Counting;

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
    /// one offset to every enthalpy satisfies all of them at once, and the level is something the block
    /// cannot determine. A stated temperature determines it, which is why this term and that constraint
    /// cancel exactly as <see cref="FluxNodes"/> and <see cref="PressureNodes"/> do.
    /// </value>
    /// <remarks>
    /// <para>
    /// <strong>A free level is a redundant equation, not a missing unknown</strong> (<c>D-75</c>). The
    /// offset is not independent of the enthalpies it offsets — it is a null direction of columns
    /// already counted — so a column for it would equal the sum of others and be singular by
    /// construction. The true statement is the dual: such a component's node energy balances sum to
    /// <em>exactly</em> zero, term by cancelling term along every branch, so one of them carries no
    /// information and is dropped. Counted as an unknown, the table read square while the assembled
    /// system was a row over on <c>m2-simple-loop</c> (<c>S-24</c>).
    /// </para>
    /// <strong>The graph cannot pick this datum for itself</strong>, unlike the pressure one. Every
    /// pressure being relative to an arbitrary node changes no result; every temperature being relative
    /// to one changes the physics. So a level nothing fills is <c>FS2211</c> and not <c>FS2201</c>. A
    /// coupled exchanger fills it without being asked: its duty reads absolute temperatures on both
    /// sides, so a uniform offset no longer satisfies its relation.
    /// </remarks>
    public required ImmutableArray<HydraulicComponent> LevelComponents { get; init; }

    /// <summary>Gets the number of energy balances dropped because their component's level is free.</summary>
    public int EnthalpyLevels => LevelComponents.Length;

    /// <summary>Gets the total number of unknowns.</summary>
    public int Unknowns =>
        BranchFlows + NodePressures + NodeEnthalpies + ComponentUnknowns.Length + ExternalFluxes
        + Promotions.Length;

    /// <summary>Gets the total number of equations.</summary>
    public int Equations =>
        PressureRelations + MassBalances + EnergyBalances + ControlVolumeBalances + StatedPressures
        + Constraints.Length + Datums - EnthalpyLevels;

    /// <summary>Gets how far the system is from square.</summary>
    /// <value>
    /// Positive when over-specified (<c>FS2210</c>), negative when under-specified (<c>FS2211</c>),
    /// zero when the circuit can be solved.
    /// </value>
    public int Excess => Equations - Unknowns;
}
