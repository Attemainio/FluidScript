using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Topology.Counting;

/// <summary>Checks that a graph has exactly one solution, before the solver is asked to find it.</summary>
/// <remarks>
/// <para>
/// <strong>Every check here produces a far better message than the linear algebra would.</strong> "This
/// circuit is over-specified by 1; remove HE1.in" sends the user to a line they wrote. A singular
/// Jacobian sends them nowhere, and an ill-conditioned one sends them somewhere worse — to a plausible
/// answer.
/// </para>
/// <para>
/// <strong>It runs on the graph alone.</strong> Nothing here reaches back into the semantic model, which
/// is what lets the solver's own tests build a graph by hand and check it, and what keeps the tier-10
/// boundary the architecture test asserts.
/// </para>
/// </remarks>
public static partial class WellPosedness
{
    /// <summary>A heat exchanger's stated side-1 inlet temperature.</summary>
    /// <remarks>
    /// Side 1 only. Side 2 is either not in the graph (Duty, Rated) or a coupled stream whose design
    /// point is handled as a pair (<c>D-97</c>), so <c>in2</c> is never a demand on a node here.
    /// </remarks>
    private static readonly string[] Inlets = ["in"];

    /// <summary>A heat exchanger's side-1 statements that pin a flow, in the order they are matched.</summary>
    private static readonly string[] FlowPins = ["out", "dt"];

    /// <summary>A heat exchanger's statements that fix an absolute temperature rather than a difference.</summary>
    /// <remarks><c>dt</c> is deliberately absent: it is the difference the level is free of.</remarks>
    private static readonly string[] Terminals = ["in", "out", "in2", "out2"];

    /// <summary>Checks a lowered graph.</summary>
    /// <param name="graph">The graph to check.</param>
    /// <returns>The counting table, the hydraulic partition, and the diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// It reports rather than throws, whatever the graph contains — a graph lowered from a script under
    /// editing is malformed most of the time, and this runs on every keystroke.
    /// </remarks>
    public static WellPosednessResult Check(CircuitGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var hydraulics = HydraulicPartition.Of(graph);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        ReportDatums(hydraulics, diagnostics);
        ReportIsolation(hydraulics, diagnostics);
        ReportCompetingDatums(graph, hydraulics, diagnostics);
        ReportDriverlessLoops(graph, diagnostics);
        ReportStates(graph, diagnostics);
        ReportStaticHead(graph, hydraulics, diagnostics);
        ReportClosure(graph, hydraulics, diagnostics);
        ReportBoundaries(graph, hydraulics, diagnostics);
        ReportSetpoints(graph, diagnostics);
        ReportScheduledActuators(graph, diagnostics);

        var constraints = Constraints(graph, hydraulics);
        var assignment = Promote(graph, hydraulics, constraints);
        var promotions = assignment.Promotions;
        var counting = Count(graph, hydraulics, constraints, promotions);

        ReportBalance(graph, hydraulics, counting, assignment, diagnostics);
        ReportReaches(graph, promotions, diagnostics);

        return new WellPosednessResult(counting, hydraulics, diagnostics.ToImmutable());
    }

    /// <summary>The flow statements that pin a branch outright: a mass flow or a volume flow, per side (P5.13b).</summary>
    public static readonly ImmutableArray<string> StatedFlows = ["flow", "vflow", "flow2", "vflow2"];

    /// <summary>Whether a constraint's parameter is one of <see cref="StatedFlows"/>.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns><see langword="true"/> for a stated flow.</returns>
    public static bool IsStatedFlow(string parameter) => StatedFlows.Contains(parameter);

    /// <summary>A coupled exchanger's two sides: the terminals that pin each, and the port that finds its hydraulic.</summary>
    private static readonly (string Inlet, string Outlet, string Change, int Port)[] CoupledSides =
    [
        ("in", "out", "dt", 0),
        ("in2", "out2", "dt2", 2),
    ];

    /// <summary>Whether an exchanger states a duty of exactly nothing: a consumer switched off.</summary>
    /// <param name="element">The component.</param>
    /// <returns><see langword="true"/> when <c>power</c> is stated and zero.</returns>
    /// <remarks>
    /// <para>
    /// <c>power=0</c> is an operating state, not a missing size (<c>S-56</c>): the coil is there, its
    /// design terminals are written, and today it takes nothing. Its <c>out</c> with <c>in</c> then
    /// pins the branch at zero flow -- a pump holding a stopped branch -- and its <c>in</c> asks nothing
    /// of the split that feeds it. An unstated <c>power</c> is a size to find and is not this.
    /// </para>
    /// </remarks>
    public static bool ZeroDuty(IFlowComponent element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element is HeatExchangerComponent
            && HydraulicPartition.Stated(element, "power") is { } power
            && Math.Abs(power) <= Solvers.Tolerances.PowerZero;
    }
}
