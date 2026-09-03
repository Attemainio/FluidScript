using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>Everything graph construction and well-posedness can report.</summary>
/// <remarks>
/// <para>
/// <c>FS22xx</c> is the topology range (<c>23</c>). Its subject is the circuit rather than the script,
/// which is why most of these carry a component name and no span: the graph holds names, not source
/// text, and a diagnostic about a node the binder inferred has no line to point at.
/// </para>
/// <para>
/// <strong>These are checked before the solver runs</strong>, because every one of them produces a far
/// better message here than in the linear algebra. "This circuit is under-specified by 1; add a
/// pressure to N1, N2 or N3" is actionable; a singular Jacobian is not.
/// </para>
/// <para>
/// Codes this area owns and does not yet raise: <c>FS2205</c>–<c>FS2209</c> are unallocated, and
/// nothing in the range is deferred — <c>P3.4b</c> raises all ten of <c>23</c>'s error cases, and
/// <c>P3.4c</c> the two consistency codes <c>D-64</c> added beside them.
/// </para>
/// </remarks>
public static class TopologyDiagnostics
{
    /// <summary>No pressure is stated anywhere in a hydraulic connected component.</summary>
    /// <value><c>FS2201</c>, informational.</value>
    /// <remarks>
    /// A deliberate softening of principle P3 ("infer only what is unambiguous"). Which node carries
    /// the datum is arbitrary; that every pressure is then relative is not, and that is what the
    /// message says. Erroring instead would make a closed loop — the common case, and the whole of the
    /// syntax reference — unsolvable until the user typed a number that carries no engineering
    /// meaning.
    /// </remarks>
    public static DiagnosticDescriptor DatumChosen { get; } = new(
        "FS2201",
        DiagnosticSeverity.Info,
        "Using '{node}' as the pressure datum. Pressures are relative to it.");

    /// <summary>A port inference rule I3 had to terminate.</summary>
    /// <value><c>FS2202</c>, a warning.</value>
    /// <remarks>
    /// Zero flow is the conservative termination: it changes no other result and it keeps the graph
    /// solvable, so the user sees a diagram with a visibly dangling stub rather than an error. A
    /// three-way valve's bypass is exempt — a mixing valve used as a two-way is an ordinary design,
    /// and warning about it would train the reader to ignore the code.
    /// </remarks>
    public static DiagnosticDescriptor OpenPortTerminated { get; } = new(
        "FS2202",
        DiagnosticSeverity.Warning,
        "'{component}' port '{port}' is not connected; treating it as closed.");

    /// <summary>A closed circuit whose heat does not balance.</summary>
    /// <value><c>FS2203</c>, an error.</value>
    /// <remarks>
    /// <para>
    /// <strong>The counting argument cannot see this, and no amount of promotion will.</strong> A closed
    /// loop with a 30 kW source and no sink is square: it has as many equations as unknowns and no
    /// solution, because summing the energy balances around it gives <c>Σ Q̇ = 0</c> and the stated
    /// duties do not. Consistency is a different question from squareness and needs its own check.
    /// </para>
    /// <para>
    /// <strong>Steady mode only.</strong> The same circuit in a transient is perfectly valid — the water
    /// heats up, which is what the storage term is for. A check that fired there would reject every
    /// warm-up study there is.
    /// </para>
    /// <para>
    /// A pump adds no heat in this model: it contributes one pressure relation and no energy row, so a
    /// closed loop of pumps and pipes balances at exactly zero rather than nearly zero.
    /// </para>
    /// </remarks>
    public static DiagnosticDescriptor UnbalancedClosedCircuit { get; } = new(
        "FS2203",
        DiagnosticSeverity.Error,
        "'{circuit}' is closed and its heat does not balance: {power} with nowhere to go. "
        + "Add a load, a source, or a boundary.");

    /// <summary>Fluid that can enter a circuit and not leave it, or the reverse.</summary>
    /// <value><c>FS2204</c>, an error.</value>
    /// <remarks>
    /// The mass analogue of <see cref="UnbalancedClosedCircuit"/>, and invisible to the count for the
    /// same reason: a stated <c>flow</c> is a known injection, so a circuit that injects mass and has
    /// nowhere to put it is square and inconsistent. <c>D-64</c>'s <c>return</c> exists to make the
    /// difference between "fluid leaves here" and "this stub is not finished" something the script says
    /// rather than something the checker guesses.
    /// </remarks>
    public static DiagnosticDescriptor UnpairedBoundary { get; } = new(
        "FS2204",
        DiagnosticSeverity.Error,
        "'{circuit}' has a {present} and no {missing}. Fluid must both enter and leave, or neither.");

    /// <summary>More equations than unknowns.</summary>
    /// <value><c>FS2210</c>, an error.</value>
    /// <remarks>
    /// <strong>The message must name candidates.</strong> "Over-specified by 1" is a puzzle; naming the
    /// statements whose removal squares the system is a fix. Producing that list is what forces the
    /// counting pass to attribute each equation to the statement that added it, rather than accumulate
    /// a total.
    /// </remarks>
    public static DiagnosticDescriptor OverSpecified { get; } = new(
        "FS2210",
        DiagnosticSeverity.Error,
        "This circuit is over-specified by {n}. Remove one of: {list}.");

