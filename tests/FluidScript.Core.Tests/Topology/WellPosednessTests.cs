using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Tests.Topology;

/// <summary>
/// Well-posedness, from the second half of <c>plan/20-core-domain/23-topology-and-graph.md</c>: the
/// pressure datum, the counting argument, promotion, and the ten <c>FS22xx</c> codes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The counting table is the test.</strong> <c>23</c> writes the cooling loop's out by hand,
/// row by row, and balances it at 20 = 20 — including which sized parameter each stated constraint
/// promotes. A counting scheme can be wrong in a way that still balances on some circuits, so every
/// reference circuit is counted here and not just the one.
/// </para>
/// <para>
/// <strong>Two of the reference circuits do not balance, and that is the finding rather than the
/// bug.</strong> The simple loop is a closed adiabatic ring with a 30 kW source and no sink, and the
/// distribution header states three mixed inlet temperatures with two mixing valves to meet them. Both
/// come out over-specified by exactly one, each naming the statement nothing can satisfy — which is
/// what the check is for.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class WellPosednessTests
{
    /// <summary>The cooling loop exactly as <c>23</c>'s worked example writes it.</summary>
    /// <remarks>
    /// Not <see cref="GraphFixture.CoolingLoop"/>, which states <c>PU1 head</c> and <c>3WV kv</c> and
    /// no <c>in</c>/<c>out</c>. The counting table needs the document's own version: two constraints
    /// and two free parameters for them to promote, which is the pairing the 20 = 20 rests on. Only
    /// <c>dn=25</c> is added, because the catalogue that would supply it is <c>P3.5</c>.
    /// </remarks>
    private const string Documented = """
        fluidscript 1
        circuit cooling 100
        fluid water

        HE1 heat_exchanger power=30 in=20 out=50
        3WV three_way_valve
        PU1 pump
        P1  pipe length=25 dn=25

        connections
        N1 - N2
        N2 - PU1
        PU1 - HE1
        HE1 - 3WV
        3WV - N2
        3WV - P1
        P1 - N3

        N1 node t=6 p=300
        N3 node p=280
        """;

    private static WellPosednessResult Check(string source) =>
        WellPosedness.Check(GraphFixture.Lower(source).Graph);

    private static string[] Codes(WellPosednessResult result) =>
        [.. result.Diagnostics.Select(static diagnostic => diagnostic.Code)];

    // ---- the counting argument, against 23's own table -------------------------------------------

    [Fact]
    public void TheCoolingLoopCountingTableBalancesAtTwentyEqualsTwenty()
    {
        var table = Check(Documented).Counting;

        // Every row of 23's table, not just the total: a scheme that gets the total right by two
        // compensating errors is exactly what a single assertion on Excess would let through.
        Assert.Equal(4, table.BranchFlows);
        Assert.Equal(6, table.NodePressures);
        Assert.Equal(6, table.NodeEnthalpies);
        Assert.Equal(2, table.ExternalFluxes);
        Assert.Equal(2, table.Promotions.Length);

        Assert.Equal(6, table.PressureRelations);
        Assert.Equal(4, table.MassBalances);
        Assert.Equal(6, table.EnergyBalances);
        Assert.Equal(2, table.StatedPressures);
        Assert.Equal(2, table.Constraints.Length);
        Assert.Equal(0, table.Datums);

        Assert.Equal(20, table.Unknowns);
        Assert.Equal(20, table.Equations);
        Assert.Equal(0, table.Excess);
    }

    [Fact]
    public void TheCoolingLoopsTwoConstraintsPromoteTheTwoParametersTheDocumentNames()
    {
        // "Two constraints, two promotions, and they pair off exactly." The mixed inlet can only be met
        // by the mixing split and the fixed flow only by the pump, and swapping them would still count
        // to twenty while describing a different circuit.
        var table = Check(Documented).Counting;

        Assert.Equal(
            ["3WV.position<-HE1.in", "PU1.head<-HE1.out"],
            table.Promotions.Select(static p => $"{p.Label}<-{p.Constraint.Label}").ToArray());
    }

    [Fact]
    public void RemovingEitherConstraintRemovesItsUnknownToo()
    {
        // The check that the counting scheme is the right one: the equation and the unknown disappear
        // together, so the system stays square rather than swinging by one in either direction.
        var without = Check(Documented.Replace(" out=50", string.Empty, StringComparison.Ordinal));
        var table = without.Counting;

        Assert.Single(table.Constraints);
        Assert.Single(table.Promotions);
        Assert.Equal(0, table.Excess);
        Assert.True(without.CanSolve);
    }

    [Fact]
    public void TheCoolingLoopsTwoStatedPressuresProduceNoDiagnosticAtAll()
    {
        // They are boundary conditions on an open primary, not competing datums -- and they are what
        // drives flow through it. A check that reported them would fire on the project's own reference.
        Assert.Empty(Check(Documented).Diagnostics);
    }

    // ---- the datum -------------------------------------------------------------------------------

    [Fact]
    public void AClosedLoopWithNoStatedPressurePicksADatumAndSolves()
    {
        var result = Check(Ring);

        Assert.Equal(["FS2201"], Codes(result));
        Assert.Equal(0, result.Counting.Excess);
        Assert.True(result.CanSolve);
        Assert.Single(result.Hydraulics);
        Assert.False(result.Hydraulics[0].DatumWasStated);
    }

    [Fact]
    public void TheDatumIsTheSameNodeHoweverOftenTheModelIsLowered()
    {
        // Stable across edits, because every pressure in the result is reported relative to it: a datum
        // that moved when an unrelated line changed would renumber the whole pressure field.
        Assert.Equal(Check(Ring).Hydraulics[0].Datum, Check(Ring).Hydraulics[0].Datum);
    }

    private const string Ring = """
        fluidscript 1
        circuit ring
        fluid water

        PU1 pump head=6 flow=0.24
        P1  pipe length=10 dn=25

        connections
        N1 - PU1 - N2 - P1 - N1
        """;

    // ---- the substation: two hydraulic components, coupled by heat -------------------------------

    private const string Substation = """
        fluidscript 1
        circuit substation
        fluid water

        NPS node t=85 p=600
        NPR node p=350
        PCV valve
        PP  pipe length=12 dn=25

        SP   pump
        SS   pipe length=30 dn=32
        SR   pipe length=30 dn=32
        LOAD heat_exchanger power=-150 dt=20

        HX1 heat_exchanger power=150 in=40 out=60 in2=85 out2=45

        connections
        NPS - PCV - PP - HX1.in2
        HX1.out2 - NPR

        HX1.out - SS - NSUP
        NSUP - LOAD - NRET
        NRET - SR - SP - HX1.in
        """;

    [Fact]
    public void TheSubstationHasTwoHydraulicComponentsOneStatedDatumAndOnePicked()
    {
        var result = Check(Substation);

        Assert.Equal(2, result.Hydraulics.Length);
        Assert.Equal([true, false], result.Hydraulics.Select(static h => h.DatumWasStated).ToArray());
        Assert.Equal("NPS", result.Hydraulics[0].Datum);
        Assert.Equal(["FS2201"], Codes(result));
        Assert.Equal(0, result.Counting.Excess);
    }

    [Fact]
    public void TheSubstationIsNotReportedAsTwoIsolatedSubgraphs()
    {
        // D-17. The two sides share no node and no flow -- only HX1, and only thermally. Reporting that
        // as an isolated subgraph would fire on the reference circuit written to demonstrate it.
        Assert.DoesNotContain("FS2213", Codes(Check(Substation)));
    }

    [Fact]
    public void RemovingTheExchangerLeavesTwoGenuinelyIsolatedSubgraphs()
    {
        // The other half of the same check: without the shared component nothing couples the sides, and
        // FS2213 must still catch what it was written for.
        var split = Substation
            .Replace("NPS - PCV - PP - HX1.in2", "NPS - PCV - PP - NPX", StringComparison.Ordinal)
            .Replace("HX1.out2 - NPR", "NPX - NPR", StringComparison.Ordinal)
            .Replace("HX1.out - SS - NSUP", "NSX - SS - NSUP", StringComparison.Ordinal)
            .Replace("NRET - SR - SP - HX1.in", "NRET - SR - SP - NSX", StringComparison.Ordinal)
            .Replace("HX1 heat_exchanger power=150 in=40 out=60 in2=85 out2=45", string.Empty, StringComparison.Ordinal);

        Assert.Contains("FS2213", Codes(Check(split)));
    }

    [Fact]
    public void ACoupledExchangersTerminalTemperaturesAreADesignPointNotConstraints()
    {
        // D-19: once both sides are wired, in/out/in2/out2 are what 24 sizes UA from. Counting them as
        // demands on the solved state reports the substation over-specified by three.
        var table = Check(Substation).Counting;

        Assert.Equal(["LOAD.dt"], table.Constraints.Select(static c => c.Label).ToArray());
        Assert.Equal(0, table.Excess);
    }

    // ---- the storage header: a tank's ports ------------------------------------------------------

    [Fact]
    public void TheStorageHeaderDecomposesIntoFourBranchesMeetingAtTheTank()
    {
        var lowered = GraphFixture.Lower(StorageHeader);
        var result = WellPosedness.Check(lowered.Graph);
        var tank = lowered.Graph.Components.Single(static c => c.Kind == "tank");

        Assert.Equal(4, lowered.Graph.Branches.Length);
        Assert.All(
            lowered.Graph.Branches,
            branch => Assert.True(branch.From.Element == tank || branch.To.Element == tank));

        // Invariant 8: K materialized ports contribute K-1 independent pressure relations, and the tank
        // contributes a mass balance because it is a junction element.
        Assert.Equal(3, result.Counting.PressureRelations);
        Assert.True(CircuitGraph.IsJunctionElement(tank));
        Assert.Equal(5, result.Counting.MassBalances);
        Assert.Equal(0, result.Counting.Excess);
        Assert.Empty(result.Diagnostics);
    }

    private const string StorageHeader = """
        fluidscript 1
        circuit storageHeader
        fluid dynamic water

        S1 node t=60 p=300 flow=0.12
        S2 node t=45 flow=0.08
        T1 tank volume=300 layers=5 t1=25 t2=30 t3=40 t4=50 t5=60 in1_elevation=90% in2_elevation=30% out1_elevation=90% out2_elevation=30%
        RAD_NETWORK node flow=0.12
        AHU_NETWORK node flow=0.08

        connections
        S1 - T1.in1
        S2 - T1.in2
        T1.out1 - RAD_NETWORK
        T1.out2 - AHU_NETWORK
        """;

    // ---- over- and under-specification ------------------------------------------------------------

    [Fact]
    public void AConstraintWithNothingToPromoteIsAnOverSpecificationThatNamesIt()
    {
        // The simple loop: a closed ring with a 30 kW source and no sink. Continuity forces its inlet
        // and outlet enthalpies equal, so no parameter anywhere can make the inlet 20 degrees while the
        // outlet is 50 -- and there is no mixing valve for `in` to promote. Letting it fall back to the
        // valve's kv would square the count and report a circuit with no solution as solvable.
        var result = Check("""
            fluidscript 1
            circuit simpleLoop
            fluid water

            HE1 heat_exchanger power=30 in=20 out=50
            CV1 valve
            PU1 pump
            P1  pipe length=25 dn=25

            connections
            N1 - PU1 - N2 - HE1 - N3 - CV1 - N4 - P1 - N1
            """);

        Assert.Equal(1, result.Counting.Excess);
        Assert.False(result.CanSolve);

        var reported = result.Diagnostics.Single(static d => d.Code == "FS2210");
        Assert.Contains("over-specified by 1", reported.Message, StringComparison.Ordinal);
        Assert.Contains("HE1.in", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnderSpecifiedTableCountsAsOneEvenThoughNoScriptReachesIt()
    {
        // Hand-built, because with the equation set 23 specifies the structural terms cancel identically
        // for every graph tried, and no script has been found that under-specifies -- S-8 in
        // plan/30-solver/defects.md, which is a finding about 23 rather than about this code. The
        // arithmetic still has to be right in both directions, and the sign is what picks the code.
        var table = new CountingTable
        {
            BranchFlows = 1,
            NodePressures = 2,
            NodeEnthalpies = 2,
            ExternalFluxes = 1,
            Promotions = [],

            // Zero, as a branch whose only component the catalogue could not resolve would leave it.
            PressureRelations = 0,
            MassBalances = 2,
            EnergyBalances = 2,
            StatedPressures = 0,
            Constraints = [],
            Datums = 0,
        };

        Assert.Equal(6, table.Unknowns);
        Assert.Equal(4, table.Equations);
        Assert.Equal(-2, table.Excess);
    }

    [Fact]
    public void AComponentTheCatalogueCannotResolveIsReportedRatherThanThrown()
    {
        // Dropping a component takes its connections with it, which leaves a branch ending somewhere
        // that is not a vertex of the branch graph. Indexing that end threw -- on a script that is
        // merely incomplete, which no stage may do.
        var lowered = GraphFixture.Lower("""
            fluidscript 1
            circuit broken
            fluid water

            PU1 pump head=6 flow=0.24
            P1  pipe length=10 dn=999

            connections
            N1 - PU1 - N2 - P1 - N1
            """);

        Assert.Equal(["P1"], lowered.Unresolved);
        Assert.Empty(lowered.Graph.Loops);

        var result = WellPosedness.Check(lowered.Graph);

        Assert.DoesNotContain("FS2210", Codes(result));
    }

    // ---- the remaining codes ---------------------------------------------------------------------

    [Fact]
    public void TwoPressuresAnIdealLinkForcesEqualAreCompetingDatums()
    {
        // D-25 makes a bare connection a zero-drop link, so nothing between these two can develop a
        // pressure difference and the second is not a boundary condition at all.
        Assert.Contains("FS2212", Codes(Check("""
            fluidscript 1
            circuit twoDatums
            fluid water

            PU1 pump head=6 flow=0.24

            connections
            N1 - N2
            N2 - PU1 - N3
            N3 - N1

            N1 node p=300
            N2 node p=280
            """)));
    }

    [Fact]
    public void ALoopWithNothingToDriveItIsReported()
    {
        // A warning rather than information: the loop simply carries no flow, and every temperature
        // downstream of it is then wrong in a way that still looks like a solved circuit.
        var result = Check("""
            fluidscript 1
            circuit passive
            fluid water

            P1 pipe length=10 dn=25
            P2 pipe length=10 dn=25

            connections
            N1 - P1 - N2 - P2 - N1
            """);

        Assert.Contains("FS2214", Codes(result));
        Assert.True(result.CanSolve);
    }

    [Fact]
    public void ALoopWithAPumpOnItIsNotReported() =>
        Assert.DoesNotContain("FS2214", Codes(Check(Ring)));

    [Fact]
    public void AStatedBoundaryOutsideTheSubstancesRangeIsReported()
    {
        var reported = Check("""
            fluidscript 1
            circuit hot
            fluid water

            PU1 pump head=6 flow=0.24
            P1  pipe length=10 dn=25

            connections
            N1 - PU1 - N2 - P1 - N1

            N1 node t=500 p=300
            """).Diagnostics.Single(static d => d.Code == "FS2215");

        Assert.Contains("500", reported.Message, StringComparison.Ordinal);
        Assert.Equal("N1", reported.ComponentName);
    }

    [Fact]
    public void ATwoSidedComponentWithNoHeatDirectionSaysWhichCircuitItLandedIn()
    {
        // D-36: the owner is the circuit on the side losing nominal enthalpy. With no terminal
        // temperatures there is nothing to read that off, and the fallback is reported rather than
        // silent because the diagram groups by circuit.
        var result = Check("""
            fluidscript 1
            circuit pair
            fluid water

            HX1 heat_exchanger power=10
            PA  pump head=6 flow=0.24
            PB  pump head=6 flow=0.24

            connections
            NA1 - PA - NA2 - HX1.in
            HX1.out - NA1
            NB1 - PB - NB2 - HX1.in2
            HX1.out2 - NB1
            """);

        var reported = result.Diagnostics.Single(static d => d.Code == "FS2216");

        Assert.Equal("HX1", reported.ComponentName);
        Assert.Equal(2, result.Hydraulics.Length);
    }

    // ---- determinism -----------------------------------------------------------------------------

    [Fact]
    public void CheckingTheSameGraphTwiceReportsTheSameThings()
    {
        var graph = GraphFixture.Lower(Substation).Graph;

        Assert.Equal(
            Codes(WellPosedness.Check(graph)),
            Codes(WellPosedness.Check(graph)));
    }

    [Fact]
    public void CheckingAGraphChangesNothingAboutIt()
    {
        // Invariant 3, one level up: the pass is a pure function of the graph, so a caller may run it,
        // report, and then hand the same graph to the solver.
        var graph = GraphFixture.Lower(Documented).Graph;
        var before = graph.Components.Select(static c => c.Name).ToArray();

        WellPosedness.Check(graph);

        Assert.Equal(before, graph.Components.Select(static c => c.Name).ToArray());
    }

    // ---- every sample in the corpus --------------------------------------------------------------

    [Fact]
    public void EverySampleIsCountedAndTheOnesThatDoNotBalanceAreTheKnownOnes()
    {
        // 23 asks that the counting check pass for every sample. Three cannot be counted at all yet and
        // two do not balance, and every one of those five is a finding about the sample or about the
        // package order rather than about the check -- F-8, F-9 and C-24. Recording the whole sweep
        // rather than the exceptions is what makes a sixth one visible the day it appears.
        var outcomes = FluidScript.Fixtures.ScriptCorpus.Samples()
            .ToDictionary(
                static sample => Path.GetFileName(sample.Name),
                static sample => Excess(sample.Text),
                StringComparer.Ordinal);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Not a solvable circuit by design, and its own header says so: PU1 appears in no
                // connection, so nothing is left to absorb `HE1 out=50`.
                ["m1-syntax-reference.fluid"] = "1",

                // A pipe with no `dn` cannot be built until the catalogue lands in P3.5 (C-24), and
                // dropping it takes its connections with it. Three samples wait on that.
                ["m1-syntax-tour.fluid"] = "unresolved PB1",
                ["m2-cooling-loop.fluid"] = "unresolved P1",
                ["m2-simple-loop.fluid"] = "unresolved P1",

                // Three exchangers each demanding a mixed inlet temperature, two mixing valves to meet
                // them, and a return temperature of 40 C from two loads that both return 30 C (F-9).
                ["m2-distribution-header.fluid"] = "1",

                ["m2-substation.fluid"] = "0",
                ["m4-storage-header.fluid"] = "0",
            },
            outcomes);
    }

    /// <summary>How far one script's counting table is from square, or why it could not be counted.</summary>
    private static string Excess(string source)
    {
        var bound = new FluidScript.Core.Binding.Binder(FluidScript.Core.Language.ComponentRegistry.Default)
            .Bind(FluidScript.Core.Syntax.FluidScriptParser.Parse(new FluidScript.Core.Syntax.SourceText(source)), "sample");

        if (bound.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
        {
            return "does not bind";
        }

        var lowered = Lowering.Lower(
            bound.Model,
            FluidScript.Core.Fluids.ConstantPropertyWater.Instance,
            new ComponentFactory(new ReferenceBores()));

        var table = WellPosedness.Check(lowered.Graph).Counting;

        return lowered.Unresolved.Length > 0
            ? $"unresolved {string.Join(",", lowered.Unresolved)}"
            : table.Excess.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
