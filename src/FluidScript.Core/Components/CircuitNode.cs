using System.Collections.Immutable;

using FluidScript.Core.Language;
using FluidScript.Core.Units;

namespace FluidScript.Core.Components;

/// <summary>A point in the circuit with a state and no extent.</summary>
/// <remarks>
/// <para>
/// The primitive. It is the only kind that accepts an arbitrary number of connections, which is what
/// makes it the junction, and <strong>every state in the circuit lives on one</strong> — a pipe or a
/// valve has states at its ports, but those ports attach here. That is why inference rule I2 exists:
/// two components joined directly have no state between them to write an equation about.
/// </para>
/// <para>
/// <strong>Its energy balance is unconditional and its mass balance is not</strong>, and the asymmetry
/// is the whole design. See <see cref="EquationCount"/>.
/// </para>
/// </remarks>
public sealed class CircuitNode : IFlowComponent
{
    private readonly ImmutableArray<UnknownDeclaration> _unknowns;
    private readonly ImmutableArray<EquationDeclaration> _equations;

    /// <summary>Initializes a node of a given degree.</summary>
    /// <param name="name">The user's identifier, or the generated name of an inferred node.</param>
    /// <param name="portCount">How many connections attach here.</param>
    /// <param name="carriesMassBalance">
    /// Whether this node is a junction element or a terminal, which <c>23</c> decides.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="portCount"/> is negative.</exception>
    public CircuitNode(string name, int portCount, bool carriesMassBalance)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(portCount);

        Name = name;
        CarriesMassBalance = carriesMassBalance;

        Ports =
        [
            .. Enumerable.Range(0, portCount).Select(index => new Port
            {
                Name = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Role = PortRole.Bidirectional,
                IsOptional = false,
            }),
        ];

        FlowGroups = [.. Enumerable.Repeat(0, portCount)];

        _unknowns =
        [
            new UnknownDeclaration(0, UnknownKind.NodePressure, name, $"{name}.p", "Pa"),
            new UnknownDeclaration(0, UnknownKind.NodeEnthalpy, name, $"{name}.h", "J/kg"),
        ];

