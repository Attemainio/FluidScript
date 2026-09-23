using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Solvers;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Sizing.Flows;

/// <summary>Reads a solved three-way valve's two legs against each other and names the balancing valve practice puts on the easier one (<c>FS4011</c>, <c>C-111</c>).</summary>
/// <remarks>
/// <para>
/// <strong>A three-way valve mixes or splits; it is not built to hold a pressure difference between
/// its legs.</strong> Its two switched ports share one <c>kv</c> and one <c>position</c>, and the
/// legs' openings are complementary, so the position the mix ratio wants is the ratio itself: half
/// and half sits at 0.5. That holds only when the two legs see the same pressure at their far ends.
/// When one path is easier than the other -- a bare bypass against a primary loop the pump has to
/// drive -- the valve has to throttle the easy leg to make the flows come out, and it leaves its
/// mixing position to do it. On the ladder's series header the radiators' valve sits at 0.784
/// throttling its bypass by 35 kPa for a coil rated 20, because the bypass returns to the mixing
/// node at 32 kPa above the primary supply.
/// </para>
/// <para>
/// <strong>Practice puts a balancing valve on the easy leg</strong>, set so that its drop matches
/// the path it is in parallel with; <c>24</c> cites the guidance. That leaves the three-way valve at
/// the position its ratio implies, with its whole travel available for control, and it does not save
/// pump head: the balancing valve dissipates what the three-way valve was dissipating. The report
/// says so, with the drop and the Kv the balancing valve needs at the solved flow.
/// </para>
/// <para>
/// <strong>The line is the valve's own full-open drop.</strong> An imbalance the valve can absorb
/// inside the drop it was sized for is the working margin every mixing valve carries; one beyond it
/// means the valve is spending more travel on balancing than on mixing. The floor is
/// <see cref="SizingDefaults.ThreeWayDropMinimum"/>, so a valve stated far too large for its flow
/// does not report a few hundred pascals as an imbalance. This threshold is this project's
/// reasoning, not a published figure; <c>24</c> marks it so.
/// </para>
/// </remarks>
public static class BypassBalance
{
    /// <summary>Reports each three-way valve whose two switched legs sit at pressures further apart than the valve's own full-open drop (<c>FS4011</c>).</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">The state vector's layout.</param>
    /// <param name="solution">The converged iterate.</param>
    /// <returns>At most one diagnostic per three-way valve, on the valve.</returns>
    public static ImmutableArray<Diagnostic> ReportSolved(CircuitGraph graph, SystemLayout layout, StateVector solution)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(solution);

        var ports = PortMap.Build(graph);
        ImmutableArray<ImmutableArray<SolvedPort?>>? solved = null;
        var said = ImmutableArray.CreateBuilder<Diagnostic>();

        for (var index = 0; index < graph.Components.Length; index++)
        {
            if (graph.Components[index] is not ThreeWayValve three
                || Read(graph, ports, layout, solution.Values.AsSpan(), index) is not { } legs)
            {
                continue;
            }

            solved ??= SolvedStates.Ports(graph, layout, solution);

            if (solved.Value[index][0] is not { } state || !double.IsFinite(state.Density) || state.Density <= 0)
            {
                continue;
            }

            var kv = SolvedStates.Resolved(layout, solution, three.Name, "kv", three.Kv);
            var line = Line(kv, state.Flow, state.Density);

            if (!double.IsFinite(line) || Math.Abs(legs.Imbalance) <= line)
            {
                continue;
            }

            // The easy leg is the one the valve throttles: the one with the larger drop across it.
            var easy = legs.Imbalance > 0 ? 2 : 1;
            var easyDrop = legs.Imbalance > 0 ? legs.DropB : legs.DropA;
            var easyFlow = legs.Imbalance > 0 ? legs.FlowB : legs.FlowA;
            var branch = graph.Branches[ports[index, easy].Branch];
            var far = ReferenceEquals(branch.From.Element, three) ? branch.To : branch.From;
            var position = SolvedStates.Resolved(layout, solution, three.Name, "position", three.Position);
            var balancing = ValveLaw.RequiredKv(easyFlow, Math.Abs(legs.Imbalance), state.Density);

            said.Add(Diagnostic.Create(
                DesignDiagnostics.LegsUnbalanced,
                span: null,
                new DiagnosticArgument("name", three.Name),
                new DiagnosticArgument("leg", three.Ports[easy].Name),
                new DiagnosticArgument("other", three.Ports[easy == 2 ? 1 : 2].Name),
                new DiagnosticArgument("drop", Kilopascals(easyDrop)),
                new DiagnosticArgument("position", position.ToString("0.00", CultureInfo.InvariantCulture)),
                new DiagnosticArgument("imbalance", Kilopascals(Math.Abs(legs.Imbalance))),
                new DiagnosticArgument("band", Kilopascals(line)),
                new DiagnosticArgument("where", far.Label),
                new DiagnosticArgument("flow", easyFlow.ToString("0.###", CultureInfo.InvariantCulture)),
                new DiagnosticArgument("kv", balancing.ToString("0.##", CultureInfo.InvariantCulture)))
                with
            { ComponentName = three.Name });
        }

