using FluidScript.Core.Diagnostics;
using FluidScript.Core.Solvers;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>The integrator on the demand-step loop, at rest, and on the storage header (P6.1, <c>33</c>).</summary>
/// <remarks>
/// <para>
/// Every figure asserted here was read off <c>diagnostics/transient/*.run.txt</c> first. The
/// demand-step loop is <c>01</c>'s reference: 8 m of DN20 in four cells on the recirculation branch,
/// the load stepping 30 → 45 kW at 60 s, the valve and the pump frozen at their design values because
/// no controller runs yet (<c>D-140</c>).
/// </para>
/// <para>
/// <strong>The trap <c>33</c> names:</strong> the 65.0 °C the outlet jumps to at the step holds the flow
/// at its pre-step value and is true for exactly one instant; the settled state is the steady solve of
/// the post-step system, 72.2 °C, because the warmed recirculation raises the exchanger's inlet. A test
/// that asserts 65 °C as the settled value fails on a correct implementation.
/// </para>
/// </remarks>
public sealed class TransientSolverTests
{
    private const string DemandStep = """
        fluidscript 1
        circuit demandStep
        fluid dynamic water

        HE1 heat_exchanger power=30 out.t=50
        3WV three_way_valve
        PU1 pump
        P1  pipe length=25
        PB  pipe length=8 dn=20 nodes={NODES}
        TC1 pi

        control actuate=3WV.position measure=N2.t by=TC1 setpoint=20

        connections
        N1 - N2
        N2 - PU1
        PU1 - HE1
        HE1 - 3WV
        3WV - PB - N2
        3WV - P1
        P1 - N3

        N1 inlet t=6 p=300
        N3 outlet p=280
        {SCHEDULE}
        """;

