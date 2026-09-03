using System.Text;

using FluidScript.Core.Components;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Tests.Topology;

/// <summary>
/// Lowering, from <c>plan/20-core-domain/23-topology-and-graph.md</c>: the graph structure, the branch
/// decomposition and the cycle basis, checked against the cooling loop that document tabulates.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The cooling loop is the test, not an example.</strong> <c>23</c> writes out its six nodes,
/// four branches and one loop by hand, together with the reasoning for each — that terminals are
/// junction elements, that interior nodes are not, that counting only the degree-≥3 elements gives
/// three loops where there is one. Every one of those is a way the decomposition can be subtly wrong
/// while still producing a plausible graph.
/// </para>
/// <para>
/// Well-posedness — the counting argument, promotion, the datum and the <c>FS22xx</c> codes — is the
/// second half of the same document and is not asserted here.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class LoweringTests
{
    /// <summary>A canonical rendering of everything about a graph that lowering decides.</summary>
    /// <param name="graph">The graph to describe.</param>
    /// <returns>A stable string, comparable between two lowerings.</returns>
    /// <remarks>
    /// Structure rather than object identity, because two lowerings of one model build two sets of
    /// components and every reference differs. What invariant 6 asks is that the <em>shape and the
    /// order</em> are the same, which is exactly what this captures.
    /// </remarks>
    private static string Describe(CircuitGraph graph)
    {
        var text = new StringBuilder();

        text.AppendLine($"mode {graph.Mode}");

        foreach (var node in graph.Nodes)
        {
            text.AppendLine($"node {node.Name} {node.Origin} v={node.ThermalVolume:0.######}");
        }

        foreach (var component in graph.Components)
        {
            text.AppendLine(
                $"component {component.Name} {component.Kind} ports={component.Ports.Length} "
                + $"groups=[{string.Join(",", component.FlowGroups)}] eq={component.EquationCount}");
        }

        foreach (var branch in graph.Branches)
        {
            text.AppendLine(
                $"branch {branch.Index} {branch.From.Label} -> {branch.To.Label} "
                + $"[{string.Join(",", branch.Path.Select(static part => part.Name))}]");
        }

        foreach (var loop in graph.Loops)
        {
            text.AppendLine($"loop {loop.Label}");
        }

        foreach (var group in graph.Groups)
        {
            text.AppendLine($"group {group.Source} [{string.Join(",", group.Members)}]");
        }

        return text.ToString();
    }

    private static string Pair(Branch branch) => $"{branch.From.Label}|{branch.To.Label}";

    // ---- the cooling loop, as 23 tabulates it ---------------------------------------------------

    [Fact]
    public void TheCoolingLoopHasTheSixNodesTheDocumentCounts()
    {
        var graph = GraphFixture.Lower(GraphFixture.CoolingLoop).Graph;

        Assert.Equal(6, graph.Nodes.Length);
        Assert.Equal(
            ["N1", "N3", "N2", "PU1__HE1", "HE1__3WV", "3WV__P1"],
            graph.Nodes.Select(static node => node.Name));

        // N1 and N3 carry boundary conditions and were written; N2 comes from rule I1 and the three
        // `__` nodes from I2, one per pair of directly connected non-node components.
        Assert.Equal(
            [NodeOrigin.Declared, NodeOrigin.Declared, NodeOrigin.Inferred,
             NodeOrigin.Inferred, NodeOrigin.Inferred, NodeOrigin.Inferred],
            graph.Nodes.Select(static node => node.Origin));
    }

    [Fact]
    public void TheCoolingLoopHasTenComponentsOfWhichTheUserWroteSix()
    {
        var graph = GraphFixture.Lower(GraphFixture.CoolingLoop).Graph;

        Assert.Equal(10, graph.Components.Length);
        Assert.Equal(4, graph.Components.Count(static c => c is not CircuitNode));
    }

    [Fact]
    public void TerminalsAreJunctionElementsAndInteriorNodesAreNot()
    {
        // The correction 23 spells out: counting only the degree->=3 elements gives Loops = 4 - 2 + 1
        // = 3, and this circuit has one. A branch has to end somewhere, so a terminal is a vertex.
        var graph = GraphFixture.Lower(GraphFixture.CoolingLoop).Graph;

        Assert.Equal(
            ["N1", "N3", "3WV", "N2"],
            graph.JunctionElements.Select(static element => element.Name));
    }

    [Fact]
    public void TheCoolingLoopDecomposesIntoTheFourBranchesTheDocumentTabulates()
    {
        var graph = GraphFixture.Lower(GraphFixture.CoolingLoop).Graph;
        var pairs = graph.Branches.Select(Pair).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(4, graph.Branches.Length);

        // Direction is the walk's, not the document's, so each row is checked in whichever orientation
        // the walk found it. What must match is which two ends a branch joins.
        Assert.Contains(pairs, p => p is "N1|N2" or "N2|N1");
        Assert.Contains(pairs, p => p is "3WV.a|N2" or "N2|3WV.a");
        Assert.Contains(pairs, p => p is "3WV.b|N2" or "N2|3WV.b");
        Assert.Contains(pairs, p => p is "3WV.c|N3" or "N3|3WV.c");
    }

    [Fact]
    public void ABranchCarriesTheComponentsAndTheInteriorNodesAlongIt()
    {
        var graph = GraphFixture.Lower(GraphFixture.CoolingLoop).Graph;
        var longest = graph.Branches.MaxBy(static branch => branch.Path.Length)!;

        // Interior nodes belong in the path: they carry pressure and enthalpy unknowns, and only their
        // mass balance is subsumed by the branch owning one flow.
        Assert.Equal(
            ["PU1", "PU1__HE1", "HE1", "HE1__3WV"],
            longest.Path.Select(static part => part.Name).OrderBy(static n => n, StringComparer.Ordinal)
                .OrderBy(static n => n switch { "PU1" => 0, "PU1__HE1" => 1, "HE1" => 2, _ => 3 }));
    }

    [Fact]
    public void ABareConnectionIsABranchWithNothingAlongIt() =>
        // D-25 makes `N1 - N2` an ideal zero-drop link rather than an inferred pipe, so the branch
        // exists and its path is empty.
        Assert.Empty(
            GraphFixture.Lower(GraphFixture.CoolingLoop).Graph.Branches
                .Single(static branch => Pair(branch) is "N1|N2" or "N2|N1").Path);

    [Fact]
    public void TheCoolingLoopHasExactlyOneIndependentLoop()
    {
        var graph = GraphFixture.Lower(GraphFixture.CoolingLoop).Graph;

        Assert.Single(graph.Loops);
        Assert.Equal(
            graph.Loops.Length,
            graph.Branches.Length - graph.JunctionElements.Length + 1);
    }

    // ---- flow groups decide junction-ness, and port count does not ------------------------------

    [Fact]
    public void AThreeWayValveIsAJunctionAndAFourPortExchangerIsNot()
    {
        // The case D-63 exists for. Both have "more than two ports"; the valve has one group of three
        // and the exchanger two groups of two, and no count separates them.
        Assert.True(CircuitGraph.IsJunctionElement(new ThreeWayValve("TV1", kv: 6.3)));
        Assert.False(CircuitGraph.IsJunctionElement(new HeatExchanger("HX1", power: 1000)));

        Assert.Equal(3, new ThreeWayValve("TV1", kv: 6.3).Ports.Length);
        Assert.Equal(4, new HeatExchanger("HX1", power: 1000).Ports.Length);
    }

    [Theory]
    [InlineData(1, true)]   // A terminal: a branch has to end somewhere.
    [InlineData(2, false)]  // Interior to a branch, which owns its flow.
    [InlineData(3, true)]   // A split.
    [InlineData(4, true)]
    public void ANodeIsAJunctionUnlessItHasExactlyTwoConnections(int connections, bool expected) =>
        Assert.Equal(
            expected,
            CircuitGraph.IsJunctionElement(new CircuitNode("N", connections, carriesMassBalance: true)));

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void ANodeCarriesAMassBalanceOnTheSameRule(int connections, bool expected)
    {
        var source = $"""
            fluidscript 1
            N1 node p=300
            {string.Join("\n", Enumerable.Range(2, connections).Select(i => $"N{i} node t=40"))}

            connections
            {string.Join("\n", Enumerable.Range(2, connections).Select(i => $"N1 - N{i}"))}
            """;

        var node = GraphFixture.Lower(source).Graph.Components
            .OfType<CircuitNode>().Single(static n => n.Name == "N1");

        Assert.Equal(expected, node.CarriesMassBalance);
    }

    // ---- invariants 6 and 9 ---------------------------------------------------------------------

    [Fact]
    public void LoweringTheSameModelTwiceYieldsTheSameGraphIncludingOrdering() =>
        Assert.Equal(
            Describe(GraphFixture.Lower(GraphFixture.CoolingLoop).Graph),
            Describe(GraphFixture.Lower(GraphFixture.CoolingLoop).Graph));

    [Fact]
    public void AddingInstrumentsLeavesTheGraphByteIdentical()
    {
        // Invariant 9, and the property D-61 is worth having: a hundred sensors must not double the
        // size of the solve to compute nothing. Nothing exempts them -- an observer has no ports, and
        // the filter that drops a portless component drops it.
        var instrumented = GraphFixture.CoolingLoop + """


            TE1 t_sensor at N1
            PE1 p_sensor at N3
            FE1 flow_sensor at N2
            """;

        Assert.Equal(
            Describe(GraphFixture.Lower(GraphFixture.CoolingLoop).Graph),
            Describe(GraphFixture.Lower(instrumented).Graph));
    }

    [Fact]
    public void AControllerIsNotInTheGraphEither()
    {
        var controlled = GraphFixture.CoolingLoop + """


            TE1 t_sensor at N1
            PID1 pid kp=3
            control 3WV with TE1 by PID1 setpoint=6
            """;

        Assert.Equal(
            Describe(GraphFixture.Lower(GraphFixture.CoolingLoop).Graph),
            Describe(GraphFixture.Lower(controlled).Graph));
    }

    // ---- pipe discretization --------------------------------------------------------------------

    [Fact]
    public void APipeWithFourInternalNodesBecomesFiveSubPipesAndFourCells()
    {
        var source = """
            fluidscript 1
            N1 node t=60 p=300
            N2 node t=40
            P1 pipe length=10 dn=25 nodes=4

            connections
            N1 - P1
            P1 - N2
            """;

        var graph = GraphFixture.Lower(source).Graph;
        var cells = graph.Nodes.Where(static node => node.Origin == NodeOrigin.PipeInternal).ToArray();
        var pipes = graph.Components.OfType<Pipe>().ToArray();

        Assert.Equal(4, cells.Length);
        Assert.Equal(5, pipes.Length);

        // The five sub-pipe lengths sum to the declared length, which is the check that catches a
        // division by n rather than n+1.
        Assert.Equal(10.0, pipes.Sum(static pipe => pipe.Length), tolerance: 1e-9);

        // Each cell owns a quarter of the fluid volume; the endpoint nodes own none, because they are
        // shared with whatever else connects there.
        var volume = Math.PI * 0.0273 * 0.0273 / 4 * 10;
        Assert.All(cells, cell => Assert.Equal(volume / 4, cell.ThermalVolume, tolerance: 1e-12));
        Assert.All(
            graph.Nodes.Where(static node => node.Origin != NodeOrigin.PipeInternal),
            node => Assert.Equal(0, node.ThermalVolume));

        // One group, nine members: the source pipe itself is not among them, because it is not in the
        // graph any more.
        var group = Assert.Single(graph.Groups);
        Assert.Equal("P1", group.Source);
        Assert.Equal(9, group.Members.Length);
        Assert.DoesNotContain("P1", group.Members);
        Assert.All(group.Members, member => Assert.Contains(
            graph.Components, component => component.Name == member));
    }

    [Fact]
    public void AnExpandedPipeStillJoinsTheSameTwoNodes()
    {
        var source = """
            fluidscript 1
            N1 node t=60 p=300
            N2 node t=40
            P1 pipe length=10 dn=25 nodes=2

            connections
            N1 - P1
            P1 - N2
            """;

        var graph = GraphFixture.Lower(source).Graph;

        // Two terminals, so one branch, and every expanded part is along it: three sub-pipes and two
        // internal nodes. A rewiring that dropped an end would leave two branches or none.
        var branch = Assert.Single(graph.Branches);

        Assert.Equal(5, branch.Path.Length);
        Assert.True(Pair(branch) is "N1|N2" or "N2|N1", Pair(branch));
    }

    [Fact]
    public void APipeWithoutNodesIsNotExpanded()
    {
        var graph = GraphFixture.Lower(GraphFixture.CoolingLoop).Graph;

        Assert.Empty(graph.Groups);
        Assert.Single(graph.Components.OfType<Pipe>());
    }

    // ---- what lowering cannot build -------------------------------------------------------------

    [Fact]
    public void APipeWhoseBoreNoCatalogueKnowsIsReportedRatherThanInvented()
    {
        // DN is a designation, not a diameter, so a pipe whose designation is not in the catalogue has
        // no bore -- and inventing one from the number would be a 16 % area error that nothing in the
        // result would look wrong about (C-24).
        var source = """
            fluidscript 1
            N1 node t=60 p=300
            N2 node t=40
            P1 pipe length=10 dn=1200

            connections
            N1 - P1
            P1 - N2
            """;

        var result = GraphFixture.Lower(source);

        Assert.Equal(["P1"], result.Unresolved);
        Assert.DoesNotContain(result.Graph.Components, static c => c.Name == "P1");
    }

    [Fact]
    public void TheGraphCarriesEachComponentsCircuit()
    {
        var graph = GraphFixture.Lower(GraphFixture.CoolingLoop).Graph;

        Assert.All(
            graph.Components,
            component => Assert.Equal("cooling", graph.CircuitOf[component.Name]));
    }

    [Fact]
    public void AStaticCircuitLowersToASteadySolve() =>
        Assert.Equal(SolveMode.Steady, GraphFixture.Lower(GraphFixture.CoolingLoop).Graph.Mode);

    [Fact]
    public void AnyDynamicCircuitMakesTheWholeSolveTransient()
    {
        // The conservative direction: a steady circuit solved in time reaches its equilibrium and stays
        // there, while a transient one solved steadily loses every storage term it was written for.
        var source = """
            fluidscript 1
            circuit storage 100
            fluid dynamic water

            N1 node t=60 p=300
            N2 node t=40
            T1 tank volume=500

            connections
            N1 - T1
            T1 - N2
            """;

        Assert.Equal(SolveMode.Transient, GraphFixture.Lower(source).Graph.Mode);
    }
}
