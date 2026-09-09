using FluidScript.Core.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Topology;

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
        // Dropping `out=30` from `HE_AHU` removes its flow constraint, leaving `HE_RAD` the only claimant.
        // `PU_AHU` is then free and comes first in graph order, and `PU_RAD` is the one on the radiator's
        // own branch. The old order handed the radiator the air handler's pump; the new one does not.
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-distribution-header.fluid"))
            .Replace("HE_AHU  heat_exchanger in=50 out=30 power=-24 kW",
                "HE_AHU  heat_exchanger in=50 power=-24 kW", StringComparison.Ordinal);

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
        // The cooling loop states the same physical fact two ways. `HE1 in=20` is the exchanger's inlet,
        // fed from `N2`; `N2 node t=20` is that node directly. Dropping the first frees `3WV` so the second
        // has something to claim, and it must claim the same valve.
        var counting = WellPosedness.Check(GraphFixture.Lower(CoolingLoop(
            "HE1 heat_exchanger power=30 out=50",
            "N3 return p=280\nN2 node t=20")).Graph).Counting;

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
            "HE1 heat_exchanger power=30 out=50", "N3 return p=280")).Graph).Counting;

        var with = WellPosedness.Check(GraphFixture.Lower(CoolingLoop(
            "HE1 heat_exchanger power=30 out=50",
            "N3 return p=280\nN2 node t=20")).Graph).Counting;

        Assert.Equal(without.Excess, with.Excess);
        Assert.Equal(without.Unknowns + 1, with.Unknowns);
        Assert.Equal(without.Equations + 1, with.Equations);
    }

    private static string CoolingLoop(string exchanger, string boundary) =>
        File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-cooling-loop.fluid"))
            .Replace("HE1 heat_exchanger power=30 in=20 out=50", exchanger, StringComparison.Ordinal)
            .Replace("N3 return p=280", boundary, StringComparison.Ordinal);

    private static (string, string)? Claimed(
        CountingTable counting, string component, ConstraintKind kind = ConstraintKind.FixedFlow) =>
        counting.Promotions
            .Where(promotion =>
                promotion.Constraint.Kind == kind
                && string.Equals(promotion.Constraint.Component, component, StringComparison.Ordinal))
            .Select(promotion => ((string, string)?)(promotion.Component, promotion.Parameter))
            .FirstOrDefault();
}
