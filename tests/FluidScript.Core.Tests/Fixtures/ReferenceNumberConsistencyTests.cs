using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Fixtures;

/// <summary>
/// Re-derives every figure in <see cref="ReferenceNumbers"/> from the inputs the plan states, so a
/// transcription slip in the constants fails here rather than inside a solver test.
/// </summary>
/// <remarks>
/// <para>
/// This is the durable form of P0.3 in <c>plan/00-foundation/08-implementation-sequence.md</c>. It
/// deliberately shares no code with anything under <c>src/</c>: the arithmetic is written out at the
/// assertion site, because a test that computed the expected value with the same helper the product
/// uses would agree with a wrong helper.
/// </para>
/// <para>
/// It proves internal consistency, not physical truth. The raw enthalpies these derivations start
/// from are checked against CoolProp by validation cases V4 and V5, which need the real property
/// backend and therefore arrive in P3.2.
/// </para>
/// </remarks>
public sealed class ReferenceNumberConsistencyTests
{
    private const double StandardGravity = 9.81;

    /// <summary>Relative tolerance matching the precision the plan quotes its figures to.</summary>
    private const double QuotedPrecision = 2e-3;

    private static void AssertRelative(double expected, double actual, double tolerance, string because)
    {
        var deviation = Math.Abs(actual - expected) / Math.Abs(expected);
        Assert.True(
            deviation <= tolerance,
            $"{because}: expected {expected:G6}, derived {actual:G6} ({deviation:P2} apart).");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CoolingLoop_FlowsFollowFromTheStatedDutyAndTemperatures()
    {
        const double duty = 30_000.0;
        var secondaryRise = ReferenceNumbers.WaterEnthalpy.At50C - ReferenceNumbers.WaterEnthalpy.At20C;
        var fullRise = ReferenceNumbers.WaterEnthalpy.At50C - ReferenceNumbers.WaterEnthalpy.At6C;

        var secondaryFlow = duty / secondaryRise;
        var mixingFraction = secondaryRise / fullRise;
        var primaryFlow = mixingFraction * secondaryFlow;

        AssertRelative(ReferenceNumbers.CoolingLoop.SecondaryFlow, secondaryFlow, QuotedPrecision,
            "Secondary flow is the stated duty over the 20-50 C enthalpy rise");
        AssertRelative(ReferenceNumbers.CoolingLoop.MixingFraction, mixingFraction, QuotedPrecision,
            "The mixing fraction at N2 is the ratio of the two enthalpy rises");
        AssertRelative(ReferenceNumbers.CoolingLoop.PrimaryFlow, primaryFlow, QuotedPrecision,
            "Primary flow is the primary share of the secondary flow");
        AssertRelative(ReferenceNumbers.CoolingLoop.RecirculationFlow, secondaryFlow - primaryFlow,
            QuotedPrecision, "Recirculation is what the primary does not supply");

        // The primary side must carry the same duty it was sized from, or the mixing fraction is wrong.
        AssertRelative(duty, primaryFlow * fullRise, QuotedPrecision,
            "The primary-side energy balance closes on the stated duty");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SimpleLoop_HeadFollowsFromTheLoopDropAtThePumpInletDensity()
    {
        const double pumpInletDensity = 998.2;

        var head = ReferenceNumbers.SimpleLoop.LoopPressureDrop / (pumpInletDensity * StandardGravity);

        AssertRelative(ReferenceNumbers.SimpleLoop.PumpHead, head, QuotedPrecision,
            "Pump head is the loop drop expressed at the pump's own inlet density");
        Assert.Equal(ReferenceNumbers.CoolingLoop.SecondaryFlow, ReferenceNumbers.SimpleLoop.Flow);

        // Rounding Kv down raises the valve's share of the loop drop, so the achieved authority must
        // land above the 0.5 target. Below it would mean the rounding went the wrong way.
        Assert.True(ReferenceNumbers.SimpleLoop.AchievedAuthority > 0.5);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Substation_LmtdAndEffectivenessNtuRoutesAgree()
    {
        const double duty = 150_000.0;
        const double hotIn = 85.0, hotOut = 45.0, coldIn = 40.0, coldOut = 60.0;
        const double overallCoefficient = 3300.0;
        const double plateArea = 0.10;

        var capacityHot = duty / (hotIn - hotOut);
        var capacityCold = duty / (coldOut - coldIn);
        var capacityRatio = capacityHot / capacityCold;

        var lmtd = ((hotIn - coldOut) - (hotOut - coldIn))
            / Math.Log((hotIn - coldOut) / (hotOut - coldIn));
        var uaFromLmtd = duty / lmtd;

        var effectiveness = duty / (capacityHot * (hotIn - coldIn));
        var ntu = Math.Log((1 - effectiveness * capacityRatio) / (1 - effectiveness))
            / (1 - capacityRatio);
        var uaFromNtu = ntu * capacityHot;

        AssertRelative(ReferenceNumbers.Substation.Lmtd, lmtd, QuotedPrecision, "Counterflow LMTD");
        AssertRelative(ReferenceNumbers.Substation.RequiredUa, uaFromLmtd, QuotedPrecision,
            "UA by the LMTD route");
        AssertRelative(ReferenceNumbers.Substation.RequiredUa, uaFromNtu, QuotedPrecision,
            "UA by the effectiveness-NTU route");

        // The claim M2b exits on: two formulations that share no code land on the same conductance.
        AssertRelative(uaFromLmtd, uaFromNtu, 1e-9, "The two UA routes agree to rounding");

        var requiredArea = uaFromNtu / overallCoefficient;
        AssertRelative(ReferenceNumbers.Substation.RequiredArea, requiredArea, QuotedPrecision,
            "Required area at the stated u");

        var effectivePlates = (int)Math.Ceiling(requiredArea / plateArea);
        Assert.Equal(ReferenceNumbers.Substation.TotalPlates, effectivePlates + 2);

        // The selected size overshoots, so the achieved approach must be closer than the 5.0 K design.
        var installedNtu = effectivePlates * plateArea * overallCoefficient / capacityHot;
        var decay = Math.Exp(-installedNtu * (1 - capacityRatio));
        var installedEffectiveness = (1 - decay) / (1 - capacityRatio * decay);
        var deliveredDuty = installedEffectiveness * capacityHot * (hotIn - coldIn);
        var approach = hotIn - (deliveredDuty / capacityHot) - coldIn;

        AssertRelative(ReferenceNumbers.Substation.AchievedApproach, approach, QuotedPrecision,
            "Achieved cold-end approach at the selected plate count");
        Assert.True(approach < hotOut - coldIn);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DistributionHeader_SourceFlowIsTheBranchSum()
    {
        var drawn = ReferenceNumbers.DistributionHeader.AhuHeaderFlow
            + ReferenceNumbers.DistributionHeader.RadiatorHeaderFlow;

        AssertRelative(ReferenceNumbers.DistributionHeader.SourceFlow, drawn, QuotedPrecision,
            "The source carries what the two subcircuits draw from the header");

        // And the same number a second way, from HS1 alone: 54 kW over the header's own 60-30 rise.
        // The two derivations agreeing is the whole of this fixture's design point, and they did not
        // agree while HS1 demanded a 40 C return the circuit cannot produce (F-16).
        AssertRelative(
            ReferenceNumbers.DistributionHeader.SourceFlow,
            54_000.0 / (4_180.0 * 30.0),
            QuotedPrecision,
            "The source's own energy balance carries what the header does");

        // Both branches run 50/30 C, so their flows must be in the ratio of their stated duties, and
        // the header draws are in that same ratio because they share the header's 30 K.
        AssertRelative(
            30_000.0 / 24_000.0,
            ReferenceNumbers.DistributionHeader.RadiatorBranchFlow
                / ReferenceNumbers.DistributionHeader.AhuBranchFlow,
            QuotedPrecision,
            "Two branches at the same temperatures split flow in proportion to duty");

        AssertRelative(
            30_000.0 / 24_000.0,
            ReferenceNumbers.DistributionHeader.RadiatorHeaderFlow
                / ReferenceNumbers.DistributionHeader.AhuHeaderFlow,
            QuotedPrecision,
            "And so do what they draw");

        // Two thirds of each loop flow comes from the header and one third recirculates: 20 K of the
        // header's 30 K. A fixture whose loop and header flows were the same number would prove
        // neither, which is why both are recorded.
        AssertRelative(
            20.0 / 30.0,
            ReferenceNumbers.DistributionHeader.AhuHeaderFlow
                / ReferenceNumbers.DistributionHeader.AhuBranchFlow,
            QuotedPrecision,
            "The header supplies the exchanger's rise over the header's own");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StorageHeader_Layer2DerivativeFollowsFromItsInletAndMass()
    {
        const double tankVolume = 0.300;
        const int layers = 5;
        const double fixtureDensity = 1000.0;
        const double inletFlow = 0.08;
        const double inletTemperature = 45.0;
        const double initialLayerTemperature = 30.0;

        var layerMass = tankVolume / layers * fixtureDensity;
        AssertRelative(ReferenceNumbers.StorageHeader.LayerMass, layerMass, 1e-9, "Layer mass");

        var derivative = inletFlow / layerMass * (inletTemperature - initialLayerTemperature);
        AssertRelative(ReferenceNumbers.StorageHeader.InitialLayer2Derivative, derivative, 1e-9,
            "Layer 2 warms at the inlet flow times the temperature difference over its mass");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DemandStepLoop_TransportTimesFollowFromOneCellResidence()
    {
        const int cellsBetweenExchangerAndMeasurement = 4;
        const double cflSafetyFactor = 0.9;

        AssertRelative(
            ReferenceNumbers.DemandStepLoop.DeadTime,
            cellsBetweenExchangerAndMeasurement * ReferenceNumbers.DemandStepLoop.ThermalCellResidenceTime,
            QuotedPrecision,
            "Dead time is four thermal cells in series");

        AssertRelative(
            ReferenceNumbers.DemandStepLoop.CflStepLimit,
            cflSafetyFactor * ReferenceNumbers.DemandStepLoop.ThermalCellResidenceTime,
            QuotedPrecision,
            "The CFL limit is set by the smallest control volume");
    }
}
