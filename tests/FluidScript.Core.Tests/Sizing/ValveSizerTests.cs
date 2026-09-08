using System.Collections.Immutable;

using FluidScript.Core.Catalogs;
using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Sizing;
using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Sizing;

/// <summary>
/// The control-valve rule from <c>plan/20-core-domain/24-auto-sizing.md</c>, held against that
/// document's own worked example.
/// </summary>
/// <remarks>
/// <para>
/// The reference case is the simple loop's <c>CV1</c>: 0.2392 kg/s through a branch that drops
/// 22.35 kPa without the valve, at a target authority of 0.5. <c>24</c> asks for Kv 1.833, takes
/// <strong>Kv 1.6</strong> after rounding down, and reports an achieved authority of
/// <strong>0.57</strong> — higher than the target, which is what rounding down to a smaller valve must
/// always do.
/// </para>
/// <para>
/// Nothing here writes the Kv relation out. The rule inverts <see cref="ValveLaw"/>, and so does the
/// expectation, so the two agree or the law is wrong — the √10⁵ in that conversion is a trap the
/// document names twice.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ValveSizerTests
{
    /// <summary>The simple loop's branch drop excluding its valve, in Pa (<c>24</c>).</summary>
    private const double WorkedExampleBranchDrop = 22_350;

    /// <summary>The simple loop's design flow, in kg/s.</summary>
    private const double WorkedExampleFlow = 0.2392;

    private static SizingContext At(
        double branchDrop, double massFlow = WorkedExampleFlow, double celsius = 20)
    {
        var state = Water.Instance.FromPressureTemperature(
            Quantity.FromSi(200_000, Dimension.Pressure),
            Quantity.FromSi(celsius + 273.15, Dimension.Temperature));

        Assert.True(state.IsSuccess, state.Error?.Message);

        return new SizingContext
        {
            State = state.Value,
            MassFlow = massFlow,
            BranchDrop = branchDrop,

            // The valve rule reads the branch, never the loop: authority is a statement about the
            // branch a valve controls, and a loop total would make a valve's authority depend on
            // circuits it has no effect on.
            LoopDrop = 999_999,
        };
    }

    private static SizingResult Size(SizingContext context, double? authority = null, double target = 0.5)
    {
        var valve = new Valve("CV1", kv: 630)
        {
            StatedParameters = authority is { } stated
                ? ImmutableDictionary<string, Quantity>.Empty.Add(
                    "authority", Quantity.FromSi(stated, Dimension.Dimensionless))
                : ImmutableDictionary<string, Quantity>.Empty,
        };

        var result = new ValveSizer(ValveKvR5.Instance, target).Size(valve, context);

        Assert.True(result.IsSuccess, result.Error?.Message);

        return result.Value;
    }

    private static double Kv(SizingResult sized) => sized.Values["kv"].Value.SiValue;

    private static double Authority(SizingResult sized) => sized.Values["authority"].Value.SiValue;

    [Fact]
    public void TheWorkedExamplesValveTakesTheKvTheDocumentTakes()
    {
        var sized = Size(At(WorkedExampleBranchDrop));

        Assert.Equal(1.6, Kv(sized));
        Assert.Contains("Kv 1.6", sized.Values["kv"].Basis, StringComparison.Ordinal);
        Assert.Contains("R5 preferred numbers", sized.Values["kv"].Basis, StringComparison.Ordinal);
        Assert.False(sized.Values["kv"].FromDefault);
        Assert.Empty(sized.Notes);
    }

    [Fact]
    public void TheAchievedAuthorityIsReportedAndIsAboveTheTarget()
    {
        // `24`'s acceptance criterion, and the reason rounding down is the safe direction: a smaller
        // valve drops more, so it takes a larger share than asked for. A report showing an achieved
        // authority *below* the target after rounding down would mean the rounding went the wrong way.
        //
        // `24` prints 0.57 and this is 0.565, and the whole of the difference is the density: the
        // document works its example at the loop's 35 C mean and required Kv 1.833, the rule works it
        // at the valve's own 20 C inlet and requires 1.823, and the achieved share follows the square
        // of that ratio. Third instance of the same basis difference after the pipe gradient and the
        // required Kv, and the first where it moves a number that is *reported* rather than one that
        // only selects a row.
        var sized = Size(At(WorkedExampleBranchDrop));

        Assert.Equal(0.565, Authority(sized), 3);
        Assert.True(Authority(sized) > 0.5, "rounding down can only raise authority");
        Assert.Contains(
            "0.56 achieved against a target of 0.5",
            sized.Values["authority"].Basis,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheChosenValveDropsWhatTheLawSaysItDrops()
    {
        // Computed, not transcribed: the drop implied by the reported authority is re-derived from
        // `ValveLaw` itself, so the rule's arithmetic and the solver's cannot drift apart.
        var sized = Size(At(WorkedExampleBranchDrop));
        var achieved = Authority(sized);
        var implied = achieved * WorkedExampleBranchDrop / (1 - achieved);
        var recovered = ValveLaw.RequiredKv(WorkedExampleFlow, implied, 998.2);

        Assert.Equal(Kv(sized), recovered, 2);
    }

    [Fact]
    public void AStatedAuthorityIsTheTargetRatherThanThePreference()
    {
        // `24`'s invariant 1 in its sizing form: a stated parameter is a constraint. A script asking
        // for 0.7 gets a valve sized to 0.7 and not to the rule's default.
        var loose = Size(At(WorkedExampleBranchDrop), authority: 0.3);
        var tight = Size(At(WorkedExampleBranchDrop), authority: 0.7);

        Assert.True(Kv(tight) < Kv(loose), "more authority is a smaller valve");
        Assert.True(Authority(tight) > Authority(loose));
    }

    [Fact]
    public void AHalfAuthorityTargetAsksForExactlyTheBranchsOwnDrop()
    {
        // a * (rest + valve) = valve at a = 0.5 gives valve = rest, which is the one point on the
        // curve that can be checked without inverting anything.
        var sized = Size(At(10_000), target: 0.5);
        var wanted = ValveLaw.RequiredKv(WorkedExampleFlow, 10_000, 998.2);

        // The catalogue rounds down from `wanted`, so the chosen row is at or below it and within one
        // R5 step -- 1.6x, which is the coarseness `C-49` is about.
        Assert.True(Kv(sized) <= wanted);
        Assert.True(Kv(sized) * 1.6 > wanted);
    }

    [Fact]
    public void ABranchThatResistsNothingIsRefusedRatherThanSizedIntoTheSmoothingBand()
    {
        // Below the regularisation drop the Kv law is a quadratic that exists to keep a *closed* valve
        // differentiable. A valve sized to sit there has an authority that is not what the arithmetic
        // says, so the rule declines and says what to do instead.
        var sized = Size(At(0));

        Assert.Empty(sized.Values);
        Assert.Contains("could not be sized", Assert.Single(sized.Notes), StringComparison.Ordinal);
        Assert.Contains("smoothing band", Assert.Single(sized.Notes), StringComparison.Ordinal);
    }

    [Fact]
    public void AFlowTooLargeForTheSeriesClampsAndSaysSo()
    {
        // 200 kg/s through a branch dropping 22 kPa wants a Kv far above the series.
        var sized = Size(At(WorkedExampleBranchDrop, massFlow: 200));

        Assert.Equal(630, Kv(sized));
        Assert.Contains("the series stops at 630", Assert.Single(sized.Notes), StringComparison.Ordinal);
    }

    [Fact]
    public void AValveWithNoAuthorityLeftIsSaidToBeASwitch()
    {
        // `FS4006`. A tiny flow through a branch that resists a great deal cannot be controlled by
        // anything in the catalogue: the smallest row still passes more than its share.
        var sized = Size(At(400_000, massFlow: 0.02));

        Assert.True(Authority(sized) < SizingDefaults.ValveAuthorityMinimum);
        Assert.Contains("behave as a switch", string.Join(" ", sized.Notes), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRuleClaimsKvAndAuthorityAndNothingElse()
    {
        var sizer = new ValveSizer(ValveKvR5.Instance);

        Assert.Equal(["kv", "authority"], sizer.Parameters);
        Assert.True(sizer.CanSize(new Valve("CV1", 1.6)));
        Assert.False(sizer.CanSize(new Pipe("P1", 25, 0.0273)));
    }

    [Fact]
    public void TheProvisionalIsTheLargestRowSoABootstrapIsBarelyDisturbed()
    {
        // A valve has no component at all without a `kv`, so something has to go in before flows can be
        // estimated. The smallest row would hand every other rule on the loop a branch drop dominated
        // by a valve nobody has sized yet; the largest is the nearest thing the series has to an open
        // port.
        var sizer = new ValveSizer(ValveKvR5.Instance);

        Assert.Equal(630, sizer.Provisional["kv"].SiValue);
    }

    [Fact]
    public void TheRuleRefusesAComponentThatIsNotAValve()
    {
        var result = new ValveSizer(ValveKvR5.Instance)
            .Size(new Pipe("P1", 25, 0.0273), At(WorkedExampleBranchDrop));

        Assert.False(result.IsSuccess);
        Assert.Contains("two-way valve", result.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AThreeWayValveUsedAsATwoWayIsSizedByThisRule()
    {
        // `C-61`. With the bypass unconnected the component is hydraulically a two-way valve, and the
        // topology says so: measured on `m2-distribution-header`, `TV_AHU` is not a junction element and
        // sits inside a branch's `Path`, so it gets the same single-branch context a `valve` gets.
        // Excluding it by type left it holding the bootstrap Kv 630 for the whole run.
        var valve = new ThreeWayValve("TV1", 630, bypassConnected: false);
        var result = new ValveSizer(ValveKvR5.Instance).Size(valve, At(WorkedExampleBranchDrop));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1.6, result.Value.Values["kv"].Value.SiValue);
    }

    [Fact]
    public void AThreeWayValveWithItsBypassConnectedIsStillRefused()
    {
        // It stands on three branches at once, so a single-branch context does not describe it and the
        // authority its `BranchDrop` reports is one leg of three. `24`'s three-way rule and an
        // `OuterLoop` pass own that case (`C-61`); answering it here would look right and be wrong.
        var valve = new ThreeWayValve("3WV", 630, bypassConnected: true);

        Assert.False(new ValveSizer(ValveKvR5.Instance).CanSize(valve));

        var result = new ValveSizer(ValveKvR5.Instance).Size(valve, At(WorkedExampleBranchDrop));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ATargetOutsideZeroToOneIsAFailureRatherThanANegativeKv()
    {
        // a/(1-a) is negative above 1 and blows up at it, so the guard is on the target rather than on
        // the number that comes out of it.
        foreach (var target in new[] { 0.0, 1.0, 1.5, -0.2 })
        {
            var result = new ValveSizer(ValveKvR5.Instance, target)
                .Size(new Valve("CV1", 630), At(WorkedExampleBranchDrop));

            Assert.False(result.IsSuccess);
        }
    }
}
