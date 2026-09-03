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
}
