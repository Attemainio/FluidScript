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

    private static (string, string)? Claimed(CountingTable counting, string component) =>
        counting.Promotions
            .Where(promotion =>
                promotion.Constraint.Kind == ConstraintKind.FixedFlow
                && string.Equals(promotion.Constraint.Component, component, StringComparison.Ordinal))
            .Select(promotion => ((string, string)?)(promotion.Component, promotion.Parameter))
            .FirstOrDefault();
}
