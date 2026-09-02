using System.Collections.Immutable;

using FluidScript.Core.Binding;
using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Fluids;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;
using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Components;

/// <summary>The observer family: what an instrument reads, and how a model gets one.</summary>
/// <remarks>
/// <c>P3.0</c>'s whole content. Nothing solves until <c>P3.6</c>, so every reading here is taken from
/// a <see cref="NodeObservation"/> built by hand — which is the point of the observer being outside
/// the equation system in the first place (<c>D-61</c>): it is testable without one.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ObserverTests
{
    private const string Script = """
        fluidscript 1
        circuit heating
        NB1 node
        NB2 node
        TE1 t_sensor at NB2
        PE1 p_sensor at NB2
        FE1 flow_sensor at NB1
        """;

    private static SemanticModel Model(string source)
    {
        var result = new Binder(ComponentRegistry.Default)
            .Bind(FluidScriptParser.Parse(new SourceText(source)), "observers");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);

        return result.Model;
    }

    private static NodeObservation Observation(double celsius, double gaugeKilopascals, double kilogramsPerSecond)
    {
        var state = Water.Instance.FromPressureTemperature(
            Quantity.FromSi(gaugeKilopascals * 1000, Dimension.Pressure),
            Quantity.FromSi(celsius + 273.15, Dimension.Temperature));

        Assert.True(state.IsSuccess, state.Error?.Message);

        return new NodeObservation
        {
            State = state.Value,
            MassFlow = Quantity.FromSi(kilogramsPerSecond, Dimension.MassFlow),
        };
    }

    private static ComponentKindInfo Kind(string keyword) =>
        ComponentRegistry.Default.Kinds.Single(kind => kind.Keyword == keyword);

    private static PlacedSensor Sensor(string kind) => new("X1", Kind(kind), "N9");

    [Fact]
    public void EveryObserverKindInTheRegistryHasAReading()
    {
        // The backstop under PlacedSensor.Read's NotSupportedException. A registry row added without a
        // reading is a programming error that no script can reach, so this is where it has to fail.
        var observers = ComponentRegistry.Default.Kinds
            .Where(static kind => kind.IsObserver)
            .ToImmutableArray();

        Assert.NotEmpty(observers);

        foreach (var kind in observers)
        {
            var reading = new PlacedSensor("X1", kind, "N9").Read(Observation(60, 250, 0.31));

            Assert.True(
                double.IsFinite(reading.SiValue),
                $"'{kind.Keyword}' measures '{kind.MeasuredProperty}' and produced {reading.SiValue}.");
        }
    }

    [Fact]
    public void ATemperatureSensorReadsTheNodeTemperature()
    {
        var reading = Sensor("t_sensor").Read(Observation(60, 250, 0.31));

        Assert.Equal(Dimension.Temperature, reading.Dimension);
        Assert.Equal(333.15, reading.SiValue, 1e-9);
    }

    [Fact]
    public void APressureSensorReadsGaugePressureAndNotAbsolute()
    {
        // The D-26 trap, and the reason this test names it. 250 kPa gauge is 351.325 kPa absolute; a
        // sensor that reported the number the property backend was handed would be out by one
        // atmosphere and would still look like a plausible plant pressure.
        var reading = Sensor("p_sensor").Read(Observation(60, 250, 0.31));

        Assert.Equal(Dimension.Pressure, reading.Dimension);
        Assert.Equal(250_000, reading.SiValue, 1e-6);
    }

    [Fact]
    public void AFlowSensorReadsTheMassFlowThroughTheNode()
    {
        var reading = Sensor("flow_sensor").Read(Observation(60, 250, 0.31));

        Assert.Equal(Dimension.MassFlow, reading.Dimension);
        Assert.Equal(0.31, reading.SiValue, 1e-12);
    }

    [Fact]
    public void AnInstrumentObservesTheNodeAndNotItself()
    {
        // D-61: "TE1.t is N2.t". The property reference names the node, which is what makes a
        // controller reading a sensor and a controller reading a node the same evaluation.
        var observed = Assert.Single(Sensor("t_sensor").ObservedProperties);

        Assert.Equal("N9", observed.Component);
        Assert.Equal("t", observed.Property);
    }

    [Fact]
    public void AnInstrumentCarriesNoParametersAndNoMode()
    {
        var sensor = Sensor("t_sensor");

        Assert.Null(sensor.Mode);
        Assert.Empty(sensor.StatedParameters);
        Assert.Empty(sensor.SizedParameters);
        Assert.Empty(sensor.DefaultParameters);
    }

    [Fact]
    public void TheKindReportedIsTheCanonicalSpellingNotTheAlias()
    {
        var model = Model("""
            fluidscript 1
            circuit heating
            NB1 node
            TE1 te at NB1
            """);

        var sensor = Assert.Single(ModelObservers.Collect(model));

        Assert.Equal("t_sensor", sensor.Kind);
    }

    [Fact]
    public void APlacedObserverBecomesAnInstrumentOnItsNode()
    {
        var observers = ModelObservers.Collect(Model(Script));

        Assert.Equal(["TE1", "PE1", "FE1"], observers.Select(static o => o.Name));
        Assert.Equal(["NB2", "NB2", "NB1"], observers.Select(static o => o.AttachedNode));
        Assert.Equal(["t", "p", "flow"], observers.Select(static o => o.MeasuredProperty));
    }

    [Fact]
    public void AComponentThatCarriesFlowIsNotAnInstrument()
    {
        var model = Model("""
            fluidscript 1
            circuit heating
            PU1 pump
            NB1 node
            TE1 t_sensor at NB1
            """);

        Assert.Equal(["TE1"], ModelObservers.Collect(model).Select(static o => o.Name));
    }

    [Fact]
    public void AnUnplacedObserverIsSkippedRatherThanReportedTwice()
    {
        // FS1533 already warned at bind time. A model under editing is malformed most of the time, so
        // a second complaint here would be noise -- and throwing would break the pipeline rule.
        var result = new Binder(ComponentRegistry.Default).Bind(
            FluidScriptParser.Parse(new SourceText("""
                fluidscript 1
                circuit heating
                TE1 t_sensor
                """)),
            "unplaced");

        Assert.Contains(result.Diagnostics, static d => d.Code == "FS1533");
        Assert.Empty(ModelObservers.Collect(result.Model));
    }

    [Fact]
    public void ReadingAnInstrumentResolvesToReadingItsNode()
    {
        var observers = ModelObservers.Collect(Model(Script));

        Assert.Equal(
            new PropertyReference("NB2", "t"),
            ModelObservers.Resolve(new PropertyReference("TE1", "t"), observers));
    }

    [Fact]
    public void ABareInstrumentAndAQualifiedOneResolveIdentically()
    {
        var observers = ModelObservers.Collect(Model(Script));

        Assert.Equal(
            ModelObservers.Resolve(new PropertyReference("FE1", "flow"), observers),
            ModelObservers.Resolve(new PropertyReference("FE1", ""), observers));
    }

    [Fact]
    public void AReferenceToANodeIsLeftAlone()
    {
        // `measure=N2.t` is still legal; D-61 changed where a user *should* point, not what binds.
        var reference = new PropertyReference("NB2", "t");

        Assert.Equal(reference, ModelObservers.Resolve(reference, ModelObservers.Collect(Model(Script))));
    }

    [Fact]
    public void AKindThatObservesNothingCannotBeAnInstrument()
    {
        Assert.Throws<ArgumentException>(() => new PlacedSensor("PU1", Kind("pump"), "N1"));
    }
}
