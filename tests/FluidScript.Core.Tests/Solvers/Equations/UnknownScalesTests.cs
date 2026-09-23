using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>The scale vector: what makes one convergence tolerance mean one thing.</summary>
[Trait("Category", "Unit")]
public sealed class UnknownScalesTests
{
    [Fact]
    public void APressureAndAnEnthalpyTakeTheirWorkingRangeAndAFlowTakesItsOwn()
    {
        // 36's table. A pressure's magnitude is a property of the fluid and the same everywhere; a
        // flow's is a property of this branch and nothing outside the model knows it.
        var (layout, scales) = Scale("m2-cooling-loop.fluid", flow: 0.24);

        Assert.Equal(1e5, Of(layout, scales, UnknownKind.NodePressure).Distinct().Single());
        Assert.Equal(1e5, Of(layout, scales, UnknownKind.NodeEnthalpy).Distinct().Single());
        Assert.Equal(0.24, Of(layout, scales, UnknownKind.BranchFlow).Distinct().Single(), 1e-12);
    }

    [Fact]
    public void ABranchTheSeedPutsAtRestTakesTheFloorRatherThanZero()
    {
        // The whole reason there is a floor. A closed valve's branch is a legitimate part of a real
        // circuit and its seeded flow is exactly zero; without the floor every residual on it is
        // divided by nothing.
        var (layout, scales) = Scale("m2-cooling-loop.fluid", flow: 0);

        Assert.Equal(1e-3, Of(layout, scales, UnknownKind.BranchFlow).Distinct().Single(), 1e-12);
    }

    [Fact]
    public void AReversedFlowScalesByItsMagnitude()
    {
        // Reverse flow is legal, and a negative scale would flip the sign of every residual on that
        // branch -- which would look exactly like a sign error in the component.
        var (layout, scales) = Scale("m2-cooling-loop.fluid", flow: -0.24);

        Assert.Equal(0.24, Of(layout, scales, UnknownKind.BranchFlow).Distinct().Single(), 1e-12);
    }

    [Fact]
    public void EveryScaleIsPositiveOnEverySample()
    {
        // A zero or negative scale is a division that produces infinity or a flipped sign, and neither
        // presents as a scaling bug: the first is FS3007 somewhere unrelated and the second is a
        // component that appears to have its convention backwards.
        foreach (var path in Directory.GetFiles(RepositoryLayout.Samples, "*.fluid").Order(StringComparer.Ordinal))
        {
            var graph = GraphFixture.Lower(File.ReadAllText(path)).Graph;
            var layout = SystemLayout.Build(graph, WellPosedness.Check(graph).Counting);
            var scales = UnknownScales.Build(layout, new StateVector([]));

            Assert.Equal(layout.Count, scales.Length);
            Assert.All(scales, static scale => Assert.True(scale > 0));
        }
    }

    [Fact]
    public void ScalingBringsTheSeedsMagnitudesWithinTwoOrdersOfEachOther()
    {
        // 36's acceptance criterion, and the entire argument for scaling in one number. Unscaled, a
        // circuit's unknowns span 1e5 Pa against 2.4e-1 kg/s -- six orders -- so a convergence test on
        // the raw norm measures the pressures and nothing else.
        var graph = GraphFixture.Lower(GraphFixture.CoolingLoop).Graph;
        var layout = SystemLayout.Build(graph, WellPosedness.Check(graph).Counting);

        var seed = Seed(layout, flow: 0.24);
        var scales = UnknownScales.Build(layout, seed);

        var scaled = seed.Values.Select((value, index) => Math.Abs(value) / scales[index])
            .Where(static value => value > 0)
            .ToArray();

        var spread = scaled.Max() / scaled.Min();

        Assert.True(spread <= 100, $"the scaled seed still spans {spread:0.#} orders.");

        // And unscaled it does not, which is what makes the assertion above worth making.
        var raw = seed.Values.Select(Math.Abs).Where(static value => value > 0).ToArray();

        Assert.True(raw.Max() / raw.Min() > 1e4);
    }

    private static IEnumerable<double> Of(
        SystemLayout layout, ImmutableArray<double> scales, UnknownKind kind) =>
        layout.Unknowns.Where(unknown => unknown.Kind == kind).Select(unknown => scales[unknown.Index]);

    private static (SystemLayout Layout, ImmutableArray<double> Scales) Scale(string sample, double flow)
    {
        var graph = GraphFixture
            .Lower(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample)))
            .Graph;

        var layout = SystemLayout.Build(graph, WellPosedness.Check(graph).Counting);
        var seed = Seed(layout, flow);

        return (layout, UnknownScales.Build(layout, seed));
    }

    /// <summary>A seed with plausible magnitudes, which is all a scale vector reads.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="flow">kg/s to put on every branch.</param>
    /// <returns>The seed.</returns>
    private static StateVector Seed(SystemLayout layout, double flow) =>
        new([.. layout.Unknowns.Select(unknown => unknown.Kind switch
        {
            UnknownKind.BranchFlow or UnknownKind.ExternalMassFlux => flow,
            UnknownKind.NodePressure => 300_000.0,
            UnknownKind.NodeEnthalpy => 167_500.0,
            _ => 1.0,
        })]);
}
