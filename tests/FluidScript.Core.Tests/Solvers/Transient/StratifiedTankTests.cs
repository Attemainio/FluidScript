using FluidScript.Core.Solvers;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>The tank in time (P6.2): V15, V16 and V17 of <c>62</c>.</summary>
/// <remarks>
/// <para>
/// V15 has a closed form, V16 checks the conservative accounting with simultaneous branches, and V17
/// checks both the layer-order algorithm and the claim that more layers improve the answer rather
/// than merely changing it.
/// </para>
/// <para>
/// The storage header holds 300 dm³ in five layers, charged from two sources and drawn by two
/// networks: 0.12 kg/s at 60 °C into the top layer and out of it, 0.08 kg/s at 45 °C into layer 2 and
/// out of it. Each layer's inflow and outflow are equal, so no interface flow exists and every layer
/// evolves alone.
/// </para>
/// </remarks>
public sealed class StratifiedTankTests
{
    private const string Header = """
        fluidscript 1
        circuit storageHeader
        fluid dynamic water

        S1 inlet t=60 flow=0.12
        S2 inlet t=45 flow=0.08
        T1 tank volume=300 layers={LAYERS} {PROFILE} in.level=90% in[2].level=30% out.level=90% out[2].level=30%
        RAD_NETWORK outlet flow=0.12
        AHU_NETWORK outlet flow=0.08

        connections
        S1 - T1.in
        S2 - T1.in[2]
        T1.out - RAD_NETWORK
        T1.out[2] - AHU_NETWORK
        """;

