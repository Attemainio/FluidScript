using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Sizing;
using FluidScript.Core.Sizing.Sizers;

namespace FluidScript.Core.Tests.Sizing.Sizers;

/// <summary>
/// The exchanger rule from <c>plan/20-core-domain/24-auto-sizing.md</c>: it sizes the design
/// <em>flow</em> a stated or defaulted <c>dp</c> is measured at, and nothing else.
/// </summary>
/// <remarks>
/// The only sizer without a direct test until the 2026-09-14 review, and the one whose own remarks
/// cite a defect (<c>C-59</c>) in exactly this arithmetic. Contexts are built by hand, as the pump
/// tests build theirs: a rule sees the flow through its component and the state there, not a graph.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ExchangerSizerTests
{
    /// <summary>The simple loop's design flow, 30 kW over a 30 K rise, in kg/s.</summary>
    private const double WorkedExampleFlow = 0.2392;

    private static FluidState WaterAt(double celsius)
    {
        var state = Water.Instance.FromPressureTemperature(
            Quantity.FromSi(200_000, Dimension.Pressure),
            Quantity.FromSi(celsius + 273.15, Dimension.Temperature));

        Assert.True(state.IsSuccess, state.Error?.Message);

        return state.Value;
    }

    private static SizingContext At(double massFlow) =>
        new()
        {
            State = WaterAt(20),
            MassFlow = massFlow,
            BranchDrop = 0,
            LoopDrop = null,
        };

    private static HeatExchangerComponent Exchanger(double? statedDrop = null) =>
        new("HE1", power: 30_000)
        {
            StatedParameters = statedDrop is { } stated
                ? ImmutableDictionary<string, Quantity>.Empty.Add("dp", Quantity.FromSi(stated, Dimension.Pressure))
                : ImmutableDictionary<string, Quantity>.Empty,
            DefaultParameters = ImmutableDictionary<string, Quantity>.Empty.Add(
                "dp", Quantity.FromSi(20_000, Dimension.Pressure)),
        };

    private static SizingResult Size(HeatExchangerComponent exchanger, SizingContext context)
    {
        var result = new ExchangerSizer().Size(exchanger, context);

        Assert.True(result.IsSuccess, result.Error?.Message);

        return result.Value;
    }

    [Fact]
    public void TheDesignFlowIsTheFlowTheCircuitRunsAt()
    {
        var sized = Size(Exchanger(), At(WorkedExampleFlow));

        var flow = Assert.Single(sized.Values);

        Assert.Equal("flow", flow.Key);
        Assert.Equal(WorkedExampleFlow, flow.Value.Value.SiValue, 1e-9);
        Assert.False(flow.Value.FromDefault);
    }

    [Fact]
    public void TheBasisNamesTheDropTheFlowIsMeasuredAtAndTheVolumeFlow()
    {
        // 0.2392 kg/s of 20 °C water (998.2 kg/m³) is 0.2396 l/s; a stated 35 kPa is quoted, not the
        // 20 kPa default, because the basis line is what the user reads against their own script.
        var sized = Size(Exchanger(statedDrop: 35_000), At(WorkedExampleFlow));
        var basis = sized.Values["flow"].Basis;

        Assert.Contains("35 kPa", basis, StringComparison.Ordinal);
        Assert.Contains("0.24 l/s", basis, StringComparison.Ordinal);
    }

    [Fact]
    public void ADefaultedDropIsQuotedWhenNothingIsStated()
    {
        var sized = Size(Exchanger(), At(WorkedExampleFlow));

        Assert.Contains("20 kPa", sized.Values["flow"].Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void AReversedFlowSizesByItsMagnitude()
    {
        // The rule pairs a drop with a flow; which way the water runs is the solve's business (D-69).
        var sized = Size(Exchanger(), At(-WorkedExampleFlow));

        Assert.Equal(WorkedExampleFlow, sized.Values["flow"].Value.SiValue, 1e-9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public void NoFlowYetSizesNothingRatherThanPinningAZeroDesignFlow(double massFlow)
    {
        // A design flow of zero makes the quadratic resistance infinite the moment anything flows.
        var sized = Size(Exchanger(), At(massFlow));

        Assert.Empty(sized.Values);
    }

    [Fact]
    public void SomethingThatIsNotAnExchangerIsRefusedNotThrownAt()
    {
        var result = new ExchangerSizer().Size(new PumpComponent("PU1", shutOffHead: 10, curvature: 1), At(WorkedExampleFlow));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ItSizesFlowAndBuildsWithoutAProvisional()
    {
        var sizer = new ExchangerSizer();

        Assert.Equal(["flow"], sizer.Parameters);
        Assert.Empty(sizer.Provisional);
        Assert.True(sizer.CanSize(Exchanger()));
        Assert.False(sizer.CanSize(new PumpComponent("PU1", shutOffHead: 10, curvature: 1)));
    }
}
