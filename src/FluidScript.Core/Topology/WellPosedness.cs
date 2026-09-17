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

        var constraints = Constraints(graph, hydraulics);
        var promotions = Promote(graph, hydraulics, constraints);
        var counting = Count(graph, hydraulics, constraints, promotions);

        ReportBalance(graph, hydraulics, counting, promotions, diagnostics);
        ReportReaches(graph, promotions, diagnostics);

        return new WellPosednessResult(counting, hydraulics, diagnostics.ToImmutable());
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
            //
            // `D-86`: the test is the node's *kind*. A stated pressure on an interior node is a datum,
            // not a boundary condition, so it brings no flux unknown -- and this must agree with
            // `HydraulicComponent.HasUnknownFlux`, which decides the matching mass-balance drop. The two
            // are the same rule written twice; changing one alone leaves the table short an equation and
            // advising a pressure on a mid-branch node (`S-39`).
            if (node.Component.CarriesMassBalance
                && HydraulicPartition.Stated(node.Component, HydraulicPartition.Flow) is null
                && node.Component.Boundary is not BoundaryRole.Interior)
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
    /// <strong>Nor are a coupled or rated exchanger's terminal temperatures.</strong> Once both sides are
    /// wired, or a second-side profile is stated, <c>in</c>, <c>out</c>, <c>in2</c> and <c>out2</c> are the
    /// <em>rating design point</em> that <c>24</c> sizes UA from (<c>D-19</c>), not demands on the solved
    /// state. Counting them as constraints reports the substation over-specified by three, on the
    /// reference circuit written to demonstrate that two circuits can be solved together. A rated
    /// exchanger that cannot rate -- no size, or a profile too thin to fix its second side -- delivers
    /// its stated duty and is counted exactly as a Duty one.
    /// </para>
    /// </remarks>

    private static ImmutableArray<ComponentConstraint> Constraints(
        CircuitGraph graph, ImmutableArray<HydraulicComponent> hydraulics)
    {
        var constraints = ImmutableArray.CreateBuilder<ComponentConstraint>();

        // Which components have already had their dropped enthalpy level paid for (`D-90`). A level pays
        // for exactly one statement, so the first lone terminal in each takes it and the rest are flow
        // pins as before.
        var levelled = new HashSet<int>();

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
                || Rates(hydraulics, element))
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

            // A terminal pins a flow only when the other end of the same side is known too. With `in` and
            // `out` both stated, `power` gives m = Q/(h_out - h_in) and the flow follows; with `out`
            // alone that is one equation in two unknowns and pins nothing -- but it does fix an absolute
            // temperature, which is precisely what a closed circuit's dropped level needs. An open
            // circuit takes its inlet from a boundary, so the inlet is known without being stated and a
            // lone `out` pins the flow there exactly as before.
            var owed = hydraulics.FirstOrDefault(candidate => candidate.Index == hydraulic) is { } block
                && NeedsEnthalpyLevel(graph, hydraulics, block);

            foreach (var parameter in FlowPins)
            {
                if (HydraulicPartition.Stated(element, parameter) is null)
                {
                    continue;
                }

                var pays = owed
                    && Partner(parameter) is { } partner
                    && HydraulicPartition.Stated(element, partner) is null
                    && levelled.Add(hydraulic);

                constraints.Add(new ComponentConstraint(
                    element.Name,
                    parameter,
                    pays ? ConstraintKind.EnthalpyLevel : ConstraintKind.FixedFlow,
                    hydraulic));
            }
        }

        // `D-97`. A coupled exchanger's design point is what sizes UA, and it also says what each side
        // runs at: `power` with `in2`/`out2` is a flow on the side-2 branch as surely as `LOAD.dt` is one
        // on the secondary. It pins a side only where nothing else already does -- the substation's
        // secondary is pinned by `LOAD.dt`, its primary by `HX1`'s 85/45 -- because two pins on one
        // hydraulic are one constraint too many, and the exchanger's is the one a designer would drop.
        // A rated exchanger has the same design point and one wired side, so side 2 finds no hydraulic
        // and side 1 is pinned by the same rule.
        foreach (var element in graph.Components)
        {
            if (element is not HeatExchanger || !Rates(hydraulics, element))
            {
                continue;
            }
            foreach (var (inlet, outlet, change, port) in CoupledSides)
            {
                var pin = HydraulicPartition.Stated(element, outlet) is not null
                    && HydraulicPartition.Stated(element, inlet) is not null
                        ? outlet
                        : HydraulicPartition.Stated(element, change) is not null ? change : null;

                if (pin is null || SideHydraulic(graph, hydraulics, element, port) is not { } side)
                {
                    continue;
                }

                var pinned = constraints.Any(existing =>
                    existing.Hydraulic == side
                    && existing.Kind is ConstraintKind.FixedFlow or ConstraintKind.EnthalpyLevel)
                    || hydraulics[side].Boundaries.Any(static boundary =>
                        HydraulicPartition.Stated(boundary.Component, HydraulicPartition.Flow) is not null);

                if (!pinned)
                {
                    constraints.Add(new ComponentConstraint(element.Name, pin, ConstraintKind.FixedFlow, side));
                }
            }
        }

        return constraints.ToImmutable();
    }

    /// <summary>A coupled exchanger's two sides: the terminals that pin each, and the port that finds its hydraulic.</summary>
    private static readonly (string Inlet, string Outlet, string Change, int Port)[] CoupledSides =
    [
        ("in", "out", "dt", 0),
        ("in2", "out2", "dt2", 2),
    ];

    /// <summary>The hydraulic component one side of a coupled exchanger runs in.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="hydraulics">The partition.</param>
    /// <param name="element">The exchanger.</param>
    /// <param name="port">The side's inlet port index: 0 for side 1, 2 for side 2.</param>
    /// <returns>The hydraulic's index, or <see langword="null"/> when the port is not connected.</returns>
    /// <remarks>
    /// The exchanger itself belongs to both hydraulics, so its membership says nothing; the element on the
    /// other end of the port -- a node, by rule I2 -- belongs to exactly one.
    /// </remarks>
    private static int? SideHydraulic(
        CircuitGraph graph, ImmutableArray<HydraulicComponent> hydraulics, IFlowComponent element, int port)
    {
        var index = graph.Components.IndexOf(element);

        if (index < 0)
        {
            return null;
        }

        var peer = graph.Adjacency.Peer(index, port);

        if (!peer.Exists)
        {
            return null;
        }

        var neighbour = graph.Components[peer.Component];

        foreach (var hydraulic in hydraulics)
        {
            if (hydraulic.Elements.Contains(neighbour))
            {
                return hydraulic.Index;
            }
        }

        return null;
    }

    /// <summary>The terminal at the other end of the same side, whose value makes a flow computable.</summary>
    /// <param name="parameter">A terminal or difference parameter.</param>
    /// <returns>
    /// The partner terminal, or <see langword="null"/> for a parameter that is already a difference and so
    /// has none -- <c>dt</c> states the rise outright and needs no second temperature to pin a flow.
    /// </returns>
    private static string? Partner(string parameter) => parameter switch
    {
        "out" => "in",
        "out2" => "in2",
        _ => null,
    };

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

    /// <summary>Whether a component's duty follows from the temperatures it sees rather than from a stated number.</summary>
    /// <param name="hydraulics">The hydraulic partition.</param>
    /// <param name="element">The component to classify.</param>
    /// <returns><see langword="true"/> for a coupled exchanger, or a rated one whose rating can rate.</returns>
    /// <remarks>
    /// The condition the energy block actually runs under: a coupled exchanger reads both sides' inlets,
    /// and a rated one reads its side-1 inlet against the profile it was given. Either way its duty
    /// depends on the absolute temperature level, which is what fixes a closed circuit's level and what
    /// turns its stated terminals from demands into a design point. A rated exchanger with no size yet,
    /// or a profile that does not fix its second side, delivers its stated duty and is not this.
    /// </remarks>
    private static bool Rates(ImmutableArray<HydraulicComponent> hydraulics, IFlowComponent element) =>
        IsCoupled(hydraulics, element) || element is HeatExchanger { Rating.CanRate: true };

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
            // A rated exchanger's duty depends on the temperature its side 1 enters at, which is what fixes
            // the level a closed circuit's own balances leave free (`P4.1`); a coupled one couples it to
            // the other circuit's. Without a size to rate against the duty is a constant and fixes nothing.
            if (Rates(hydraulics, element))
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
                    && IsFree(graph, element, "position"))
                {
                    yield return (element.Name, "position");
                }
            }

            yield break;
        }

        if (constraint.Kind is ConstraintKind.NodeTemperature)
        {
            // A temperature stated on an interior node is a **setpoint**, and in a steady circuit the thing
            // that holds it is the mixing split feeding that node -- exactly as a stated inlet is held by the
            // split feeding an exchanger. [`34-controllers`](../../plan/30-solver/34-controllers.md) states
            // the pairing outright: in the steady circuit the stated value "promotes the valve position into
            // a solver unknown -- the circuit is *solved* into position", and "a controller does the same job
            // dynamically". A controller is therefore the transient counterpart of this promotion, not a
            // prerequisite for it, which is what the previous reading of this branch assumed.
            //
            // Until this arrived, a stated node temperature added a row and promoted nothing, so the circuit
            // reported over-specified by exactly one --- breaking `23`'s rule that a constraint and its
            // promotion appear and disappear together, which is that document's own check that the counting
            // scheme is the right one (`S-48`).
            var measured = graph.Components.FirstOrDefault(
                element => string.Equals(element.Name, constraint.Component, StringComparison.Ordinal));

            // The splits that reach the node: either end of a branch the node lies on, and either end of a
            // branch that ends at it. Ordering matters for the same reason it does below (`S-45`) --- taking a
            // distant valve while an adjacent one is free is what makes a circuit rank-deficient and square at
            // once --- so the reaching ones are offered first and the rest only after.
            var reaching = new HashSet<IFlowComponent>();

            if (measured is not null)
            {
                foreach (var branch in graph.Branches)
                {
                    if (!branch.Path.Contains(measured)
                        && !ReferenceEquals(branch.From.Element, measured)
                        && !ReferenceEquals(branch.To.Element, measured))
                    {
                        continue;
                    }

                    reaching.Add(branch.From.Element);
                    reaching.Add(branch.To.Element);

                    foreach (var element in branch.Path)
                    {
                        reaching.Add(element);
                    }
                }
            }

            foreach (var element in hydraulic.Elements)
            {
                if (string.Equals(element.Kind, "three_way_valve", StringComparison.Ordinal)
                    && IsFree(graph, element, "position")
                    && reaching.Contains(element))
                {
                    yield return (element.Name, "position");
                }
            }

            foreach (var element in hydraulic.Elements)
            {
                if (string.Equals(element.Kind, "three_way_valve", StringComparison.Ordinal)
                    && IsFree(graph, element, "position")
                    && !reaching.Contains(element))
                {
                    yield return (element.Name, "position");
                }
            }

            yield break;
        }

        if (constraint.Kind is not ConstraintKind.FixedFlow)
        {
            // Every kind the enum carries is handled above, so this is the guard for one added later: a
            // constraint nothing can absorb reports as over-specified rather than being quietly dropped.
            yield break;
        }

        var owner = graph.Components.FirstOrDefault(
            element => string.Equals(element.Name, constraint.Component, StringComparison.Ordinal));

        // A duty with no stated power determines the power: the constraint promotes the exchanger's own
        // parameter before it reaches for anything else's.
        if (owner is not null && IsFree(graph, owner, "power"))
        {
            yield return (owner.Name, "power");
        }

        // Every element on a branch the constraint's owner sits on. A pump there drives the very flow the
        // constraint pins, so it is the candidate an engineer would name.
        var local = new HashSet<IFlowComponent>();

        if (owner is not null)
        {
            foreach (var branch in graph.Branches)
            {
                if (!branch.Path.Contains(owner))
                {
                    continue;
                }

                foreach (var element in branch.Path)
                {
                    local.Add(element);
                }
            }
        }

        // Own branch first, then the rest. **Reaching across the plant is deliberate and stays** ---
        // `Promote`'s first-come rule exists so that two parallel branches downstream of one pump share it,
        // the first taking its head and the second falling to its own balancing valve. What was missing is
        // an order: `hydraulic.Elements` is graph order, which is arbitrary with respect to the constraint,
        // and a hydraulic component is the whole connected plant rather than one circuit.
        //
        // Measured (`S-45`): with `PU_AHU.head` stated, `HE_AHU`'s flow constraint took `PU_RAD.head` ---
        // the other consumer's pump --- and that variant counts square at 44/44 while ranking 43,
        // deficient and over-specified at once. A promotion that crosses the plant is not wrong in itself;
        // taking a distant pump while a local one is free is.
        foreach (var element in hydraulic.Elements)
        {
            if (string.Equals(element.Kind, "pump", StringComparison.Ordinal)
                && IsFree(graph, element, "head")
                && local.Contains(element))
            {
                yield return (element.Name, "head");
            }
        }

        foreach (var element in hydraulic.Elements)
        {
            if (string.Equals(element.Kind, "pump", StringComparison.Ordinal)
                && IsFree(graph, element, "head")
                && !local.Contains(element))
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

            // On the constraint's own hydraulic: a coupled exchanger sits on a branch of each, and a valve on
            // the other circuit cannot move this one's flow.
            foreach (var element in branch.Path)
            {
                if (element.Kind is "valve" or "three_way_valve"
                    && IsFree(graph, element, "kv")
                    && hydraulic.Elements.Contains(element))
                {
                    yield return (element.Name, "kv");
                }
            }
        }
    }

    /// <summary>Whether a parameter is available to be promoted.</summary>
    /// <param name="graph">The graph, for which sized values are provisionals.</param>
    /// <param name="component">The component that owns it.</param>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <returns><see langword="true"/> when nothing has decided it yet.</returns>
    /// <remarks>
    /// Free means the script did not state it and the registry declares no visible default for it —
    /// which is exactly the <c>Sized</c> omission policy, and exactly what <c>D-02</c> leaves for
    /// something else to choose. A stated parameter is never promoted: two things setting one unknown
    /// is the trap <c>D-02</c> creates, and it reports as an over-specification naming both.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <strong>All three maps, and the third one was missing.</strong> <c>ComponentFactory.Defaults</c>
    /// already wrote down the rule this enforces — "well-posedness looks for a parameter no map claims"
    /// — but the check read two of them, so once the outer loop began filling
    /// <see cref="IComponent.SizedParameters"/> a parameter could be sized and promoted at once: chosen
    /// by a rule and solved for as an unknown, with the two answers disagreeing and nothing saying so.
    /// It cost nothing before <c>P3.7b</c>, because no map was ever filled.
    /// </para>
    /// <para>
    /// <strong>A sized value that is a bootstrap provisional is still free</strong> (<c>D-96</c>,
    /// <c>C-75</c>). The bootstrap writes a Kv into every unstated valve so that the valve exists, and
    /// from the third map alone that placeholder looked decided — so the balancing-valve promotion below
    /// never fired after <c>C-58</c>, and a pump with a stated head was refused with the exchanger's
    /// temperatures named instead of the valve closing on the surplus. The graph says which sized values
    /// are placeholders; a rule that later sizes one clears it, and a promotion keeps it a placeholder.
    /// </para>
    /// </remarks>
    private static bool IsFree(CircuitGraph graph, IFlowComponent component, string parameter) =>
        !component.StatedParameters.ContainsKey(parameter)
        && !component.DefaultParameters.ContainsKey(parameter)
        && (!component.SizedParameters.ContainsKey(parameter)
            || graph.ProvisionalParameters.Contains($"{component.Name}.{parameter}"));

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
            if (left is not CircuitNode || right is not CircuitNode)
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

    /// <summary>The fill-pressure margin practice adds above the static head, for the pressure the message suggests.</summary>
    /// <value>
    /// Pa. Half a bar: an expansion vessel's pre-charge is set to the static height plus 0.2 bar and the
    /// fill pressure 0.3 bar above that (Flamco's <em>Reference Guide</em>, Reflex's <em>Professional
    /// planning, calculation and equipment</em>, IMI Pneumatex's Statico manual, all after EN 12828).
    /// </value>
    private const double FillMargin = 50_000;

    /// <summary>The temperature the static-head density is taken at.</summary>
    /// <value>K. 20 °C: the plant is filled cold, and the check is about filling.</value>
    private const double FillTemperature = 293.15;

    /// <summary>Reports the highest node of each hydraulic part whose static head takes it below the substance's floor (<c>S-60</c>).</summary>
    /// <remarks>
    /// <para>
    /// Water at the top of a 32 m riser is 313 kPa below the bottom, and a script that states no
    /// pressure has its datum picked at 0 gauge. The seed then puts the top of the building at
    /// −213 kPa, water has no state there, and the solve stopped with <c>FS3007</c> — an impossible
    /// fluid state after 0 steps — which reads as a solver failure when the plant as written simply has
    /// no fill pressure. Now that every node has a height (<c>D-70</c>) the check is arithmetic before
    /// the seed: <c>p_datum − ρg(z − z_datum)</c> against the substance's floor, at the density the
    /// plant is filled at.
    /// </para>
    /// <para>
    /// One diagnostic per hydraulic part, on its highest node, because every node above the floor line
    /// fails for the one reason and the fix is one number on the datum. The number suggested is what
    /// practice writes: the static head plus half a bar.
    /// </para>
    /// </remarks>
    private static void ReportStaticHead(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var floor = graph.Substance.ValidRange.MinimumAbsolutePressure;

        foreach (var hydraulic in hydraulics)
        {
            var datum = hydraulic.Nodes.FirstOrDefault(node => node.Name == hydraulic.Datum);

            if (datum?.Component is not CircuitNode anchor)
            {
                continue;
            }

            var gauge = HydraulicPartition.Stated(anchor, HydraulicPartition.Pressure) ?? 0;

            // Density where the plant is filled: at the datum, cold. A property call that fails here
            // is a state FS2215 has already reported, and there is nothing to add.
            if (!graph.Substance.FromPressureTemperature(
                    Quantity.FromSi(Math.Max(gauge, 0), Dimension.Pressure),
                    Quantity.FromSi(FillTemperature, Dimension.Temperature)).TryGetValue(out var filled))
            {
                continue;
            }

            var density = filled.Density.SiValue;
            GraphNode? highest = null;
            var rise = 0.0;

            foreach (var node in hydraulic.Nodes)
            {
                if (node.Component is CircuitNode placed && placed.Elevation - anchor.Elevation > rise)
                {
                    rise = placed.Elevation - anchor.Elevation;
                    highest = node;
                }
            }

            if (highest is null)
            {
                continue;
            }

            var head = density * UnitTable.StandardGravity * rise;
            var absolute = gauge + UnitTable.StandardAtmosphere - head;

            if (absolute >= floor)
            {
                continue;
            }

            // The datum pressure practice would state: static head plus the fill margin, in whole tens
            // of kPa above the floor's own gauge value.
            var needed = Math.Ceiling((floor - UnitTable.StandardAtmosphere + head + FillMargin) / 10_000) * 10;

            diagnostics.Add(Diagnostic.Create(
                TopologyDiagnostics.StaticHeadBelowFloor,
                span: null,
                new DiagnosticArgument("node", highest.Name),
                new DiagnosticArgument("rise", rise.ToString("0.#", CultureInfo.InvariantCulture)),
                new DiagnosticArgument("datum", hydraulic.Datum),
                new DiagnosticArgument("short", ((floor - absolute) / 1000).ToString("0", CultureInfo.InvariantCulture)),
                new DiagnosticArgument("substance", graph.Substance.Name),
                new DiagnosticArgument("needed", needed.ToString("0", CultureInfo.InvariantCulture)))
                with
            { ComponentName = highest.Name });
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
            if (element is not HeatExchanger exchanger)
            {
                continue;
            }

            if (HydraulicPartition.Stated(exchanger, "power") is null)
            {
                return null;
            }

            var power = exchanger.Power;

                scale += Math.Abs(power);

            if (IsCoupled(hydraulics, exchanger))
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
    /// mass crosses there to hold the number — so an <c>inlet</c> paired with a pressure-driven outlet is
    /// not reported. What a pressure cannot do is stand in for the boundary it sits <em>on</em>: a
    /// <c>inlet p=300</c> is one node, and a circuit whose only flux is at that node passes none.
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
            // D-115: a boundary is a terminal with one connection; the flow splits or merges at a node after it (FS2205).
            foreach (var node in hydraulic.Nodes)
            {
                if (node.Component.Boundary is not BoundaryRole.Interior && node.Component.Ports.Length > 1)
                {
                    diagnostics.Add(Diagnostic.Create(
                        TopologyDiagnostics.BoundaryFanOut,
                        span: null,
                        new DiagnosticArgument("node", node.Component.Name),
                        new DiagnosticArgument("kind", node.Component.Boundary is BoundaryRole.Inlet ? "inlet" : "outlet"),
                        new DiagnosticArgument("count", node.Component.Ports.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))));
                }
            }

            var supplied = hydraulic.Boundaries.Any(
                static node => node.Component.Boundary is BoundaryRole.Inlet);
            var returned = hydraulic.Boundaries.Any(
                static node => node.Component.Boundary is BoundaryRole.Outlet);

            var exits = hydraulic.Nodes.Any(static node =>
                node.Component.Boundary is BoundaryRole.Outlet
                || (node.Component.Boundary is not BoundaryRole.Inlet
                    && HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure) is not null));

            var entries = hydraulic.Nodes.Any(static node =>
                node.Component.Boundary is BoundaryRole.Inlet
                || (node.Component.Boundary is not BoundaryRole.Outlet
                    && (HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure) is not null
                        || HydraulicPartition.Stated(node.Component, HydraulicPartition.Flow) is not null)));

            string present;
            string missing;

            if (supplied && !exits)
            {
                (present, missing) = ("inlet", "outlet");
            }
            else if (returned && !entries)
            {
                (present, missing) = ("outlet", "inlet");
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
