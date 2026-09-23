using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>Warnings about the design rather than the script: the <c>FS40xx</c> range.</summary>
/// <remarks>
/// <para>
/// A design warning is raised against a <em>solved or sized</em> state -- a temperature the fluid
/// cannot have, a velocity a pipe should not carry, an approach an exchanger cannot reach -- so every
/// code here lands with the stage that computes the thing it warns about, not with the binder
/// (<c>D-53</c>). <c>16</c> tabulates nine; this registers the ones a stage actually raises, which
/// is the same rule every other family follows.
/// </para>
/// <para>
/// <c>FS4004</c>-<c>FS4006</c> are today sizing <em>notes</em> rather than diagnostics, which is
/// <c>C-74</c>'s subject and not repeated here. <c>FS4011</c> is the first code past <c>16</c>'s
/// original ten, raised for <c>C-111</c>; <c>FS4013</c> is the first raised across scenarios rather
/// than within one solve (<c>C-121</c>).
/// </para>
/// </remarks>
public static class DesignDiagnostics
{
    /// <summary>An extended exchanger whose achieved approach is below the minimum it must respect.</summary>
    /// <value><c>FS4008</c>, an error.</value>
    /// <remarks>
    /// Approach → 0 is ε → its arrangement maximum and NTU → ∞, so the approach bound is what stops
    /// sizing from returning an infinite exchanger (<c>22</c>). A stated <c>approach</c> is a
    /// constraint like any other (<c>D-02</c>); without one the bound is <c>hx.approach_min</c>, 3 K,
    /// below which a water/water plate exchanger's area grows faster than any plate count can follow
    /// (<c>24</c>). Live for Rated and Coupled modes since <c>P4.1</c>; Duty mode has no approach.
    /// </remarks>
    public static DiagnosticDescriptor ApproachBelowMinimum { get; } = new(
        "FS4008",
        DiagnosticSeverity.Error,
        "'{name}': the approach is {approach} K, below the {minimum} K it must respect. Raise the duty's temperature difference, or accept a closer approach with approach={approach}.");

    /// <summary>A three-way valve whose two switched legs sit at pressures further apart than its own full-open drop, so it throttles the easier leg instead of mixing.</summary>
    /// <value><c>FS4011</c>, a warning.</value>
    /// <remarks>
    /// Raised after the solve by <c>BypassBalance</c>, which is where the leg pressures exist. It names
    /// the balancing valve practice puts on the easy leg -- its drop and Kv at the solved flow -- and
    /// is advice, not a refusal: the solved state is right, and the pump head does not fall when the
    /// balancing valve is added (<c>C-111</c>, <c>24</c>).
    /// </remarks>
    public static DiagnosticDescriptor LegsUnbalanced { get; } = new(
        "FS4011",
        DiagnosticSeverity.Warning,
        "'{name}' throttles its {leg} leg by {drop} kPa at position {position}: that path is {imbalance} kPa easier than the {other} path, more than the {band} kPa the valve drops fully open. A balancing valve between {where} and {name}.{leg} dropping {imbalance} kPa at {flow} kg/s (Kv {kv}) would level the legs and leave the valve its travel.");

    /// <summary>A three-way valve written as a mixing or a diverting valve that the solve runs the other way.</summary>
    /// <value><c>FS4012</c>, a warning.</value>
    /// <remarks>
    /// Raised after the solve, where the flow directions are known: both switched legs entering is
    /// mixing, both leaving is diverting. A seat body is built for one service and the script named
    /// which by its spelling; a bare <c>three_way_valve</c> claims nothing and is never reported
    /// (<c>C-65</c>, <c>D-136</c>).
    /// </remarks>
    public static DiagnosticDescriptor ArrangementContradictsKind { get; } = new(
        "FS4012",
        DiagnosticSeverity.Warning,
        "'{name}' is written as a {declared} valve and the solve runs it {actual}: {detail}. A body built for one service must not be used for the other. Write it as three_way_valve if the arrangement is open, or wire the ports for {declared}.");

    /// <summary>A control valve asked to control a lighter case than its installed rangeability reaches.</summary>
    /// <value><c>FS4013</c>, a warning.</value>
    /// <remarks>
    /// <para>
    /// <c>24</c>'s minimum-flow row, raised by <c>ScenarioSizing</c> once every case has been solved on
    /// the merged plant (<c>C-121</c>). The check is <c>Q_min / Q_max &gt; 1 / (R·√a)</c>: <c>R</c> is the
    /// trim's bench rangeability (<see cref="Sizing.SizingDefaults.ValveRangeability"/>), and the √a
    /// accounts for the valve's share of the drop rising toward 1 as it closes, so it sees more drop
    /// near its seat than on the bench. That correction is this project's reasoning rather than a
    /// standard's, and the part most worth testing.
    /// </para>
    /// <para>
    /// A plant with one case has no turn-down to check. <c>Q_max</c> is the heaviest case's flow, which
    /// is the valve's full-lift flow only to the extent the Kv was chosen on that case; it is the flow
    /// the valve must deliver, which is what the question is about.
    /// </para>
    /// </remarks>
    public static DiagnosticDescriptor TurnDownBeyondRange { get; } = new(
        "FS4013",
        DiagnosticSeverity.Warning,
        "'{name}' must pass {light} kg/s in {lightCase} and {heavy} kg/s in {heavyCase}, {ratio} % of its heaviest flow. {trim} valve at authority {authority} controls down to about {limit} % ({range}:1 × √{authority}), so in {lightCase} it will open and shut rather than modulate. Give the light case a smaller valve in parallel, or split the duty.");

    /// <summary>Gets every code this family emits, for the registry to collect.</summary>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        ApproachBelowMinimum,
        LegsUnbalanced,
        ArrangementContradictsKind,
        TurnDownBeyondRange,
    ];
}
