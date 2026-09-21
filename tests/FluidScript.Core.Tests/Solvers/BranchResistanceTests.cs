using FluidScript.Core.Components;
using FluidScript.Core.Sizing;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>What a component's own law says it resists, read for the seed and read for sizing (<c>S-46</c>, <c>S-47</c>).</summary>
public sealed class BranchResistanceTests
{
    private const string Loop =
        """
        fluidscript 1
        circuit heating
        fluid water

        PU  pump head=5 m
        TV  valve kv=1
        HE  load in.t=80 out.t=60 power=20 kW
        N1  node p=150 kPa

        N1 - PU - TV - HE - N1
        """;

    /// <summary>A valve read at a flow it would never pass is believed only up to the top of the selection band by the seed, and in full by sizing (<c>S-47</c>).</summary>
    [Fact]
    public void TheSeedCapsAValveAtTheSelectionBandAndSizingReadsItInFull()
    {
        var graph = GraphFixture.Lower(Loop).Graph;
        var valve = Assert.Single(graph.Components.OfType<Valve>());
        var state = graph.Substance.FromPressureTemperature(
            Quantity.FromSi(1.5e5, Dimension.Pressure),
            Quantity.FromSi(343.15, Dimension.Temperature)).Value;

        // 1 kg/s through Kv 1 is about 1.3 MPa: a flow no circuit here drives through that valve.
        var law = ValveLaw.PressureDrop(1, 1.0, state.Density.SiValue);
        Assert.True(law > 1e6, $"the law gives {law} Pa");

        var sizing = BranchResistance.Of(graph, state, valve, 1.0);
        var seed = BranchResistance.Of(graph, state, valve, 1.0, bound: Tolerances.SeedValveExcursion);

        Assert.Equal(law, sizing, law * 1e-6);
        Assert.Equal(Tolerances.SeedValveExcursion, seed, 1e-9);
    }

    /// <summary>The seed's excursion is the top of the published selection band, and moving either one alone is a change the corpus has to be re-measured under.</summary>
    [Fact]
    public void TheSeedBelievesAValveUpToTheTopOfTheSelectionBand()
    {
        Assert.Equal(SizingDefaults.ThreeWayDropMaximum, Tolerances.SeedValveExcursion);
    }
}
