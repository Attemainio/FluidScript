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
    public SolveContext(ISubstance substance, ReadOnlySpan<PortState> ports, ReadOnlySpan<double> flows)
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

    /// <summary>Gets the number of ports.</summary>
    public int PortCount => Flows.Length;

    /// <summary>Gets whether this context carries evaluated port states.</summary>
    /// <value>
    /// <see langword="false"/> for a context built by <see cref="ForSingleComponent"/>. A component
    /// that reads <see cref="Ports"/> should check this rather than indexing an empty span.
    /// </value>
    public bool HasPortStates => !Ports.IsEmpty;

    /// <summary>Builds a context for a component tested on its own, with no ports.</summary>
    /// <param name="substance">The substance, normally one of the fakes.</param>
    /// <param name="flows">The mass flow at each port, kg/s — usually one value.</param>
    /// <returns>A context carrying those flows and no port states.</returns>
    /// <remarks>
    /// <para>
    /// For a unit test of a component whose residual is written against its own stated parameters and
    /// the flow through it — <c>62</c>'s worked example is exactly this shape. A component that reads
    /// <see cref="Ports"/> needs the full constructor and a hand-built state.
    /// </para>
    /// <para>
    /// It takes a span rather than a <see langword="double"/> because the caller has to own the
    /// storage: a span over a by-value parameter cannot outlive the call that made it, and the
    /// compiler says so rather than letting the context carry a dangling reference. At a call site the
    /// difference is one pair of brackets — <c>ForSingleComponent(FakeWater.Instance, [0.2391])</c>.
    /// </para>
    /// </remarks>
    public static SolveContext ForSingleComponent(ISubstance substance, ReadOnlySpan<double> flows) =>
        new(substance, [], flows);
}