    /// <summary>Fewer equations than unknowns.</summary>
    /// <value><c>FS2211</c>, an error.</value>
    /// <remarks>
    /// The mirror of <see cref="OverSpecified"/> and under the same obligation to name candidates: the
    /// unknowns nothing constrains are exactly what the user has to constrain.
    /// </remarks>
    public static DiagnosticDescriptor UnderSpecified { get; } = new(
        "FS2211",
        DiagnosticSeverity.Error,
        "This circuit is under-specified by {n}. Add one of: {list}.");

    /// <summary>Two stated pressures with nothing between them that could make them differ.</summary>
    /// <value><c>FS2212</c>, an error.</value>
    /// <remarks>
    /// <strong>Two stated pressures are normal and this code must not fire on them.</strong> The
    /// cooling loop states <c>N1 p=300</c> and <c>N3 p=280</c>, and it must: those two are what drive
    /// flow through its primary. What is degenerate is two pressures separated only by ideal links —
    /// bare node-to-node connections, which <c>D-25</c> makes zero-drop — because then the second is
    /// not a boundary condition at all but a second, contradictory datum on the same equipotential.
    /// </remarks>
    public static DiagnosticDescriptor CompetingDatums { get; } = new(
        "FS2212",
        DiagnosticSeverity.Error,
        "'{a}' and '{b}' both set a pressure on the same closed loop, with no path between them for "
        + "flow to take. Remove one, or connect them.");

    /// <summary>A subgraph coupled to the rest of the model by nothing at all.</summary>
    /// <value><c>FS2213</c>, an error.</value>
    /// <remarks>
    /// <strong>More than one hydraulic connected component is legal</strong> (<c>D-17</c>): a rated
    /// exchanger joins two streams that never mix, and the substation's primary and secondary share no
    /// node. This fires only when a subgraph shares no node <em>and</em> no component with the rest,
    /// which is the difference between two circuits coupled by heat and two circuits that are two
    /// unrelated models in one file.
    /// </remarks>
    public static DiagnosticDescriptor IsolatedSubgraph { get; } = new(
        "FS2213",
        DiagnosticSeverity.Error,
        "'{list}' are not connected to the rest of the circuit.");

    /// <summary>A loop with no component that can drive flow around it.</summary>
    /// <value><c>FS2214</c>, a warning.</value>
    /// <remarks>
    /// <strong>A warning rather than information, because its consequence is silent.</strong> The loop
    /// simply carries no flow, and every temperature downstream of it is then wrong in a way that
    /// still looks like a solved circuit. It is almost always a pump on the wrong leg, which is the
    /// mistake this project's own reference circuit records.
    /// </remarks>
    public static DiagnosticDescriptor LoopWithoutDriver { get; } = new(
        "FS2214",
        DiagnosticSeverity.Warning,
        "Nothing drives flow around {loop}; it will carry none. Is a pump on the wrong leg?");

    /// <summary>A stated boundary state the substance cannot be in.</summary>
    /// <value><c>FS2215</c>, an error.</value>
    /// <remarks>
    /// Checked against the substance's own validity range before the solver starts, because a property
    /// call outside it either fails inside a Newton iteration — where the message names a residual and
    /// not a temperature — or returns an extrapolation that looks like an answer.
    /// </remarks>
    public static DiagnosticDescriptor StateOutsideRange { get; } = new(
        "FS2215",
        DiagnosticSeverity.Error,
        "{substance} cannot be at {state}.");

    /// <summary>A two-sided component whose owning circuit could not be read off its heat direction.</summary>
    /// <value><c>FS2216</c>, informational.</value>
    /// <remarks>
    /// <strong>Ownership is a tagging and grouping question, never a solver one.</strong> No equation,
    /// unknown, datum or balance depends on it, so the fallback — the lower circuit number — is safe as
    /// well as deterministic. It is reported rather than silent because the diagram groups by circuit,
    /// and a component that landed somewhere arbitrary should say so.
    /// </remarks>
    public static DiagnosticDescriptor AmbiguousOwnership { get; } = new(
        "FS2216",
        DiagnosticSeverity.Info,
        "'{component}' touches {a} and {b} with no clear heat direction; tagging it into {chosen}.");

    /// <summary>A subcircuit attaching to one of its own components.</summary>
    /// <value><c>FS2217</c>, an error.</value>
    /// <remarks>
    /// <strong><c>FS2217</c> and <c>FS1518</c> partition one mistake and never both fire.</strong>
    /// <c>FS1518</c> is the binder's: the name resolves to nothing. This one is the topology's: the
    /// name resolves, to a component of the attaching circuit. Splitting by whether resolution
    /// succeeded — rather than by which document is convenient — is what keeps a single typo from
    /// producing two errors.
    /// </remarks>
    public static DiagnosticDescriptor SelfAttachment { get; } = new(
        "FS2217",
        DiagnosticSeverity.Error,
        "'{circuit}' attaches to '{node}', which is one of its own components. "
        + "A subcircuit attaches to another circuit.");

    /// <summary>Gets every code this area registers.</summary>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        DatumChosen,
        OpenPortTerminated,
        UnbalancedClosedCircuit,
        UnpairedBoundary,
        OverSpecified,
        UnderSpecified,
        CompetingDatums,
        IsolatedSubgraph,
        LoopWithoutDriver,
        StateOutsideRange,
        AmbiguousOwnership,
        SelfAttachment,
    ];
}
