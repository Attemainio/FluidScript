using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Solvers;

/// <summary>The reference magnitude every residual is divided by before the solver tests it.</summary>
/// <remarks>
/// <para>
/// <strong>The diagonal <c>36</c> calls <c>DF</c>, and the half of the scaling that <c>22</c>'s
/// invariant 5 could not assert.</strong> A component evaluated alone has nothing to be comparable to,
/// so the property is one of the assembled system and belongs here (<c>S-3</c>). Without it the
/// convergence norm measures the pressure equation and nothing else: a pascal residual and a kg/s
/// residual differ by five orders before either is wrong.
/// </para>
/// <para>
/// <strong>A row's scale comes from its unit, and the unit is already on the row.</strong>
/// <see cref="EquationDeclaration.ResidualSiUnit"/> exists so a message can say "off by 4.2 kW"; that
/// it also determines the scale is the same fact used twice, not a coincidence — both need to know
/// what the number physically is.
/// </para>
/// <para>
/// <strong>Power is derived rather than tabulated.</strong> An energy balance is <c>Σ ṁ h</c>, so its
/// natural magnitude is a flow times an enthalpy and its scale is the product of the two scales
/// already in the table. Adding a <c>power_scale</c> row to <c>36</c> would be a fourth number to keep
/// consistent with three that already determine it, and it would not track the branch.
/// </para>
/// </remarks>
public static class ResidualScales
{
    /// <summary>Builds the scale of every equation row.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="layout">The residual vector's layout.</param>
    /// <param name="ports">Which branch each port draws its flow from.</param>
    /// <param name="unknowns">The unknown scales, whose branch entries are the flow scales.</param>
    /// <param name="system">The state vector's layout, which locates those branch entries.</param>
    /// <returns>One positive scale per row, in the layout's order.</returns>
    /// <remarks>
    /// <para>
    /// <strong>A mass balance belongs to a node and flow is scaled per branch, so a node's row takes
    /// the largest of the branches meeting it</strong> (<c>S-13</c>). The residual is a sum of those
    /// flows, so its magnitude is set by the biggest; scaling by the smallest would inflate a residual
    /// that can never get that small and the row would never converge. <c>36</c> introduced per-branch
    /// scaling for a node joining a 10 kg/s primary to a 0.05 kg/s bypass and does not say what that
    /// node's own row takes, which is the one topology both rules were written for.
    /// </para>
    /// <para>
    /// A row belonging to no component — a stated pressure, a datum, an ideal link — takes the fixed
    /// reference for its unit, because there is no branch to ask.
    /// </para>
    /// </remarks>
    public static ImmutableArray<double> Build(
        CircuitGraph graph,
        EquationLayout layout,
        PortMap ports,
        ImmutableArray<double> unknowns,
        SystemLayout system)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(system);

        var owners = Owners(graph, layout);
        var scales = ImmutableArray.CreateBuilder<double>(layout.Count);

        for (var row = 0; row < layout.Count; row++)
        {
            var flow = Flow(graph, ports, unknowns, system, owners[row]);

            scales.Add(layout.Rows[row].ResidualSiUnit switch
            {
                "Pa" => Tolerances.PressureScale,
                "J/kg" => Tolerances.EnthalpyScale,
                "K" => Tolerances.TemperatureScale,
                "kg/s" => flow,
                "W" => flow * Tolerances.EnthalpyScale,

                // A unit nothing here recognises scales by one, which leaves the row in SI and is the
                // honest answer: a wrong scale is worse than none, because it silently reweights a
                // convergence test that reads as though it had been thought about.
                _ => 1,
            });
        }

        return scales.MoveToImmutable();
    }

    /// <summary>Finds which component owns each row, or <c>-1</c> for the rows assembly owns.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="layout">The residual vector's layout.</param>
    /// <returns>One component index per row.</returns>
    private static int[] Owners(CircuitGraph graph, EquationLayout layout)
    {
        var owners = new int[layout.Count];

        Array.Fill(owners, -1);

        for (var index = 0; index < graph.Components.Length; index++)
        {
            var rows = layout.Components[index];

            for (var row = rows.FirstRow; row < rows.FirstRow + rows.RowCount; row++)
            {
                owners[row] = index;
            }
        }

        return owners;
    }

    /// <summary>The flow magnitude a component's rows are measured against.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="ports">Which branch each port draws its flow from.</param>
    /// <param name="unknowns">The unknown scales.</param>
    /// <param name="system">The state vector's layout.</param>
    /// <param name="component">The owning component, or <c>-1</c>.</param>
    /// <returns>kg/s, the largest branch scale meeting it, floored.</returns>
    private static double Flow(
        CircuitGraph graph,
        PortMap ports,
        ImmutableArray<double> unknowns,
        SystemLayout system,
        int component)
    {
        var largest = Tolerances.FlowScaleFloor;

        if (component < 0)
        {
            return largest;
        }

        for (var port = 0; port < graph.Components[component].Ports.Length; port++)
        {
            var binding = ports[component, port];

            if (binding.CarriesFlow)
            {
                largest = Math.Max(largest, unknowns[system.BranchFlow(binding.Branch)]);
            }
        }

        return largest;
    }
}
