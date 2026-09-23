using FluidScript.Core.Components.Exchangers;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Components.Exchangers;

/// <summary>
/// The ε-NTU relations of <c>22</c>, held against the substation's figures in <c>01</c> and against
/// the LMTD route, which shares no code with them.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EffectivenessTests
{
    // The substation's design point (01): 150 kW, primary 85/45 at C = 3750 W/K, secondary 40/60 at
    // C = 7500 W/K, so Cr = 0.5, ε = 150/168.75 = 0.8889 and NTU = 2 ln 5.
    private const double Duty = 150_000;
    private const double CapacityMin = 3750;
    private const double CapacityRatio = 0.5;
    private const double DesignEffectiveness = Duty / (CapacityMin * 45);

    [Fact]
    public void TheSubstationsCounterflowSizeIsTwoLnFive()
    {
        var ntu = Effectiveness.Ntu(DesignEffectiveness, CapacityRatio, ExchangerArrangement.Counter);

        Assert.Equal(2 * Math.Log(5), ntu, 1e-12);
        Assert.Equal(ReferenceNumbers.Substation.RequiredUa, ntu * CapacityMin, 0.5);
    }

    [Fact]
    public void TheTwoRoutesAgreeOnTheSubstationToRounding()
    {
        // ε-NTU from the design point, LMTD from the same four terminals: two formulations, one number.
        var byNtu = Effectiveness.Ntu(DesignEffectiveness, CapacityRatio, ExchangerArrangement.Counter) * CapacityMin;
        var byLmtd = LogMeanTemperatureDifference.Conductance(
            Duty, LogMeanTemperatureDifference.Counterflow(85 + 273.15, 45 + 273.15, 40 + 273.15, 60 + 273.15));

        Assert.Equal(byNtu, byLmtd, byNtu * 1e-9);
    }

    [Theory]
    [InlineData(ExchangerArrangement.Counter, 0.0)]
    [InlineData(ExchangerArrangement.Counter, 0.5)]
    [InlineData(ExchangerArrangement.Counter, 0.9995)]
    [InlineData(ExchangerArrangement.Counter, 1.0)]
    [InlineData(ExchangerArrangement.Parallel, 0.3)]
    [InlineData(ExchangerArrangement.Crossflow, 0.0)]
    [InlineData(ExchangerArrangement.Crossflow, 0.6)]
    [InlineData(ExchangerArrangement.Crossflow, 1.0)]
    public void TheInverseRoundTrips(ExchangerArrangement arrangement, double capacityRatio)
    {
        foreach (var ntu in new[] { 0.2, 1, 3.2, 8 })
        {
            var effectiveness = Effectiveness.Of(ntu, capacityRatio, arrangement);
            var back = Effectiveness.Ntu(effectiveness, capacityRatio, arrangement);

            // Inside the balanced band the forward and inverse blends are each C¹ but not exact
            // inverses of one another; 1e-5 is a hundred times their measured disagreement.
            var inBand = capacityRatio > 1 - Effectiveness.BalancedBand && capacityRatio < 1;

            Assert.Equal(ntu, back, ntu * (inBand ? 1e-5 : 1e-7));
        }
    }

    [Fact]
    public void BalancedCounterflowIsNtuOverOnePlusNtu()
    {
        Assert.Equal(3.0 / 4.0, Effectiveness.Of(3, 1, ExchangerArrangement.Counter), 1e-12);
        Assert.Equal(3, Effectiveness.Ntu(0.75, 1, ExchangerArrangement.Counter), 1e-12);
    }

    [Fact]
    public void EffectivenessIsContinuouslyDifferentiableAcrossTheBalancedBlend()
    {
        // 22's criterion: ε is C¹ across Cr = 1, verified by finite differences either side of the
        // blend. The case an LMTD residual could not evaluate at all.
        const double ntu = 3.2;
        const double step = 1e-7;
        var band = Effectiveness.BalancedBand;

        foreach (var edge in new[] { 1 - band, 1 - (band / 2) })
        {
            var below = (Effectiveness.Of(ntu, edge, ExchangerArrangement.Counter)
                - Effectiveness.Of(ntu, edge - step, ExchangerArrangement.Counter)) / step;
            var above = (Effectiveness.Of(ntu, edge + step, ExchangerArrangement.Counter)
                - Effectiveness.Of(ntu, edge, ExchangerArrangement.Counter)) / step;

            Assert.True(Math.Abs(above - below) < 1e-3 * Math.Max(1, Math.Abs(below)), $"slope jumps at Cr = {edge}: {below} vs {above}");
        }

        // And the blended value stays within the band's own width of the closed form.
        Assert.Equal(
            Effectiveness.Of(ntu, 1 - band, ExchangerArrangement.Counter),
            Effectiveness.Of(ntu, 1, ExchangerArrangement.Counter),
            2e-3);
    }

    [Fact]
    public void ParallelFlowCannotExceedItsCeiling()
    {
        Assert.Equal(1 / 1.5, Effectiveness.Maximum(0.5, ExchangerArrangement.Parallel), 1e-12);
        Assert.True(Effectiveness.Of(50, 0.5, ExchangerArrangement.Parallel) <= 1 / 1.5);
        Assert.True(Effectiveness.Of(2, 0.5, ExchangerArrangement.Parallel) < 1 / 1.5);
        Assert.True(double.IsNaN(Effectiveness.Ntu(0.7, 0.5, ExchangerArrangement.Parallel)));
    }

    [Fact]
    public void AnImpossibleEffectivenessHasNoSize()
    {
        Assert.True(double.IsNaN(Effectiveness.Ntu(1.0, 0.5, ExchangerArrangement.Counter)));
        Assert.True(double.IsNaN(Effectiveness.Ntu(1.2, 0.5, ExchangerArrangement.Crossflow)));
        Assert.True(double.IsNaN(Effectiveness.Of(-1, 0.5, ExchangerArrangement.Counter)));
    }

    [Fact]
    public void APhaseChangingSideIsOneMinusExpMinusNtu()
    {
        // Cr = 0 is shared by an emitter against a room, a condensing zone and a boiling zone (22).
        foreach (var arrangement in new[] { ExchangerArrangement.Counter, ExchangerArrangement.Parallel, ExchangerArrangement.Crossflow })
        {
            Assert.Equal(1 - Math.Exp(-2.5), Effectiveness.Of(2.5, 0, arrangement), 1e-9);
        }
    }

    [Fact]
    public void TheLogMeanHandlesEqualEndsAndRefusesACross()
    {
        Assert.Equal(ReferenceNumbers.Substation.Lmtd, LogMeanTemperatureDifference.Of(25, 5), 5e-4);
        Assert.Equal(10, LogMeanTemperatureDifference.Of(10, 10), 1e-12);
        Assert.Equal(LogMeanTemperatureDifference.Of(10, 10.001), LogMeanTemperatureDifference.Of(10.001, 10), 1e-12);

        // Inside the band the series stands in for the quotient; at the same point they must agree.
        const double a = 10, b = 10 * (1 + 1.5e-4);

        Assert.Equal((b - a) / Math.Log(b / a), LogMeanTemperatureDifference.Of(a, b), 1e-9);
        Assert.True(double.IsNaN(LogMeanTemperatureDifference.Of(10, -1)));
        Assert.True(double.IsNaN(LogMeanTemperatureDifference.Counterflow(350, 320, 330, 360)));
    }
}
