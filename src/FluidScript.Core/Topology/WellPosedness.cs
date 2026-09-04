using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Units;

namespace FluidScript.Core.Topology;

/// <summary>What the well-posedness pass found.</summary>
/// <param name="Counting">The counting argument, term by term.</param>
/// <param name="Hydraulics">The hydraulic connected components, each with its datum.</param>
/// <param name="Diagnostics">Everything worth telling the user, in a stable order.</param>
public sealed record WellPosednessResult(
    CountingTable Counting,
    ImmutableArray<HydraulicComponent> Hydraulics,
    ImmutableArray<Diagnostic> Diagnostics)
{
    /// <summary>Gets whether the circuit can be handed to the solver.</summary>
    /// <value>
    /// <see langword="true"/> when the system is square and nothing was reported as an error. A warning
    /// does not block a solve: a loop with no driver still has an answer, and the answer is zero flow.
    /// </value>
    public bool CanSolve =>
        Counting.Excess == 0
        && !Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}

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
public static class WellPosedness
{
    /// <summary>A heat exchanger's stated inlet temperatures, in the order they are matched.</summary>
    private static readonly string[] Inlets = ["in", "in2"];

    /// <summary>A heat exchanger's statements that pin a flow, in the order they are matched.</summary>
    private static readonly string[] FlowPins = ["out", "out2", "dt", "dt2"];

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
        ReportOwnership(graph, hydraulics, diagnostics);
        ReportClosure(graph, hydraulics, diagnostics);
        ReportBoundaries(graph, hydraulics, diagnostics);

        var constraints = Constraints(graph, hydraulics);
        var promotions = Promote(graph, hydraulics, constraints);
        var counting = Count(graph, hydraulics, constraints, promotions);

        ReportBalance(graph, hydraulics, counting, promotions, diagnostics);

