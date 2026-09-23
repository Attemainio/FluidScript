using FluidScript.Core.Topology.Counting;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Topology.Counting;

/// <summary>Which parameter a constraint reaches for, and how far (<c>S-45</c>).</summary>
public sealed class PromotionLocalityTests
{
    private static string Header(string edit) =>
        File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-distribution-header.fluid"))
            .Replace("PU_AHU  pump", edit, StringComparison.Ordinal);

    [Fact]
    [Trait("Category", "Unit")]
    public void EachConsumersFlowConstraintClaimsItsOwnPump()
    {
        var counting = WellPosedness.Check(GraphFixture.Lower(Header("PU_AHU  pump")).Graph).Counting;

        Assert.Equal(("PU_AHU", "head"), Claimed(counting, "HE_AHU"));
        Assert.Equal(("PU_RAD", "head"), Claimed(counting, "HE_RAD"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ALocalPumpIsTakenEvenWhenADistantOneComesFirstInGraphOrder()
    {
        // `S-45`. The candidate list walked the hydraulic component in graph order, and a hydraulic
        // component is the whole connected plant rather than one circuit -- so which pump a constraint got
        // depended on where its owner happened to sit in the file.
        //
        // Dropping `out.t=30` from `HE_AHU` removes its flow constraint, leaving `HE_RAD` the only claimant.
        // `PU_AHU` is then free and comes first in graph order, and `PU_RAD` is the one on the radiator's
        // own branch. The old order handed the radiator the air handler's pump; the new one does not.
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-distribution-header.fluid"))
            .Replace("HE_AHU  heat_exchanger in.t=50 out.t=30 power=-24 kW",
                "HE_AHU  heat_exchanger in.t=50 power=-24 kW", StringComparison.Ordinal);

        var counting = WellPosedness.Check(GraphFixture.Lower(source).Graph).Counting;

        Assert.Equal(("PU_RAD", "head"), Claimed(counting, "HE_RAD"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASetpointOnANodeIsHeldByTheSplitThatFeedsIt()
    {
        // `S-48`. A temperature stated on an interior node is a **setpoint**, and in a steady circuit what
        // holds it is the mixing split feeding that node --- the same pairing a stated inlet already has,
        // and the one `34-controllers` describes when it says the circuit is *solved* into position and a
        // controller does that job dynamically. Before this, the constraint reached for nothing.
        //
        // The cooling loop states the same physical fact two ways. `HE1 in.t=20` is the exchanger's inlet,
        // fed from `N2`; `N2 node t=20` is that node directly. Dropping the first frees `3WV` so the second
        // has something to claim, and it must claim the same valve.
        var counting = WellPosedness.Check(GraphFixture.Lower(CoolingLoop(
            "HE1 heat_exchanger power=30 out.t=50",
            "N3 outlet p=280\nN2 node t=20")).Graph).Counting;

        Assert.Equal(("3WV", "position"), Claimed(counting, "N2", ConstraintKind.NodeTemperature));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASetpointBringsItsOwnUnknownSoTheCountIsUnmoved()
    {
        // `23`'s check on the whole counting scheme: "Remove either constraint and both the equation and
        // its unknown disappear together." A constraint that added a row and promoted nothing broke it,
        // and the symptom was a circuit reported over-specified by exactly one for stating a setpoint.
        //
        // Asserted as a difference rather than as an absolute, because the absolute is the sum of every
        // other counting rule and would have to be restated here to be checked.
        var without = WellPosedness.Check(GraphFixture.Lower(CoolingLoop(
            "HE1 heat_exchanger power=30 out.t=50", "N3 outlet p=280")).Graph).Counting;

        var with = WellPosedness.Check(GraphFixture.Lower(CoolingLoop(
            "HE1 heat_exchanger power=30 out.t=50",
            "N3 outlet p=280\nN2 node t=20")).Graph).Counting;

        Assert.Equal(without.Excess, with.Excess);
        Assert.Equal(without.Unknowns + 1, with.Unknowns);
        Assert.Equal(without.Equations + 1, with.Equations);
    }

    private static string CoolingLoop(string exchanger, string boundary) =>
        File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-cooling-loop.fluid"))
            .Replace("HE1 heat_exchanger power=30 in.t=20 out.t=50", exchanger, StringComparison.Ordinal)
            .Replace("N3 outlet p=280", boundary, StringComparison.Ordinal);

    [Fact]
    public void ALoneOutletPaysTheClosedCircuitsEnthalpyLevelInsteadOfPinningAFlow()
    {
        // `D-90`. `power` with `out` alone is one equation in two unknowns and pins no flow -- but it
        // fixes an absolute temperature, and a closed circuit has dropped one energy balance as its
        // level because adding the same enthalpy to every node satisfies all of them. So it raises
        // `EnthalpyLevel`, which promotes nothing, and that is what pays for the dropped balance.
        //
        // The count expected exactly this and nothing arranged it: whichever constraint ran out of
        // candidates paid, so graph order decided which statement was physics and which was a demand.
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-distribution-header.fluid"))
            .Replace(
                "HS1     heat_exchanger power=54",
                "HS1     heat_exchanger power=54 out.t=80",
                StringComparison.Ordinal);

        var counting = WellPosedness.Check(GraphFixture.Lower(source).Graph).Counting;

        var level = counting.Constraints.Single(
            constraint => constraint.Kind == ConstraintKind.EnthalpyLevel);

        Assert.Equal("HS1", level.Component);
        Assert.Equal("out", level.Parameter);

        // The half that makes it worth having: it claims no unknown.
        Assert.Null(Claimed(counting, "HS1", ConstraintKind.EnthalpyLevel));
        Assert.DoesNotContain(counting.Promotions, promotion => promotion.Constraint == level);
    }

    [Fact]
    public void AnOutletWithItsInletStatedStillPinsAFlow()
    {
        // The other side of `D-90`, and the reason it is conditional rather than a blanket reading of
        // `out`. With both ends stated, `power` gives m = Q/(h_out - h_in) and the flow genuinely
        // follows, so the statement needs an unknown to pay for it exactly as before. `m2-cooling-loop`
        // states `HE1 power=30 in.t=20 out.t=50` and is open besides, so no level is dropped at all.
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-cooling-loop.fluid"));
        var counting = WellPosedness.Check(GraphFixture.Lower(source).Graph).Counting;

        Assert.DoesNotContain(
            counting.Constraints,
            constraint => constraint.Kind == ConstraintKind.EnthalpyLevel);

        Assert.Contains(
            counting.Constraints,
            constraint => constraint.Kind == ConstraintKind.FixedFlow
                && string.Equals(constraint.Component, "HE1", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheFirstPinnedFlowTakesThePumpAndTheSecondFallsToItsOwnValve()
    {
        // `D-130`: an actuator is claimed once, first come in constraint order, and a pump's head comes
        // before a valve's Kv. Two duties on one pumped loop each pin the loop's flow; whichever is
        // declared first takes the pump, the other its branch's valve, and swapping the declarations
        // swaps the claims.
        static string Loop(string first, string second) => $"""
            fluidscript 1
            circuit loop
            fluid water

            {first}
            {second}
            CV1  valve
            PU1  pump
            P1   pipe length=25 dn=25

            connections
            N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5 - P1 - N1
            """;

        const string source = "HE1  heat_exchanger power=30 out.t=50 dt=30";
        const string load = "LOAD heat_exchanger power=-30 dt=30";

        var counting = WellPosedness.Check(GraphFixture.Lower(Loop(source, load)).Graph).Counting;
        Assert.Equal(("PU1", "head"), Claimed(counting, "HE1"));
        Assert.Equal(("CV1", "kv"), Claimed(counting, "LOAD"));

        var swapped = WellPosedness.Check(GraphFixture.Lower(Loop(load, source)).Graph).Counting;
        Assert.Equal(("PU1", "head"), Claimed(swapped, "LOAD"));
        Assert.Equal(("CV1", "kv"), Claimed(swapped, "HE1"));
    }

    /// <summary>The injection header with a mixing valve at the source (<c>diagnostics/scratch/s55-main-valve</c>), the source's own lines editable.</summary>
    private static string InjectionHeader(string source, string pumps = "PU_AHU  pump", string radiatorPump = "PU_RAD  pump") => $"""
        fluidscript 1
        project static plant_01

        circuit heating 100
        fluid water

        {source}
        TV_MAIN three_way_valve

        connections
        N1 - HS1 - TV_MAIN.a
        N1 - TV_MAIN.b
        TV_MAIN.ab - N3
        N3 node t=60
        N3 - N4
        N6 - N5
        N5 - N1

        N1 node p=250

        circuit AHU 101

        HE_AHU  load in.t=50 out.t=30 power=24 kW
        TV_AHU  three_way_valve
        {pumps}

        connections
        N3 - TV_AHU.a length=12 dn=25
        NM_AHU - TV_AHU.b
        TV_AHU.ab - PU_AHU - HE_AHU - NM_AHU
        NM_AHU - N5 length=12 dn=25

        circuit radiators 102

        HE_RAD  load in.t=50 out.t=30 power=30 kW
        TV_RAD  three_way_valve
        {radiatorPump}

        connections
        N4 - TV_RAD.a length=18 dn=25
        NM_RAD - TV_RAD.b
        TV_RAD.ab - PU_RAD - HE_RAD - NM_RAD
        NM_RAD - N6 length=18 dn=25
        """;

    [Fact]
    [Trait("Category", "Unit")]
    public void AHeaderSetpointIsHeldByTheValveWhoseStreamReachesItAndNeverByAConsumersValve()
    {
        // `S-45`, `D-133`. `N3 t=60` is the supply header's setpoint, on the stream `TV_MAIN` mixes; `TV_AHU`
        // draws from `N3` through its `a` port and cannot hold it at any position. Before `D-133` the
        // source's mixed inlet took `TV_MAIN` first and the setpoint fell to `TV_AHU`: square, and
        // non-finite at iteration zero.
        var result = WellPosedness.Check(GraphFixture.Lower(InjectionHeader("HS1 heat_exchanger power=54 kW in.t=40 out.t=80")).Graph);
        var counting = result.Counting;

        Assert.Equal(("TV_MAIN", "position"), Claimed(counting, "N3", ConstraintKind.NodeTemperature));
        Assert.Equal(("TV_AHU", "position"), Claimed(counting, "HE_AHU", ConstraintKind.MixedInlet));
        Assert.Equal(("TV_RAD", "position"), Claimed(counting, "HE_RAD", ConstraintKind.MixedInlet));

        // The source's inlet is the return: no split's stream reaches it, so it pays the closed plant's
        // level, and the one too many is among the three flows and the setpoint that share three
        // actuators. The report names that group and what it shares, instead of the radiator, which was
        // last in line.
        Assert.Null(Claimed(counting, "HS1", ConstraintKind.MixedInlet));
        var reported = result.Diagnostics.Single(static d => d.Code == "FS2210");
        Assert.Contains("over-specified by 1", reported.Message, StringComparison.Ordinal);
        Assert.Contains("HS1.out.t, N3.t, HE_AHU.out.t, HE_RAD.out.t share", reported.Message, StringComparison.Ordinal);
        Assert.Contains("TV_MAIN.position", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TwoDemandsOnOneValveAreNamedTogetherWithWhatTheyShare()
    {
        // `D-133`: what stays unmatched is reported as its group. With both consumer heads stated the
        // source's pinned flow has one actuator left, the main valve's position (its leg's share of the
        // header flow), and the header setpoint needs the same one. Neither is "the" one too many.
        var result = WellPosedness.Check(GraphFixture.Lower(InjectionHeader(
            "HS1 heat_exchanger power=54 kW in.t=40 out.t=80", "PU_AHU  pump head=6", "PU_RAD  pump head=6")).Graph);

        var reported = result.Diagnostics.Single(static d => d.Code == "FS2210");
        Assert.Contains("HS1.out.t, N3.t share TV_MAIN.position", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheIndexBranchWithoutAValveGetsThePumpWhicheverIsDeclaredFirst()
    {
        // `D-133`: first come, then augmented. Two parallel loads below one pump, one branch balanced by a
        // valve and the other -- the index branch -- bare. Declared first, the balanced branch takes the
        // pump under `D-130` (pump before valve) and leaves the bare one nothing; the augmenting path moves
        // it onto its own valve so the bare branch gets the pump. Before `D-133` this reported
        // over-specified by one and asked for a valve on the branch that was meant to have none.
        static string Parallel(string first, string second) => $"""
            fluidscript 1
            circuit parallel
            fluid water

            HS1  heat_exchanger power=50 out.t=70
            PU1  pump
            {first}
            {second}
            CV1  valve

            connections
            N1 - PU1 - N2 - HS1 - N3
            N3 - RAD1 - CV1 - N4
            N3 - RAD2 - N4
            N4 - N1
            """;

        const string balanced = "RAD1 load power=30 dt=20";
        const string index = "RAD2 load power=20 dt=20";

        foreach (var script in new[] { Parallel(balanced, index), Parallel(index, balanced) })
        {
            var result = WellPosedness.Check(GraphFixture.Lower(script).Graph);

            Assert.Equal(0, result.Counting.Excess);
            Assert.Equal(("CV1", "kv"), Claimed(result.Counting, "RAD1"));
            Assert.Equal(("PU1", "head"), Claimed(result.Counting, "RAD2"));
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APumpSharingNoLoopWithAFlowIsNotOffered()
    {
        // `D-133`: a pump moves the flow of every branch on a loop through it and of no other. Two rings
        // joined at one node share no loop, so the pumped ring's pump is not a candidate for a flow pinned
        // on the other, however the header pressure is set.
        var result = WellPosedness.Check(GraphFixture.Lower("""
            fluidscript 1
            circuit rings
            fluid water

            HS1  heat_exchanger power=50 out.t=70
            PU1  pump
            RAD1 load power=50 dt=20

            connections
            N1 - PU1 - HS1 - N1
            N1 - RAD1 - N1
            """).Graph);

        Assert.Null(Claimed(result.Counting, "RAD1"));
    }

    private static (string, string)? Claimed(
        CountingTable counting, string component, ConstraintKind kind = ConstraintKind.FixedFlow) =>
        counting.Promotions
            .Where(promotion =>
                promotion.Constraint.Kind == kind
                && string.Equals(promotion.Constraint.Component, component, StringComparison.Ordinal))
            .Select(promotion => ((string, string)?)(promotion.Component, promotion.Parameter))
            .FirstOrDefault();
}
