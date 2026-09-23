using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Topology.Counting;

public static partial class WellPosedness
{
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
            if (vertex is not NodeComponent node || node.CarriesMassBalance)
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
            if (element is NodeComponent)
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
        // Keyed by object with a reference comparer: a branch path carries the NodeComponent, and an
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
                if (part is NodeComponent)
                {
                    if (previous is NodeComponent)
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

            if (previous is NodeComponent && branch.To.Element is NodeComponent)
            {
                relations++;
                Link(nodes, ideal, previous, branch.To.Element);
            }
        }

        foreach (var vertex in graph.JunctionElements)
        {
            if (vertex is NodeComponent)
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

            if (element is not NodeComponent node || node.CarriesMassBalance)
            {
                balances++;
            }
        }

        return balances;
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
        IsCoupled(hydraulics, element) || element is HeatExchangerComponent { Rating.CanRate: true };

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
}
