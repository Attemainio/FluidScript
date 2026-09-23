using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Solvers.Transient;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology.Construction;

namespace FluidScript.Core.Tests.Solvers.Transient;

/// <summary>Where a run's t = 0 sits on a time curve, and the parameters that follow the clock (<c>D-149</c>, <c>S-79</c>).</summary>
[Trait("Category", "Unit")]
public sealed class CurveClockTests
{
    /// <summary>
    /// The demand-step loop with its duty read off a weather chain: <c>heating</c> by the outdoor
    /// temperature, the outdoor temperature by the clock. 2026-01-15T06:00 is Unix 1 768 456 800; the
    /// outdoor air warms from −26 to −6 °C over the first five minutes, so the duty falls from 30 to 15 kW.
    /// </summary>
    private const string Weather = """
        fluidscript 1
        project dynamic demand{START}
        design tout=-26

        curve outdoor time
        2026-01-15T06:00:00  -26
        2026-01-15T06:05:00  -6

        curve heating outdoor
        -26  30
        -6   15

        circuit demandStep
        fluid dynamic water

        HE1 heat_exchanger power=heating out.t=50
        3WV three_way_valve
        PU1 pump
        P1  pipe length=25
        PB  pipe length=8 dn=20 nodes=4
        TC1 pi

        control actuate=3WV.position measure=NS.t by=TC1 setpoint=20

        connections
        N1 - N2
        N2 - NS
        NS - PU1
        PU1 - HE1
        HE1 - 3WV
        3WV - PB - N2
        3WV - P1
        P1 - N3

        N1 inlet t=6 p=300
        N3 outlet p=280
        """;

    private static string Script(string start = " start=\"2026-01-15T06:00:00\"") =>
        Weather.Replace("{START}", start, StringComparison.Ordinal);

    private static BindResult Bind(string text) =>
        new Binder(ComponentRegistry.Default).Bind(FluidScriptParser.Parse(new SourceText(text)));

    [Fact]
    public void TheStartIsReadAsATimeCurvesRowsAreRead()
    {
        var result = Bind(Script());

        Assert.DoesNotContain(result.Diagnostics, d => d.Code is "FS1545" or "FS1546" or "FS1547");
        Assert.Equal(1_768_456_800, result.Model.Project.Start);
        Assert.Equal(1_768_456_800, Bind(Script(" start=1768456800")).Model.Project.Start);
    }

    [Theory]
    [InlineData(" start=2026")]
    [InlineData(" start=\"15.1.2026 06:00\"")]
    [InlineData(" start=\"2026-01-15T06:00:00+02:00\"")]
    public void AStartThatIsNotATimeIsFs1545(string start)
    {
        // `2026` is a year of Unix seconds -- 33 minutes into 1970 -- and reads; the check that it is
        // the year meant is the curve's rows, which then start 56 years later. So only the two that
        // cannot be read at all are refused: a culture's date, and an offset no curve row accepts.
        var codes = Bind(Script(start)).Diagnostics.Select(static d => d.Code).ToArray();

        if (start == " start=2026")
        {
            Assert.DoesNotContain("FS1545", codes);
            return;
        }

        Assert.Contains("FS1545", codes);
    }

    [Fact]
    public void ADynamicCircuitFollowingTheClockWithNoStartIsWarnedOnceOnTheReader()
    {
        var result = Bind(Script(start: string.Empty));

        var warning = Assert.Single(result.Diagnostics, d => d.Code == "FS1546");
        Assert.Contains("'outdoor'", warning.Message, StringComparison.Ordinal);
        Assert.Null(GraphFixture.Lower(Script(start: string.Empty)).Graph.Clock);
    }

    [Fact]
    public void AStartInAFileWithNoClockIsFs1547()
    {
        var source = Script().Replace("project dynamic", "project static", StringComparison.Ordinal)
            .Replace("fluid dynamic water", "fluid water", StringComparison.Ordinal);

        Assert.Contains(Bind(source).Diagnostics, d => d.Code == "FS1547");
    }

