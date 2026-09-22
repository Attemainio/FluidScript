using FluidScript.Fixtures;
using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Fluids;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;

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
/// <strong>Every reference circuit balances now, and two of them did not.</strong> The simple loop was
/// a closed adiabatic ring with a 30 kW source and no sink, and the distribution header asked its
/// source for a 40 °C return from two loads that both return 30 °C. Finding those is what this pass is
/// for; <c>P3.4c</c> fixed the circuits and both are asserted here at zero.
/// </para>
/// <para>
/// <strong>Squareness is not consistency, and <c>FS2203</c> and <c>FS2204</c> are the difference.</strong>
/// A closed loop with a source and no sink counts 0 = 0 and has no solution, because summing its
/// energy balances gives <c>Σ Q̇ = 0</c> against duties that do not. No arrangement of the counting
/// argument reaches that, which is why both codes are checked separately and tested against a square
/// circuit rather than a lopsided one.
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

        HE1 heat_exchanger power=30 in.t=20 out.t=50
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

        N1 inlet t=6 p=300
        N3 outlet p=280
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
            ["3WV.position<-HE1.in.t", "PU1.head<-HE1.out.t"],
            table.Promotions.Select(static p => $"{p.Label}<-{p.Constraint.Label}").ToArray());
    }

    [Fact]
    public void RemovingEitherConstraintRemovesItsUnknownToo()
    {
        // The check that the counting scheme is the right one: the equation and the unknown disappear
        // together, so the system stays square rather than swinging by one in either direction.
        var without = Check(Documented.Replace(" out.t=50", string.Empty, StringComparison.Ordinal));
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

        N1 node t=60
        """;

    // ---- the substation: two hydraulic components, coupled by heat -------------------------------

    private const string Substation = """
        fluidscript 1
        circuit substation
        fluid water

        NPS inlet t=85 p=600
        NPR outlet p=350
        PCV valve
        PP  pipe length=12 dn=25

        SP   pump
        SS   pipe length=30 dn=32
        SR   pipe length=30 dn=32
        LOAD heat_exchanger power=-150 dt=20

        HX1 heat_exchanger power=150 in.t=40 out.t=60 in[2].t=85 out[2].t=45

        connections
        NPS - PCV - PP - HX1.in[2]
        HX1.out[2] - NPR

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
    public void AClosedCircuitsPickedDatumIsThePumpSuction()
    {
        // D-98. The most-connected node (`NSUP`, 23's old rule) is downstream of the pump, so putting it at
        // zero gauge put the suction 21 kPa *below* zero -- and water's validated range starts at 100 kPa
        // absolute, so the first property read there failed and the substation was NonFinite before
        // Newton took a step. The suction is the loop's low point; a datum there keeps every other node
        // above it, which is what a datum at zero gauge has to do to stay inside the property domain.
        var result = Check(Substation);

        Assert.Equal("SR__SP", result.Hydraulics[1].Datum);
        Assert.Contains("SR__SP", Assert.Single(result.Diagnostics, static d => d.Code == "FS2201").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSubstationIsNotReportedAsTwoIsolatedSubgraphs()
    {
        // D-17. The two sides share no node and no flow -- only HX1, and only thermally. Reporting that
        // as an isolated subgraph would fire on the reference circuit written to demonstrate it.
        Assert.DoesNotContain("FS2213", Codes(Check(Substation)));
    }

    [Fact]
    public void TwoIndependentCircuitsInOneProjectEachSolveOnTheirOwn()
    {
        // `C-93`, `D-132`. Two `circuit` blocks joined by nothing are two systems: each counts with its
        // own datum (one stated, one picked) and its own dropped level, and FS2213 only says so.
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Root, "tests", "FluidScript.Core.Tests", "Layout", "Ladder", "step-11a-two-loops.fluid"));
        var result = Check(source);

        Assert.True(result.CanSolve, string.Join("; ", Codes(result)));
        Assert.Equal(2, result.Hydraulics.Length);
        Assert.Contains("FS2213", Codes(result));
        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RemovingTheExchangerLeavesTwoGenuinelyIsolatedSubgraphs()
    {
        // The other half of the same check: without the shared component nothing couples the sides, and
        // FS2213 still names them -- as information now (`D-132`), since each side is a system of its own.
        var split = Substation
            .Replace("NPS - PCV - PP - HX1.in[2]", "NPS - PCV - PP - NPX", StringComparison.Ordinal)
            .Replace("HX1.out[2] - NPR", "NPX - NPR", StringComparison.Ordinal)
            .Replace("HX1.out - SS - NSUP", "NSX - SS - NSUP", StringComparison.Ordinal)
            .Replace("NRET - SR - SP - HX1.in", "NRET - SR - SP - NSX", StringComparison.Ordinal)
            .Replace("HX1 heat_exchanger power=150 in.t=40 out.t=60 in[2].t=85 out[2].t=45", string.Empty, StringComparison.Ordinal);

        Assert.Contains("FS2213", Codes(Check(split)));
    }

    [Fact]
    public void TheCoupledExchangerLiesOnTwoBranchesAndIsNeitherAJunctionNorABalance()
    {
        // 23's criterion for D-17, and the one that separates an exchanger from a three-way valve: both have
        // more than two ports, but the exchanger's are two groups of two, so it is interior to a branch of
        // each side, appears in both paths, and carries no mass balance -- the substation's two balances
        // are NPS's and NPR's, the primary's boundaries.
        var graph = GraphFixture.Lower(Substation).Graph;
        var exchanger = graph.Components.OfType<HeatExchanger>().Single(static x => x.Name == "HX1");

        Assert.DoesNotContain(exchanger, graph.JunctionElements);
        Assert.Equal(2, graph.Branches.Count(branch => branch.Path.Contains(exchanger)));
        Assert.Equal(2, WellPosedness.Check(graph).Counting.MassBalances);
    }

    [Fact]
    public void ACoupledExchangersTerminalTemperaturesAreADesignPointNotConstraints()
    {
        // D-19: once both sides are wired, in/out/in2/out2 are what 24 sizes UA from. Counting them as
        // demands on the solved state reports the substation over-specified by three. D-97 keeps one of
        // them as a flow pin: the primary loop has no other constraint on its flow, so the design point
        // 85/45 at 150 kW is what fixes it, and `HX1.out[2].t` is that pin -- a derived-flow row, not a
        // temperature demand. The counting table labels a constraint by the model's parameter key
        // (`out2`), not the script spelling; the report's spelling is P5.13b's (L-56).
        var table = Check(Substation).Counting;

        // The label is the script's spelling, the record's `Parameter` the key `out2` (`L-56`).
        Assert.Equal(["LOAD.dt", "HX1.out[2].t"], table.Constraints.Select(static c => c.Label).ToArray());
        Assert.Equal(["dt", "out2"], table.Constraints.Select(static c => c.Parameter).ToArray());
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

        // Four of the five balances, because every boundary states the flow crossing it: with no
        // external flux left unknown, summing them gives an identity and one is implied by the rest.
        Assert.Equal(4, result.Counting.MassBalances);
        Assert.False(result.Hydraulics[0].HasUnknownFlux);
        Assert.Equal(0, result.Counting.Excess);

        // No pressure is stated anywhere -- every boundary states a flow instead -- so the datum is
        // picked and said so, and that one informational line is the whole diagnostic set.
        Assert.Equal(["FS2201"], Codes(result));
    }

    private const string StorageHeader = """
        fluidscript 1
        circuit storageHeader
        fluid dynamic water

        S1 inlet t=60 flow=0.12
        S2 inlet t=45 flow=0.08
        T1 tank volume=300 layers=5 layer[1].t=25 layer[2].t=30 layer[3].t=40 layer[4].t=50 layer[5].t=60 in.level=90% in[2].level=30% out.level=90% out[2].level=30%
        RAD_NETWORK outlet flow=0.12
        AHU_NETWORK outlet flow=0.08

        connections
        S1 - T1.in
        S2 - T1.in[2]
        T1.out - RAD_NETWORK
        T1.out[2] - AHU_NETWORK
        """;

    // ---- over- and under-specification ------------------------------------------------------------

    [Fact]
    public void AConstraintWithNothingToPromoteIsAnOverSpecificationThatNamesIt()
    {
        // The balanced ring with one temperature too many. `HE1 in.t=20` is its enthalpy datum -- the one
        // absolute value a closed circuit's difference relations cannot supply -- and `N1 t=20` demands
        // the same level a second time. Nothing is free to meet it: there is no mixing valve for either
        // to promote, and letting one fall back to the valve's kv would square the count by moving a
        // parameter that changes no temperature.
        var result = Check(BalancedRing.Replace(
            "circuit ring", "circuit simpleLoop", StringComparison.Ordinal) + "\nN1 node t=20\n");

        Assert.Equal(1, result.Counting.Excess);
        Assert.False(result.CanSolve);

        var reported = result.Diagnostics.Single(static d => d.Code == "FS2210");
        Assert.Contains("over-specified by 1", reported.Message, StringComparison.Ordinal);
        Assert.Contains("HE1.in", reported.Message, StringComparison.Ordinal);
        Assert.Contains("N1.t", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2210_AStatedPressureOnADeadEndNodeIsToldToWriteOutlet()
    {
        // The cooling loop with its return written `N3 node p=280` instead of `N3 outlet p=280` (C-106).
        // Since D-86 a node's p= is a datum that passes no mass, so the return branch carries zero and
        // N1's and N3's pressures both set the level of one hydraulic: over-specified by one. The old
        // list named nothing -- its filter on the mass balance excluded every node once a boundary
        // carried one -- and the fix a user needs is a word, not a removal.
        var script = FluidScript.Fixtures.ScriptCorpus.Samples()
            .Single(static s => s.Name.EndsWith("m2-cooling-loop.fluid", StringComparison.Ordinal)).Text
            .Replace("N3 outlet p=280", "N3 node p=280", StringComparison.Ordinal);
        var result = Check(script);

        Assert.Equal(1, result.Counting.Excess);

        var reported = result.Diagnostics.Single(static d => d.Code == "FS2210");
        Assert.Contains("N1.p, N3.p", reported.Message, StringComparison.Ordinal);
        Assert.Contains("write 'N3 outlet'", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFlowNothingOnItsBranchCanChangeIsToldToAddAValve()
    {
        // C-28, 23's promotion rules: two parallel loads each fix their branch flow by power and dt, and
        // neither branch holds a valve to absorb it. "Remove one of" is the wrong advice for a flow
        // the designer wants; the second sentence says what is missing.
        var result = Check("""
            fluidscript 1
            circuit parallel
            fluid water

            HS1  heat_exchanger power=50 out.t=70
            PU1  pump
            RAD1 load power=30 dt=20
            RAD2 load power=20 dt=20

            connections
            N1 - PU1 - N2 - HS1 - N3
            N3 - RAD1 - N4
            N3 - RAD2 - N4
            N4 - N1
            """);

        Assert.False(result.CanSolve);
        var reported = result.Diagnostics.Single(static d => d.Code == "FS2210");
        Assert.Contains(", or add a valve: nothing on the branch through", reported.Message, StringComparison.Ordinal);
        Assert.Contains("RAD", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStatedPumpHeadConstrainsRatherThanSeedsAndTheBalancingValveAbsorbsIt()
    {
        // M2a, `R-02`: `head=15` on the simple loop, whose ring needs 5.28 m. A stated value is never
        // promoted, so the duty's flow constraint that would have taken the head has to go somewhere
        // else -- and `23`'s rule sends it to the first free valve on the branch, which is what a
        // balancing valve is for. `C-75`: until `D-96` the bootstrap Kv counted as decided, so the
        // constraint found nothing, `FS2210` fired and named `HE1.in`/`HE1.out` -- the exchanger's own
        // duty temperatures -- as the things to remove.
        var result = Check("""
            fluidscript 1
            circuit simpleLoop
            fluid water

            HE1  heat_exchanger power=30 in.t=20 out.t=50
            LOAD heat_exchanger power=-30 dp=0
            CV1  valve
            PU1  pump head=15
            P1   pipe length=25

            connections
            N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5 - P1 - N1
            """);

        Assert.DoesNotContain(result.Counting.Promotions, static p => p.Label == "PU1.head");

        var promotion = Assert.Single(result.Counting.Promotions, static p => p.Constraint.Kind == ConstraintKind.FixedFlow);
        Assert.Equal("CV1.kv", promotion.Label);
        Assert.True(result.CanSolve);
        Assert.DoesNotContain("FS2210", Codes(result));
    }

    [Fact]
    public void AProvisionalIsFreeAndARuleSizedValueIsNot()
    {
        // `D-96` in one line each way. The graph `Check` lowers comes from the outer loop's bootstrap, so
        // `CV1.kv` is a provisional there and counts as free; a graph whose overlay carries a rule's
        // choice for the same parameter -- the second lowering of any run -- does not offer it.
        const string script = """
            fluidscript 1
            circuit simpleLoop
            fluid water

            HE1  heat_exchanger power=30 in.t=20 out.t=50
            LOAD heat_exchanger power=-30 dp=0
            CV1  valve
            PU1  pump head=15
            P1   pipe length=25 dn=25

            connections
            N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5 - P1 - N1
            """;

        var bootstrapped = Check(script);
        Assert.Contains("CV1.kv", bootstrapped.Counting.Promotions.Select(static p => p.Label));

        var chosen = SizingOverlay.Empty.With("CV1", "kv", Quantity.FromSi(1.6, Dimension.Kv));
        var lowered = Lowering.Lower(
            GraphFixture.Bind(script), ConstantPropertyWater.Instance, new ComponentFactory(GraphFixture.Bores(), chosen));
        var sized = WellPosedness.Check(lowered.Graph);

        Assert.Empty(lowered.Graph.ProvisionalParameters);
        Assert.DoesNotContain("CV1.kv", sized.Counting.Promotions.Select(static p => p.Label));
        Assert.False(sized.CanSolve);
        Assert.Contains("FS2210", Codes(sized));
    }

    [Fact]
    public void AnUnderSpecifiedTableCountsAsOneEvenThoughNoScriptReachesIt()
    {
        // Hand-built, to check the arithmetic in both directions at a magnitude no script produces. A
        // script does reach FS2211 now -- a closed circuit whose temperature level nothing fixes, which
        // is a test of its own below -- but only ever by one, since the enthalpy datum is one per
        // component.
        var table = new CountingTable
        {
            BranchFlows = 1,
            NodePressures = 2,
            NodeEnthalpies = 2,
            FluxNodes = [Dummy("N1")],
            Promotions = [],
            ComponentUnknowns = [],
            ControlVolumeBalances = 0,

            // Zero, as a branch whose only component the catalogue could not resolve would leave it.
            PressureRelations = 0,
            IdealLinks = [],
            MassBalances = 2,
            EnergyBalances = 2,
            PressureNodes = [],
            Constraints = [],
            DatumComponents = [],
            LevelComponents = [],
        };

        Assert.Equal(6, table.Unknowns);
        Assert.Equal(4, table.Equations);
        Assert.Equal(-2, table.Excess);
    }

    /// <summary>A node with nothing behind it, for a table built to check arithmetic.</summary>
    /// <param name="name">The node's name.</param>
    /// <returns>A node that carries a mass balance and no state worth reading.</returns>
    private static GraphNode Dummy(string name) => new()
    {
        Name = name,
        Component = new CircuitNode(name, portCount: 1, carriesMassBalance: true),
        Origin = NodeOrigin.Declared,
        ThermalVolume = 0,
    };

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

            N1 node t=60
            """);

        Assert.Contains("FS2214", Codes(result));
        Assert.True(result.CanSolve);
    }

    // ---- FS2203 and FS2204: consistency, which no count can see -----------------------------------

    /// <summary>A closed ring with a 30 kW source and no sink.</summary>
    private const string SourceRing = """
        fluidscript 1
        circuit ring
        fluid water

        HE1 heat_exchanger power=30 in.t=20 out.t=50
        CV1 valve
        PU1 pump
        P1  pipe length=25 dn=25

        connections
        N1 - PU1 - N2 - HE1 - N3 - CV1 - N4 - P1 - N1
        """;

    /// <summary>The same ring with the load it was missing.</summary>
    private const string BalancedRing = """
        fluidscript 1
        circuit ring
        fluid water

        HE1  heat_exchanger power=30 in.t=20 out.t=50
        LOAD load power=30
        CV1  valve
        PU1  pump
        P1   pipe length=25 dn=25

        connections
        N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5 - P1 - N1
        """;

    [Fact]
    public void AClosedCircuitWhoseHeatDoesNotBalanceIsReported()
    {
        var result = Check(SourceRing);
        var reported = result.Diagnostics.Single(static d => d.Code == "FS2203");

        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);
        Assert.Contains("30 kW", reported.Message, StringComparison.Ordinal);

        // The whole reason the code exists: the system is square and has no solution, so nothing in the
        // counting argument could have found this and no promotion would have helped.
        Assert.Equal(0, result.Counting.Excess);
    }

    [Fact]
    public void TheSameRingWithItsLoadIsAccepted()
    {
        var result = Check(BalancedRing);

        Assert.DoesNotContain("FS2203", Codes(result));
        Assert.Equal(0, result.Counting.Excess);
        Assert.True(result.CanSolve);
    }

    [Fact]
    public void ATransientIsNotHeldToTheSteadyBalance() =>
        // The same circuit warming up, which is what the storage term is for. A check that fired here
        // would reject every warm-up study there is.
        Assert.DoesNotContain(
            "FS2203",
            Codes(Check(SourceRing.Replace("fluid water", "fluid dynamic water", StringComparison.Ordinal))));

    [Fact]
    public void ACoupledExchangerIsAHeatPathOutOfAClosedCircuit() =>
        // The substation's secondary is closed and holds HX1 and a load of equal and opposite duty. The
        // sign of a coupled duty is not readable from the graph, so both readings are tried and one of
        // them balances -- which is what stops this reporting the project's own reference circuit.
        Assert.DoesNotContain("FS2203", Codes(Check(Substation)));

    [Fact]
    public void ASupplyWithNothingToReturnThroughIsReported()
    {
        var reported = Check("""
            fluidscript 1
            circuit probe
            fluid water

            S1  inlet t=60 flow=0.2
            HE1 heat_exchanger power=-30
            PU1 pump

            connections
            S1 - PU1 - N2 - HE1 - N3
            """).Diagnostics.Single(static d => d.Code == "FS2204");

        Assert.Contains("inlet", reported.Message, StringComparison.Ordinal);
        Assert.Contains("outlet", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASupplyPairedWithAReturnIsNotReported()
    {
        var result = Check("""
            fluidscript 1
            circuit probe
            fluid water

            S1  inlet t=60 flow=0.2
            R1  outlet
            HE1 heat_exchanger power=-30
            PU1 pump

            connections
            S1 - PU1 - N2 - HE1 - N3 - R1
            """);

        Assert.DoesNotContain("FS2204", Codes(result));
        Assert.Equal(0, result.Counting.Excess);
    }

    [Fact]
    public void AnUnpairedInletIsReportedForItsOwnFragmentOnly()
    {
        // D-115's fourth point, verified: the check runs per hydraulic component, so a closed loop in
        // the same project is neither blamed nor excused for the open fragment beside it.
        var result = Check("""
            fluidscript 1
            circuit open
            fluid water

            S1  inlet t=60 flow=0.2
            HE1 heat_exchanger power=-30
            PU1 pump

            connections
            S1 - PU1 - N2 - HE1 - N3

            circuit closed
            fluid water

            HS2 heat_exchanger power=30 out.t=60
            HE2 heat_exchanger power=-30
            PU2 pump
            N4 node p=150 t=40

            connections
            N4 - PU2 - HS2 - HE2 - N4
            """);

        var reported = Assert.Single(result.Diagnostics, static d => d.Code == "FS2204");

        Assert.Contains("open", reported.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("closed", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABoundaryWithTwoConnectionsIsReported()
    {
        // D-115: a boundary is a terminal with one connection; the split belongs to a node after it.
        var reported = Check("""
            fluidscript 1
            circuit probe
            fluid water

            S1  inlet t=60 p=300
            R1  outlet p=280
            HE1 heat_exchanger power=-20
            HE2 heat_exchanger power=-10

            connections
            S1 - HE1 - R1
            S1 - HE2 - R1
            """).Diagnostics.Where(static d => d.Code == "FS2205").ToList();

        Assert.Equal(2, reported.Count);
        Assert.Contains(reported, static d => d.Message.Contains("'S1' is an inlet with 2 connections", StringComparison.Ordinal));
        Assert.Contains(reported, static d => d.Message.Contains("'R1' is an outlet with 2 connections", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoStatedPressuresAreACompletePairAndNeedNoBoundaryKind() =>
        // A circuit with neither kind is never reported: a closed loop needs neither, and the cooling
        // loop's two pressures are a complete pair of boundary conditions written the older way.
        Assert.DoesNotContain("FS2204", Codes(Check(Documented)));

    // ---- the enthalpy datum ----------------------------------------------------------------------

    [Fact]
    public void AClosedCircuitCarriesOneEnthalpyLevelItCannotDetermine()
    {
        // The temperature analogue of the pressure datum, and the reason it is counted rather than
        // picked: every relation in a closed circuit is a difference, so one absolute value is missing
        // and only the script can supply it.
        var result = Check(BalancedRing);

        Assert.Equal(1, result.Counting.EnthalpyLevels);
        Assert.Equal(0, result.Counting.Excess);
    }

    [Fact]
    public void AClosedCircuitWithNoTemperatureAnywhereIsUnderSpecified()
    {
        // S-8's missing case. With every stated temperature gone the difference relations are all that
        // is left and the level floats -- which the count now sees, and reports as the thing to add
        // rather than as a pressure.
        var result = Check("""
            fluidscript 1
            circuit ring
            fluid water

            PU1 pump head=6 flow=0.24
            P1  pipe length=10 dn=25

            connections
            N1 - PU1 - N2 - P1 - N1
            """);

        var reported = result.Diagnostics.Single(static d => d.Code == "FS2211");

        Assert.Equal(-1, result.Counting.Excess);
        Assert.Contains("a temperature on N1", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStatedTerminalThatPinsAFlowDoesNotSilenceTheMissingTemperature()
    {
        // S-52, D-90: a component can state temperatures and still hold no level. `in.t=40` is a mixed
        // inlet the three-way valve's position answers; `out.t=70` with `power` pins the flow and the
        // pump's head absorbs it. Both statements are matched, so nothing pays for the dropped level
        // and the circuit is short by one on the thermal side. The advice used to scan for any stated
        // temperature, find these two, and offer a pressure to a hydraulic half that was already
        // square -- advice that made the header square and singular when taken. Read from the
        // constraint list, it leads with the temperatures and keeps the pressure as the last resort.
        var result = Check("""
            fluidscript 1
            circuit ring
            fluid water

            PU1 pump
            HS1 heat_exchanger power=50 in.t=40 out.t=70
            LD1 load power=50
            3WV three_way_valve
            P1  pipe length=10 dn=25

            connections
            N1 - PU1 - N2 - HS1 - N3 - LD1 - N4 - 3WV - N1
            N3 - P1 - 3WV
            """);

        var reported = result.Diagnostics.Single(static d => d.Code == "FS2211");

        Assert.Equal(-1, result.Counting.Excess);
        Assert.StartsWith("This circuit is under-specified by 1. Add one of: a temperature on N1", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOpenCircuitHasNoSuchLevel() =>
        // Mass arrives carrying an enthalpy, and that enthalpy is the level. Nothing has to be stated
        // for it, which is why the cooling loop states no temperature outside its supply.
        Assert.Equal(0, Check(Documented).Counting.EnthalpyLevels);

    [Fact]
    public void ATransientNeedsNoEnthalpyDatumEither() =>
        // It starts from an initial state, which fixes the level before the first step.
        Assert.Equal(
            0,
            Check(BalancedRing.Replace("fluid water", "fluid dynamic water", StringComparison.Ordinal))
                .Counting.EnthalpyLevels);

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
    public void ATwoSidedComponentInOneCircuitIsTwoHydraulicsAndNoOwnershipQuestion()
    {
        // D-36's second fallback: both sides in one circuit is that circuit, with nothing to report.
        // Ownership itself is the binder's (`OwnershipTests`); what the partition owes is the two
        // hydraulics that make it a question at all. Until P4.3 this circuit raised FS2216 from here,
        // for a fallback the check did not apply.
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
            NB1 - PB - NB2 - HX1.in[2]
            HX1.out[2] - NB1
            """);

        Assert.DoesNotContain("FS2216", Codes(result));
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
        // 23 asks that the counting check pass for every sample, and every M2 and M4 one now does. The
        // two that do not are the syntax files, which are not plant: one is deliberately unsolvable and
        // says so in its own header, and the other is a tour of productions. Recording the whole sweep
        // rather than the exceptions is what makes a third one visible the day it appears.
        var outcomes = FluidScript.Fixtures.ScriptCorpus.Samples()
            .ToDictionary(
                static sample => Path.GetFileName(sample.Name),
                static sample => Excess(sample.Text),
                StringComparer.Ordinal);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Not a solvable circuit by design, and its own header says so: PU1 appears in no
                // connection, so nothing is left to absorb `HE1 out.t=50`.
                ["m1-syntax-reference.fluid"] = "1",

                // PB1 used to read "unresolved": a pipe with no `dn` had no bore and so was not built,
                // and dropping it took its connections with it (C-24). P3.7b's outer loop chooses the
                // diameter, so the graph is complete for the first time and what is left is the tour
                // being a tour -- productions' worth of components that no circuit closes. Three since
                // D-115 wired its expressions circuit through junctions.
                // 4 since D-133: `TV2` mixes `SB1` with water tapped *from* `NJ1`, so its stream is `RB1` and
                // it never held `HE2`'s inlet; the promotion that made it 3 was the reach S-45 was filed for.
                // 5 since D-141: the tour's `control ... measure=NJ2.t ... setpoint=20` is a constraint on
                // NJ2's temperature now, and its actuator TV3.position is already HE3.in.t's, so nothing
                // is left to hold it -- the tour states both, which a plant would not.
                ["m1-syntax-tour.fluid"] = "5",

                ["m2-cooling-loop.fluid"] = "0",
                ["m2-simple-loop.fluid"] = "0",
                // `D-91` and the injection-header rewrite: positive load capacities lower to
                // negative core duties and the prepared model is square. Newton still finds a zero pivot,
                // so the remaining defect is rank rather than a missing equation.
                ["m2-distribution-header.fluid"] = "0",
                ["m2-substation.fluid"] = "0",
                ["m4-storage-header.fluid"] = "0",
                // Square, and not solvable as written: the control line's setpoint is not yet a
                // constraint, so the valve defaults to fully open and `HE1.out.t`'s promotion runs the
                // pump away (S-75, D-141). Counting is right; the constraint source is what P6.0 adds.
                ["m4-demand-step.fluid"] = "0",
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

        // Through the same prepare step a solve takes, because a pipe with no chosen diameter has no
        // bore and so is not built at all -- counting a graph short a component would report a shape
        // nobody solves (`P3.7b`).
        var lowered = GraphFixture.Lower(source);

        var table = WellPosedness.Check(lowered.Graph).Counting;

        return lowered.Unresolved.Length > 0
            ? $"unresolved {string.Join(",", lowered.Unresolved)}"
            : table.Excess.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void AFlowConstraintHeldByAPumpOffItsOwnBranchSaysSo()
    {
        // `S-45`'s original measurement, now reported rather than silent: with `PU_AHU`'s head stated,
        // `HE_AHU`'s flow constraint has no free pump on its branch and takes the radiator's. The reach is
        // allowed -- a shared upstream pump is claimed the same way and is right to be -- so this is a
        // warning naming the claim, not a refusal. The sample as written reaches nowhere and says nothing.
        var sample = FluidScript.Fixtures.ScriptCorpus.Samples()
            .Single(static candidate => candidate.Name.EndsWith("m2-distribution-header.fluid", StringComparison.Ordinal))
            .Text;

        Assert.DoesNotContain("FS2218", Codes(Check(sample)));

        var stated = sample.Replace("PU_AHU  pump", "PU_AHU  pump head=6", StringComparison.Ordinal);
        var result = Check(stated);
        var reach = Assert.Single(result.Diagnostics, static d => d.Code == "FS2218");

        Assert.Contains("HE_AHU.out", reach.Message, StringComparison.Ordinal);
        Assert.Contains("PU_RAD", reach.Message, StringComparison.Ordinal);
    }

    private const string RoofLoopWithoutAFillPressure = """
        fluidscript 1
        circuit heating
        fluid water

        HE1  heat_exchanger power=30 in.t=20 out.t=50
        LOAD heat_exchanger power=-30 dp=0 elevation=32
        CV1  valve
        PU1  pump
        P1   pipe length=32
        P2   pipe length=32

        connections
        N1 - PU1 - N2 - HE1 - N3 - P1 - N4 - LOAD - N5 - CV1 - N6 - P2 - N1
        """;

    [Fact]
    public void ATallPlantWithNoFillPressureIsNamedBeforeTheSeed()
    {
        // `S-60`: with the datum picked at 0 gauge, the top of a 32 m riser sits 313 kPa lower -- 212 kPa
        // below zero absolute, which is 213 kPa under water's floor now that the floor is the triple point
        // (`D-121`; it read 312 kPa against the old 100 kPa floor). Before, that was FS3007 after 0 steps
        // and a report that said nothing about height. The number suggested is practice's: the static
        // head plus half a bar, in whole tens -- 313 + 50 rounds up to 370, whatever the floor.
        var result = Check(RoofLoopWithoutAFillPressure);

        var head = Assert.Single(result.Diagnostics, static d => d.Code == "FS2220");

        Assert.Equal(DiagnosticSeverity.Error, head.Severity);
        Assert.Equal(
            "'N4' is 32 m above 'N1', which puts it 213 kPa below the lowest pressure water can be at. "
            + "State a pressure on 'N1' of at least 370 kPa.",
            head.Message);
        Assert.False(result.CanSolve);
    }

    [Fact]
    public void AStatedFillPressureThatCoversTheStaticHeadIsNotReported()
    {
        var result = Check(RoofLoopWithoutAFillPressure + "\nN1 node p=450\n");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Code == "FS2220");
        Assert.True(result.CanSolve);
    }
}
