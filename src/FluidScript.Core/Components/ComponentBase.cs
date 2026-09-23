using System.Collections.Immutable;

using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Components;

/// <summary>What every flow component shares: its name, its parameter maps and its declarations.</summary>
/// <remarks>
/// <para>
/// <strong>Identity and bookkeeping only; the physics stays in the derived type.</strong> Every
/// implementer declared the same name, the same three parameter maps, the same stored equation list and
/// the same empty unknown list, and none of them differed in any of it (<c>D-147</c>). What a kind
/// actually is — its ports, its flow groups, its residuals — is abstract here and written once per kind.
/// </para>
/// <para>
/// <strong>The interface's default members are restated as virtual, not left to the interface.</strong>
/// A derived type that declares <c>InjectsEnergy</c> without the base declaring it would not implement
/// <see cref="IFlowComponent.InjectsEnergy"/> at all: the interface maps to its own default through the
/// base, and the derived member would be a public property nothing calls. The pipe's rise would stop
/// reaching the energy balance with nothing failing to compile.
/// </para>
/// <para>
/// <see cref="IFlowComponent"/> stays the contract. Assembly, the sizers and the tests program against
/// it, and a test double implements it without inheriting this.
/// </para>
/// </remarks>
public abstract class ComponentBase : IFlowComponent
{
    /// <summary>Initializes the parts every component shares.</summary>
    /// <param name="name">The user's identifier, or the generated name of an inferred component.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    protected ComponentBase(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public abstract string Kind { get; }

    /// <inheritdoc/>
    /// <value><see langword="null"/> unless the kind has modes.</value>
    public virtual string? Mode => null;

    /// <inheritdoc/>
    public ImmutableDictionary<string, Quantity> StatedParameters { get; init; }
        = ImmutableDictionary<string, Quantity>.Empty;

    /// <inheritdoc/>
    public ImmutableDictionary<string, Quantity> SizedParameters { get; init; }
        = ImmutableDictionary<string, Quantity>.Empty;

    /// <inheritdoc/>
    public ImmutableDictionary<string, Quantity> DefaultParameters { get; init; }
        = ImmutableDictionary<string, Quantity>.Empty;

    /// <inheritdoc/>
    public abstract ImmutableArray<Port> Ports { get; }

    /// <inheritdoc/>
    public abstract ImmutableArray<int> FlowGroups { get; }

    /// <inheritdoc/>
    public abstract int EquationCount { get; }

    /// <inheritdoc/>
    public virtual ImmutableArray<ResolvedParameter> Resolvable => [];

    /// <inheritdoc/>
    public virtual bool InjectsEnergy => false;

    /// <summary>Gets the equations the derived constructor declares, in residual order.</summary>
    /// <value>Empty until the derived constructor sets it.</value>
    protected ImmutableArray<EquationDeclaration> Equations { get; init; } = [];

    /// <summary>Gets the unknowns the derived constructor declares.</summary>
    /// <value>
    /// Empty for most kinds: a component's flow belongs to its branch and its pressures to its nodes.
    /// A node and a tank own state of their own.
    /// </value>
    protected ImmutableArray<UnknownDeclaration> Unknowns { get; init; } = [];

    /// <inheritdoc/>
    public ImmutableArray<UnknownDeclaration> DeclareUnknowns() => Unknowns;

    /// <inheritdoc/>
    public ImmutableArray<EquationDeclaration> DeclareEquations() => Equations;

    /// <inheritdoc/>
    public abstract void EvaluateResiduals(in SolveContext context, Span<double> residuals);

    /// <inheritdoc/>
    public virtual void EvaluateEnergyInjection(in SolveContext context, Span<double> injection) => injection.Clear();
}
