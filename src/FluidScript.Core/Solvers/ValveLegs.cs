using FluidScript.Core.Components;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Solvers;

/// <summary>Tells a three-way valve's bypass leg from the leg it exchanges flow with the plant through.</summary>
/// <remarks>
/// <para>
/// <strong>The bypass leg is the one that closes the valve's own loop</strong>, which is what a bypass is.
/// So it is the leg whose far end gets back to the common leg's far end in the fewest components without
/// passing through the valve, and the other leg is the variable one.
/// </para>
/// <para>
/// <strong>This replaced asking whether a leg's immediate neighbour states a pressure</strong> (<c>C-66</c>).
/// That was a proxy for the same idea, and it coincides with it only when the plant boundary happens to be
/// adjacent: on <c>m2-cooling-loop</c> the controlled leg runs straight into <c>N3 return p=280</c>, so it
/// worked. A header states its pressure at the plant, three or more hops from any valve leg, so
/// <em>every</em> three-way valve on one declined and kept the bootstrap Kv 630 -- a Kv law demanding about
/// 78 kg/s on a plant that moves 0.93, whose residual buries every other equation in the system.
/// </para>
/// <para>
/// <strong>The distance comparison is this project's reasoning rather than an inherited convention</strong>,
/// and the case it would read backwards is a short tap off a header feeding a long secondary. A stronger
/// signal exists and was not needed here: on a header the two legs land in different <em>circuits</em>,
/// which the script declares outright.
/// </para>
/// </remarks>
public static class ValveLegs
{
    /// <summary>Which leg carries what the other two split.</summary>
    /// <param name="legs">The valve's three legs.</param>
    /// <param name="flows">Each leg's absolute flow at the current iterate, kg/s.</param>
    /// <param name="valve">The valve, so each leg's own end can be told from its far one.</param>
    /// <returns>The index of the common leg.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The port name is believed before the flows, because on the first pass the flows are the
    /// seed's and the seed is free to orient a branch either way</strong> (<c>C-66</c>). <c>D-85</c> settles
    /// <c>ab</c> as the common port, and a script that writes <c>TV_AHU.b - NM_AHU</c> has said which leg is
    /// which. Measured on the header's seed: the <c>a</c> leg carries 0.5736 kg/s against 0.2868 on the
    /// other two, so mass balance holds with <c>a</c> as the sum and "largest flow" names the wrong leg --
    /// after which the two remaining legs share a far end, are equidistant, and the valve declines.
    /// </para>
    /// <para>
    /// The flow test remains the fallback for a script that connects the valve without naming ports, where
    /// binding is positional and the letters carry no meaning. It is right at a converged iterate and only
    /// there, which is why it cannot be the primary test.
    /// </para>
    /// </remarks>
    public static int Common(Branch[] legs, double[] flows, IFlowComponent valve)
    {
        for (var leg = 0; leg < legs.Length; leg++)
        {
            var port = ReferenceEquals(legs[leg].From.Element, valve)
                ? legs[leg].From.PortName
                : legs[leg].To.PortName;

            if (string.Equals(port, "ab", StringComparison.Ordinal))
            {
                return leg;
            }
        }

        return Array.IndexOf(flows, flows.Max());
    }


    /// <param name="graph">The lowered circuit, walked to measure how far each leg runs.</param>
    /// <param name="legs">The valve's three legs.</param>
    /// <param name="common">The index of the leg carrying what the other two split.</param>
    /// <param name="valve">The valve itself, which no walk may pass back through.</param>
    /// <returns>The index of the variable leg, or -1 when the two cannot be told apart.</returns>
    public static int Variable(CircuitGraph graph, Branch[] legs, int common, IFlowComponent valve)
    {
        var anchor = Far(legs[common], valve);
        var first = -1;
        var second = -1;

        for (var leg = 0; leg < legs.Length; leg++)
        {
            if (leg == common)
            {
                continue;
            }

            if (first < 0)
            {
                first = leg;
            }
            else
            {
                second = leg;
            }
        }

        if (first < 0 || second < 0)
        {
            return -1;
        }

        var reach = Distance(graph, Far(legs[first], valve), anchor, valve);
        var other = Distance(graph, Far(legs[second], valve), anchor, valve);

        return reach == other ? -1 : reach > other ? first : second;
    }

    /// <summary>The element at the end of a leg that is not the valve.</summary>
    /// <param name="leg">A branch with the valve at one end.</param>
    /// <param name="valve">The valve, so the other end can be told from it.</param>
    /// <returns>The element at the leg's far end.</returns>
    public static IFlowComponent Far(Branch leg, IFlowComponent valve) =>
        ReferenceEquals(leg.From.Element, valve) ? leg.To.Element : leg.From.Element;

    /// <summary>Components crossed getting from one element to another without passing through a third.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="from">Where the walk starts.</param>
    /// <param name="target">Where it is trying to reach.</param>
    /// <param name="barred">An element the walk may not enter, which is how a leg is measured around a valve.</param>
    /// <returns>The fewest components crossed, or <see cref="int.MaxValue"/> when the target is unreachable.</returns>
    /// <remarks>
    /// Unreachable is the ordinary answer rather than an error: a leg that leaves through a boundary --
    /// <c>m2-cooling-loop</c>'s controlled leg reaches <c>N3</c> and stops -- has no way back to the valve's
    /// own loop at all, which is the strongest possible statement that it is not the bypass.
    /// </remarks>
    public static int Distance(
        CircuitGraph graph, IFlowComponent from, IFlowComponent target, IFlowComponent barred)
    {
        if (ReferenceEquals(from, target))
        {
            return 0;
        }

        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance) { barred, from };
        var frontier = new Queue<(IFlowComponent Element, int Steps)>();

        frontier.Enqueue((from, 0));

        while (frontier.Count > 0)
        {
            var (element, steps) = frontier.Dequeue();

            foreach (var branch in graph.Branches)
            {
                IFlowComponent? next = ReferenceEquals(branch.From.Element, element) ? branch.To.Element
                    : ReferenceEquals(branch.To.Element, element) ? branch.From.Element
                    : null;

                if (next is null || !seen.Add(next))
                {
                    continue;
                }

                var crossed = steps + branch.Path.Length + 1;

                if (ReferenceEquals(next, target))
                {
                    return crossed;
                }

                frontier.Enqueue((next, crossed));
            }
        }

        return int.MaxValue;
    }
}
