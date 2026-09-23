using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Sizing;
using FluidScript.Core.Sizing.Sizers;

namespace FluidScript.Core.Tests.Sizing.Sizers;

/// <summary>
/// The pump rule from <c>plan/20-core-domain/24-auto-sizing.md</c>, held against that document's own
/// worked example.
/// </summary>
/// <remarks>
/// <para>
/// The reference case is the simple loop's <c>PU1</c>: 51.7 kPa around the ring at 0.2392 kg/s, which
/// <c>24</c> converts at the pump's own inlet density and reads as <strong>5.28 m</strong>. Nothing here
/// transcribes that number from a table — the drop goes in, the head comes out, and the density is the
/// one the substance reports for the state the pump is actually standing in.
/// </para>
/// <para>
/// The contexts are built by hand rather than lowered from a script, and <see cref="SizingContext"/>
/// says why: a rule sees the flow through its component and the fluid state there, so it is testable
/// without a graph. What the graph feeds it is <see cref="FluidScript.Core.Solvers.Passes.OuterLoop"/>'s business.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PumpSizerTests
{
    /// <summary>The simple loop's ring resistance at its design flow, in Pa (<c>24</c>).</summary>
    private const double WorkedExampleDrop = 51_700;

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

    private static SizingContext At(
        double? loopDrop, double massFlow = WorkedExampleFlow, double celsius = 20) =>
        new()
        {
            State = WaterAt(celsius),
            MassFlow = massFlow,
            BranchDrop = 0,
            LoopDrop = loopDrop,
        };

    private static SizingResult Size(SizingContext context, double? margin = null)
    {
        var pump = new Pump("PU1", shutOffHead: 10, curvature: 1)
        {
            StatedParameters = margin is { } stated
                ? ImmutableDictionary<string, Quantity>.Empty.Add(
                    "margin", Quantity.FromSi(stated, Dimension.Dimensionless))
                : ImmutableDictionary<string, Quantity>.Empty,
        };

        var result = new PumpSizer().Size(pump, context);

        Assert.True(result.IsSuccess, result.Error?.Message);

        return result.Value;
    }

    private static double Head(SizingResult sized) => sized.Values["head"].Value.SiValue;

    [Fact]
    public void TheSimpleLoopsPumpTakesTheHeadTheWorkedExampleGives()
    {
        var sized = Size(At(WorkedExampleDrop));

        // 51 700 Pa / (998.x kg/m3 * 9.80665) = 5.28 m. `24` prints two decimals and so does the basis,
        // which is why the assertion is written to two rather than to a density this test re-derives.
        Assert.Equal(5.28, Head(sized), 2);
        Assert.Contains("5.28 m", sized.Values["head"].Basis, StringComparison.Ordinal);
        Assert.Contains("0.24 l/s", sized.Values["head"].Basis, StringComparison.Ordinal);
        Assert.Contains("51.7 kPa", sized.Values["head"].Basis, StringComparison.Ordinal);
        Assert.False(sized.Values["head"].FromDefault);
        Assert.Empty(sized.Notes);
    }

    [Fact]
    public void TheHeadIsConvertedAtTheInletRatherThanAtTheLoopMean()
    {
        // `24` flags this as a trap because both readings look right. The same drop is 5.28 m at the
        // pump's 20 C inlet and 5.30 m at the loop's 35 C mean, and the gap grows with the spread --
        // so a rule that quietly took the mean would disagree with the document by more than rounding.
        var inlet = Head(Size(At(WorkedExampleDrop, celsius: 20)));
        var mean = Head(Size(At(WorkedExampleDrop, celsius: 35)));

        Assert.Equal(5.28, inlet, 2);
        Assert.Equal(5.30, mean, 2);
        Assert.True(mean > inlet, "a thinner fluid needs more metres for the same pressure");
    }

    [Fact]
    public void AStatedMarginMultipliesTheHeadAndSaysSoInTheBasis()
    {
        var sized = Size(At(WorkedExampleDrop), margin: 1.1);

        Assert.Equal(5.28 * 1.1, Head(sized), 2);
        Assert.Contains("margin 1.1", sized.Values["head"].Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void AMarginOfOneIsNotWorthSaying()
    {
        // A margin of one is the absence of a margin. Printing it in every basis string would train a
        // reader to skip the clause on the one pump where it is not one.
        Assert.DoesNotContain(
            "margin", Size(At(WorkedExampleDrop), margin: 1).Values["head"].Basis, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "margin", Size(At(WorkedExampleDrop)).Values["head"].Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void APumpOnNoClosedCircuitIsSizedToZeroAndToldWhichThingIsMissing()
    {
        // `C-57`. This is the m1 sample's case: PU1 is declared and never connected, so there is no
        // cycle through it -- which is a missing connection, not a missing loss.
        var sized = Size(At(loopDrop: null));

        Assert.Equal(0, Head(sized));
        Assert.Contains("no closed circuit", Assert.Single(sized.Notes), StringComparison.Ordinal);
        Assert.Contains("no closed circuit", sized.Values["head"].Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void ACircuitNothingDrivesIsToldItHasNoFlowRatherThanNoResistance()
    {
        // `C-57` again, and the reason the three cases are separated: at zero flow every loss law
        // returns zero, so a real circuit full of real pipes reports a drop of zero. Blaming the pipes
        // sends the reader to the wrong line -- what is missing is the duty.
        var sized = Size(At(loopDrop: 0, massFlow: 0));

        Assert.Equal(0, Head(sized));
        Assert.Contains("no flow", Assert.Single(sized.Notes), StringComparison.Ordinal);
        // C-74: 24's FS2304, an error, because a head sized against no flow is not a size.
        var code = Assert.Single(sized.Diagnostics);
        Assert.Equal("FS2304", code.Code);
        Assert.Equal(FluidScript.Core.Diagnostics.DiagnosticSeverity.Error, code.Severity);
    }

    [Fact]
    public void ACircuitOfIdealLinksIsToldItHasNoModelledResistance()
    {
        var sized = Size(At(loopDrop: 0));

        Assert.Equal(0, Head(sized));
        Assert.Contains("no modelled resistance", Assert.Single(sized.Notes), StringComparison.Ordinal);
        // C-74: FS2312 (D-25's informational) on the wire, anchored to the pump.
        var code = Assert.Single(sized.Diagnostics);
        Assert.Equal("FS2312", code.Code);
        Assert.Equal("PU1", code.ComponentName);
    }

    [Fact]
    public void ANetRiseAroundTheLoopClampsToZeroRatherThanToANegativeHead()
    {
        // A second pump on the ring can out-push everything that resists, and `Resistance` reports a
        // pump's contribution as negative because that is what a pump does to a loop. A negative head
        // is not a pump, so the clamp is the honest reading and the note explains the zero.
        var sized = Size(At(loopDrop: -12_000));

        Assert.Equal(0, Head(sized));
        Assert.Contains("no modelled resistance", Assert.Single(sized.Notes), StringComparison.Ordinal);
    }

    [Fact]
    public void ADropThatIsNotANumberIsAFailureRatherThanAHeadOfNaN()
    {
        var result = new PumpSizer().Size(new Pump("PU1", 10, 1), At(double.NaN));

        Assert.False(result.IsSuccess);
        Assert.Contains("resistance is not yet known", result.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRuleClaimsHeadAndNothingElse()
    {
        var sizer = new PumpSizer();

        Assert.Equal("head", Assert.Single(sizer.Parameters));
        Assert.Empty(sizer.Provisional);
        Assert.True(sizer.CanSize(new Pump("PU1", 10, 1)));
        Assert.False(sizer.CanSize(new Pipe("P1", 25, 0.0273)));
    }

    [Fact]
    public void TheRuleRefusesAComponentThatIsNotAPump()
    {
        // `CanSize` is the gate, but a rule that trusted it and cast anyway would throw on the day a
        // caller skipped the gate. A pipeline stage returns its refusal instead.
        var result = new PumpSizer().Size(new Pipe("P1", 25, 0.0273), At(WorkedExampleDrop));

        Assert.False(result.IsSuccess);
        Assert.Contains("not a pump", result.Error!.Message, StringComparison.Ordinal);
    }
}
