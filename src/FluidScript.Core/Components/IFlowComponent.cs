using System.Collections.Immutable;

using FluidScript.Core.Language;

namespace FluidScript.Core.Components;


/// <summary>What a solver unknown stands for.</summary>
/// <remarks>Transcribed from <c>31</c>, which specifies it. Tier 30 owns the meaning.</remarks>
public enum UnknownKind
{
    /// <summary>The one mass flow a whole branch shares.</summary>
    BranchFlow,

    /// <summary>A node's pressure.</summary>
    NodePressure,

    /// <summary>A node's specific enthalpy.</summary>
    NodeEnthalpy,

    /// <summary>Flow injected into or extracted from the circuit at a terminal.</summary>
    ExternalMassFlux,

    /// <summary>A component parameter promoted to an unknown by sizing.</summary>
    Parameter,
}

/// <summary>What an equation asserts.</summary>
/// <remarks>Transcribed from <c>31</c>, which specifies it.</remarks>
public enum EquationKind
{
    /// <summary>A pressure relation along a branch or across a component.</summary>
    Pressure,

    /// <summary>A mass balance.</summary>
    Mass,

    /// <summary>An energy balance.</summary>
    Energy,

    /// <summary>A stated boundary condition.</summary>
    Boundary,

    /// <summary>A constraint a component imposes on its own state.</summary>
    ComponentConstraint,
}

/// <summary>One unknown the solver varies.</summary>
/// <param name="Index">Its position in the state vector, assigned at assembly.</param>
/// <param name="Kind">What it stands for.</param>
/// <param name="OwnerComponentId">The component that declared it.</param>
/// <param name="Name">A human-readable name, for diagnostics.</param>
/// <param name="SiUnit">The SI unit of its value, so a report can say what a number means.</param>
public sealed record UnknownDeclaration(
    int Index,
    UnknownKind Kind,
    string OwnerComponentId,
    string Name,
    string SiUnit);

/// <summary>One equation the solver drives to zero.</summary>
/// <param name="Index">Its position in the residual vector, assigned at assembly.</param>
/// <param name="Kind">What it asserts.</param>
/// <param name="OwnerComponentId">The component that contributes it.</param>
/// <param name="Name">A human-readable name, for diagnostics.</param>
/// <param name="ResidualSiUnit">
/// The SI unit of the residual itself, which is what makes <c>"HX1 energy balance off by 4.2 kW"</c>
/// possible instead of <c>"residual[17] = 4200"</c>.
/// </param>
public sealed record EquationDeclaration(
    int Index,
    EquationKind Kind,
    string OwnerComponentId,
    string Name,
    string ResidualSiUnit);
/// <summary>One parameter a component reads from the solve, and the value it holds now.</summary>
/// <param name="Name">The canonical parameter name, spelled as the registry and a promotion spell it.</param>
/// <param name="Value">
/// What the component uses when nothing supplies one, in SI. The component's own stored value, so that
/// filling a parameter buffer needs no knowledge of the kind.
/// </param>
/// <param name="SiUnit">
/// The SI unit of the value, so a promoted column can say what its number means. A dimensionless
/// parameter carries <c>"1"</c>; <c>kv</c> is <c>"m3/h"</c>, which is not SI and says so by being the
/// one exception — a valve coefficient is defined by a standard's test rig and has no SI form.
/// </param>
public readonly record struct ResolvedParameter(string Name, double Value, string SiUnit);

/// <summary>An attachment point carrying a fluid state and a flow.</summary>
public sealed record Port
{
    /// <summary>Gets the port's name, as a qualified connection writes it.</summary>
    public required string Name { get; init; }

    /// <summary>Gets what this port does in the nominal flow direction.</summary>
    public required PortRole Role { get; init; }

    /// <summary>Gets whether inference rule I3 may leave this port unconnected.</summary>
    public required bool IsOptional { get; init; }

