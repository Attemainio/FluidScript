using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Topology.Counting;

public static partial class WellPosedness
{
    /// <summary>Says which controllers' setpoints the design solve does not hold, and why (<c>D-141</c>).</summary>
    /// <remarks>
    /// Lowering decided; this reads the decision off the graph and names it. Two reasons, two codes: the
    /// measurement is one the solve could hold but the actuator or the node was stated (<c>FS3210</c>),
    /// or it is not a plain node's temperature at all (<c>FS3211</c>).
    /// </remarks>
    private static void ReportSetpoints(CircuitGraph graph, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var setpoint in graph.Setpoints)
        {
            if (setpoint.Applied)
            {
                continue;
            }

            var measurement = $"{setpoint.Measured}.{setpoint.Parameter}";

            if (setpoint.Reason is { } reason)
            {
                diagnostics.Add(Diagnostic.Create(
                    ControllerDiagnostics.StartsOffSetpoint,
                    null,
                    new DiagnosticArgument("controller", setpoint.Controller),
                    new DiagnosticArgument("reason", reason),
                    new DiagnosticArgument("measurement", measurement),
                    new DiagnosticArgument(
                        "setpoint",
                        string.Equals(setpoint.Parameter, HydraulicPartition.Temperature, StringComparison.Ordinal)
                            ? (setpoint.Value.SiValue - 273.15).ToString("0.##", CultureInfo.InvariantCulture) + " °C"
                            : setpoint.Value.ToString())));

                continue;
            }

            diagnostics.Add(Diagnostic.Create(
                ControllerDiagnostics.MeasurementNotHeld,
                null,
                new DiagnosticArgument("controller", setpoint.Controller),
                new DiagnosticArgument("measurement", measurement)));
        }
    }

    /// <summary>Refuses each run event whose target a controller already drives (<c>FS3109</c>).</summary>
    /// <remarks>
    /// Applied or not: a setpoint that the design solve could not hold still owns its actuator once the
    /// run starts (<c>D-140</c>), so the schedule is refused either way.
    /// </remarks>
    private static void ReportScheduledActuators(CircuitGraph graph, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        // A parameter following a curve of time is moved by the run exactly as a scheduled one is
        // (`D-149`), so the same two refusals apply to it.
        var moved = graph.Schedule
            .Select(static change => (change.Component, change.Parameter))
            .Concat(graph.Clock?.Drives.Select(static drive => (drive.Component, drive.Parameter)) ?? []);

        foreach (var change in moved)
        {
            var target = graph.Components.FirstOrDefault(component =>
                string.Equals(component.Name, change.Component, StringComparison.Ordinal));

            if (target is not null && !target.Resolvable.Any(parameter => string.Equals(parameter.Name, change.Parameter, StringComparison.Ordinal)))
            {
                // A parameter the run has no slot for (FS3105): the schedule would be silently ignored.
                var movable = string.Join(", ", target.Resolvable.Select(parameter => $"'{parameter.Name}'"));

                diagnostics.Add(Diagnostic.Create(
                    TransientDiagnostics.NotSchedulable,
                    null,
                    new DiagnosticArgument("target", $"{change.Component}.{change.Parameter}"),
                    new DiagnosticArgument(
                        "reason",
                        movable.Length == 0
                            ? $"a {target.Kind} has no parameter a run can move"
                            : $"a run can move {movable} on a {target.Kind}, not '{change.Parameter}'")));
                continue;
            }

            var owner = graph.Setpoints.FirstOrDefault(setpoint =>
                string.Equals(setpoint.ActuatorComponent, change.Component, StringComparison.Ordinal)
                && string.Equals(setpoint.ActuatorParameter, change.Parameter, StringComparison.Ordinal));

            if (owner is null)
            {
                continue;
            }

            diagnostics.Add(Diagnostic.Create(
                TransientDiagnostics.ScheduledActuator,
                null,
                new DiagnosticArgument("target", $"{change.Component}.{change.Parameter}"),
                new DiagnosticArgument("controller", owner.Controller)));
        }
    }

    /// <summary>Names each flow constraint whose pump sits on no branch its owner does.</summary>
    /// <remarks>
    /// The locality <c>Candidates</c> prefers, checked after the fact: a local pump is offered first, so a
    /// promotion that landed elsewhere means none was free. Whether that is a shared upstream pump doing
    /// its job or a sibling's pump doing the wrong one is not decidable here (<c>S-45</c>), so it is said
    /// rather than judged.
    /// </remarks>
    private static void ReportReaches(
        CircuitGraph graph,
        ImmutableArray<Promotion> promotions,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var promotion in promotions)
        {
            if (promotion.Constraint.Kind is not ConstraintKind.FixedFlow
                || !string.Equals(promotion.Parameter, "head", StringComparison.Ordinal))
            {
                continue;
            }

            var owner = graph.Components.FirstOrDefault(
                element => string.Equals(element.Name, promotion.Constraint.Component, StringComparison.Ordinal));
            var pump = graph.Components.FirstOrDefault(
                element => string.Equals(element.Name, promotion.Component, StringComparison.Ordinal));

            if (owner is null || pump is null)
            {
                continue;
            }

            var shared = graph.Branches.Any(branch =>
                branch.Path.Contains(owner) && branch.Path.Contains(pump));

            if (!shared)
            {
                diagnostics.Add(Diagnostic.Create(
                    TopologyDiagnostics.ConstraintReachesAcross,
                    span: null,
                    new DiagnosticArgument("constraint", promotion.Constraint.Label),
                    new DiagnosticArgument("pump", pump.Name)));
            }
        }
    }

    // ---- the reports -------------------------------------------------------------------------------

    /// <summary>Reports every datum the graph had to pick for itself.</summary>
    private static void ReportDatums(
        CircuitGraph graph, ImmutableArray<HydraulicComponent> hydraulics, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var hydraulic in hydraulics)
        {
            if (!hydraulic.DatumWasStated && hydraulic.Datum.Length > 0)
            {
                var node = graph.Nodes.FirstOrDefault(node => string.Equals(node.Name, hydraulic.Datum, StringComparison.Ordinal));
                var named = node is not null && !Written(node) && Spoken(graph, node, "p") is { } spoken
                    ? $"the node at {spoken}"
                    : $"'{hydraulic.Datum}'";

                diagnostics.Add(Diagnostic.Create(
                    TopologyDiagnostics.DatumChosen, span: null, new DiagnosticArgument("node", named))
                    with
                { ComponentName = hydraulic.Datum });
            }
        }
    }

    /// <summary>A node as a script can name it: <c>N1</c> when it was written, else a port beside it that takes the quantity.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="node">The node.</param>
    /// <param name="quantity">The port quantity the advice will be stated with: <c>p</c>, which every port takes, or <c>t</c>, which only some do.</param>
    /// <returns><c>N1</c>, a port as <c>HE1.secondary.in</c>, or <see langword="null"/> when no port beside the node takes the quantity.</returns>
    /// <remarks>
    /// <para>
    /// A node the binder inserted has a name nobody wrote -- <c>PU1__HE1</c> between two components, <c>TT1_node</c> under a
    /// chain sensor -- and advice that names it cannot be followed (<c>L-66</c>). What the user can write is the port it stands
    /// for: <c>PU1 pump out.p = 300</c> pins that node (<c>D-124</c>), and <c>HE1 heat_exchanger out.t = 40</c> fixes its
    /// temperature. A pump, a pipe or a valve port takes no <c>t</c>, so a temperature is offered on a port that does.
    /// </para>
    /// <para>
    /// A peer whose own name carries the binder's <c>__</c> stem is an inserted pipe (<c>I7</c>), and is offered last; a
    /// written pipe's end comes after the equipment's port, so a picked datum reads as the pump suction <c>D-98</c> chose
    /// (<c>SP.in</c>), not the pipe that feeds it.
    /// </para>
    /// </remarks>
    private static string? Spoken(CircuitGraph graph, GraphNode node, string quantity)
    {
        if (Written(node))
        {
            return node.Name;
        }

        var index = graph.Components.IndexOf(node.Component);
        var peers = Enumerable.Range(0, graph.Adjacency.PortCount(index))
            .Select(port => graph.Adjacency.Peer(index, port))
            .Where(peer => peer != PortRef.None && graph.Components[peer.Component] is not NodeComponent)
            .OrderBy(peer => graph.Components[peer.Component].Name.Contains("__", StringComparison.Ordinal))
            .ThenBy(peer => graph.Components[peer.Component] is PipeComponent);

        foreach (var peer in peers)
        {
            var element = graph.Components[peer.Component];
            var kind = ComponentRegistry.Default.ByKeyword(element.Kind);
            var key = element.Ports[peer.Port].Name;
            var port = kind?.PortName(key) ?? key;

            if (quantity == "p" || kind?.ResolveParameter($"{port}.{quantity}", out _) is not null)
            {
                return $"{element.Name}.{port}";
            }
        }

        return null;
    }

    /// <summary>Whether a node's name is one the script wrote.</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> for a declared node and for one a connection named (<c>I1</c>).</returns>
    /// <remarks>
    /// <see cref="NodeOrigin.Inferred"/> is not the answer: <c>N1 - PU1 - N1</c> declares no node and still names one, which
    /// <c>I1</c> infers under the user's own name. What the binder generated carries its stem -- <c>__</c> joining the two
    /// things it sits between (<c>I2</c>, <c>I3</c>, <c>I7</c>, <c>I9</c>), and <c>_node</c> under a chain sensor, which the
    /// reader names before <c>I1</c> sees it. A user who writes such a name gets advice in ports instead, which is still
    /// advice a line can follow.
    /// </remarks>
    private static bool Written(GraphNode node) =>
        node.Origin == NodeOrigin.Declared
        || (node.Origin == NodeOrigin.Inferred
            && !node.Name.Contains("__", StringComparison.Ordinal)
            && !node.Name.TrimEnd("0123456789".ToCharArray()).EndsWith("_node", StringComparison.Ordinal));

    /// <summary>The components beside an inserted node, for advice no port of theirs can take.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="node">The node.</param>
    /// <returns>Their names, in port order.</returns>
    private static IEnumerable<string> Beside(CircuitGraph graph, GraphNode node)
    {
        var index = graph.Components.IndexOf(node.Component);

        return Enumerable.Range(0, graph.Adjacency.PortCount(index))
            .Select(port => graph.Adjacency.Peer(index, port))
            .Where(peer => peer != PortRef.None && graph.Components[peer.Component] is not NodeComponent)
            .Select(peer => graph.Components[peer.Component].Name)
            .Distinct(StringComparer.Ordinal);
    }

    /// <summary>Reports any part of the model nothing couples to the rest.</summary>
    /// <remarks>
    /// Coupling is by shared element, not by shared flow. Two hydraulic components joined by a coupled
    /// exchanger share that exchanger and are not isolated — which is the substation, and the whole
    /// reason <c>D-17</c> closed the earlier reading of this check.
    /// </remarks>
    private static void ReportIsolation(
        ImmutableArray<HydraulicComponent> hydraulics, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (hydraulics.Length < 2)
        {
            return;
        }

        var piece = new int[hydraulics.Length];

        for (var i = 0; i < hydraulics.Length; i++)
        {
            piece[i] = i;
        }

        for (var i = 0; i < hydraulics.Length; i++)
        {
            for (var j = i + 1; j < hydraulics.Length; j++)
            {
                if (hydraulics[i].Elements.Any(hydraulics[j].Elements.Contains))
                {
                    piece[j] = Math.Min(piece[i], piece[j]);
                }
            }
        }

        var first = new HashSet<int>();

        for (var i = 0; i < hydraulics.Length; i++)
        {
            if (first.Add(piece[i]) && first.Count == 1)
            {
                continue;
            }

            if (first.Count > 1 && piece[i] == i)
            {
                diagnostics.Add(Diagnostic.Create(
                    TopologyDiagnostics.IsolatedSubgraph,
                    span: null,
                    new DiagnosticArgument("list", string.Join(", ", hydraulics[i].Elements.Select(static e => e.Name)))));
            }
        }
    }

    /// <summary>Reports two stated pressures an ideal link forces to be equal.</summary>
    /// <remarks>
    /// <para>
    /// Two stated pressures are ordinary and must not be reported: the cooling loop's <c>N1 p=300</c>
    /// and <c>N3 p=280</c> are what drive its primary. The degenerate case is two of them with nothing
    /// between them that could develop a pressure difference, where the second is not a boundary
    /// condition at all but a second datum on the same equipotential.
    /// </para>
    /// <para>
    /// The equipotentials are labelled once, by one walk over every branch, and each pair is then a
    /// label comparison. A flood fill per pair was quadratic in the stated pressures and quadratic
    /// again in the graph, for a question the pair count never changes the answer to.
    /// </para>
    /// </remarks>
    private static void ReportCompetingDatums(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var equipotential = Equipotentials(graph);

        foreach (var hydraulic in hydraulics)
        {
            for (var i = 0; i < hydraulic.StatedPressures.Length; i++)
            {
                for (var j = i + 1; j < hydraulic.StatedPressures.Length; j++)
                {
                    var a = hydraulic.StatedPressures[i];
                    var b = hydraulic.StatedPressures[j];

                    if (!equipotential.TryGetValue(a.Component, out var first)
                        || !equipotential.TryGetValue(b.Component, out var second)
                        || first != second)
                    {
                        continue;
                    }

                    diagnostics.Add(Diagnostic.Create(
                        TopologyDiagnostics.CompetingDatums, span: null, new DiagnosticArgument("a", a.Name), new DiagnosticArgument("b", b.Name))
                        with
                    { ComponentName = b.Name });
                }
            }
        }
    }

    /// <summary>Labels every node by the set of nodes ideal links alone join it to.</summary>
    /// <param name="graph">The graph.</param>
    /// <returns>Node to label; two nodes share a label when nothing between them could make their pressures differ.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Adjacency inside a branch, not whole branches.</strong> <c>N1 - N2</c> in the middle of a
    /// longer run is still an ideal link between two separate pressure unknowns, and a check that only
    /// compared branch <em>ends</em> would miss every one that is not a branch of its own -- which is
    /// most of them, since a degree-two node is interior by construction.
    /// </para>
    /// <para>
    /// Only a node-to-node adjacency is an ideal link (<c>D-25</c>). Anything else along a branch is a
    /// component that can develop a pressure difference, however small its stated resistance, and two
    /// pressures either side of one are ordinary boundary conditions rather than competing datums.
    /// </para>
    /// </remarks>
    private static Dictionary<IFlowComponent, int> Equipotentials(CircuitGraph graph)
    {
        var neighbours = new Dictionary<IFlowComponent, List<IFlowComponent>>();

        foreach (var branch in graph.Branches)
        {
            var previous = branch.From.Element;

            foreach (var part in branch.Path)
            {
                Link(previous, part);
                previous = part;
            }

            Link(previous, branch.To.Element);
        }

        var label = new Dictionary<IFlowComponent, int>();
        var queue = new Queue<IFlowComponent>();
        var next = 0;

        foreach (var start in neighbours.Keys)
        {
            if (!label.TryAdd(start, next))
            {
                continue;
            }

            queue.Enqueue(start);

            while (queue.TryDequeue(out var node))
            {
                foreach (var neighbour in neighbours[node])
                {
                    if (label.TryAdd(neighbour, next))
                    {
                        queue.Enqueue(neighbour);
                    }
                }
            }

            next++;
        }

        return label;

        void Link(IFlowComponent left, IFlowComponent right)
        {
            if (left is not NodeComponent || right is not NodeComponent)
            {
                return;
            }

            Neighbours(left).Add(right);
            Neighbours(right).Add(left);
        }

        List<IFlowComponent> Neighbours(IFlowComponent node)
        {
            if (!neighbours.TryGetValue(node, out var list))
            {
                neighbours[node] = list = [];
            }

            return list;
        }
    }

    /// <summary>Reports every loop nothing can drive flow around.</summary>
    /// <remarks>
    /// <para>
    /// Read from <c>ComponentKindInfo.DrivesFlow</c>, which is explicit registry metadata. Inspecting
    /// residual code or guessing from parameter names is forbidden (<c>D-30</c>): a rule that infers
    /// structure from an implementation detail changes meaning when the implementation does.
    /// </para>
    /// <para>
    /// <strong>Driven is asked of the loop's block, not of the loop</strong> (<c>S-55</c>). A fundamental
    /// cycle with no pump on it still carries flow when a pump elsewhere in the same biconnected block
    /// pushes through it -- the pump-free mixing header's source valve and exchanger are that cycle, driven
    /// by the consumer pumps -- and a path between two boundaries is driven by them. <see cref="HydraulicBlocks"/>
    /// holds both facts; a loop is reported only when nothing in its block moves anything.
    /// </para>
    /// </remarks>
    private static void ReportDriverlessLoops(
        CircuitGraph graph, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var blocks = HydraulicBlocks.ForDrivers(graph);

        foreach (var loop in graph.Loops)
        {
            // A cycle lies inside one block, so its first branch answers for it.
            var driven = loop.Branches.Length > 0 && blocks.Drives(loop.Branches[0]);

            if (!driven)
            {
                diagnostics.Add(Diagnostic.Create(
                    TopologyDiagnostics.LoopWithoutDriver, span: null, new DiagnosticArgument("loop", loop.Label)));
            }
        }
    }
}
