using FluidScript.Core.Fluids;

namespace FluidScript.Core.Components;

/// <summary>One port's fluid state at the current iterate, in SI.</summary>
/// <remarks>
/// <para>
/// <strong>Every property is already evaluated.</strong> A component reads them; it never fixes a
/// state itself. That is not a convenience — it is what makes <c>22</c>'s zero-allocation rule
/// achievable at all, because <see cref="FluidState"/> is a reference type and every call that
/// produced one inside a residual evaluation would allocate, N+1 times per Newton iteration
/// (<c>C-16</c>).
/// </para>
/// <para>
/// Pressure is gauge and temperature absolute, like everywhere else in the model (<c>D-26</c>). The
/// state pairing a component consumes and produces is <c>(p, h)</c>; temperature and density are
/// derived and are carried here so that no component has to invert <c>cp</c> to get one
/// (<c>22</c> convention 3).
/// </para>
/// </remarks>
public readonly record struct PortState
{
    /// <summary>Gets the gauge pressure at the port.</summary>
    /// <value>Pa, gauge.</value>
    public required double Pressure { get; init; }

    /// <summary>Gets the specific enthalpy at the port.</summary>
    /// <value>J/kg.</value>
    public required double Enthalpy { get; init; }

    /// <summary>Gets the temperature at the port.</summary>
    /// <value>K.</value>
    public required double Temperature { get; init; }

    /// <summary>Gets the density at the port.</summary>
    /// <value>kg/m³.</value>
    public required double Density { get; init; }

    /// <summary>Gets the specific heat at the port.</summary>
    /// <value>J/(kg·K).</value>
    public required double SpecificHeat { get; init; }

    /// <summary>Gets the dynamic viscosity at the port.</summary>
    /// <value>Pa·s.</value>
    /// <remarks>A pipe cannot form a Reynolds number without it, so it is carried rather than fetched.</remarks>
    public required double DynamicViscosity { get; init; }

    /// <summary>Gets the thermal conductivity at the port.</summary>
    /// <value>W/(m·K).</value>
    /// <remarks>
    /// Unused by any v1 residual: an exchanger given <c>ua</c> needs no transport property, and one
    /// sized from geometry is <c>P3.5</c>'s. It is carried because the set a port offers should be the
    /// set <see cref="FluidState"/> holds — a component reaching for the seventh property and finding
    /// six would have no way to get it, this type being the only thing it may read.
    /// </remarks>
    public required double ThermalConductivity { get; init; }
}