    [Fact]
    public void TheDutyFollowsTheChainFromTheStatedStart()
    {
        // At t = 0 the outdoor curve's first row, −26 °C, so heating's 30 kW; half way through the ramp
        // −16 °C and 22.5 kW; at five minutes −6 °C and 15 kW; beyond the last row, held there.
        var clock = GraphFixture.Lower(Script()).Graph.Clock;

        Assert.NotNull(clock);

        var drive = Assert.Single(clock.Drives);
        Assert.Equal("HE1", drive.Component);
        Assert.Equal("power", drive.Parameter);

        Assert.Equal(30_000, clock.ValueAt(drive, 0)!.Value, 6);
        Assert.Equal(22_500, clock.ValueAt(drive, 150)!.Value, 6);
        Assert.Equal(15_000, clock.ValueAt(drive, 300)!.Value, 6);
        Assert.Equal(15_000, clock.ValueAt(drive, 3_600)!.Value, 6);
        Assert.Equal(300, clock.NextRow(0), 9);
        Assert.Equal(double.PositiveInfinity, clock.NextRow(300));
    }

    [Fact]
    public void AStartLaterOnTheAxisReadsTheCurveLaterOn()
    {
        // The same file started at 06:02:30 is 150 s into the ramp at its own t = 0.
        var clock = GraphFixture.Lower(Script(" start=\"2026-01-15T06:02:30\"")).Graph.Clock!;

        Assert.Equal(22_500, clock.ValueAt(clock.Drives[0], 0)!.Value, 6);
    }

    [Fact]
    public void AScheduledLoadKeepsItsRoleWordsSign()
    {
        // D-91's rule on the schedule path: `HL.power = 45` on a load is a 45 kW consumer. Written
        // straight through, the run was handed +45 kW, a heater.
        var graph = GraphFixture.Lower("""
            fluidscript 1
            circuit demand
            fluid dynamic water

            HL  load power=30 out.t=40
            PU1 pump

            connections
            N1 - PU1 - HL - N2

            N1 inlet t=50 p=300
            N2 outlet p=280

            schedule
            at 60 s   HL.power = 45
            """).Graph;

        var change = Assert.Single(graph.Schedule);

        Assert.Equal(-45_000, change.ToValue);
    }

    [Fact]
    [Trait("Category", "Validation")]
    public async Task ARunFollowingTheWeatherSettlesWhereItsLastHourSays()
    {
        // `33` invariant 8 with the clock in it: past the last row the duty holds at 15 kW, so the run's
        // final frame must be the steady plant at 15 kW -- which `SteadyAt` reaches by reading the same
        // clock at the horizon. A run that ignored the curve would end at 30 kW, 15 kW away.
        var run = await TransientRunFixture.RunAsync(
            "curve-clock-weather", Script(), new TransientSettings { Horizon = 1_800 }, TestContext.Current.CancellationToken);

        Assert.Contains(run.Frames, frame => Math.Abs(frame.Time - 300) < 1e-9);

        // The rise across HE1 is its duty over the stream's capacity, 0.2393 kg/s: 30 K at 30 kW, 22.5 K
        // half way down the ramp, 15 K from five minutes on. A run that ignored the curve stays at 30 K.
        double Rise(TransientFrame frame) => run.NodeCelsius(frame, "HE1__3WV.h") - run.NodeCelsius(frame, "PU1__HE1.h");

        Assert.Equal(30.0, Rise(run.At(0)), 0.3);
        Assert.Equal(22.5, Rise(run.At(150)), 0.3);
        Assert.Equal(15.0, Rise(run.Frames[^1]), 0.3);

        var steady = await run.SteadyAfterAsync(TestContext.Current.CancellationToken);
        var last = run.Frames[^1];

        for (var index = 0; index < last.State.Values.Length; index++)
        {
            Assert.Equal(steady.Values[index], last.State.Values[index], Math.Max(1e-3, Math.Abs(steady.Values[index]) * 1e-3));
        }
    }
}

