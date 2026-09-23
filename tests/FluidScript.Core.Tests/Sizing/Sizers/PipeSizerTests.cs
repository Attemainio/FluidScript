using FluidScript.Core.Catalogs;
using FluidScript.Core.Catalogs.Pipes;
using FluidScript.Core.Components;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Sizing;
using FluidScript.Core.Sizing.Sizers;

namespace FluidScript.Core.Tests.Sizing.Sizers;

/// <summary>
/// The pipe rule from <c>plan/20-core-domain/24-auto-sizing.md</c>, held against that document's own
/// worked example.
/// </summary>
/// <remarks>
/// The reference case is the simple loop's <c>P1</c>: 0.2392 kg/s of water at the loop's 35 °C mean,
/// which <c>24</c> sizes to <strong>DN25 at 94.1 Pa/m and 0.411 m/s</strong> after DN15 (1299 Pa/m) and
/// DN20 (292 Pa/m) miss the 150 Pa/m target. Nothing here transcribes a gradient: the sizer builds a
/// one-metre candidate and asks <see cref="PipeComponent.PressureDrop"/>, so this test compares the document
/// against the solver's own friction model rather than against a table someone typed twice.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PipeSizerTests
{
    private static ICatalog<PipeSpec> Steel => SteelEn10255.Instance;

    private static SizingContext At(double massFlow, double celsius)
    {
        var state = Water.Instance.FromPressureTemperature(
            Quantity.FromSi(200_000, Dimension.Pressure),
            Quantity.FromSi(celsius + 273.15, Dimension.Temperature));

        Assert.True(state.IsSuccess, state.Error?.Message);

        // The pipe rule reads neither drop; a valve's authority and a pump's head are what those are
        // for, and both are sized against a circuit rather than against one component.
        return new SizingContext
        {
            State = state.Value,
            MassFlow = massFlow,
            BranchDrop = 0,
            LoopDrop = 0,
        };
    }

    private static SizingResult Size(double massFlow, double celsius, double target = 150)
    {
        var result = new PipeSizer(Steel, target)
            .Size(new PipeComponent("P1", 25, 0.0273), At(massFlow, celsius));

        Assert.True(result.IsSuccess, result.Error?.Message);

        return result.Value;
    }

    [Fact]
    public void TheRuleTakesAPipeAndNothingElse()
    {
        // The sizer is reached through `ISizer.CanSize` alone; this pins the dispatch directly.
        var graph = Topology.GraphFixture.Lower(
            """
            fluidscript 1
            circuit loop
            fluid water
            HE1  heat_exchanger power=30 in.t=20 out.t=50
            LOAD heat_exchanger power=-30 dp=0
            PU1  pump
            P1   pipe length=10
            connections
            N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - P1 - N1
            """).Graph;

        var sizer = new PipeSizer(Steel);

        Assert.True(sizer.CanSize(graph.Components.Single(static c => c.Name == "P1")));
        Assert.False(sizer.CanSize(graph.Components.Single(static c => c.Name == "PU1")));
        Assert.False(sizer.CanSize(graph.Components.Single(static c => c.Name == "HE1")));
    }

    [Fact]
    public void TheSimpleLoopsPipeSizesToTheDiameterTheWorkedExampleGives()
    {
        var sized = Size(0.2392, 35);

        Assert.Equal(25.0, sized.Values["dn"].Value.SiValue);
        Assert.Contains("DN25", sized.Values["dn"].Basis, StringComparison.Ordinal);
        Assert.Contains("Pa/m", sized.Values["dn"].Basis, StringComparison.Ordinal);
        Assert.False(sized.Values["dn"].FromDefault);
        Assert.Empty(sized.Notes);
    }

    [Fact]
    public void TheGradientTheRuleSelectsOnIsTheOneTheSolverWouldSee()
    {
        // `24`'s acceptance criterion: computed, not transcribed. The candidate the rule chose is
        // re-derived here from the same component the residual uses, so the two cannot drift.
        var state = At(0.2392, 35).State;
        var bore = 0.0273;
        var velocity = 0.2392 / state.Density.SiValue / (Math.PI * bore * bore / 4);

        var gradient = new PipeComponent("check", 1, bore, SteelEn10255.Roughness)
            .PressureDrop(velocity, state.Density.SiValue, state.DynamicViscosity.SiValue);

        Assert.Equal(0.411, velocity, 0.01);
        Assert.Equal(94.1, gradient, 3.0);
        Assert.True(gradient <= 150, $"DN25 at {gradient:F1} Pa/m should meet the 150 Pa/m target.");
    }

    [Fact]
    public void ADiameterMeetingTheTargetIsNeverSkippedForASmallerOneThatDoesNot()
    {
        // The rule is "smallest meeting the target", so the size below the chosen one must miss it --
        // otherwise the walk stopped late and every pipe in the model is a size too big.
        var state = At(0.2392, 35).State;
        var below = Steel.Entries.Last(entry => entry.Spec.NominalDiameter < 25);
        var velocity = 0.2392 / state.Density.SiValue
            / (Math.PI * below.Spec.InsideDiameter * below.Spec.InsideDiameter / 4);

        var gradient = new PipeComponent("check", 1, below.Spec.InsideDiameter, below.Spec.Roughness)
            .PressureDrop(velocity, state.Density.SiValue, state.DynamicViscosity.SiValue);

        Assert.True(gradient > 150, $"DN{below.Spec.NominalDiameter} at {gradient:F1} Pa/m already met the target.");
    }

    [Fact]
    public void ALowerTargetBuysExactlyOneCatalogueStepAndTripsTheVelocityFloor()
    {
        // 20 kW of water at 70/40 C is 0.160 kg/s. `C-48`: the whole 100-versus-150 Pa/m argument is one
        // step for this branch, and the 100 Pa/m answer sits under the sedimentation minimum -- which is
        // why the floor has to be a rule rather than a catalogue row nothing reads.
        var loose = Size(0.160, 55);
        var tight = Size(0.160, 55, target: 100);

        Assert.Equal(20.0, loose.Values["dn"].Value.SiValue);
        Assert.Equal(20.0, tight.Values["dn"].Value.SiValue);
        Assert.Contains(tight.Notes, note => note.Contains("stepped down", StringComparison.Ordinal));
    }

    [Fact]
    public void TheVelocityCeilingIsHardAndSaysWhenItMoved()
    {
        // Exercised with a target loose enough that the gradient stops deciding, because at the default
        // 150 Pa/m the ceiling is unreachable -- see the test below. The rule still has to be right for
        // the day a script states its own target.
        var sized = Size(0.2392, 35, target: 100_000);
        Assert.Contains(sized.Notes, note => note.Contains("stepped up", StringComparison.Ordinal));
        // C-74: the same finding as a code the wire can carry, anchored to the pipe.
        var stepped = Assert.Single(sized.Diagnostics, static d => d.Code == "FS2307");
        Assert.Equal("P1", stepped.ComponentName);
        Assert.Contains(sized.Notes, note => note.Contains("stepped up", StringComparison.Ordinal));
        Assert.True(
            SizingDefaults.VelocityMaximum((int)sized.Values["dn"].Value.SiValue)
                >= Velocity(sized.Values["dn"].Value.SiValue, 0.2392, 35),
            "the chosen size still exceeds its own velocity ceiling.");
    }

    [Fact]
    public void AtTheDefaultTargetTheVelocityCeilingNeverBindsAndTheGradientAlwaysDoes()
    {
        // `C-54`. Gradient falls roughly as D^-5 and velocity as D^-2, so a size meeting a 150 Pa/m
        // target is already far below its noise limit -- DN25 in the worked example runs 0.41 m/s
        // against a 1.0 m/s ceiling. `24` presents the velocity check as a step that fires; on this
        // catalogue at this target it does not, and a rule nothing exercises is a rule nothing tests.
        foreach (var flow in new[] { 0.05, 0.1, 0.2392, 0.5, 1.0, 2.0, 5.0, 12.0 })
        {
            var sized = Size(flow, 55);

            Assert.DoesNotContain(sized.Notes, note => note.Contains("stepped up", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AFlowLargerThanTheSeriesClampsToTheLargestSizeAndSaysBothWaysItMissed()
    {
        // 40 kg/s is past the end of EN 10255, which stops at DN150 because the standard does. 12 kg/s
        // is not -- it still lands on DN125 inside the target, which is worth knowing: the series is
        // wider than a domestic reading of it suggests.
        var sized = Size(40.0, 55);

        Assert.Equal(150.0, sized.Values["dn"].Value.SiValue);
        Assert.Contains(sized.Notes, note => note.Contains("largest size", StringComparison.Ordinal));
        Assert.Equal("P1", Assert.Single(sized.Diagnostics, static d => d.Code == "FS2305").ComponentName); // C-74

        // And the velocity ceiling it also breaches, which the rule used to pass over in silence
        // because step 2 can only step up while there is somewhere to step to.
        Assert.Contains(sized.Notes, note => note.Contains("no larger size", StringComparison.Ordinal));
    }

    private static double Velocity(double nominal, double massFlow, double celsius)
    {
        var spec = Steel.Entries.Single(entry => entry.Spec.NominalDiameter == (int)nominal).Spec;
        var density = At(massFlow, celsius).State.Density.SiValue;

        return massFlow / density / (Math.PI * spec.InsideDiameter * spec.InsideDiameter / 4);
    }

    [Fact]
    public void EveryDiameterTheRuleCanChooseIsACatalogueEntryAndCarriesABasis()
    {
        // `24`'s invariants 2 and 5, swept rather than sampled: no continuous value escapes, and no
        // sized value arrives without something a user can argue with.
        var nominals = Steel.Entries.Select(static entry => (double)entry.Spec.NominalDiameter).ToHashSet();

        foreach (var flow in new[] { 0.05, 0.1, 0.2392, 0.5, 1.0, 2.0, 5.0, 12.0, 40.0 })
        {
            var sized = Size(flow, 55);

            Assert.Contains(sized.Values["dn"].Value.SiValue, nominals);
            Assert.NotEmpty(sized.Values["dn"].Basis);
        }
    }

    [Fact]
    public void AFlowOfZeroIsRefusedRatherThanSizedFromNothing()
    {
        var result = new PipeSizer(Steel).Size(new PipeComponent("P1", 25, 0.0273), At(0, 35));

        Assert.False(result.IsSuccess);
    }
}