/// <summary>What a flow component sees when it evaluates its residuals.</summary>
/// <remarks>
/// <para>
/// <strong>A <see langword="ref"/> struct, deliberately.</strong> It carries spans over buffers the
/// solver owns, so it cannot be stored, boxed or captured — and a component that tried to keep one
/// between iterations would not compile rather than reading a stale iterate.
/// </para>
/// <para>
/// <strong>It contains no way to fix a state.</strong> <see cref="Substance"/> is here for the
/// constants a component may legitimately need, not as a licence to call the backend: a property call
/// costs a few hundred microseconds and allocates, and this method runs N+1 times per Newton
/// iteration. Everything a residual needs is in <see cref="Ports"/> already.
/// </para>
/// <para>
/// <c>22</c> and <c>62</c> both name this type and neither defines it; <c>31</c> defines the unknown
/// and equation records and stops short of this one. It is placed here rather than in tier 30 for the
/// same reason <see cref="NodeObservation"/> is: it describes what a component <em>reads</em>, and the
/// component interface is this document's (<c>C-17</c>).
/// </para>
/// </remarks>
public readonly ref struct SolveContext
{
    /// <summary>Initializes a context over the solver's own buffers.</summary>
    /// <param name="substance">The circuit's working fluid.</param>
    /// <param name="ports">One entry per port, in the component's declared port order.</param>
    /// <param name="flows">Mass flow at each port, kg/s, positive into the component.</param>
    /// <param name="unknowns">
    /// The component's own unknowns at this iterate, in the order <c>DeclareUnknowns</c> gave them.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="ports"/> is non-empty and differs in length from <paramref name="flows"/>.
    /// </exception>
    /// <remarks>
    /// <strong>States may be omitted; flows may not.</strong> A component whose residual is written
    /// against its own stated parameters and the flow through it never reads a port state, and a test
    /// of one should not have to invent five properties per port to say so — an invented state reads as
    /// data the assertion depends on. Passing no states is therefore legal and means exactly that;
    /// passing some means passing all.
    /// </remarks>
    /// <param name="parameters">
    /// This component's resolvable parameter values, indexed as <c>Resolvable</c> declares them. Empty
    /// where the caller has nothing to supply, which is the ordinary case for a unit test.
    /// </param>
    public SolveContext(
        ISubstance substance,
        ReadOnlySpan<PortState> ports,
        ReadOnlySpan<double> flows,
        ReadOnlySpan<double> unknowns = default,
        ReadOnlySpan<double> parameters = default)
    {
        ArgumentNullException.ThrowIfNull(substance);

        if (!ports.IsEmpty && ports.Length != flows.Length)
        {
            throw new ArgumentException(
                $"Got {ports.Length} port states and {flows.Length} flows; there must be one flow per port.",
                nameof(flows));
        }

        Substance = substance;
        Ports = ports;
        Flows = flows;
        Unknowns = unknowns;
        Parameters = parameters;
    }

    /// <summary>Gets the working fluid of the circuit this component sits in.</summary>
    /// <remarks>For declared constants only. Fixing a state here allocates, and this is a hot path.</remarks>
    public ISubstance Substance { get; }

    /// <summary>Gets each port's state, in the component's declared port order.</summary>
    public ReadOnlySpan<PortState> Ports { get; }

    /// <summary>Gets the mass flow at each port, in the same order.</summary>
    /// <value>
    /// kg/s, <strong>positive into the component</strong>. A negative value is legal and means the
    /// flow reversed, which is a real answer rather than a mistake (<c>22</c> convention 2).
    /// </value>
    public ReadOnlySpan<double> Flows { get; }

    /// <summary>Gets this component's own unknowns at the current iterate.</summary>
    /// <value>
    /// SI, in the order <c>DeclareUnknowns</c> declared them. A node's pressure and enthalpy live here
    /// rather than in <see cref="Ports"/>: a node <em>is</em> a state point, and the states its ports
    /// carry belong to what is attached to it, not to it.
    /// </value>
    public ReadOnlySpan<double> Unknowns { get; }

    /// <summary>Gets the number of ports.</summary>
    public int PortCount => Flows.Length;
    /// <summary>Gets whether this context carries evaluated port states.</summary>
    /// <value>
    /// A component that reads <see cref="Ports"/> should check this rather than indexing an empty span.
    /// </value>
    /// <remarks>
    /// <para>
    /// <strong>There is no <c>ForSingleComponent</c> helper, and the attempt to write one is worth
    /// recording.</strong> <c>62</c>'s worked example builds a context from a substance and a mass flow
    /// alone and evaluates a duty exchanger's energy balance against it. That cannot work: the relation
    /// <c>22</c> states is <c>Q̇ = ṁ(h_out − h_in)</c> over the <em>solved</em> port enthalpies, and a
    /// context carrying no port states has no enthalpies to difference (<c>C-19</c>).
    /// </para>
    /// <para>
    /// The reading that example implies — a duty from stated terminal temperatures and a <c>cp</c> — is
    /// a real relation, but it is the one reporting what three stated values imply about the fourth,
    /// not a residual. It lives on the component, as <c>HeatExchanger.ImpliedFlow</c>.
    /// </para>
    /// </remarks>
    public bool HasPortStates => !Ports.IsEmpty;

    /// <summary>Gets the values of the parameters this component reads from outside itself.</summary>
    /// <value>
    /// SI, indexed as <c>IFlowComponent.Resolvable</c> declares them, and empty where the caller
    /// supplied none. Read it through <see cref="Parameter"/> rather than directly.
    /// </value>
    /// <remarks>
    /// <strong>Separate from <see cref="Unknowns"/>, and the two must not be merged.</strong> A
    /// component's own unknown is state it carries — a tank's mixed enthalpy — and exists whatever the
    /// script says. A resolvable parameter is a coefficient somebody else decides: sizing, in the outer
    /// loop, or promotion, per iterate (<c>D-02</c>). Putting a promoted pump head at index 1 of a
    /// tank's unknowns would make a component's own indices depend on what the user wrote.
    /// </remarks>
    public ReadOnlySpan<double> Parameters { get; }

    /// <summary>Reads one resolvable parameter, or the component's own value where none was supplied.</summary>
    /// <param name="index">Its position in <c>IFlowComponent.Resolvable</c>.</param>
    /// <param name="own">What to use when the caller supplied nothing — the component's stored value.</param>
    /// <returns>The value in SI.</returns>
    /// <remarks>
    /// The fallback is what lets a residual be tested against a hand-built context with no parameter
    /// buffer at all, and what lets a component whose parameter nothing promoted keep using its own.
    /// One bounds check on the iteration path, and no branch a profiler will find.
    /// </remarks>
    public double Parameter(int index, double own) =>
        index < Parameters.Length ? Parameters[index] : own;
}