    private static string Script(int nodes = 4, string schedule = "schedule\nat 60 s   HE1.power = 45") =>
        DemandStep.Replace("{NODES}", nodes.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{SCHEDULE}", schedule, StringComparison.Ordinal);

    [Fact]
    [Trait("Category", "Validation")]
    public async Task ARunWithNoDisturbanceStaysAtItsDesignState()
    {
        // V9, invariant 3: the strongest test available. Pinned at the design state and frozen at the
        // design promotions, every step's derivative is the balance the design solve already closed.
        var run = await TransientRunFixture.RunAsync("m4-demand-step-rest", Script(schedule: string.Empty), new TransientSettings { Horizon = 600 }, TestContext.Current.CancellationToken);
        var last = run.Frames[^1];

        Assert.Equal(601, run.Frames.Count);

        for (var index = 0; index < last.Differential.Length; index++)
        {
            Assert.Equal(run.Snapshot.DifferentialInitial[index], last.Differential[index], 1.0);
        }

        Assert.True(last.EnergyDrift < 1e-3, last.EnergyDrift.ToString("G3", System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(last.Settled);
        Assert.DoesNotContain(run.Frames.SelectMany(f => f.Diagnostics), d => d.Code is "FS3104" or "FS3106" or "FS3107");
        Assert.Equal(run.Snapshot.Initial.Values.Length, last.State.Values.Length);
        Assert.Equal(run.Snapshot.Initial.Values[0], last.State.Values[0], 1e-6);
    }

    [Fact]
    [Trait("Category", "Validation")]
    public async Task TheDemandStepLandsOnSixtySecondsAndTheOutletLeavesItAsAFirstOrderLag()
    {
        // Invariant 10b: the step lands *on* the 60 s frame, never between two. What the frame shows
        // changed with `C-114`. The duty rises at 60 s, but the exchanger's 0.5 dm³ still holds the
        // water that was in it, so the outlet is unmoved at 60 s and empties from there.
        //
        // 45 000 W over 0.2392 kg/s at 4178 J/(kg·K) is 45.0 K above the 20.07 °C inlet, so the
        // asymptote is still 65.1 °C. The approach is exactly first order with tau = m/mdot =
        // 0.494 kg / 0.2392 kg/s = 2.065 s, which is the one number a hold-up has to get right:
        //
        //   t − 60    1 − e^(−t/tau)    measured          predicted
        //        1 s           0.3833   55.82 °C          50.06 + 0.3833 · 15.03 = 55.82
        //        2 s           0.6196   59.37 °C          50.06 + 0.6196 · 15.03 = 59.37
        var run = await TransientRunFixture.RunSampleAsync("m4-demand-step.fluid", new TransientSettings { Horizon = 120 }, TestContext.Current.CancellationToken);
        var before = run.NodeCelsius(run.At(59), "HE1__3WV.h");

        Assert.Equal(50.06, before, 0.1);
        Assert.Equal(before, run.NodeCelsius(run.At(60), "HE1__3WV.h"), 1e-9);

        var tau = 0.494 / 0.2392;
        var asymptote = 65.09;

        foreach (var elapsed in new[] { 1, 2, 3, 4 })
        {
            var expected = before + ((asymptote - before) * (1 - Math.Exp(-elapsed / tau)));

            Assert.Equal(expected, run.NodeCelsius(run.At(60 + elapsed), "HE1__3WV.h"), 0.15);
        }

        Assert.Equal(20.07, run.NodeCelsius(run.At(60), "N2.h"), 0.05);
        Assert.All(run.Frames.Select((f, i) => (f.Time, i)), pair => Assert.Equal(pair.i, pair.Time, 1e-9));
    }

    [Fact]
    [Trait("Category", "Validation")]
    public async Task TheFrontCrossesTheRecirculationPipeAsAFourStageLag()
    {
        // 33's worked example while it holds: the closed form assumes a 65 °C source, and the source
        // rises as the warmed front returns to N2 (65.0 at 70 s, 65.5 at 80 s, 66.5 at 90 s), so the
        // rows past 90 s are above the table on a correct run. PB#n4 is the last cell.
        //
        // Each row is about half a kelvin below what it read before `C-114`, because the exchanger's
        // hold-up delays the front by its own 2.1 s before the pipe's four stages ever see it.
        var run = await TransientRunFixture.RunSampleAsync("m4-demand-step.fluid", new TransientSettings { Horizon = 200 }, TestContext.Current.CancellationToken);

        Assert.True(run.Celsius(run.At(70), "PB#n4.h") - 50.06 < 0.5, "the outlet moved more than 0.5 K ten seconds after the step");
        Assert.Equal(50.25, run.Celsius(run.At(70), "PB#n4.h"), 0.2);
        Assert.Equal(51.91, run.Celsius(run.At(80), "PB#n4.h"), 0.3);
        Assert.Equal(55.12, run.Celsius(run.At(90), "PB#n4.h"), 0.4);

        // Ordering: the front reaches the first cell before the last, by about three residence times.
        var first = run.Frames.First(f => run.Celsius(f, "PB#n1.h") > 57.5).Time;
        var last = run.Frames.First(f => run.Celsius(f, "PB#n4.h") > 57.5).Time;

        Assert.InRange(last - first, 20, 35);
    }

    [Fact]
    [Trait("Category", "Validation")]
    public async Task TheSettledStateIsTheSteadySolveOfThePostStepSystem()
    {
        // Invariant 8, V8: two solvers sharing no time-stepping code agree. The settled loop is 72.2 °C
        // at the outlet and 27.1 °C at N2 — not 65 °C (see the class remarks).
        var run = await TransientRunFixture.RunSampleAsync("m4-demand-step.fluid", new TransientSettings { Horizon = 600 }, TestContext.Current.CancellationToken);
        var steady = await run.SteadyAfterAsync(TestContext.Current.CancellationToken);
        var last = run.Frames[^1];
        var layout = run.Snapshot.System.Unknowns;

        Assert.True(last.Settled);
        Assert.Equal(72.2, run.NodeCelsius(last, "HE1__3WV.h"), 0.1);
        Assert.Equal(27.1, run.NodeCelsius(last, "N2.h"), 0.1);

        for (var index = 0; index < steady.Values.Length; index++)
        {
            var scale = layout.Unknowns[index].Kind == Core.Components.UnknownKind.NodeEnthalpy ? Tolerances.EnthalpyScale : Math.Max(Math.Abs(steady.Values[index]), 1e-3);

            Assert.True(
                Math.Abs(last.State.Values[index] - steady.Values[index]) / scale < 1e-3,
                $"{layout.Unknowns[index].Name}: run {last.State.Values[index]:G6}, steady {steady.Values[index]:G6}");
        }
    }

    [Fact]
    [Trait("Category", "Validation")]
    public async Task DoublingTheCellsSharpensTheFront()
    {
        // V11: the same 8 m in eight cells arrives with the same mean delay and a narrower spread. The
        // time the last cell takes from 10 % to 50 % of the first 15 K rise is the measure, because the
        // 90 % point is inside the slow loop warm-up the feedback adds.
        var four = await TransientRunFixture.RunAsync("m4-demand-step-n4", Script(4), new TransientSettings { Horizon = 200 }, TestContext.Current.CancellationToken);
        var eight = await TransientRunFixture.RunAsync("m4-demand-step-n8", Script(8), new TransientSettings { Horizon = 200 }, TestContext.Current.CancellationToken);

        static double Spread(TransientRunFixture.Run run, string last)
        {
            var ten = run.Frames.First(f => run.Celsius(f, last) > 50.06 + 1.5).Time;
            var half = run.Frames.First(f => run.Celsius(f, last) > 50.06 + 7.5).Time;

            return half - ten;
        }

        var wide = Spread(four, "PB#n4.h");
        var narrow = Spread(eight, "PB#n8.h");

        Assert.True(narrow < wide, $"eight cells spread {narrow} s, four cells {wide} s");
        Assert.Equal(8, eight.Snapshot.DifferentialInitial.Length);
    }

    [Fact]
    [Trait("Category", "Validation")]
    public async Task ARampMovesTheLoadLinearly()
    {
        // `over 60 s .. 120 s HE1.power = 30 .. 45`: half way, 37.5 kW lifts the outlet 37.5 K above N2.
        var run = await TransientRunFixture.RunAsync("m4-demand-step-ramp", Script(schedule: "schedule\nover 60 s .. 120 s   HE1.power = 30 .. 45"), new TransientSettings { Horizon = 130 }, TestContext.Current.CancellationToken);

        Assert.Equal(30.0, run.NodeCelsius(run.At(60), "HE1__3WV.h") - run.NodeCelsius(run.At(60), "N2.h"), 0.3);
        Assert.Equal(37.5, run.NodeCelsius(run.At(90), "HE1__3WV.h") - run.NodeCelsius(run.At(90), "N2.h"), 0.3);
        Assert.Equal(45.0, run.NodeCelsius(run.At(120), "HE1__3WV.h") - run.NodeCelsius(run.At(120), "N2.h"), 0.3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ManyCellsMakeTheCflLimitBindAndFs3101NamesOne()
    {
        // Forty cells of 0.2 m hold 0.074 kg each; at 0.0763 kg/s that is 0.97 s, and 0.9 of it is under
        // the 1 s frame interval, so the limit binds and the info names the cell.
        var run = await TransientRunFixture.RunAsync("m4-demand-step-n40", Script(40), new TransientSettings { Horizon = 3 }, TestContext.Current.CancellationToken);
        var info = Assert.Single(run.Frames.SelectMany(f => f.Diagnostics), d => d.Code == "FS3101");

        Assert.Equal(DiagnosticSeverity.Info, info.Severity);
        Assert.Contains("by 'PB#n", info.Message, StringComparison.Ordinal);
        Assert.Contains("Fewer internal nodes", info.Message, StringComparison.Ordinal);
        Assert.All(run.Frames.Skip(1), f => Assert.True(f.StepTaken < 1.0));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AStepThatCannotBeBalancedEndsTheRunWithFs3103()
    {
        // A gigawatt into 0.24 kg/s of water leaves the property domain; the algebraic solve at the step
        // fails, halving does not help, and the run ends on that frame saying so. FS3102 (the step under
        // its floor) and FS3107 (a non-finite state) are the same shape of exit and are not provoked here.
        var run = await TransientRunFixture.RunAsync("m4-demand-step-overload", Script(schedule: "schedule\nat 3 s   HE1.power = 1000000"), new TransientSettings { Horizon = 10 }, TestContext.Current.CancellationToken);
        var last = run.Frames[^1];
        var error = Assert.Single(last.Diagnostics, d => d.Code == "FS3103");

        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.StartsWith("Could not balance the circuit at t = 3 s", error.Message, StringComparison.Ordinal);
        Assert.True(last.Time <= 3);
        Assert.True(run.Frames.Count < 11);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheRunTimeCodesAreErrorsWhereTheRunStopsAndInformationWhereItGoesOn()
    {
        Assert.Equal(DiagnosticSeverity.Info, TransientDiagnostics.StepLimited.Severity);
        Assert.Equal(DiagnosticSeverity.Error, TransientDiagnostics.StepTooSmall.Severity);
        Assert.Equal(DiagnosticSeverity.Error, TransientDiagnostics.StepNotBalanced.Severity);
        Assert.Equal(DiagnosticSeverity.Info, TransientDiagnostics.NotSettled.Severity);
        Assert.Equal(DiagnosticSeverity.Warning, TransientDiagnostics.EnergyDrift.Severity);
        Assert.Equal(DiagnosticSeverity.Error, TransientDiagnostics.InvariantFailed.Severity);
        Assert.Equal(DiagnosticSeverity.Error, TransientDiagnostics.CannotInitializeLayer.Severity);
        Assert.Equal(["FS3101", "FS3102", "FS3103", "FS3104", "FS3105", "FS3106", "FS3107", "FS3108", "FS3109"], TransientDiagnostics.All.Select(d => d.Code).ToArray());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CancellationStopsWithinOneStep()
    {
        using var cancellation = new CancellationTokenSource();
        var frames = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Enumerate(cancellation.Token))
            {
                if (++frames == 5)
                {
                    await cancellation.CancelAsync();
                }
            }
        });

        Assert.Equal(5, frames);

        async IAsyncEnumerable<TransientFrame> Enumerate([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            var run = await TransientRunFixture.RunAsync("m4-demand-step-cancel", Script(), new TransientSettings { Horizon = 5 }, TestContext.Current.CancellationToken);

            await foreach (var frame in new TransientSolver(new NewtonSolver()).RunAsync(run.Snapshot, token))
            {
                yield return frame;
            }
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TheStorageHeadersSecondLayerWarmsAtTwoHundredthsOfAKelvinPerSecond()
    {
        // 33's storage-header cross-check: 0.08 kg/s at 45 °C into layer 2 at 30 °C, the same flow drawn
        // from it, 60 l of water: dT/dt = 0.08 × 15 / 59.7 = 0.020 K/s, 0.20 K over ten seconds. Every
        // other layer's inflow matches its own temperature or is absent, so nothing else moves. No
        // inversion within ten seconds; the remix is P6.2's.
        var run = await TransientRunFixture.RunSampleAsync("m4-storage-header.fluid", new TransientSettings { Horizon = 10 }, TestContext.Current.CancellationToken);
        var start = run.At(0);
        var end = run.At(10);

        Assert.Equal(0.20, run.Celsius(end, "T1.layer[2].h") - run.Celsius(start, "T1.layer[2].h"), 0.01);

        foreach (var layer in new[] { 1, 3, 4, 5 })
        {
            Assert.Equal(0, run.Celsius(end, $"T1.layer[{layer}].h") - run.Celsius(start, $"T1.layer[{layer}].h"), 0.005);
        }

        Assert.True(end.EnergyDrift < 1e-3);
    }
}