        return new WellPosednessResult(counting, hydraulics, diagnostics.ToImmutable());
    }

    // ---- the counting argument ---------------------------------------------------------------------

    /// <summary>Assembles the counting table from the graph and what it found.</summary>
    private static CountingTable Count(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ImmutableArray<ComponentConstraint> constraints,
        ImmutableArray<Promotion> promotions)
    {
        var balances = 0;

        foreach (var vertex in graph.JunctionElements)
        {
            if (vertex is not CircuitNode node || node.CarriesMassBalance)
            {
                balances++;
            }
        }

        var datums = ImmutableArray.CreateBuilder<HydraulicComponent>();
        var levels = ImmutableArray.CreateBuilder<HydraulicComponent>();

        foreach (var hydraulic in hydraulics)
        {
            if (NeedsEnthalpyLevel(graph, hydraulics, hydraulic))
            {
                levels.Add(hydraulic);
            }

            // The datum equation and the redundant mass balance answer two different questions, and
            // conflating them is wrong on a circuit whose only stated pressure sits mid-branch. The
            // datum fixes the pressure level, and is needed exactly when no pressure is stated.
            if (!hydraulic.DatumWasStated)
            {
                datums.Add(hydraulic);
            }

            // The redundancy is about mass, not pressure: with every external flux known, summing the
            // balances gives an identity and one of them is implied by the rest. A pressure stated on a
            // node that carries no mass balance admits no flux, so it leaves the component closed in
            // exactly this sense however emphatically it was written.
            if (!hydraulic.HasUnknownFlux && Balances(hydraulic) > 0)
            {
                balances--;
            }
        }

        var fluxes = ImmutableArray.CreateBuilder<GraphNode>();
        var pressures = ImmutableArray.CreateBuilder<GraphNode>();

        foreach (var node in graph.Nodes)
        {
            var stated = HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure) is not null;

            if (stated)
            {
                pressures.Add(node);
            }

            // A node interior to a branch carries no mass balance, so there is nowhere for an external
            // flux to enter. A stated pressure there is an equation with no unknown to absorb it, which
            // is exactly the over-specification a mid-branch `p=` really is.
            //
            // A `return` brings the same unknown with no equation of its own, and that is the point of
            // it: the flow leaving is whatever the circuit delivers. A bare terminal node brings
            // neither -- its flux is zero, which is a dead leg (`D-64`).
            //
            // A stated `flow` names the flux outright, so there is no unknown left to declare. It is
            // not an equation either: counting it as one and keeping the unknown gives the same total
            // and a table that reads as though the circuit had to work to meet it.
            if (node.Component.CarriesMassBalance
                && HydraulicPartition.Stated(node.Component, HydraulicPartition.Flow) is null
                && (stated || node.Component.Boundary is BoundaryRole.Return))
            {
                fluxes.Add(node);
            }
        }

        var relations = Relations(graph, out var links);
        var owned = ImmutableArray.CreateBuilder<UnknownDeclaration>();
        var volumes = 0;

        // A control volume's own state, which only the component can name (`D-74`). Both terms are read
        // from the component rather than derived, because it is the sole authority on what state it
        // carries -- there is no independent count of a tank's mixed enthalpy to check this against.
        foreach (var element in graph.Components)
        {
            if (element is CircuitNode)
            {
                continue;
            }

            owned.AddRange(element.DeclareUnknowns());

            foreach (var equation in element.DeclareEquations())
            {
                if (equation.Kind is EquationKind.Energy)
                {
                    volumes++;
                }
            }
        }

        return new CountingTable
        {
            BranchFlows = graph.Branches.Length,
            NodePressures = graph.Nodes.Length,
            NodeEnthalpies = graph.Nodes.Length,
            ComponentUnknowns = owned.ToImmutable(),
            FluxNodes = fluxes.ToImmutable(),
            Promotions = promotions,
            PressureRelations = relations,
            IdealLinks = links,
            MassBalances = balances,
            EnergyBalances = graph.Nodes.Length,
            ControlVolumeBalances = volumes,
            PressureNodes = pressures.ToImmutable(),
            Constraints = constraints,
            DatumComponents = datums.ToImmutable(),
            LevelComponents = levels.ToImmutable(),
        };
    }

    /// <summary>The pressure relations the components impose between node pressures.</summary>
    /// <param name="graph">The graph.</param>
    /// <returns>The count.</returns>
    /// <remarks>
    /// Three sources, and the third is the one that gets forgotten. Each flow component a branch crosses
    /// imposes one; a junction element that is not a node imposes one fewer than the branch ends it
    /// carries, which is invariant 8's <c>K − 1</c> for a tank and <c>23</c>'s two rows for a three-way
    /// valve; and each bare node-to-node adjacency imposes one, because <c>D-25</c> makes it an ideal
    /// zero-drop link between two <em>separate</em> pressure unknowns.
    /// </remarks>
    /// <param name="links">Receives the bare node-to-node adjacencies, in the order the walk meets them.</param>
    private static int Relations(CircuitGraph graph, out ImmutableArray<IdealLink> links)
    {
        // Keyed by object with a reference comparer: a branch path carries the CircuitNode, and an
        // assembler writing the link's row needs the GraphNode wrapped around it to reach the unknowns.
        var nodes = new Dictionary<object, GraphNode>(graph.Nodes.Length, ReferenceEqualityComparer.Instance);

        foreach (var node in graph.Nodes)
        {
            nodes[node.Component] = node;
        }

        var ideal = ImmutableArray.CreateBuilder<IdealLink>();
        var relations = 0;

        foreach (var branch in graph.Branches)
        {
            IFlowComponent previous = branch.From.Element;

            foreach (var part in branch.Path)
            {
                if (part is CircuitNode)
                {
                    if (previous is CircuitNode)
                    {
                        relations++;
                        Link(nodes, ideal, previous, part);
                    }
                }
                else
                {
                    relations++;
                }

                previous = part;
            }

            if (previous is CircuitNode && branch.To.Element is CircuitNode)
            {
                relations++;
                Link(nodes, ideal, previous, branch.To.Element);
            }
        }

        foreach (var vertex in graph.JunctionElements)
        {
            if (vertex is CircuitNode)
            {
                continue;
            }

            var ends = 0;

            foreach (var branch in graph.Branches)
            {
                if (ReferenceEquals(branch.From.Element, vertex))
                {
                    ends++;
                }

                if (ReferenceEquals(branch.To.Element, vertex))
                {
                    ends++;
                }
            }

            if (ends > 1)
            {
                relations += ends - 1;
            }
        }

        links = ideal.ToImmutable();

        return relations;
    }

    /// <summary>Records one bare node-to-node adjacency as an ideal zero-drop link.</summary>
    /// <param name="nodes">The graph's nodes, by the component carrying their unknowns.</param>
    /// <param name="ideal">The links collected so far.</param>
    /// <param name="from">The node the walk arrives from.</param>
    /// <param name="to">The node it continues to.</param>
    /// <remarks>
    /// A node the graph does not list would be a lowering defect rather than a user error, and the link
    /// simply goes unnamed: the assembler's row total then disagrees with
    /// <see cref="CountingTable.Equations"/>, which is a loud failure in the one place built to notice it.
    /// Throwing here would put that failure in the pass that has to survive a malformed script.
    /// </remarks>
    private static void Link(
        Dictionary<object, GraphNode> nodes,
        ImmutableArray<IdealLink>.Builder ideal,
        IFlowComponent from,
        IFlowComponent to)
    {
        if (nodes.TryGetValue(from, out var left) && nodes.TryGetValue(to, out var right))
        {
            ideal.Add(new IdealLink(left, right));
        }
    }

    /// <summary>How many mass balances one hydraulic component contributes.</summary>
    private static int Balances(HydraulicComponent hydraulic)
    {
        var balances = 0;

        foreach (var element in hydraulic.Elements)
        {
            if (!CircuitGraph.IsJunctionElement(element))
            {
                continue;
            }

            if (element is not CircuitNode node || node.CarriesMassBalance)
            {
                balances++;
            }
        }

        return balances;
    }

    // ---- constraints and promotion -----------------------------------------------------------------

    /// <summary>Every stated parameter the circuit must satisfy rather than merely read.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="hydraulics">Its hydraulic partition.</param>
    /// <returns>The constraints, in graph order then parameter order.</returns>
    /// <remarks>
    /// <para>
    /// <strong>A stated value is a constraint only when the circuit has to work to meet it.</strong>
    /// <c>HE1 power=30</c> is a coefficient in an energy balance that already exists; <c>HE1 out=50</c>
    /// is a demand on a node enthalpy the balance also determines, and the two together fix a flow the
    /// hydraulics determine as well. That collision is the whole subject of this pass.
    /// </para>
    /// <para>
    /// <strong>A temperature on a boundary node is not a constraint.</strong> <c>N1 t=6 p=300</c> states
    /// what enters the model there; there is no upstream enthalpy for it to contradict. The same
    /// <c>t</c> on a node with no external mass is a demand on a temperature the circuit computes, and
    /// something has to move to meet it.
    /// </para>
    /// <para>
    /// <strong>Nor are a coupled exchanger's terminal temperatures.</strong> Once both sides are wired,
    /// <c>in</c>, <c>out</c>, <c>in2</c> and <c>out2</c> are the <em>rating design point</em> that
    /// <c>24</c> sizes UA from (<c>D-19</c>), not demands on the solved state. Counting them as
    /// constraints reports the substation over-specified by three, on the reference circuit written to
    /// demonstrate that two circuits can be solved together.
    /// </para>
    /// </remarks>
    private static ImmutableArray<ComponentConstraint> Constraints(
        CircuitGraph graph, ImmutableArray<HydraulicComponent> hydraulics)
    {
        var constraints = ImmutableArray.CreateBuilder<ComponentConstraint>();

        foreach (var element in graph.Components)
        {
            var hydraulic = Owner(hydraulics, element);

            if (element is CircuitNode)
            {
                if (HydraulicPartition.Stated(element, HydraulicPartition.Temperature) is not null
                    && HydraulicPartition.Stated(element, HydraulicPartition.Pressure) is null
                    && HydraulicPartition.Stated(element, HydraulicPartition.Flow) is null)
                {
                    constraints.Add(new ComponentConstraint(
                        element.Name,
                        HydraulicPartition.Temperature,
                        ConstraintKind.NodeTemperature,
                        hydraulic));
                }

                continue;
            }

            if (!string.Equals(element.Kind, "heat_exchanger", StringComparison.Ordinal)
                || IsCoupled(hydraulics, element))
            {
                continue;
            }

            foreach (var parameter in Inlets)
            {
                if (HydraulicPartition.Stated(element, parameter) is not null)
                {
                    constraints.Add(new ComponentConstraint(
                        element.Name, parameter, ConstraintKind.MixedInlet, hydraulic));
                }
            }

            foreach (var parameter in FlowPins)
            {
                if (HydraulicPartition.Stated(element, parameter) is not null)
                {
                    constraints.Add(new ComponentConstraint(
                        element.Name, parameter, ConstraintKind.FixedFlow, hydraulic));
                }
            }
        }

        return constraints.ToImmutable();
    }

    /// <summary>Whether both of a component's sides carry flow.</summary>
    /// <param name="hydraulics">The hydraulic partition.</param>
    /// <param name="element">The component to classify.</param>
    /// <returns><see langword="true"/> when it belongs to more than one hydraulic component.</returns>
    /// <remarks>
    /// <strong>Read from the partition rather than from a mode field</strong>, because the mode is
    /// computed from what the script connected and there is no <c>mode=</c> parameter to read
    /// (<c>D-19</c>). A component in two hydraulic components is one whose second flow group is wired,
    /// which is exactly the condition <c>Coupled</c> names.
    /// </remarks>
    private static bool IsCoupled(
        ImmutableArray<HydraulicComponent> hydraulics, IFlowComponent element)
    {
        var sides = 0;

        foreach (var hydraulic in hydraulics)
        {
            if (hydraulic.Elements.Contains(element))
            {
                sides++;
            }
        }

        return sides > 1;
    }

    /// <summary>Whether a component's energy block leaves its own temperature level free.</summary>
    /// <param name="graph">The graph, which is what says whether time is being integrated.</param>
    /// <param name="hydraulics">The hydraulic partition.</param>
    /// <param name="hydraulic">The component to classify.</param>
    /// <returns><see langword="true"/> when the level is an unknown nothing in the block determines.</returns>
    /// <remarks>
    /// <para>
    /// Three conditions, and each one removes the freedom for a different reason. <strong>Closed</strong>:
    /// external mass arrives carrying an enthalpy, and that enthalpy is the level. <strong>Steady</strong>:
    /// a transient starts from an initial state, which fixes the level before the first step.
    /// <strong>Uncoupled</strong>: a two-sided exchanger's duty reads absolute temperatures on both
    /// sides, so a uniform offset on one side alone no longer satisfies its relation.
    /// </para>
    /// <para>
    /// <strong>Whether the circuit's duties balance does not enter into it.</strong> An unbalanced closed
    /// loop has the same rank deficiency and the same square count — and no solution, which is
    /// <c>FS2203</c>'s subject and not this one's.
    /// </para>
    /// </remarks>
    private static bool NeedsEnthalpyLevel(
        CircuitGraph graph, ImmutableArray<HydraulicComponent> hydraulics, HydraulicComponent hydraulic)
    {
        if (graph.Mode is not SolveMode.Steady || !hydraulic.IsClosed)
        {
            return false;
        }

        foreach (var element in hydraulic.Elements)
        {
            if (IsCoupled(hydraulics, element))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether anything in a component states the temperature its relations leave free.</summary>
    /// <param name="hydraulic">The component to search.</param>
    /// <returns><see langword="true"/> when a node temperature or an exchanger terminal is stated.</returns>
    /// <remarks>
    /// Used only to word <c>FS2211</c>: the count is the same either way, and this decides whether the
    /// message may sensibly ask for a temperature the script has already given.
    /// </remarks>
    private static bool FixesEnthalpyLevel(HydraulicComponent hydraulic)
    {
        foreach (var node in hydraulic.Nodes)
        {
            if (HydraulicPartition.Stated(node.Component, HydraulicPartition.Temperature) is not null)
            {
                return true;
            }
        }

        foreach (var element in hydraulic.Elements)
        {
            foreach (var parameter in Terminals)
            {
                if (HydraulicPartition.Stated(element, parameter) is not null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Matches each constraint to the sized parameter that can absorb it.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="hydraulics">Its hydraulic partition.</param>
    /// <param name="constraints">The constraints, in the order they claim candidates.</param>
    /// <returns>One promotion per constraint that found a free parameter.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Greedy, first-come, and that is the point rather than a shortcut.</strong> Two parallel
    /// branches each pinning a flow cannot both be met by the one pump between them; the first takes the
    /// pump head and the second falls to its own branch's balancing valve, which is exactly what a
    /// balancing valve is for.
    /// </para>
    /// <para>
    /// <strong>A mixed inlet accepts only a mixing split.</strong> Letting it fall back to a valve's
    /// <c>kv</c> would square the count on a circuit whose inlet temperature no parameter can reach —
    /// a closed adiabatic ring with a heat source, say — and report it solvable when it has no solution
    /// at all. An unmatched constraint is the honest answer there.
    /// </para>
    /// </remarks>
    private static ImmutableArray<Promotion> Promote(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ImmutableArray<ComponentConstraint> constraints)
    {
        var promotions = ImmutableArray.CreateBuilder<Promotion>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach (var constraint in constraints)
        {
            foreach (var (component, parameter) in Candidates(graph, hydraulics, constraint))
            {
                if (!taken.Add($"{component}.{parameter}"))
                {
                    continue;
                }

                promotions.Add(new Promotion(component, parameter, constraint));
                break;
            }
        }

        return promotions.ToImmutable();
    }

    /// <summary>The sized parameters that could absorb one constraint, best first.</summary>
    private static IEnumerable<(string Component, string Parameter)> Candidates(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ComponentConstraint constraint)
    {
        var hydraulic = hydraulics.FirstOrDefault(candidate => candidate.Index == constraint.Hydraulic);

        if (hydraulic is null)
        {
            yield break;
        }

        if (constraint.Kind is ConstraintKind.MixedInlet)
        {
            // Only the mixing split can move a mixed inlet temperature.
            foreach (var element in hydraulic.Elements)
            {
                if (string.Equals(element.Kind, "three_way_valve", StringComparison.Ordinal)
                    && IsFree(element, "position"))
                {
                    yield return (element.Name, "position");
                }
            }

            yield break;
        }

        if (constraint.Kind is not ConstraintKind.FixedFlow)
        {
            // A temperature stated on an interior node is met by whatever controls the branch feeding
            // it, and a controller is not in the graph -- it has no ports, so lowering drops it. Until
            // one reaches here the constraint has no candidate, which reports as over-specified rather
            // than being quietly absorbed.
            yield break;
        }

        var owner = graph.Components.FirstOrDefault(
            element => string.Equals(element.Name, constraint.Component, StringComparison.Ordinal));

        // A duty with no stated power determines the power: the constraint promotes the exchanger's own
        // parameter before it reaches for anything else's.
        if (owner is not null && IsFree(owner, "power"))
        {
            yield return (owner.Name, "power");
        }

        foreach (var element in hydraulic.Elements)
        {
            if (string.Equals(element.Kind, "pump", StringComparison.Ordinal) && IsFree(element, "head"))
            {
                yield return (element.Name, "head");
            }
        }

        // The parallel case: branches sharing their endpoints share a pressure difference, so a branch's
        // flow can only be moved by changing its own resistance. The first unsized valve along it is
        // what a balancing valve is.
        if (owner is null)
        {
            yield break;
        }

        foreach (var branch in graph.Branches)
        {
            if (!branch.Path.Contains(owner))
            {
                continue;
            }

            foreach (var element in branch.Path)
            {
                if (element.Kind is "valve" or "three_way_valve" && IsFree(element, "kv"))
                {
                    yield return (element.Name, "kv");
                }
            }
        }
    }

    /// <summary>Whether a parameter is available to be promoted.</summary>
    /// <param name="component">The component that owns it.</param>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <returns><see langword="true"/> when nothing has decided it yet.</returns>
    /// <remarks>
    /// Free means the script did not state it and the registry declares no visible default for it —
    /// which is exactly the <c>Sized</c> omission policy, and exactly what <c>D-02</c> leaves for
    /// something else to choose. A stated parameter is never promoted: two things setting one unknown
    /// is the trap <c>D-02</c> creates, and it reports as an over-specification naming both.
    /// </remarks>
    private static bool IsFree(IFlowComponent component, string parameter) =>
        !component.StatedParameters.ContainsKey(parameter)
        && !component.DefaultParameters.ContainsKey(parameter);

    /// <summary>Which hydraulic component an element belongs to.</summary>
    /// <returns>Its index, or zero when nothing claims it.</returns>
    private static int Owner(ImmutableArray<HydraulicComponent> hydraulics, IFlowComponent element)
    {
        foreach (var hydraulic in hydraulics)
        {
            if (hydraulic.Elements.Contains(element))
            {
                return hydraulic.Index;
            }
        }

        return 0;
    }

    // ---- the reports -------------------------------------------------------------------------------

    /// <summary>Reports every datum the graph had to pick for itself.</summary>
    private static void ReportDatums(
        ImmutableArray<HydraulicComponent> hydraulics, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var hydraulic in hydraulics)
        {
            if (!hydraulic.DatumWasStated && hydraulic.Datum.Length > 0)
            {
                diagnostics.Add(Diagnostic.Create(
                    TopologyDiagnostics.DatumChosen, span: null, new DiagnosticArgument("node", hydraulic.Datum))
                    with
                { ComponentName = hydraulic.Datum });
            }
        }
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
    /// Two stated pressures are ordinary and must not be reported: the cooling loop's <c>N1 p=300</c>
    /// and <c>N3 p=280</c> are what drive its primary. The degenerate case is two of them with nothing
    /// between them that could develop a pressure difference, where the second is not a boundary
    /// condition at all but a second datum on the same equipotential.
    /// </remarks>
    private static void ReportCompetingDatums(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var hydraulic in hydraulics)
        {
            for (var i = 0; i < hydraulic.StatedPressures.Length; i++)
            {
                for (var j = i + 1; j < hydraulic.StatedPressures.Length; j++)
                {
                    var a = hydraulic.StatedPressures[i];
                    var b = hydraulic.StatedPressures[j];

                    if (!Equipotential(graph, a, b))
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

    /// <summary>Whether two nodes are joined by ideal links alone.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="from">One stated pressure's node.</param>
    /// <param name="to">The other's.</param>
    /// <returns><see langword="true"/> when nothing between them could make the two differ.</returns>
    /// <remarks>
    /// <strong>Adjacency inside a branch, not whole branches.</strong> <c>N1 - N2</c> in the middle of a
    /// longer run is still an ideal link between two separate pressure unknowns, and a check that only
    /// compared branch <em>ends</em> would miss every one that is not a branch of its own — which is
    /// most of them, since a degree-two node is interior by construction.
    /// </remarks>
    private static bool Equipotential(CircuitGraph graph, GraphNode from, GraphNode to)
    {
        var reached = new HashSet<IFlowComponent> { from.Component };
        var grew = true;

        while (grew)
        {
            grew = false;

            foreach (var branch in graph.Branches)
            {
                var previous = branch.From.Element;

                foreach (var part in branch.Path)
                {
                    grew |= Join(reached, previous, part);
                    previous = part;
                }

                grew |= Join(reached, previous, branch.To.Element);
            }
        }

        return reached.Contains(to.Component);
    }

    /// <summary>Joins two adjacent elements into the equipotential set, when both are nodes.</summary>
    /// <param name="reached">The set built so far.</param>
    /// <param name="left">The earlier element along the branch.</param>
    /// <param name="right">The later one.</param>
    /// <returns><see langword="true"/> when the set grew.</returns>
    /// <remarks>
    /// Only a node-to-node adjacency is an ideal link (<c>D-25</c>). Anything else along a branch is a
    /// component that can develop a pressure difference, however small its stated resistance, and two
    /// pressures either side of one are ordinary boundary conditions rather than competing datums.
    /// </remarks>
    private static bool Join(HashSet<IFlowComponent> reached, IFlowComponent left, IFlowComponent right)
    {
        if (left is not CircuitNode || right is not CircuitNode)
        {
            return false;
        }

        if (reached.Contains(left))
        {
            return reached.Add(right);
        }

        return reached.Contains(right) && reached.Add(left);
    }

    /// <summary>Reports every loop nothing can drive flow around.</summary>
    /// <remarks>
    /// Read from <c>ComponentKindInfo.DrivesFlow</c>, which is explicit registry metadata. Inspecting
    /// residual code or guessing from parameter names is forbidden (<c>D-30</c>): a rule that infers
    /// structure from an implementation detail changes meaning when the implementation does.
    /// </remarks>
    private static void ReportDriverlessLoops(
        CircuitGraph graph, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var loop in graph.Loops)
        {
            var driven = false;

            foreach (var branch in loop.Branches)
            {
                foreach (var part in branch.Path)
                {
                    driven |= ComponentRegistry.Default.ByKeyword(part.Kind)?.DrivesFlow == true;
                }
            }

            if (!driven)
            {
                diagnostics.Add(Diagnostic.Create(
                    TopologyDiagnostics.LoopWithoutDriver, span: null, new DiagnosticArgument("loop", loop.Label)));
            }
        }
    }

    /// <summary>Reports every stated boundary state the substance cannot be in.</summary>
    private static void ReportStates(CircuitGraph graph, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var range = graph.Substance.ValidRange;

        foreach (var node in graph.Nodes)
        {
            var temperature = HydraulicPartition.Stated(node.Component, HydraulicPartition.Temperature);
            var pressure = HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure);

            if (temperature is null && pressure is null)
            {
                continue;
            }

            // Where a boundary states only one of the two, the other is checked at the middle of its
            // validated span rather than at an edge: the point is to catch a stated value that is out
            // of range, not to report a temperature because no one wrote a pressure beside it.
            var kelvin = temperature ?? ((range.MinimumTemperature + range.MaximumTemperature) / 2);
            var absolute = pressure is { } gauge
                ? gauge + UnitTable.StandardAtmosphere
                : (range.MinimumAbsolutePressure + range.MaximumAbsolutePressure) / 2;

            if (range.Contains(kelvin, absolute))
            {
                continue;
            }

            diagnostics.Add(Diagnostic.Create(
                TopologyDiagnostics.StateOutsideRange,
                span: null,
                new DiagnosticArgument("substance", graph.Substance.Name),
                new DiagnosticArgument("state", Describe(temperature, pressure)))
                with
            { ComponentName = node.Name });
        }
    }

    /// <summary>Renders a boundary state the way the script wrote it.</summary>
    private static string Describe(double? temperature, double? gaugePressure)
    {
        var parts = new List<string>(2);

        if (temperature is { } kelvin)
        {
            parts.Add((kelvin - 273.15).ToString("0.###", CultureInfo.InvariantCulture) + " °C");
        }

        if (gaugePressure is { } pascal)
        {
            parts.Add((pascal / 1000).ToString("0.###", CultureInfo.InvariantCulture) + " kPa");
        }

        return string.Join(" and ", parts);
    }

    /// <summary>Reports a two-sided component whose owning circuit is not read off its heat direction.</summary>
    /// <remarks>
    /// <c>D-36</c>: the owner is the circuit on the side <em>losing</em> nominal enthalpy. Ownership is a
    /// tagging and grouping question and never a solver one — no equation, unknown, datum or balance
    /// depends on it — so the fallback of the lower circuit number is safe as well as deterministic.
    /// </remarks>
    private static void ReportOwnership(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var element in graph.Components)
        {
            var sides = hydraulics.Where(hydraulic => hydraulic.Elements.Contains(element)).ToArray();

            if (sides.Length < 2)
            {
                continue;
            }

            var one = HydraulicPartition.Stated(element, "in");
            var oneOut = HydraulicPartition.Stated(element, "out");
            var two = HydraulicPartition.Stated(element, "in2");
            var twoOut = HydraulicPartition.Stated(element, "out2");

            // A determinate direction needs both sides' terminal temperatures: without them there is
            // nothing to read a losing side off, and D-31's "the leftmost circuit owns it" is a layout
            // outcome that D-03 forbids Core from computing.
            if (one is { } a && oneOut is { } b && two is { } c && twoOut is { } d && (b - a) * (d - c) < 0)
            {
                continue;
            }

            var circuits = sides
                .Select(side => graph.CircuitOf.GetValueOrDefault(element.Name, string.Empty))
                .ToArray();

            diagnostics.Add(Diagnostic.Create(
                TopologyDiagnostics.AmbiguousOwnership,
                span: null,
                new DiagnosticArgument("component", element.Name),
                new DiagnosticArgument("a", Name(graph, sides[0])),
                new DiagnosticArgument("b", Name(graph, sides[1])),
                new DiagnosticArgument("chosen", circuits[0]))
                with
            { ComponentName = element.Name });
        }
    }

    /// <summary>A hydraulic component's reportable name: the circuit its first element belongs to.</summary>
    private static string Name(CircuitGraph graph, HydraulicComponent hydraulic) =>
        hydraulic.Elements.Length > 0
            ? graph.CircuitOf.GetValueOrDefault(hydraulic.Elements[0].Name, hydraulic.Index.ToString(CultureInfo.InvariantCulture))
            : hydraulic.Index.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reports a closed circuit whose stated duties cannot sum to zero (<c>FS2203</c>).</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="hydraulics">Its hydraulic partition.</param>
    /// <param name="diagnostics">Where to report.</param>
    /// <remarks>
    /// <para>
    /// <strong>Consistency is a different question from squareness.</strong> A closed ring with a 30 kW
    /// source and no sink has exactly as many equations as unknowns and no solution, because summing its
    /// energy balances gives <c>Σ Q̇ = 0</c> against stated duties that do not. The counting pass cannot
    /// see it and no amount of promotion will, so it is checked here.
    /// </para>
    /// <para>
    /// <strong>Steady mode only.</strong> The same circuit in a transient is a warm-up study: the water
    /// heats up, which is what the storage term is for.
    /// </para>
    /// </remarks>
    private static void ReportClosure(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (graph.Mode is not SolveMode.Steady)
        {
            return;
        }

        foreach (var hydraulic in hydraulics)
        {
            if (!hydraulic.IsClosed || Imbalance(hydraulics, hydraulic) is not { } imbalance)
            {
                continue;
            }

            diagnostics.Add(Diagnostic.Create(
                TopologyDiagnostics.UnbalancedClosedCircuit,
                span: null,
                new DiagnosticArgument("circuit", Name(graph, hydraulic)),
                new DiagnosticArgument(
                    "power",
                    (imbalance / 1000).ToString("0.###", CultureInfo.InvariantCulture) + " kW")));
        }
    }

    /// <summary>The heat a closed circuit's stated duties leave with nowhere to go.</summary>
    /// <param name="hydraulics">The hydraulic partition, which is what says whether a side is wired.</param>
    /// <param name="hydraulic">The closed component to sum.</param>
    /// <returns>
    /// The signed imbalance in W, positive when heat is added; or <see langword="null"/> when the
    /// circuit can balance, when a duty is still to be sized, or when the sign search is too wide to
    /// mean anything.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>An unstated duty ends the sum rather than contributing zero</strong> (<c>D-02</c>). A duty
    /// the script never wrote is one sizing may still choose, and a total missing a term says nothing
    /// about the total.
    /// </para>
    /// <para>
    /// <strong>A coupled exchanger's sign is not readable from the graph.</strong> <c>power</c> is
    /// positive when side 1 gains, and which side is in <em>this</em> component is a fact about ports
    /// that the branch decomposition does not carry — a branch records its ends' ports and an exchanger
    /// is interior to a branch on each side. So every assignment is tried and the circuit is reported
    /// only when none of them balances, which is the reading that cannot produce a false error: the
    /// substation's closed secondary balances under exactly one of its two.
    /// </para>
    /// <para>
    /// A pump contributes nothing here. It writes a pressure relation and no energy row, so a closed
    /// loop of pumps and pipes balances at exactly zero rather than nearly zero.
    /// </para>
    /// </remarks>
    private static double? Imbalance(
        ImmutableArray<HydraulicComponent> hydraulics, HydraulicComponent hydraulic)
    {
        // Four couplings is sixteen assignments; past that a balance found by search says more about
        // arithmetic luck than about the circuit, so the check stands down instead of reporting one.
        const int searchLimit = 4;

        var known = 0d;
        var scale = 0d;
        var coupled = new List<double>();

        foreach (var element in hydraulic.Elements)
        {
            if (!string.Equals(element.Kind, "heat_exchanger", StringComparison.Ordinal))
            {
                continue;
            }

            if (HydraulicPartition.Stated(element, "power") is not { } power)
            {
                return null;
            }

            scale += Math.Abs(power);

            if (IsCoupled(hydraulics, element))
            {
                coupled.Add(power);
            }
            else
            {
                known += power;
            }
        }

        if (coupled.Count > searchLimit)
        {
            return null;
        }

        var smallest = double.PositiveInfinity;
        var imbalance = 0d;

        for (var assignment = 0; assignment < 1 << coupled.Count; assignment++)
        {
            var total = known;

            for (var i = 0; i < coupled.Count; i++)
            {
                total += (assignment & (1 << i)) == 0 ? coupled[i] : -coupled[i];
            }

            if (Math.Abs(total) < smallest)
            {
                smallest = Math.Abs(total);
                imbalance = total;
            }
        }

        // Stated duties are exact to the digit the script wrote, so the only slack the sum needs is the
        // rounding of adding them together.
        return smallest > 1e-9 * Math.Max(scale, 1) ? imbalance : null;
    }

    /// <summary>Reports fluid that can enter a circuit and not leave it, or the reverse (<c>FS2204</c>).</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="hydraulics">Its hydraulic partition.</param>
    /// <param name="diagnostics">Where to report.</param>
    /// <remarks>
    /// <para>
    /// The mass analogue of <see cref="ReportClosure"/>, and invisible to the count for the same reason:
    /// a stated <c>flow</c> is a known injection, so a circuit that injects mass with nowhere to put it
    /// is square and inconsistent.
    /// </para>
    /// <para>
    /// <strong>A stated pressure is a way in and a way out</strong> as surely as a boundary kind is —
    /// mass crosses there to hold the number — so a <c>supply</c> paired with a pressure-driven outlet is
    /// not reported. What a pressure cannot do is stand in for the boundary it sits <em>on</em>: a
    /// <c>supply p=300</c> is one node, and a circuit whose only flux is at that node passes none.
    /// </para>
    /// <para>
    /// <strong>A circuit with neither boundary kind is never reported here.</strong> A closed loop needs
    /// neither (<c>D-64</c>), and the cooling loop's two stated pressures are a complete pair of
    /// boundary conditions written the older way.
    /// </para>
    /// </remarks>
    private static void ReportBoundaries(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var hydraulic in hydraulics)
        {
            var supplied = hydraulic.Boundaries.Any(
                static node => node.Component.Boundary is BoundaryRole.Supply);
            var returned = hydraulic.Boundaries.Any(
                static node => node.Component.Boundary is BoundaryRole.Return);

            var exits = hydraulic.Nodes.Any(static node =>
                node.Component.Boundary is BoundaryRole.Return
                || (node.Component.Boundary is not BoundaryRole.Supply
                    && HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure) is not null));

            var entries = hydraulic.Nodes.Any(static node =>
                node.Component.Boundary is BoundaryRole.Supply
                || (node.Component.Boundary is not BoundaryRole.Return
                    && (HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure) is not null
                        || HydraulicPartition.Stated(node.Component, HydraulicPartition.Flow) is not null)));

            string present;
            string missing;

            if (supplied && !exits)
            {
                (present, missing) = ("supply", "return");
            }
            else if (returned && !entries)
            {
                (present, missing) = ("return", "supply");
            }
            else
            {
                continue;
            }

            diagnostics.Add(Diagnostic.Create(
                TopologyDiagnostics.UnpairedBoundary,
                span: null,
                new DiagnosticArgument("circuit", Name(graph, hydraulic)),
                new DiagnosticArgument("present", present),
                new DiagnosticArgument("missing", missing)));
        }
    }

    /// <summary>Reports a system that is not square, naming what would square it.</summary>
    /// <remarks>
    /// <strong>The list is the whole value of the message.</strong> "Over-specified by 1" is a puzzle;
    /// "remove HE1.in" is a fix. The candidates for an over-specification are the constraints that found
    /// nothing to promote, because those are precisely the statements the circuit cannot meet.
    /// </remarks>
    private static void ReportBalance(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        CountingTable counting,
        ImmutableArray<Promotion> promotions,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (counting.Excess == 0)
        {
            return;
        }

        if (counting.Excess > 0)
        {
            var absorbed = promotions.Select(static promotion => promotion.Constraint).ToHashSet();
            var unmatched = counting.Constraints.Where(constraint => !absorbed.Contains(constraint)).ToArray();

            var candidates = unmatched.Length > 0
                ? unmatched.Select(static constraint => constraint.Label)
                : Overstated(graph);

            diagnostics.Add(Diagnostic.Create(
                TopologyDiagnostics.OverSpecified,
                span: null,
                new DiagnosticArgument("n", counting.Excess.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticArgument("list", string.Join(", ", candidates))));

            return;
        }

        diagnostics.Add(Diagnostic.Create(
            TopologyDiagnostics.UnderSpecified,
            span: null,
            new DiagnosticArgument("n", (-counting.Excess).ToString(CultureInfo.InvariantCulture)),
            new DiagnosticArgument("list", string.Join(", ", Understated(graph, hydraulics)))));
    }

    /// <summary>What could be removed when no constraint is the culprit.</summary>
    /// <remarks>
    /// A pressure stated on a node with no mass balance is the case that lands here: the node is
    /// interior to a branch, so there is nowhere for external mass to enter and nothing to absorb the
    /// statement.
    /// </remarks>
    private static IEnumerable<string> Overstated(CircuitGraph graph) =>
        graph.Nodes
            .Where(static node =>
                HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure) is not null
                && !node.Component.CarriesMassBalance)
            .Select(static node => $"{node.Name}.p");

    /// <summary>What could be added to square an under-specified circuit.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="hydraulics">Its hydraulic partition, which is what knows a level is unfilled.</param>
    /// <returns>The candidates, the thermal ones first.</returns>
    /// <remarks>
    /// <strong>A missing temperature is named ahead of a missing pressure</strong>, because it is the one
    /// the graph could not have picked for itself. A circuit that states no pressure gets a datum and an
    /// <c>FS2201</c>, so it never arrives here short of one; a circuit whose temperature level nothing
    /// fixes has no such fallback.
    /// </remarks>
    private static string[] Understated(
        CircuitGraph graph, ImmutableArray<HydraulicComponent> hydraulics)
    {
        var candidates = new List<string>();

        foreach (var hydraulic in hydraulics)
        {
            if (NeedsEnthalpyLevel(graph, hydraulics, hydraulic) && !FixesEnthalpyLevel(hydraulic))
            {
                candidates.AddRange(hydraulic.Nodes.Select(static node => $"a temperature on {node.Name}"));
            }
        }

        candidates.AddRange(graph.Nodes
            .Where(static node =>
                node.Component.CarriesMassBalance
                && HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure) is null)
            .Select(static node => $"a pressure on {node.Name}"));

        return candidates.Count > 0 ? [.. candidates] : ["a boundary condition"];
    }
}