    /// <summary>Gets the normalized bottom-to-top height of a tank port.</summary>
    /// <value>0 at the bottom and 1 at the top; <see langword="null"/> for every other kind.</value>
    /// <remarks>
    /// It never enters a hydraulic pressure equation (<c>22</c> invariant): it selects which layer a
    /// port talks to, and nothing else. A hydrostatic term would be a different model.
    /// </remarks>
    public double? NormalizedElevation { get; init; }
}

/// <summary>A component that participates in a fluid graph and its equation system.</summary>
/// <remarks>
/// <para>
/// The other half of <c>22</c>'s hierarchy from <see cref="IObserver"/>. Where an observer reads state
/// and contributes nothing, this carries flow, declares unknowns and writes residuals.
/// </para>
/// <para>
/// <strong>Analytic Jacobians are deliberately absent.</strong> v1 differentiates numerically
/// (<c>32</c>); adding an optional <c>EvaluateJacobian</c> later is a non-breaking addition, while
/// requiring it now would triple the work per component for a speed-up nobody has measured a need for.
/// </para>
/// </remarks>
public interface IFlowComponent : IComponent
{
    /// <summary>Gets this component's ports, in declaration order.</summary>
    /// <remarks>An unqualified connection binds in this order, so it is part of the contract.</remarks>
    ImmutableArray<Port> Ports { get; }

    /// <summary>Gets which flow group each port belongs to, indexed as <see cref="Ports"/> is.</summary>
    /// <value>
    /// One entry per port, holding a small group id. Ports sharing an id must carry the same mass
    /// flow. Ids are dense from zero and their order carries no meaning.
    /// </value>
    /// <remarks>
    /// <para>
    /// <strong>This, and never the port count, is what makes a component a junction element</strong>
    /// — one whose ports carry different flows, so branches must split at it (<c>D-63</c>). A coupled
    /// heat exchanger has four ports in <em>two</em> groups of two, because nothing flows from side 1
    /// to side 2; a three-way valve has the same "more than two ports" and one group of three. No
    /// count separates them, and a port-count test gives the exchanger a mass balance that is false
    /// whenever the two sides carry different flows — which is always.
    /// </para>
    /// <para>
    /// <strong>The component declares it because only the component knows.</strong> Computing groups
    /// in lowering from a kind's port names is the kind-specific table <c>D-30</c> exists to prevent,
    /// and it would put the exchanger's special case in the one file that has to stay general.
    /// </para>
    /// </remarks>
    ImmutableArray<int> FlowGroups { get; }

    /// <summary>Gets how many equations this component contributes.</summary>
    /// <remarks>
    /// <strong>Constant for the life of the component.</strong> It may depend on structure fixed at
    /// lowering — how many connections a node has, say — and never on a solved value, because a system
    /// whose size changed between Newton iterations would be a different system each time.
    /// </remarks>
    int EquationCount { get; }

    /// <summary>Declares the unknowns this component adds to the system.</summary>
    /// <returns>
    /// One entry per scalar the solver may vary, empty for a component that only constrains state its
    /// ports already carry. <c>Index</c> is filled in by assembly, not here.
    /// </returns>
    /// <remarks>Called once, at assembly. It is not on the iteration path and may allocate.</remarks>
    ImmutableArray<UnknownDeclaration> DeclareUnknowns();

    /// <summary>Names each equation this component contributes, for diagnostics.</summary>
    /// <returns>Exactly <see cref="EquationCount"/> entries, in residual order.</returns>
    /// <remarks>
    /// Separate from <see cref="EvaluateResiduals"/> so that naming a residual costs nothing on the
    /// iteration path. <c>31</c> makes the point that the mapping from row to component exists only
    /// here — if this layer does not carry it, no later one can.
    /// </remarks>
    ImmutableArray<EquationDeclaration> DeclareEquations();

