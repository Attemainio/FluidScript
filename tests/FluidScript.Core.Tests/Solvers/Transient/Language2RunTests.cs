using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Compatibility;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Solvers.Transient;

namespace FluidScript.Core.Tests.Solvers.Transient;

/// <summary>
/// A language 2 run played in time (<c>D-169</c>): the model projected onto one run by <see cref="RunProjection"/>,
/// then the same design solve, snapshot and transient a language 1 file takes.
/// </summary>
/// <remarks>
/// The plant is <see cref="CurveClockTests"/>' weather loop written in language 2: the demand follows
/// <c>heating</c> of the driver <c>outdoor</c>, and the run hands <c>outdoor</c> to the weather curve, −26 → −6 °C
/// over five minutes, so <c>heating</c> falls 30 → 15 kW. The rise across <c>HE1</c> at its fixed 0.2393 kg/s is
/// the duty over the stream's capacity: 30 K at t = 0, 22.5 K at 150 s, 15 K from 300 s on.
/// </remarks>
[Trait("Category", "Validation")]
public sealed class Language2RunTests
{
    private static string Script(string events = "") => $$"""
        fluidscript 2

        let outdoor = -26 C

        curve weather: time
          2026-01-15 06:00   -26
          2026-01-15 06:05    -6

        curve heating: outdoor
          -26   30
           -6   15

        circuit "demandStep":
          fluid = water

          HE1  heat_exchanger  power = heating  out.t = 50
          3WV  valve3
          PU1  pump
          P1   pipe  length = 25
          PB   pipe  length = 8  dn = 20  nodes = 4
          TC1  controller:
            moves    = 3WV
            reads    = NS.t
            setpoint = 20 C

          N1 - N2
          N2 - NS
          NS - PU1
          PU1 - HE1
          HE1 - 3WV
          3WV - PB - N2
          3WV - P1
          P1 - N3

          N1  inlet   t = 6 C  p = 300
          N3  outlet  p = 280

        run "Morning":
          start    = 2026-01-15 06:00
          duration = 15 min
          frame    = 10 s
          outdoor  = weather
        {{events}}
        """;

    private static SemanticModel Bind(string text)
    {
        var source = new SourceText(text);
        var parse = MajorParser.Parse(source, ScriptCompatibility.Inspect(source).DetectedMajor, ComponentRegistry.Default);
        var bound = new Binder(ComponentRegistry.Default).Bind(parse, "script");
        var errors = parse.Diagnostics.Concat(bound.Diagnostics).Where(static d => d.Severity == FluidScript.Core.Diagnostics.DiagnosticSeverity.Error).ToArray();

        Assert.True(errors.Length == 0, string.Join("; ", errors.Select(static d => $"{d.Code} {d.Message}")));
        return bound.Model;
    }

    private static Task<TransientRunFixture.Run> PlayAsync(string name, string text)
    {
        var model = Bind(text);
        var run = Assert.Single(model.Runs);

        return TransientRunFixture.RunAsync(name, RunProjection.Project(model, run), TransientSettings.Of(run), TestContext.Current.CancellationToken);
    }

    private static double Rise(TransientFrame frame, TransientRunFixture.Run run) =>
        run.NodeCelsius(frame, "HE1__3WV.h") - run.NodeCelsius(frame, "PU1__HE1.h");

    [Fact]
    public async Task ADriverHandedToTheWeatherFollowsTheClock()
    {
        var run = await PlayAsync("language2-weather", Script());

        Assert.Equal(900, run.Frames[^1].Time, 9);
        Assert.Equal(30.0, Rise(run.At(0), run), 0.3);
        Assert.Equal(22.5, Rise(run.At(150), run), 0.3);
        Assert.Equal(15.0, Rise(run.Frames[^1], run), 0.3);
    }

    /// <summary>An event replaces what drove its target (<c>D-169</c>): after the step the duty is 20 kW, not the curve's 15.</summary>
    [Fact]
    public async Task AStepReplacesTheCurveItsTargetFollowed()
    {
        var run = await PlayAsync("language2-weather-step", Script("  at 10 min  HE1.power = 20 kW"));

        Assert.Equal(15.0, Rise(run.At(590), run), 0.3);
        Assert.Equal(20.0, Rise(run.Frames[^1], run), 0.3);
    }

    /// <summary>
    /// <c>D-175</c>: a parameter with a sizing point is held to its capacity at every step, not only in the design case.
    /// Sized at −16 °C, <c>HE1</c>'s capacity is the curve there, 22.5 kW, and its stream is sized for 22.5 kW over 30 K.
    /// While the weather asks more (26.5 kW at 70 s) it gives 22.5 and the rise stays 30 K; once the curve falls under
    /// the capacity it follows it, to 15 kW, a rise of 15/22.5 × 30 = 20 K. Unheld, 70 s would read 26.5 kW.
    /// </summary>
    [Fact]
    public async Task AComponentWithASizingPointIsHeldToItsCapacityAtEveryStep()
    {
        var script = Script().Replace(
            "HE1  heat_exchanger  power = heating  out.t = 50",
            "HE1  heat_exchanger  power = heating  out.t = 50  sized_at.outdoor = -16 C",
            StringComparison.Ordinal);
        Assert.Contains("sized_at.outdoor", script, StringComparison.Ordinal);

        var run = await PlayAsync("language2-weather-capacity", script);

        Assert.Equal(30.0, Rise(run.At(0), run), 0.3);
        Assert.Equal(30.0, Rise(run.At(70), run), 0.3);
        Assert.Equal(20.0, Rise(run.Frames[^1], run), 0.3);
    }
}