        return said.ToImmutable();
    }

    /// <summary>The imbalance a three-way valve is allowed before it is reported: its full-open drop at the common flow, never under <see cref="SizingDefaults.ThreeWayDropMinimum"/>.</summary>
    /// <param name="kv">The valve's rated Kv, m³/h at 1 bar.</param>
    /// <param name="commonFlow">kg/s through the common port; its magnitude is used.</param>
    /// <param name="density">kg/m³.</param>
    /// <returns>Pa.</returns>
    public static double Line(double kv, double commonFlow, double density) =>
        Math.Max(ValveLaw.PressureDrop(kv, commonFlow, density), SizingDefaults.ThreeWayDropMinimum);

    /// <summary>Reads a three-way valve's two switched legs off an iterate: the drop across each to the common port, and the flow each carries.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="ports">Its port map.</param>
    /// <param name="layout">The state vector's layout.</param>
    /// <param name="values">The iterate.</param>
    /// <param name="index">The valve's index in <see cref="CircuitGraph.Components"/>.</param>
    /// <returns>
    /// The reading, or <see langword="null"/> when the component is not a three-way valve with its
    /// bypass connected and all three legs reached, or when its switched legs are not both entering
    /// (mixing) or both leaving (diverting): a leg carrying nothing, or the two disagreeing, is a
    /// valve doing something else and has no leg balance to speak of.
    /// </returns>
    public static LegReading? Read(CircuitGraph graph, PortMap ports, SystemLayout layout, ReadOnlySpan<double> values, int index)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(layout);

        if (graph.Components[index] is not ThreeWayValve { BypassConnected: true })
        {
            return null;
        }

        var common = ports[index, 0];
        var legA = ports[index, 1];
        var legB = ports[index, 2];

        if (!common.CarriesFlow || !legA.CarriesFlow || !legB.CarriesFlow
            || common.Node < 0 || legA.Node < 0 || legB.Node < 0)
        {
            return null;
        }

        var intoA = legA.Sign * values[layout.BranchFlow(legA.Branch)];
        var intoB = legB.Sign * values[layout.BranchFlow(legB.Branch)];
        var mixing = intoA > Tolerances.FlowZero && intoB > Tolerances.FlowZero;
        var diverting = intoA < -Tolerances.FlowZero && intoB < -Tolerances.FlowZero;

        if (!mixing && !diverting)
        {
            return null;
        }

        var atCommon = values[layout.NodePressure(common.Node)];
        var direction = mixing ? 1 : -1;

        return new LegReading(
            mixing,
            direction * (values[layout.NodePressure(legA.Node)] - atCommon),
            direction * (values[layout.NodePressure(legB.Node)] - atCommon),
            Math.Abs(intoA),
            Math.Abs(intoB));
    }

    private static string Kilopascals(double pascals) => (pascals / 1000).ToString("0.0", CultureInfo.InvariantCulture);
}
