using FluidScript.Core.Components;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Solvers;

/// <summary>Tells a three-way valve's bypass leg from the leg it exchanges flow with the plant through.</summary>
/// <remarks>
/// <para>
/// <strong>A port name the script <em>wrote</em> settles it, because the residuals have already settled
/// it</strong> (<c>D-85</c>, <c>D-88</c>). <c>ab</c> is the common port, <c>a</c> the controlled one and
/// <c>b</c> the bypass, which is how valve bodies are labelled and what
/// <see cref="Components.ThreeWayValve"/> assumes: it gives <c>a</c> the opening <c>position</c> and <c>b</c>
/// the complement. A sizing rule that named the legs some other way would size a coefficient against one
/// leg's flow and hand it to the equation governing the other.
/// </para>
/// <para>
/// <strong>An <em>inferred</em> port name is not that, and believing it is a wrong Kv rather than a wrong
/// label.</strong> Positional binding hands out <c>a</c> and <c>b</c> in connection order; measured on
/// <c>m2-cooling-loop</c>, whose valve is wired without ports, the inferred <c>a</c> lands on the
/// recirculation leg and <c>b</c> on the control leg -- exactly backwards. Sizing the recirculation leg
/// measures authority against a branch with almost no resistance behind it, which asks for a large Kv and
/// yields a valve with no authority over the path it controls. <see cref="Topology.CircuitGraph.StatedPorts"/>
/// is what separates the two, and it is consulted before any letter is read.
/// </para>
/// <para>
/// <strong>Where the ports were not named the shape is asked instead, and the bypass is the leg that closes
/// the valve's own loop</strong>, which is what a bypass is: the leg whose far end gets back to the common
/// leg's far end in the fewest components without passing through the valve. That comparison is this
/// project's reasoning rather than an inherited convention. The case it reads backwards is a short tap off a
/// header feeding a long secondary; the case it cannot read at all is an injection circuit whose two switched
/// legs land on the same header equidistantly, and naming the ports is now the answer to both.
/// </para>
/// <para>
/// <strong>Both replaced asking whether a leg's immediate neighbour states a pressure</strong> (<c>C-66</c>).
/// That was a proxy for the same idea, and it coincides with it only when the plant boundary happens to be
/// adjacent: on <c>m2-cooling-loop</c> the controlled leg runs straight into <c>N3 return p=280</c>, so it
/// worked. A header states its pressure at the plant, three or more hops from any valve leg, so
/// <em>every</em> three-way valve on one declined and kept the bootstrap Kv 630 -- a Kv law demanding about
/// 78 kg/s on a plant that moves 0.93, whose residual buries every other equation in the system.
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
    /// <strong>An inferred <c>ab</c> is believed here, unlike the inferred <c>a</c>
    /// <see cref="Variable"/> refuses</strong> (<c>D-88</c>), and the asymmetry is in the port order rather
    /// than in the confidence. <c>ab</c> is the valve's <em>first</em> port, so positional binding gives it to
    /// the first connection written -- which for a mixing valve is the outlet and for a diverting one the
    /// inlet, the common leg either way. <c>a</c> and <c>b</c> are ports 1 and 2, handed out by the order of
    /// the two remaining connections, which says nothing about which leg recirculates.
    /// </para>
    /// <para>
    /// The flow test remains the fallback for a valve whose common leg is neither named nor first, where mass
    /// balance is all there is. It is right at a converged iterate and only there, which is why it cannot be
    /// the primary test.
    /// </para>
    /// </remarks>
    public static int Common(Branch[] legs, double[] flows, IFlowComponent valve)
    {
        for (var leg = 0; leg < legs.Length; leg++)
        {
            if (string.Equals(PortName(legs[leg], valve), "ab", StringComparison.Ordinal))
            {
                return leg;
            }
        }

        return Array.IndexOf(flows, flows.Max());
    }

    /// <summary>The name of the port a leg attaches to on the valve itself.</summary>
    /// <param name="leg">A branch with the valve at one end.</param>
    /// <param name="valve">The valve, so its own end can be told from the far one.</param>
    /// <returns>The port name, or <see langword="null"/> when the element at that end is a node.</returns>
    public static string? PortName(Branch leg, IFlowComponent valve) =>
        ReferenceEquals(leg.From.Element, valve) ? leg.From.PortName : leg.To.PortName;

    /// <summary>Whether the script wrote the port a leg attaches to, rather than the binder choosing it.</summary>
    /// <param name="graph">The lowered circuit, which carries the set of ports the script named.</param>
    /// <param name="leg">A branch with the valve at one end.</param>
    /// <param name="valve">The valve.</param>
    /// <returns><see langword="true"/> only for a port the user typed (<c>D-88</c>).</returns>
    public static bool Stated(CircuitGraph graph, Branch leg, IFlowComponent valve)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(valve);

        return PortName(leg, valve) is { } port && graph.StatedPorts.Contains($"{valve.Name}.{port}");
    }

    /// <summary>Which of the two switched legs the valve modulates the plant flow through.</summary>
    /// <param name="graph">The lowered circuit: the ports its script named, and the walk that runs otherwise.</param>
    /// <param name="legs">The valve's three legs.</param>
    /// <param name="common">The index of the leg carrying what the other two split.</param>
    /// <param name="valve">The valve itself, which no walk may pass back through.</param>
    /// <returns>The index of the variable leg, or -1 when the two cannot be told apart.</returns>
    /// <remarks>
    /// <para>
    /// <strong>A written <c>a</c> ends it</strong> (<c>D-88</c>): that is the A-AB control path every valve
    /// body is labelled for, and the one <see cref="Components.ThreeWayValve"/> gives the opening
    /// <c>position</c> to. A written <c>b</c> ends it the other way, which is not the same test -- a script
    /// may name one switched port and leave the other to positional binding, and either word is enough.
    /// </para>
    /// <para>
    /// <strong>An unwritten letter is not consulted at all</strong>, because it is connection order wearing a
    /// port name. The walk answers there, and returns -1 for the shape it cannot read: two switched legs the
    /// same distance from where the common leg lands, which is what a boiler injection circuit is.
    /// </para>
    /// </remarks>
    public static int Variable(CircuitGraph graph, Branch[] legs, int common, IFlowComponent valve)
    {
        ArgumentNullException.ThrowIfNull(legs);

        var anchor = Far(legs[common], valve);
        var first = -1;
        var second = -1;

        for (var leg = 0; leg < legs.Length; leg++)
        {
            if (leg == common)
            {
                continue;
            }

            if (Stated(graph, legs[leg], valve))
            {
                var port = PortName(legs[leg], valve);

                if (string.Equals(port, "a", StringComparison.Ordinal))
                {
                    return leg;
                }

                if (string.Equals(port, "b", StringComparison.Ordinal))
                {
                    // The bypass, named. The variable leg is then the other switched one, whether or not
                    // anything named *it*.
                    return Enumerable.Range(0, legs.Length).FirstOrDefault(
                        other => other != common && other != leg, -1);
                }
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
