using FluidScript.Core.Components;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>The one row the counting table counts that nothing can yet declare.</summary>
/// <remarks>
/// <para>
/// A component two branches cross has two pressure relations by <c>Relations()</c> and declares one,
/// because a coupled exchanger's second side has no momentum equation until <c>P4.1</c> supplies the
/// rated model (<c>S-14b</c>, <c>C-4</c>). Every reconciliation in this folder subtracts it.
/// </para>
/// <para>
/// <strong>Computed rather than listed, so it disappears by itself.</strong> Written as a set of sample
/// names it would have to be remembered and deleted; written as a count of multiply-crossed components
/// it goes to zero the day the equation lands, and every test that subtracts it starts failing until
/// this file is removed. That failure is the reminder.
/// </para>
/// </remarks>
public static class RowAllowance
{
    /// <summary>How many extra pressure relations a multiply-crossed component is counted for.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <returns>The allowance, which is zero for every model without a coupled exchanger.</returns>
    public static int CoupledCrossings(CircuitGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var crossings = new Dictionary<IFlowComponent, int>();

        foreach (var branch in graph.Branches)
        {
            foreach (var part in branch.Path)
            {
                if (part is not CircuitNode)
                {
                    crossings[part] = crossings.GetValueOrDefault(part) + 1;
                }
            }
        }

        return crossings.Values.Sum(static crossed => crossed - 1);
    }
}
