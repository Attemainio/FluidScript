using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics.Descriptors;

/// <summary>What sizing has to say as a code rather than a sentence: the <c>FS23xx</c> range.</summary>
/// <remarks>
/// <para>
/// <c>plan/20-core-domain/24-auto-sizing.md</c> tabulates thirteen; this registers the ones a rule
/// actually detects, which is the rule every other family follows (<c>C-74</c>). Until 2026-09-19
/// every one of these was a free-text entry in <c>OuterLoopResult.Notes</c> -- readable in the solve
/// explanation, invisible on the wire, anchored to nothing. The notes stay, since the explanation
/// reads them in order; each of these is raised beside its note with the component's name, so the
/// editor, the canvas badge and the log can find it (<c>44</c>).
/// </para>
/// <para>
/// Not registered: <c>FS2302</c> (two stated flows on one branch), <c>FS2303</c> (a stated value the
/// loop cannot meet), <c>FS2306</c> (a plausibility bound), <c>FS2308</c>, <c>FS2309</c>,
/// <c>FS2311</c> and <c>FS2313</c>, which no rule detects yet. A code nothing produces does not go on
/// the documentation page.
/// </para>
/// </remarks>
public static class SizingDiagnostics
{
    /// <summary>The sizing loop reached its pass cap with sizes still moving.</summary>
    /// <value><c>FS2301</c>, a warning.</value>
    /// <remarks>
    /// <c>OuterLoopResult.Settled</c> is false and the last pass's values are what the run shows. The
    /// list names what was still changing between the last two passes, so the user knows which value
    /// to state to break the cycle. <c>14</c> called the same event <c>FS1405</c> (an expression's fixed
    /// point); the loop is the one place it happens, and this is its one code (<c>L-21</c>).
    /// </remarks>
    public static DiagnosticDescriptor NotSettled { get; } = new(
        "FS2301",
        DiagnosticSeverity.Warning,
        "Sizes did not settle for {list}. Showing the last values; state them directly to fix.");

    /// <summary>A rule was asked to size against a flow nothing determined.</summary>
    /// <value><c>FS2304</c>, an error.</value>
    /// <remarks>
    /// The pump rule's case: a head is sized to the resistance at the design flow, and with no duty
    /// and no stated flow anywhere on its circuit the flow is zero and so is the head. The circuit
    /// still builds and solves at that head, which is why the sentence names what to state.
    /// </remarks>
    public static DiagnosticDescriptor NothingToSizeAgainst { get; } = new(
        "FS2304",
        DiagnosticSeverity.Error,
        "Cannot size '{name}': no flow is determined anywhere in its branch. State a duty or a flow.");

    /// <summary>A required size is beyond the largest row the catalogue has.</summary>
    /// <value><c>FS2305</c>, a warning.</value>
    /// <remarks>
    /// The pipe rule at the top of its series: the gradient target is missed in the largest bore
    /// there is, and that bore is used. A valve clamped to its series' end is <c>27</c>'s
    /// <c>FS2601</c>/<c>FS2602</c>, not this.
    /// </remarks>
    public static DiagnosticDescriptor OutsideCatalogue { get; } = new(
        "FS2305",
        DiagnosticSeverity.Warning,
        "'{name}' needs more than DN{max}, the largest size in {catalog}. Using DN{max}.");

    /// <summary>The velocity ceiling moved a pipe up a size from what the gradient target chose.</summary>
    /// <value><c>FS2307</c>, informational.</value>
    public static DiagnosticDescriptor SteppedUpForVelocity { get; } = new(
        "FS2307",
        DiagnosticSeverity.Info,
        "'{name}' stepped up to DN{n} for velocity.");

    /// <summary>A discrete plate count delivers more than the stated duty.</summary>
    /// <value><c>FS2310</c>, informational.</value>
    /// <remarks>Raised past <c>SizingDefaults.ExchangerOvershootReport</c>; below it the overshoot is the ordinary rounding of a plate count.</remarks>
    public static DiagnosticDescriptor PlateOvershoot { get; } = new(
        "FS2310",
        DiagnosticSeverity.Info,
        "'{name}' sized to {plates} plates ({area} m²); {required} m² was needed, so it delivers {actual} kW against {stated} kW.");

    /// <summary>A pump sized to zero head because its circuit models no resistance.</summary>
    /// <value><c>FS2312</c>, informational.</value>
    /// <remarks>
    /// <c>D-25</c>: an ideal connection drops nothing, so a pump on a circuit of ideal connections has
    /// nothing to develop head against. Informational because the circuit is consistent as written;
    /// the sentence says what to add if resistance was intended.
    /// </remarks>
    public static DiagnosticDescriptor NoModelledResistance { get; } = new(
        "FS2312",
        DiagnosticSeverity.Info,
        "'{name}' sized to zero head because its circuit contains no modelled resistance. Add a pipe, valve, exchanger drop, or other loss if resistance is intended.");

    /// <summary>A component every declared scenario leaves inert (<c>D-143</c>).</summary>
    /// <value><c>FS2314</c>, a warning naming the cases and what is usually missing.</value>
    /// <remarks>
    /// <para>
    /// <strong>The honest limit of a hand-written case list, made visible.</strong> A scenario list
    /// only checks what someone thought to name, and the failure that produces is not a component
    /// sized too small — it is a component sized to <em>nothing</em>. A recovery exchanger passing
    /// <c>min(Q_heat, Q_cool)</c> between a heating load that peaks in winter and a cooling load that
    /// peaks in summer is zero in both of those cases and governed by the shoulder case in between,
    /// which nobody wrote. It does not come out small; it disappears.
    /// </para>
    /// <para>
    /// <strong>A pattern, not a diagnosis.</strong> This does not claim to know which case is missing
    /// — that would need the dependency between one component's duty and another's, which the model
    /// does not carry. It reports the shape and names the likeliest cause, because a component that is
    /// inert in every case is worth a sentence whatever the reason: the other reason is that it is not
    /// needed at all, and that is also worth knowing.
    /// </para>
    /// <para>
    /// A warning rather than an error: the file is consistent, and a plant may legitimately carry a
    /// standby component that no stated case uses.
    /// </para>
    /// </remarks>
    public static DiagnosticDescriptor InertInEveryScenario { get; } = new(
        "FS2314",
        DiagnosticSeverity.Warning,
        "'{name}' carries no duty and no flow in any of the {count} scenarios ({names}), so nothing sizes it. "
        + "If it exists to serve two demands that peak in different cases, the case where both are on is not in the list.",
        language2Template: "'{name}' carries no duty and no flow in any of the {count} cases ({names}), so nothing sizes it. If it exists to serve two demands that peak in different cases, the case where both are on is not in the list.");

    /// <summary>Gets every code this family emits, for the registry to collect.</summary>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        InertInEveryScenario,
        NotSettled,
        NothingToSizeAgainst,
        OutsideCatalogue,
        SteppedUpForVelocity,
        PlateOvershoot,
        NoModelledResistance,
    ];
}
