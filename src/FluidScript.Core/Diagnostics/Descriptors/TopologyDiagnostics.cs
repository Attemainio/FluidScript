using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics.Descriptors;

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
/// Codes this area owns and does not yet raise: <c>FS2206</c>–<c>FS2209</c> are unallocated, and
/// nothing in the range is deferred — <c>P3.4b</c> raises all ten of <c>23</c>'s error cases, and
/// <c>P3.4c</c> the two consistency codes <c>D-64</c> added beside them.
/// </para>
/// </remarks>
public static class TopologyDiagnostics
{
    /// <summary>No pressure is stated anywhere in a hydraulic connected component.</summary>
    /// <value><c>FS2201</c>, a warning.</value>
    /// <remarks>
    /// A deliberate softening of principle P3 ("infer only what is unambiguous"). Which node carries
    /// the datum is arbitrary; that every pressure is then relative is not, and that is what the
    /// message says. Erroring instead would make a closed loop unsolvable until the user typed a number;
    /// a warning (<c>D-115</c>) keeps the loop solvable under editing and still says, every time, that
    /// the static pressure of a closed circuit is a design number the script has not stated.
    /// </remarks>
    public static DiagnosticDescriptor DatumChosen { get; } = new(
        "FS2201",
        DiagnosticSeverity.Warning,
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
    /// nowhere to put it is square and inconsistent. <c>D-64</c>'s <c>outlet</c> exists to make the
    /// difference between "fluid leaves here" and "this stub is not finished" something the script says
    /// rather than something the checker guesses.
    /// </remarks>
    public static DiagnosticDescriptor UnpairedBoundary { get; } = new(
        "FS2204",
        DiagnosticSeverity.Error,
        "'{circuit}' has an {present} and no {missing}. Fluid must both enter and leave, or neither.");

    /// <summary>A boundary node with more than one connection.</summary>
    /// <value><c>FS2205</c>, an error.</value>
    /// <remarks>
    /// A boundary is a terminal (<c>D-115</c>): the one place fluid crosses into or out of the model,
    /// with the one pipe that carries it. A flow that splits after the inlet or merges before the outlet
    /// does so at a node the script writes, so that the junction is a junction and the boundary states
    /// one stream's condition. Allowing more made the boundary a junction in disguise, which the seed
    /// and the drawing both had to special-case (<c>S-64</c>, <c>28</c> C19).
    /// </remarks>
    public static DiagnosticDescriptor BoundaryFanOut { get; } = new(
        "FS2205",
        DiagnosticSeverity.Error,
        "'{node}' is an {kind} with {count} connections. A boundary has one; split or merge the flow at a node after it.");

    /// <summary>More equations than unknowns.</summary>
    /// <value><c>FS2210</c>, an error.</value>
    /// <remarks>
    /// <strong>The message must name candidates.</strong> "Over-specified by 1" is a puzzle; naming the
    /// statements whose removal squares the system is a fix. Producing that list is what forces the
    /// counting pass to attribute each equation to the statement that added it, rather than accumulate
    /// a total. <c>{advice}</c> is the second sentence <c>23</c>'s promotion rules promise -- a flow
    /// nothing on its branch can change wants a valve <em>added</em>, not a statement removed -- and
    /// is empty when no unmatched constraint is a flow (<c>C-28</c>).
    /// </remarks>
    public static DiagnosticDescriptor OverSpecified { get; } = new(
        "FS2210",
        DiagnosticSeverity.Error,
        "This circuit is over-specified by {n}. Remove one of: {list}{advice}.");

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

    /// <summary>A subgraph coupled to the rest of the model by nothing at all: a system of its own, solved on its own.</summary>
    /// <value><c>FS2213</c>, information.</value>
    /// <remarks>
    /// <strong>More than one hydraulic connected component is legal</strong> (<c>D-17</c>): a rated
    /// exchanger joins two streams that never mix, and the substation's primary and secondary share no
    /// node. This fires only when a subgraph shares no node <em>and</em> no component with the rest.
    /// Until <c>D-132</c> that was an error, "two unrelated models in one file"; a project may hold
    /// several independent systems (<c>D-33</c>), and each fragment already counts, seeds and solves
    /// with its own datum and its own dropped level (<c>C-93</c>), so this now only says so.
    /// </remarks>
    public static DiagnosticDescriptor IsolatedSubgraph { get; } = new(
        "FS2213",
        DiagnosticSeverity.Info,
        "Nothing connects '{list}' to the rest of the plant, so that part is solved as a system of its own.");

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

    /// <summary>A flow constraint answered by a pump on none of its owner's branches.</summary>
    /// <value><c>FS2218</c>, a warning.</value>
    /// <remarks>
    /// <para>
    /// <strong>Reaching across the plant is allowed and this does not stop it</strong> (<c>S-45</c>). Two
    /// parallel branches below one shared pump are both served by it: the first takes its head, the second
    /// falls to its own balancing valve, and that is what a balancing valve is for. What the reach cannot
    /// tell apart is a shared <em>upstream</em> pump from a sibling consumer's, which holds the constrained
    /// branch only weakly through the header pressure.
    /// </para>
    /// <para>
    /// A warning rather than a refusal because the count is still right and the solve may still be; it
    /// is here because the case it was written for was silent. A source whose own pump had been sized
    /// before its constraint was matched reached for a consumer's pump, that consumer's constraint reached
    /// for the next, and the plant reported over-specified by one three promotions later with nothing
    /// naming the first wrong claim (<c>S-59</c>).
    /// </para>
    /// </remarks>
    public static DiagnosticDescriptor ConstraintReachesAcross { get; } = new(
        "FS2218",
        DiagnosticSeverity.Info,
        "'{constraint}' is held by '{pump}', on another branch of its loop: the flow is set through the "
        + "pressure the two branches share. If a pump on its own branch was meant to hold it, free that one.");

    /// <summary>Two stated heights joined by nothing that could span them.</summary>
    /// <value><c>FS2219</c>, an error.</value>
    /// <remarks>
    /// <para>
    /// Only a pipe or a bare node-to-node connection spans two heights (<c>D-70</c>). A pump wired
    /// straight to a load on the roof is on the roof; a pump that <em>says</em> it is in the basement
    /// and is wired straight to that load has left the riser out, and there is no rule that puts it
    /// back. An error rather than a warning because the alternative is to pick one of the two heights,
    /// which fabricates up to 10 kPa of pressure per metre of the difference.
    /// </para>
    /// <para>
    /// Reported on the later declaration's <c>elevation</c>, naming both, so that either fix — a pipe
    /// between them, or one height — is one edit away.
    /// </para>
    /// </remarks>
    public static DiagnosticDescriptor HeightsMeetWithoutAPipe { get; } = new(
        "FS2219",
        DiagnosticSeverity.Error,
        "'{second}' at {b} m is wired directly to '{first}' at {a} m. "
        + "Put a pipe between them, or give them one height.");

    /// <summary>The static head above the datum takes a node below the pressure its fluid can exist at.</summary>
    /// <value><c>FS2220</c>, an error.</value>
    /// <remarks>
    /// <para>
    /// A plant with equipment above its datum needs a fill pressure, and a script that states none has
    /// its datum picked at 0 gauge: the top of a 32 m riser is then 213 kPa below atmospheric, water
    /// has no state there, and the solve used to stop with <c>FS3007</c> after 0 steps, saying nothing
    /// about height (<c>S-60</c>). This names the node, the height, and the pressure to state.
    /// </para>
    /// <para>
    /// The suggested pressure is the static head plus half a bar, which is what practice sets: an
    /// expansion vessel's pre-charge at the static height plus 0.2 bar and the fill pressure 0.3 bar
    /// above that (Flamco, Reflex and IMI Pneumatex vessel-sizing guidance, after EN 12828).
    /// </para>
    /// </remarks>
    public static DiagnosticDescriptor StaticHeadBelowFloor { get; } = new(
        "FS2220",
        DiagnosticSeverity.Error,
        "'{node}' is {rise} m above '{datum}', which puts it {short} kPa below the lowest pressure {substance} can be at. "
        + "State a pressure on '{datum}' of at least {needed} kPa.");

    /// <summary>A solved node sits below atmospheric pressure, so the plant as written has no adequate fill pressure.</summary>
    /// <value><c>FS2221</c>, a warning.</value>
    /// <remarks>
    /// <para>
    /// <see cref="StaticHeadBelowFloor"/>'s sibling after the solve, and the loss-driven half of the
    /// same check: heights are known before the seed, but where a loop's pressures fall relative to its
    /// datum is what the solve finds out. A closed loop with no stated pressure has its datum picked at
    /// the first pump's suction and set to 0 gauge (<c>D-98</c>), and the suction is the low point only
    /// while it is the only pump: a second pump on the ring discharges into that suction, and its own
    /// suction sits its head below the datum (<c>S-29</c>). Water is liquid there and the solve completes
    /// (<c>D-121</c>); this says what the relative figures cannot, which is the pressure to fill the
    /// plant to so that nothing in it is under vacuum.
    /// </para>
    /// <para>
    /// One per hydraulic part, on its lowest node; the suggested pressure is the shortfall plus the same
    /// half-bar margin <see cref="StaticHeadBelowFloor"/> uses, in whole tens of kPa. A warning, not an
    /// error: the circuit solved, and in a loop with no stated pressure the figures are relative anyway.
    /// </para>
    /// </remarks>
    public static DiagnosticDescriptor BelowAtmospheric { get; } = new(
        "FS2221",
        DiagnosticSeverity.Warning,
        "'{node}' is {short} kPa below atmospheric pressure. State a pressure on '{datum}' of at least {needed} kPa.");

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
        ConstraintReachesAcross,
        HeightsMeetWithoutAPipe,
        StaticHeadBelowFloor,
        BelowAtmospheric,
    ];
}
