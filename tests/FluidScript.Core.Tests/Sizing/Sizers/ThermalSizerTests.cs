using System.Collections.Immutable;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Sizing;
using FluidScript.Core.Sizing.Sizers;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Sizing.Sizers;

/// <summary>
/// The extended-mode exchanger rule from <c>plan/20-core-domain/24-auto-sizing.md</c>: a design point
/// stated as four temperatures and a duty, turned into <c>UA</c>, an area and a plate count.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The substation's numbers are the fixture, and two routes must reach them.</strong> The rule
/// sizes by ε-NTU; <see cref="LogMeanTemperatureDifference"/> is a separate derivation sharing no code,
/// and <c>22</c> requires the two to agree on <see cref="ReferenceNumbers.Substation.RequiredUa"/> --
/// which is the one check that catches a wrong effectiveness formula, because the inverse of a wrong
/// formula still round-trips through itself.
/// </para>
/// <para>
/// Contexts are built by hand, as the other sizer tests build theirs: the rule reads the exchanger's
/// stated parameters and the substance, and the circuit only for a Rated exchanger's side-1 flow.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ThermalSizerTests
{
    private static readonly Dimension ConductancePerKelvin =
        Dimension.FromVector(new DimensionVector(Mass: 1, Length: 2, Time: -3, Temperature: -1));

    private static readonly Dimension HeatTransferCoefficient =
        Dimension.FromVector(new DimensionVector(Mass: 1, Length: 0, Time: -3, Temperature: -1));

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
            State = WaterAt(50),
            MassFlow = massFlow,
            BranchDrop = 0,
            LoopDrop = null,
        };

    private static Quantity Celsius(double value) => Quantity.FromSi(value + 273.15, Dimension.Temperature);

    private static Quantity Kelvin(double value) => Quantity.FromSi(value, Dimension.TemperatureDelta);

    /// <summary>The substation's design point: 150 kW, 40/60 secondary, 85/45 primary, counterflow.</summary>
    private static ImmutableDictionary<string, Quantity> DesignPoint(
        double in2 = 85,
        double out2 = 45,
        params (string Name, Quantity Value)[] extra)
    {
        var stated = ImmutableDictionary<string, Quantity>.Empty
            .Add("in", Celsius(40))
            .Add("out", Celsius(60))
            .Add("in2", Celsius(in2))
            .Add("out2", Celsius(out2));

        foreach (var (name, value) in extra)
        {
            stated = stated.SetItem(name, value);
        }

        return stated;
    }

    private static HeatExchanger Coupled(ImmutableDictionary<string, Quantity> stated, double power = 150_000) =>
        new("HX1", power, secondarySideConnected: true)
        {
            Rating = new ExchangerRating { Mode = ExchangerMode.Coupled },
            StatedParameters = stated,
        };

    private static SizingResult Size(HeatExchanger exchanger, SizingContext context)
    {
        var result = new ThermalSizer().Size(exchanger, context);

        Assert.True(result.IsSuccess, result.Error?.Message);

        return result.Value;
    }

    // ---- the substation by both routes --------------------------------------------------------------

    [Fact]
    public void TheSubstationsDesignPointSizesTheConductanceTheVisionWorksByHand()
    {
        // 01: C1 = 150 kW / 20 K = 7500 W/K, C2 = 150 kW / 40 K = 3750 W/K, Cr = 0.5, ε = 150 / (3750 · 45)
        // = 0.8889, counterflow NTU = ln((1 − ε·Cr)/(1 − ε)) / (1 − Cr) = 2 ln 5 = 3.219, UA = 12 071 W/K.
        var sized = Size(Coupled(DesignPoint()), At(1.79));

        var ua = sized.Values["ua"];

        Assert.Equal(ReferenceNumbers.Substation.RequiredUa, ua.Value.SiValue, 1.0);
        Assert.False(ua.FromDefault);
        Assert.Contains("NTU 3.219", ua.Basis, StringComparison.Ordinal);
        Assert.Contains("ε 0.8889", ua.Basis, StringComparison.Ordinal);
        Assert.Contains("Cr 0.5", ua.Basis, StringComparison.Ordinal);
        Assert.Contains("approach 5 K", ua.Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLogMeanRouteReachesTheSameConductanceSharingNoCode()
    {
        // 22's P4.1 criterion. The rule inverted ε-NTU; this is Q / LMTD with the terminal differences
        // 85 − 60 = 25 K and 45 − 40 = 5 K, LMTD = 20 / ln 5 = 12.427 K. Agreement within 0.1 % is the
        // whole point -- a mistaken effectiveness formula still round-trips through its own inverse.
        var lmtd = LogMeanTemperatureDifference.Counterflow(
            hotIn: 85 + 273.15, hotOut: 45 + 273.15, coldIn: 40 + 273.15, coldOut: 60 + 273.15);
        var byLogMean = LogMeanTemperatureDifference.Conductance(150_000, lmtd);
        var byEffectiveness = Size(Coupled(DesignPoint()), At(1.79)).Values["ua"].Value.SiValue;

        Assert.Equal(ReferenceNumbers.Substation.Lmtd, lmtd, 0.001);
        Assert.Equal(ReferenceNumbers.Substation.RequiredUa, byLogMean, 1.0);
        Assert.Equal(byLogMean, byEffectiveness, byLogMean * 1e-3);
    }

    [Fact]
    public void AStatedCoefficientTurnsTheConductanceIntoAnArea()
    {
        // 12 071 W/K over 3300 W/(m²·K) is 3.658 m². `u` alone is not a size, so `ua` is still sized.
        var sized = Size(
            Coupled(DesignPoint(extra: [("u", Quantity.FromSi(3300, HeatTransferCoefficient))])),
            At(1.79));

        Assert.Equal(ReferenceNumbers.Substation.RequiredArea, sized.Values["area"].Value.SiValue, 0.001);
        Assert.Contains("u 3300", sized.Values["area"].Basis, StringComparison.Ordinal);
        Assert.True(sized.Values.ContainsKey("ua"));
    }

    [Fact]
    public void APlateAreaTurnsTheAreaIntoAPlateCountRoundedUp()
    {
        // 3.658 m² over 0.1 m² a plate is 36.58 -> 37 transfer plates, plus the two end plates that have
        // fluid on one side only: 39, and 3.70 m² installed. The surplus is 1.1 %, under the 2 % that
        // earns a note -- but the approach closes from 5 K to 4.9 K, and the basis says so.
        var sized = Size(
            Coupled(DesignPoint(
                extra:
                [
                    ("u", Quantity.FromSi(3300, HeatTransferCoefficient)),
                    ("plate_area", Quantity.FromSi(0.1, Dimension.Area)),
                ])),
            At(1.79));

        var plates = sized.Values["plates"];

        Assert.Equal(ReferenceNumbers.Substation.TotalPlates, (int)plates.Value.SiValue);
        Assert.Contains("3.7 m²", plates.Basis, StringComparison.Ordinal);
        Assert.Contains("approach 4.9", plates.Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void ACoarsePlateRoundsUpPastTheReportingThresholdAndSaysSoAsFS2310()
    {
        // 3.658 m² over 5 m² a plate is one transfer plate and 5 m² installed, 37 % more area; the
        // duty follows the effectiveness curve, not the area, and is 5.9 % over the 150 kW stated --
        // above the 2 % `hx.overshoot_report` allows in silence (24). At 1 m² a plate the same rounding
        // is 9 % of area and 1.9 % of duty, and says nothing. C-74: the finding is a note for the
        // explanation and FS2310 on the wire, anchored to the exchanger.
        var sized = Size(
            Coupled(DesignPoint(
                extra:
                [
                    ("u", Quantity.FromSi(3300, HeatTransferCoefficient)),
                    ("plate_area", Quantity.FromSi(5.0, Dimension.Area)),
                ])),
            At(1.79));

        var overshoot = Assert.Single(sized.Diagnostics, static d => d.Code == "FS2310");
        Assert.Equal("HX1", overshoot.ComponentName);
        Assert.Contains("3.658 m² was needed", overshoot.Message, StringComparison.Ordinal);
        Assert.Contains(sized.Notes, note => note == overshoot.Message);
    }

    [Fact]
    public void AStatedConductanceIsNotSizedAgainOnlyDerivedFrom()
    {
        // The user's `ua=10000` is the size, whatever the design point wants; the rule reports what it
        // achieves and still turns it into an area when it can.
        var sized = Size(
            Coupled(DesignPoint(
                extra:
                [
                    ("ua", Quantity.FromSi(10_000, ConductancePerKelvin)),
                    ("u", Quantity.FromSi(2000, HeatTransferCoefficient)),
                ])),
            At(1.79));

        Assert.False(sized.Values.ContainsKey("ua"));
        Assert.Equal(5.0, sized.Values["area"].Value.SiValue, 1e-9);
    }

    [Fact]
    public void WithoutACoefficientNoAreaIsInventedAndTheNoteSaysSo()
    {
        // D-19 leaves `u` to the catalogue, which has no sourced coefficient yet. Guessing one would
        // print an area the user cannot trace to anything they wrote.
        var sized = Size(Coupled(DesignPoint()), At(1.79));

        Assert.False(sized.Values.ContainsKey("area"));
        Assert.Contains(sized.Notes, static note => note.Contains("no area follows", StringComparison.Ordinal));
    }

    // ---- the feasibility bound and the approach floor ----------------------------------------------

    [Fact]
    public void FS2111_ADutyBeyondWhatTheInletsAllowIsRefusedBeforeAnyInversion()
    {
        // 70/30 on the primary: C2 = 150 / 40 = 3750 W/K is Cmin, and the most any exchanger could move
        // is Cmin · (70 − 40) = 112.5 kW. 150 kW would need ε = 1.33, which no arrangement reaches.
        var sized = Size(Coupled(DesignPoint(in2: 70, out2: 30)), At(1.79));

        var diagnostic = Assert.Single(sized.Diagnostics);

        Assert.Equal("FS2111", diagnostic.Code);
        Assert.Contains("112.5 kW", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("70 °C", diagnostic.Message, StringComparison.Ordinal);
        Assert.Empty(sized.Values);
    }

    [Fact]
    public void FS4008_AnApproachBelowTheMinimumIsReportedWithTheSizeStillProduced()
    {
        // 62/45 on the primary against 40/60: the hot end closes to 62 − 60 = 2 K, under `hx.approach_min`
        // (3 K). Feasible -- Cmin is side 1's 7500 W/K and ε = 150 / (7500 · 22) = 0.91 -- and the size is
        // still reported, because the diagnostic is about the design, not about the arithmetic.
        var sized = Size(Coupled(DesignPoint(in2: 62, out2: 45)), At(1.79));

        var diagnostic = Assert.Single(sized.Diagnostics);

        Assert.Equal("FS4008", diagnostic.Code);
        Assert.Contains("approach is 2 K", diagnostic.Message, StringComparison.Ordinal);
        Assert.True(sized.Values.ContainsKey("ua"));
    }

    [Fact]
    public void AStatedApproachLowersTheFloorTheDesignIsHeldTo()
    {
        var sized = Size(Coupled(DesignPoint(62, 45, ("approach", Kelvin(2)))), At(1.79));

        Assert.Empty(sized.Diagnostics);
    }

    // ---- what the rule reads from where -------------------------------------------------------------

    [Fact]
    public void ARatedExchangerTakesItsMissingSideOneFlowFromTheCircuit()
    {
        // `in.t=40 in[2].t=85 out[2].t=45`, no `out` and no `dt`: side 1's capacity rate is the branch's own flow
        // times cp at 40 °C. At 1.7932 kg/s that is the substation's 7500 W/K, and UA lands on 12 071.
        var stated = ImmutableDictionary<string, Quantity>.Empty
            .Add("in", Celsius(40))
            .Add("in2", Celsius(85))
            .Add("out2", Celsius(45));
        var rated = new HeatExchanger("HX1", 150_000)
        {
            Rating = new ExchangerRating
            {
                Mode = ExchangerMode.Rated,
                SecondaryInletTemperature = 85 + 273.15,
                SecondaryCapacityRate = 3750,
            },
            StatedParameters = stated,
        };

        var sized = Size(rated, At(1.7932));

        Assert.Equal(ReferenceNumbers.Substation.RequiredUa, sized.Values["ua"].Value.SiValue, 20.0);
    }

    [Fact]
    public void ADesignPointThatFixesOnlyOneSideSizesNothingAndSaysWhich()
    {
        var stated = ImmutableDictionary<string, Quantity>.Empty
            .Add("in", Celsius(40))
            .Add("out", Celsius(60));

        var sized = Size(Coupled(stated), At(1.79));

        Assert.Empty(sized.Values);
        Assert.Contains(sized.Notes, static note => note.Contains("does not fix both sides", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyAnExtendedModeExchangerIsSizable()
    {
        var duty = new HeatExchanger("HE1", 30_000);

        Assert.False(new ThermalSizer().CanSize(duty));
        Assert.True(new ThermalSizer().CanSize(Coupled(DesignPoint())));
    }
}
