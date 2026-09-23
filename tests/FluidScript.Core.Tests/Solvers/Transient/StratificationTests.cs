using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers.Transient;

namespace FluidScript.Core.Tests.Solvers.Transient;

/// <summary>The density-inversion remix on its own (P6.2, <c>33</c> §Stratified tank, invariant 12).</summary>
/// <remarks>
/// The algorithm alone, on hand-built stacks, so a failure here names the pooling rule rather than a
/// run. Enthalpies are water's at atmospheric pressure, because the rule is about density and water's
/// density is what makes it non-trivial.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class StratificationTests
{
    private static double H(double celsius)
    {
        var state = Water.Instance.FromPressureTemperature(
            Quantity.FromSi(0, Dimension.Pressure),
            Quantity.FromSi(celsius + 273.15, Dimension.Temperature));

        Assert.True(state.IsSuccess, state.Error?.Message);

        return state.Value.Enthalpy.SiValue;
    }

    private static double T(double enthalpy)
    {
        var state = Water.Instance.FromPressureEnthalpy(
            Quantity.FromSi(0, Dimension.Pressure),
            Quantity.FromSi(enthalpy, Dimension.Enthalpy));

        Assert.True(state.IsSuccess, state.Error?.Message);

        return state.Value.Temperature.SiValue - 273.15;
    }

    private static double Density(double enthalpy)
    {
        var state = Water.Instance.FromPressureEnthalpy(
            Quantity.FromSi(0, Dimension.Pressure),
            Quantity.FromSi(enthalpy, Dimension.Enthalpy));

        Assert.True(state.IsSuccess, state.Error?.Message);

        return state.Value.Density.SiValue;
    }

    [Fact]
    public void AStackThatIsAlreadyStableIsLeftAlone()
    {
        // Cold at the bottom, hot at the top: what a charged buffer looks like, and nothing to do.
        double[] masses = [60, 60, 60, 60, 60];
        double[] layers = [H(25), H(30), H(40), H(50), H(60)];
        var before = (double[])layers.Clone();

        Assert.True(Stratification.Remix(masses, layers, Water.Instance, 0, out var pooled));
        Assert.Equal(0, pooled);
        Assert.Equal(before, layers);
    }

    [Fact]
    public void OneInversionPoolsTwoLayersAndLeavesTheRestWhereTheyWere()
    {
        // Layer 3 warmer than layer 4: lighter water under heavier water, which no vessel holds. The
        // pair pools to its mass-weighted mean and the scan stops, because layer 2 is denser than the
        // pool and layer 5 lighter.
        double[] masses = [60, 60, 60, 60, 60];
        double[] layers = [H(25), H(30), H(55), H(45), H(60)];

        Assert.True(Stratification.Remix(masses, layers, Water.Instance, 0, out var pooled));
        Assert.Equal(2, pooled);
        Assert.Equal(25, T(layers[0]), 0.01);
        Assert.Equal(30, T(layers[1]), 0.01);
        Assert.Equal(T(layers[2]), T(layers[3]), 1e-9);
        Assert.Equal(50, T(layers[2]), 0.05);
        Assert.Equal(60, T(layers[4]), 0.01);
    }

    [Fact]
    public void PoolingCascadesDownwardUntilTheStackIsStable()
    {
        // A hot charge dumped into the bottom: every layer above it is colder, so the pool grows down
        // to top as each merge stays lighter than the one above. One pass, five layers, one block.
        double[] masses = [60, 60, 60, 60, 60];
        double[] layers = [H(80), H(30), H(30), H(30), H(30)];

        Assert.True(Stratification.Remix(masses, layers, Water.Instance, 0, out var pooled));
        Assert.Equal(5, pooled);
        Assert.All(layers, value => Assert.Equal(T(layers[0]), T(value), 1e-9));
        Assert.Equal(40, T(layers[0]), 0.1);
    }

    [Fact]
    public void MassAndEnergyAreConservedExactly()
    {
        double[] masses = [60, 45, 70, 60, 55];
        double[] layers = [H(70), H(20), H(65), H(30), H(58)];
        var before = 0.0;

        for (var index = 0; index < layers.Length; index++)
        {
            before += masses[index] * layers[index];
        }

        Assert.True(Stratification.Remix(masses, layers, Water.Instance, 0, out _));

        var after = 0.0;

        for (var index = 0; index < layers.Length; index++)
        {
            after += masses[index] * layers[index];
        }

        Assert.Equal(before, after, Math.Abs(before) * 1e-12);
    }

    [Fact]
    public void TheResultIsStableBottomToTop()
    {
        double[] masses = [60, 60, 60, 60, 60, 60, 60, 60];
        double[] layers = [H(55), H(20), H(62), H(31), H(48), H(12), H(70), H(40)];

        Assert.True(Stratification.Remix(masses, layers, Water.Instance, 0, out var pooled));
        Assert.True(pooled > 0);

        for (var index = 0; index + 1 < layers.Length; index++)
        {
            Assert.True(
                Density(layers[index]) >= Density(layers[index + 1]) - 1e-9,
                $"layer {index + 1} is lighter than layer {index + 2}");
        }
    }

    [Fact]
    public void TheBackendHasADensityMaximumNearFourDegrees()
    {
        // The fact the whole rule rests on, asserted rather than assumed: `D-137` put water on IF97,
        // and IF97 does reproduce the anomaly. Measured here at 0 kPa gauge, kg/m³:
        //   1 °C 999.9030 · 2 °C 999.9440 · 3 °C 999.9679 · 4 °C 999.9754 · 5 °C 999.9669 · 6 °C 999.9430
        // The whole spread between 1 °C and the maximum is 0.07 kg/m³, seven parts in a hundred
        // thousand, so near the maximum the stack order is very weakly determined. It is still
        // deterministic, because the backend is a function and not a measurement, and two layers that
        // close are physically the same water — pooling them or not changes nothing anyone can read.
        Assert.True(Density(H(4)) > Density(H(1)));
        Assert.True(Density(H(4)) > Density(H(8)));
        Assert.True(Density(H(3)) < Density(H(4)));
        Assert.True(Density(H(5)) < Density(H(4)));
    }

    [Fact]
    public void ItComparesDensityAndNotTemperature()
    {
        // Water is densest near 4 °C, so 1 °C water is *lighter* than 6 °C water and belongs above it.
        // A chilled store at 6 °C under 1 °C is therefore resting correctly, and a rule written as
        // "hotter floats" would stir it. This is the case that makes the comparison density and not
        // temperature, and it is not hypothetical: it is an ice store or a chilled buffer in winter.
        double[] masses = [60, 60];
        double[] warmBelow = [H(6), H(1)];
        var before = (double[])warmBelow.Clone();

        Assert.True(Density(H(6)) > Density(H(1)), "6 °C water should be denser than 1 °C water");
        Assert.True(Stratification.Remix(masses, warmBelow, Water.Instance, 0, out var pooled));
        Assert.Equal(0, pooled);
        Assert.Equal(before, warmBelow);

        // The colder-below stack is the unstable one here, which is the inverse of every other
        // temperature range and the whole reason this test exists.
        double[] coldBelow = [H(1), H(6)];

        Assert.True(Stratification.Remix(masses, coldBelow, Water.Instance, 0, out var stirred));
        Assert.Equal(2, stirred);
        Assert.Equal(coldBelow[0], coldBelow[1], 1e-9);
    }

    [Fact]
    public void ASingleLayerHasNothingToRemix()
    {
        double[] masses = [300];
        double[] layers = [H(50)];

        Assert.True(Stratification.Remix(masses, layers, Water.Instance, 0, out var pooled));
        Assert.Equal(0, pooled);
        Assert.Equal(H(50), layers[0]);
    }
}
