using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Topology.Hydraulics;

/// <summary>The fill pressure a closed plant needs, and the check that asks for it once the pressures are solved.</summary>
/// <remarks>
/// <para>
/// A closed loop has no absolute pressure of its own: the expansion vessel's fill pressure sets it,
/// and a script that states no <c>p=</c> has its datum picked and set to 0 gauge (<c>D-98</c>). Every
/// pressure the solve reports is then relative to an arbitrary zero, and that zero coincides with the
/// real atmosphere. Until <c>D-121</c> the property table treated the coincidence as physics: water
/// below 0 gauge was refused, so any node the solve needed below the datum ended the run
/// (<c>S-62</c>, <c>S-29</c>). Now the table admits liquid water down to its saturation line, the solve
/// completes, and what remains is the engineering fact the relative figures cannot show: filled at
/// 0 gauge at the datum, this plant would be under vacuum somewhere.
/// </para>
/// <para>
/// Two checks share the margin. <see cref="WellPosedness"/>'s static-head check (<c>FS2220</c>,
/// <c>S-60</c>) runs before the seed on heights alone, because a tall riser can put its top below any
/// pressure the substance has and the seed then cannot start. This one runs after the solve on the
/// solved field, where losses and pump heads have fallen where they fall, and reports the lowest node
/// of each hydraulic part that sits below atmospheric (<c>FS2221</c>).
/// </para>
/// </remarks>
public static class FillPressure
{
    /// <summary>The margin practice adds above the pressure that would just reach atmospheric, for the pressure a message suggests.</summary>
    /// <value>
    /// Pa. Half a bar: an expansion vessel's pre-charge is set to the static height plus 0.2 bar and the
    /// fill pressure 0.3 bar above that (Flamco's <em>Reference Guide</em>, Reflex's <em>Professional
    /// planning, calculation and equipment</em>, IMI Pneumatex's Statico manual, all after EN 12828).
    /// </value>
    public const double Margin = 50_000;

    /// <summary>The temperature the static-head density is taken at.</summary>
    /// <value>K. 20 °C: the plant is filled cold, and the check is about filling.</value>
    public const double FillTemperature = 293.15;

    /// <summary>Below this gauge pressure a solved node counts as under vacuum.</summary>
    /// <value>Pa gauge. −1 Pa: the datum row pins its node to 0 exactly, and its neighbours land within roundoff of it.</value>
    private const double Vacuum = -1.0;

    /// <summary>The pressure to state on the datum so that the lowest point of the plant sits <see cref="Margin"/> above atmospheric.</summary>
    /// <param name="shortfall">Pa. How far the datum's pressure would have to rise for the lowest point to reach atmospheric exactly.</param>
    /// <returns>kPa gauge, in whole tens, as the message quotes it.</returns>
    public static double Suggest(double shortfall) => Math.Ceiling((shortfall + Margin) / 10_000) * 10;

    /// <summary>Reports the lowest solved node of each hydraulic part that sits below atmospheric pressure (<c>FS2221</c>).</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="hydraulics">Its hydraulic parts, each with its datum.</param>
    /// <param name="layout">The state vector's layout, for where each node keeps its pressure.</param>
    /// <param name="solution">The converged iterate.</param>
    /// <returns>At most one diagnostic per hydraulic part, on the node concerned.</returns>
    public static ImmutableArray<Diagnostic> ReportSolved(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        SystemLayout layout,
        StateVector solution)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(solution);

        var index = new Dictionary<GraphNode, int>(ReferenceEqualityComparer.Instance);

        for (var node = 0; node < graph.Nodes.Length; node++)
        {
            index[graph.Nodes[node]] = node;
        }

        var said = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var hydraulic in hydraulics)
        {
            var datum = hydraulic.Nodes.FirstOrDefault(node => node.Name == hydraulic.Datum);

            if (datum?.Component is not CircuitNode anchor)
            {
                continue;
            }

            GraphNode? lowest = null;
            var pressure = double.PositiveInfinity;

            foreach (var node in hydraulic.Nodes)
            {
                if (!index.TryGetValue(node, out var at))
                {
                    continue;
                }

                var gauge = solution.Values[layout.NodePressure(at)];

                if (gauge < pressure)
                {
                    pressure = gauge;
                    lowest = node;
                }
            }

            if (lowest is null || pressure >= Vacuum)
            {
                continue;
            }

            // The datum's pressure that lifts the lowest node to the margin: whatever it states now, plus
            // the depth of the vacuum, plus the margin.
            var stated = HydraulicPartition.Stated(anchor, HydraulicPartition.Pressure) ?? 0;
            var needed = Suggest(stated - pressure);

            said.Add(Diagnostic.Create(
                TopologyDiagnostics.BelowAtmospheric,
                span: null,
                new DiagnosticArgument("node", lowest.Name),
                new DiagnosticArgument("short", (-pressure / 1000).ToString("0", CultureInfo.InvariantCulture)),
                new DiagnosticArgument("datum", hydraulic.Datum),
                new DiagnosticArgument("needed", needed.ToString("0", CultureInfo.InvariantCulture)))
                with
            { ComponentName = lowest.Name });
        }

        return said.ToImmutable();
    }
}
