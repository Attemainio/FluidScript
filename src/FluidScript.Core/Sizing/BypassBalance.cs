using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Solvers;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Sizing;

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
        var values = solution.Values;
        ImmutableArray<ImmutableArray<SolvedPort?>>? solved = null;
        var said = ImmutableArray.CreateBuilder<Diagnostic>();

        for (var index = 0; index < graph.Components.Length; index++)
        {
            if (graph.Components[index] is not ThreeWayValve { BypassConnected: true } three)
            {
                continue;
            }

            var common = ports[index, 0];
            var legA = ports[index, 1];
            var legB = ports[index, 2];

            if (!common.CarriesFlow || !legA.CarriesFlow || !legB.CarriesFlow
                || common.Node < 0 || legA.Node < 0 || legB.Node < 0)
            {
                continue;
            }

            var intoA = legA.Sign * values[layout.BranchFlow(legA.Branch)];
            var intoB = legB.Sign * values[layout.BranchFlow(legB.Branch)];

            // Mixing when both switched legs enter, diverting when both leave. A leg carrying nothing,
            // or the two legs disagreeing, is a valve doing something else and is not this report's.
            var mixing = intoA > Tolerances.FlowZero && intoB > Tolerances.FlowZero;
            var diverting = intoA < -Tolerances.FlowZero && intoB < -Tolerances.FlowZero;

            if (!mixing && !diverting)
            {
                continue;
            }

            var atCommon = values[layout.NodePressure(common.Node)];
            var direction = mixing ? 1 : -1;
            var dropA = direction * (values[layout.NodePressure(legA.Node)] - atCommon);
            var dropB = direction * (values[layout.NodePressure(legB.Node)] - atCommon);
            var imbalance = dropB - dropA;

            solved ??= SolvedStates.Ports(graph, layout, solution);

            if (solved.Value[index][0] is not { } state || !double.IsFinite(state.Density) || state.Density <= 0)
            {
                continue;
            }

            var kv = SolvedStates.Resolved(layout, solution, three.Name, "kv", three.Kv);
            var fullOpen = ValveLaw.PressureDrop(kv, state.Flow, state.Density);
            var line = Math.Max(fullOpen, SizingDefaults.ThreeWayDropMinimum);

            if (!double.IsFinite(line) || Math.Abs(imbalance) <= line)
            {
                continue;
            }

            // The easy leg is the one the valve throttles: the one with the larger drop across it.
            var easy = imbalance > 0 ? 2 : 1;
            var easyDrop = imbalance > 0 ? dropB : dropA;
            var easyFlow = Math.Abs(imbalance > 0 ? intoB : intoA);
            var easyBinding = imbalance > 0 ? legB : legA;
            var branch = graph.Branches[easyBinding.Branch];
            var far = ReferenceEquals(branch.From.Element, three) ? branch.To : branch.From;
            var position = SolvedStates.Resolved(layout, solution, three.Name, "position", three.Position);
            var balancing = ValveLaw.RequiredKv(easyFlow, Math.Abs(imbalance), state.Density);

            said.Add(Diagnostic.Create(
                DesignDiagnostics.LegsUnbalanced,
                span: null,
                new DiagnosticArgument("name", three.Name),
                new DiagnosticArgument("leg", three.Ports[easy].Name),
                new DiagnosticArgument("other", three.Ports[easy == 2 ? 1 : 2].Name),
                new DiagnosticArgument("drop", Kilopascals(easyDrop)),
                new DiagnosticArgument("position", position.ToString("0.00", CultureInfo.InvariantCulture)),
                new DiagnosticArgument("imbalance", Kilopascals(Math.Abs(imbalance))),
                new DiagnosticArgument("band", Kilopascals(line)),
                new DiagnosticArgument("where", far.Label),
                new DiagnosticArgument("flow", easyFlow.ToString("0.###", CultureInfo.InvariantCulture)),
                new DiagnosticArgument("kv", balancing.ToString("0.##", CultureInfo.InvariantCulture)))
                with
            { ComponentName = three.Name });
        }

        return said.ToImmutable();
    }

    private static string Kilopascals(double pascals) => (pascals / 1000).ToString("0.0", CultureInfo.InvariantCulture);
}