    /// <summary>Evaluates this component's residuals at the given trial solution.</summary>
    /// <param name="context">Port states, flows, and this component's own unknowns, at the iterate.</param>
    /// <param name="residuals">Destination, exactly <see cref="EquationCount"/> long.</param>
    /// <remarks>
    /// <para>
    /// <strong>It must allocate nothing and call no property backend.</strong> Not "as little as
    /// possible" — nothing: it runs N+1 times per Newton iteration, and every state fix would both
    /// allocate a <c>FluidState</c> and cost a few hundred microseconds. Everything a residual needs is
    /// already evaluated in <see cref="SolveContext.Ports"/> (<c>C-16</c>).
    /// </para>
    /// <para>
    /// It must also be deterministic — the same context always gives the same residuals — because a
    /// numerical Jacobian differences this function and would otherwise measure the noise.
    /// </para>
    /// </remarks>
    void EvaluateResiduals(in SolveContext context, Span<double> residuals);

    /// <summary>Gets whether this component adds energy to the streams passing through it.</summary>
    /// <value>
    /// <see langword="false"/> for most kinds, and it is a <em>structural</em> answer: it may depend on
    /// a stated parameter, which is fixed for the whole solve, and never on a solved value. The
    /// assembler reads it to decide which node rows a component can reach, so an answer that moved
    /// between iterations would change the Jacobian's sparsity underneath it.
    /// </value>
    bool InjectsEnergy => false;

    /// <summary>Writes the energy each port delivers into the node it touches.</summary>
    /// <param name="context">Port states, flows, and this component's own unknowns, at the iterate.</param>
    /// <param name="injection">
    /// Destination, one entry per port in <see cref="Ports"/> order, in watts, positive into the node
    /// at that port. It is what the stream gains <em>beyond</em> what it carried in: the node's own
    /// balance already accounts for the enthalpy arriving through the port.
    /// </param>
    /// <remarks>
    /// <para>
    /// <strong>Energy is a flux a component contributes, not a row it owns</strong> (<c>D-69</c>). An
    /// exchanger that declared <c>Q = ṁ(h_out − h_in)</c> as its own equation was asserting the same
    /// relation as the balance of the node it discharges into, with <c>Q</c> missing from one of them,
    /// and the system was over-specified by a row per exchanger. Written this way the node keeps its
    /// balance, the count stays <c>Nodes.Length</c>, and the heat follows the flow through a reversal
    /// instead of being nailed to a port.
    /// </para>
    /// <para>
    /// <strong>Most kinds contribute nothing, and that is a statement about today rather than about
    /// physics.</strong> A pipe injects <c>−ṁgΔz</c> for its elevation now and will inject
    /// <c>−UA(T̄ − T_amb)</c> when uninsulated pipe is modelled; this member is on the interface rather
    /// than on the exchanger so that day costs no rewrite.
    /// </para>
    /// <para>
    /// The same rules as <see cref="EvaluateResiduals"/> apply: it runs on the same hot path, allocates
    /// nothing, calls no property backend, and is deterministic.
    /// </para>
    /// </remarks>
    void EvaluateEnergyInjection(in SolveContext context, Span<double> injection) => injection.Clear();

    /// <summary>Gets the parameters this component will take from the solve rather than from itself.</summary>
    /// <value>
    /// One entry per parameter something outside the component may decide, in a fixed order that is
    /// part of the contract — a residual reads them by index. Empty for a kind that decides everything
    /// it needs, which is most of them. <c>Value</c> is what the component would use if nothing
    /// supplied one, so a caller can fill a buffer without knowing the kind.
    /// </value>
    /// <remarks>
    /// <para>
    /// <strong>Two things resolve these and they are the same mechanism</strong> (<c>D-02</c>). Sizing
    /// chooses a value once per outer pass and it is a fixed coefficient for that solve; promotion
    /// makes it a solver unknown that moves per iterate, when the script states a constraint the
    /// circuit can only satisfy by moving it (<c>23</c>). Both arrive through
    /// <see cref="SolveContext.Parameters"/>, because from inside a residual they are the same thing:
    /// a number the component did not choose.
    /// </para>
    /// <para>
    /// <strong>The component declares them because only the component knows</strong>, the same argument
    /// as <see cref="FlowGroups"/>. Deriving "a pump's head is promotable" from a kind keyword outside
    /// the component is the kind-specific table <c>D-30</c> exists to prevent.
    /// </para>
    /// </remarks>
    ImmutableArray<ResolvedParameter> Resolvable => [];
}