        _equations = carriesMassBalance
            ? [
                new EquationDeclaration(0, EquationKind.Mass, name, $"{name} mass balance", "kg/s"),
                new EquationDeclaration(0, EquationKind.Energy, name, $"{name} energy balance", "W"),
            ]
            : [new EquationDeclaration(0, EquationKind.Energy, name, $"{name} energy balance", "W")];
    }

    /// <summary>The index of this node's pressure among its own unknowns.</summary>
    public const int PressureIndex = 0;

    /// <summary>The index of this node's enthalpy among its own unknowns.</summary>
    public const int EnthalpyIndex = 1;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Kind => "node";

    /// <inheritdoc/>
    /// <value>Always <see langword="null"/>: a node has no modes.</value>
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

    /// <inheritdoc/>
    public ImmutableArray<Port> Ports { get; }

    /// <inheritdoc/>
    /// <value>
    /// One group holding every port. A node is where flows meet, so all of them share it however many
    /// there are — which is what makes a node with three or more connections a junction element and
    /// one with two interior to a branch.
    /// </value>
    public ImmutableArray<int> FlowGroups { get; }

    /// <summary>Gets whether this node contributes a mass balance.</summary>
    /// <value>
    /// <see langword="true"/> for a junction element or a terminal. <c>23</c> owns the definition; this
    /// class only obeys it.
    /// </value>
    public bool CarriesMassBalance { get; }

    /// <summary>Gets which end of an open circuit this node is, when it is one.</summary>
    /// <value>
    /// <see cref="BoundaryRole.Interior"/> unless the script wrote <c>supply</c> or <c>return</c>
    /// (<c>D-64</c>).
    /// </value>
    /// <remarks>
    /// <strong>Not derivable from the parameters, which is the whole reason it is here.</strong> A
    /// terminal node with nothing stated is a legitimate dead leg carrying zero flow, and a
    /// <see cref="BoundaryRole.Return"/> with nothing stated is an outlet whose external flux is an
    /// unknown. The two are the same parameters and opposite equations, so the difference has to be
    /// something the script says.
    /// </remarks>
    public BoundaryRole Boundary { get; init; }

    /// <inheritdoc/>
    /// <value>
    /// Two for a junction element or terminal, one otherwise.
    /// <para>
    /// <strong>The energy count is unconditional, and that is the point.</strong> The tempting
    /// formulation — an energy balance only where three or more ports carry flow — makes the count
    /// depend on a <em>solved</em> flow direction, and reverse flow is legal. The system's size would
    /// then change between Newton iterations. Written over all ports with signed flows, one equation
    /// handles mixing, straight-through and reversal at a fixed size.
    /// </para>
    /// <para>
    /// <strong>The mass count is conditional, and that is not an inconsistency.</strong> A degree-two
    /// node inside a branch has one flow in and the same flow out, because the branch owns a single
    /// flow unknown for every component along it — so its mass balance is <c>ṁ − ṁ = 0</c>, a row of
    /// zeros in the Jacobian for every iterate, singular by construction rather than by user error.
    /// Almost every node in a real circuit is interior to a branch, so this is the normal shape.
    /// </para>
    /// <para>
    /// The condition is <strong>structural, not numerical</strong>: it depends on how many connections
    /// a node has, which is fixed at lowering and never changes during a solve.
    /// </para>
    /// </value>
    public int EquationCount => CarriesMassBalance ? 2 : 1;

    /// <inheritdoc/>
    public ImmutableArray<UnknownDeclaration> DeclareUnknowns() => _unknowns;

    /// <inheritdoc/>
    public ImmutableArray<EquationDeclaration> DeclareEquations() => _equations;

    /// <inheritdoc/>
    /// <remarks>
    /// Residual order is mass then energy, matching <c>22</c>'s own listing. Both are written over
    /// every attached port with signed flows:
    /// <code>
    /// Σᵢ ṁᵢ           = 0     junction elements and terminals only
    /// Σᵢ ṁᵢ · h(ṁᵢ)   = 0     every node
    /// </code>
    /// where <c>h(ṁᵢ)</c> is the arriving enthalpy for an inflow and this node's own for an outflow,
    /// blended across zero by <see cref="Smoothing.Upwind"/> so the Jacobian survives a reversal.
    /// </remarks>
    public void EvaluateResiduals(in SolveContext context, Span<double> residuals)
    {
        var own = context.Unknowns[EnthalpyIndex];
        var mass = 0.0;
        var energy = 0.0;

        for (var port = 0; port < context.PortCount; port++)
        {
            var flow = context.Flows[port];
            var arriving = context.HasPortStates ? context.Ports[port].Enthalpy : own;

            mass += flow;
            energy += flow * Smoothing.Upwind(flow, arriving, own);
        }

        if (CarriesMassBalance)
        {
            residuals[0] = mass;
            residuals[1] = energy;
        }
        else
        {
            residuals[0] = energy;
        }
    }
}

/// <summary>Which end of an open circuit a node is (<c>D-64</c>).</summary>
/// <remarks>
/// <strong>Intent, not parameters.</strong> Whether a terminal is an outlet or an unfinished stub
/// changes its mass balance from an unknown external flux to a zero-flow closure, and no combination
/// of <c>t</c>, <c>p</c> and <c>flow</c> distinguishes them — which is why the script says it and the
/// checker does not guess.
/// </remarks>
public enum BoundaryRole
{
    /// <summary>An ordinary node. Its external flux is zero unless it states a pressure.</summary>
    Interior = 0,

    /// <summary>Fluid enters the model here, in a state the script states.</summary>
    Supply,

    /// <summary>Fluid leaves the model here. Its external flux is an unknown.</summary>
    Return,
}