    private static string Script(int layers, string profile) =>
        Header.Replace("{LAYERS}", layers.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{PROFILE}", profile, StringComparison.Ordinal);

    [Fact]
    [Trait("Category", "Validation")]
    public async Task V15_OneLayerFollowsTheAnalyticMixedControlVolume()
    {
        // `layers=1` is one perfectly mixed vessel. Both sources and both loads reach it, so its
        // balance is m dh/dt = Σ ṁ_in (h_in − h) with 0.20 kg/s arriving at a mass-weighted 54 °C:
        // 0.12 at 60 and 0.08 at 45. From 20 °C the closed form is T(t) = 54 − 34 e^(−ṁt/m), with
        // m = 300 dm³ of water at 20 °C and ṁ = 0.20 kg/s, so the time constant is about 1495 s.
        var run = await TransientRunFixture.RunAsync(
            "m4-storage-header-mixed",
            Script(1, "t=20"),
            new TransientSettings { Horizon = 1800, FrameInterval = 60 },
            TestContext.Current.CancellationToken);

        var mass = run.Snapshot.ReferenceMasses[0];
        var tau = mass / 0.20;

        Assert.Single(run.Snapshot.DifferentialInitial);
        Assert.InRange(tau, 1400, 1600);

        foreach (var frame in run.Frames)
        {
            var analytic = 54.0 - (34.0 * Math.Exp(-frame.Time / tau));

            Assert.Equal(analytic, run.Celsius(frame, "T1.layer[1].h"), 0.1);
        }
    }

    [Fact]
    [Trait("Category", "Validation")]
    public async Task V16_StoredEnergyMatchesWhatCrossedTheBoundary()
    {
        // Two inlets and two outlets at once. The drift accumulator is the conservation row: it
        // compares the change in Σ m h against the integrated boundary enthalpy flow, and nothing in
        // the tank's own balance feeds it, which is what makes it a check rather than a restatement.
        var run = await TransientRunFixture.RunAsync(
            "m4-storage-header-conservation",
            Script(5, "layer[1].t=25 layer[2].t=30 layer[3].t=40 layer[4].t=50 layer[5].t=60"),
            new TransientSettings { Horizon = 600, FrameInterval = 30 },
            TestContext.Current.CancellationToken);

        var last = run.Frames[^1];

        Assert.Equal(5, last.Differential.Length);
        Assert.True(last.EnergyDrift < 1e-6, last.EnergyDrift.ToString("G3", System.Globalization.CultureInfo.InvariantCulture));

        // Layers 1, 3 and 4 have no port, so nothing enters or leaves them and no interface flow
        // exists: they must not move at all.
        foreach (var layer in new[] { 1, 3, 4 })
        {
            Assert.Equal(
                run.Celsius(run.At(0), $"T1.layer[{layer}].h"),
                run.Celsius(last, $"T1.layer[{layer}].h"),
                0.001);
        }

        // Layers 2 and 5 each carry their own source and load, so each approaches its own supply.
        Assert.InRange(run.Celsius(last, "T1.layer[2].h"), 30, 45);
        Assert.InRange(run.Celsius(last, "T1.layer[5].h"), 59.9, 60.1);
    }

    [Fact]
    [Trait("Category", "Validation")]
    public async Task V17_AnInvertedProfileRemixesTheMinimalBlockAndConservesItsEnergy()
    {
        // Layer 2 stated hotter than layers 3 and 4: lighter water under heavier water, which the
        // vessel cannot hold. The first accepted step pools the minimal violating block and leaves
        // layers 1 and 5 alone, and the pooled mean carries the block's energy exactly, so the drift
        // accumulator never sees it.
        var run = await TransientRunFixture.RunAsync(
            "m4-storage-header-inverted",
            Script(5, "layer[1].t=25 layer[2].t=55 layer[3].t=40 layer[4].t=45 layer[5].t=60"),
            new TransientSettings { Horizon = 60, FrameInterval = 10 },
            TestContext.Current.CancellationToken);

        var start = run.At(0);
        var settled = run.At(10);

        // As stated at t = 0, before any step has run.
        Assert.Equal(55, run.Celsius(start, "T1.layer[2].h"), 0.01);
        Assert.Equal(40, run.Celsius(start, "T1.layer[3].h"), 0.01);

        // After the first step: 2, 3 and 4 are one block at their mass-weighted mean, near 46.7 °C.
        Assert.Equal(run.Celsius(settled, "T1.layer[2].h"), run.Celsius(settled, "T1.layer[3].h"), 0.6);
        Assert.Equal(run.Celsius(settled, "T1.layer[3].h"), run.Celsius(settled, "T1.layer[4].h"), 0.6);
        Assert.InRange(run.Celsius(settled, "T1.layer[3].h"), 45, 49);

        // Untouched: the scan stops as soon as the block below is denser.
        Assert.Equal(25, run.Celsius(settled, "T1.layer[1].h"), 0.01);
        Assert.Equal(60, run.Celsius(settled, "T1.layer[5].h"), 0.2);

        // Stable bottom to top afterwards, and energy conserved through the overturn.
        for (var layer = 1; layer < 5; layer++)
        {
            Assert.True(
                run.Celsius(settled, $"T1.layer[{layer}].h") <= run.Celsius(settled, $"T1.layer[{layer + 1}].h") + 0.01,
                $"layer {layer} is warmer than layer {layer + 1}");
        }

        Assert.True(run.Frames[^1].EnergyDrift < 1e-6, run.Frames[^1].EnergyDrift.ToString("G3", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ALayerOutsideThePropertyDomainIsFs3108()
    {
        // A profile is a stated initial condition and the only unchecked number in a snapshot. Water
        // at −50 °C is ice, which the backend does not carry, so the run is refused before its first
        // step with the tank and the layer named rather than the backend's own message passed through.
        var error = await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(async () =>
            await TransientRunFixture.RunAsync(
                "m4-storage-header-frozen",
                Script(5, "layer[1].t=-50 layer[2].t=30 layer[3].t=40 layer[4].t=50 layer[5].t=60"),
                new TransientSettings { Horizon = 10 },
                TestContext.Current.CancellationToken));

        Assert.Contains("FS3108", error.Message, StringComparison.Ordinal);
        Assert.Contains("Cannot initialize 'T1' layer 1 at -50 °C", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Validation")]
    public async Task V17_MoreLayersConvergeRatherThanMerelyChange()
    {
        // The same vessel, the same duty, at three refinements. What must converge is the drawn
        // temperature at the top port: doubling the layer count should move it by less each time.
        var five = await Draw(5);
        var ten = await Draw(10);
        var twenty = await Draw(20);

        var first = Math.Abs(ten - five);
        var second = Math.Abs(twenty - ten);

        Assert.True(second < first, $"5→10 moved {first:0.###} K, 10→20 moved {second:0.###} K");

        async Task<double> Draw(int layers)
        {
            var run = await TransientRunFixture.RunAsync(
                $"m4-storage-header-n{layers}",
                Script(layers, "t=20"),
                new TransientSettings { Horizon = 600, FrameInterval = 60 },
                TestContext.Current.CancellationToken);

            Assert.Equal(layers, run.Snapshot.DifferentialInitial.Length);

            return run.NodeCelsius(run.Frames[^1], "RAD_NETWORK.h");
        }
    }
}
