using System.Globalization;
using System.Text;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Compatibility;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Printing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Physics.Units;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Language.Translation;

/// <summary>
/// Language 2's translation into the statements the binder reads (<c>plan/10-language/19-fluidscript-2.md</c>
/// §Translation to the binder): each translated shape binds to what its language 1 spelling binds to, and a
/// language 2 twin of a sample binds to the sample's model (invariant 6).
/// </summary>
public sealed class Language2TranslatorTests
{
    /// <summary>Binds as the pipeline does, with the parser's and the translation's diagnostics ahead of the binder's.</summary>
    private static BindResult Bind(string text, string name = "script")
    {
        var source = new SourceText(text);
        var major = ScriptCompatibility.Inspect(source).DetectedMajor;
        var parse = MajorParser.Parse(source, major, ComponentRegistry.Default);
        var bound = new Binder(ComponentRegistry.Default).Bind(parse, name);
        return bound with { Diagnostics = [.. parse.Diagnostics, .. bound.Diagnostics] };
    }

    private static string Describe(BindResult result) =>
        string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}"));

    private static BindResult Clean(string text)
    {
        var result = Bind(text);
        Assert.True(
            result.Diagnostics.All(static d => d.Severity != DiagnosticSeverity.Error),
            Describe(result));
        return result;
    }

    private static Diagnostic Only(string text, string code)
    {
        var result = Bind(text);
        var matching = result.Diagnostics.Where(d => d.Code == code).ToArray();
        Assert.True(matching.Length == 1, $"Expected exactly one {code}; got: {Describe(result)}");
        return matching[0];
    }

    private static ComponentSymbol Component(BindResult result, string name) =>
        Assert.Single(result.Model.Components, component => component.Name == name);

    private static Quantity Stated(BindResult result, string component, string parameter) =>
        Component(result, component).Parameters[parameter].Value
        ?? throw new InvalidOperationException($"{component}.{parameter} has no value.");

    /// <summary>The model's shape as text, blind to spans: what a twin must reproduce.</summary>
    private static string Shape(SemanticModel model)
    {
        var text = new StringBuilder();

        foreach (var circuit in model.Circuits)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"circuit {circuit.Name} {circuit.Number} {circuit.Substance} {circuit.Role.CanonicalName} {circuit.Mode}");
        }

        foreach (var component in model.Components.OrderBy(static c => c.Name, StringComparer.Ordinal))
        {
            var parameters = component.Parameters
                .OrderBy(static p => p.Key, StringComparer.Ordinal)
                .Select(static p => $"{p.Key}={p.Value.Value?.SiValue.ToString("R", CultureInfo.InvariantCulture)}{p.Value.Value?.Dimension}{p.Value.Symbol}");

            text.AppendLine(CultureInfo.InvariantCulture, $"component {component.Name} {component.Kind?.Keyword} in {component.CircuitName} at {component.AttachedTo} [{string.Join(' ', parameters)}]");
        }

        // The order of connections is the order of lines, which carries no meaning.
        foreach (var connection in model.Connections
            .Select(static c => $"{c.From.Component}.{c.From.Port} - {c.To.Component}.{c.To.Port}")
            .Order(StringComparer.Ordinal))
        {
            text.AppendLine(connection);
        }

        return text.ToString();
    }

    // ---- twins --------------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void TheSimpleLoopTwinBindsToTheSampleModel()
    {
        var sample = ScriptCorpus.Samples().Single(static s => s.Name.EndsWith("m2-simple-loop.fluid", StringComparison.Ordinal));

        var twin = Clean("""
            fluidscript 2

            circuit "simpleLoop":
              fluid = water

              HE1  heat_exchanger  power = 30   in.t = 20   out.t = 50
              LOAD heat_exchanger  power = -30  dp = 0
              CV1  valve
              PU1  pump

              N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5
              N5 - N1   25 m
            """);

        var original = new Binder(ComponentRegistry.Default).Bind(
            FluidScriptParser.Parse(new SourceText(sample.Text)), "script");

        Assert.Equal(Shape(original.Model), Shape(twin.Model));
    }

    /// <summary>
    /// The sample wires the district water through side 2 and the heating water through side 1, both in one
    /// circuit. Rule 3 makes the first pass written primary, so the twin writes the heating pass first; it is the
    /// order of lines, not a port, that reproduces the sample's sides.
    /// </summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void TheSubstationTwinBindsToTheSampleModel()
    {
        var sample = ScriptCorpus.Samples().Single(static s => s.Name.EndsWith("m2-substation.fluid", StringComparison.Ordinal));

        var twin = Clean("""
            fluidscript 2

            circuit "substation":
              fluid = water

              NPS  inlet   t = 85   p = 600
              NPR  outlet  p = 350
              PCV  valve
              SP   pump
              LOAD heat_exchanger  power = -150  dt = 20
              HX1  heat_exchanger  power = 150  u = 3300:
                primary.in.t    = 40
                primary.out.t   = 60
                secondary.in.t  = 85
                secondary.out.t = 45

              HX1 - NSUP     30 m  DN32
              NSUP - LOAD - NRET
              NRET - SP      30 m  DN32
              SP - HX1

              NPS - PCV
              PCV - HX1      12 m  DN25
              HX1 - NPR
            """);

        var original = new Binder(ComponentRegistry.Default).Bind(
            FluidScriptParser.Parse(new SourceText(sample.Text)), "script");

        Assert.Equal(Shape(original.Model), Shape(twin.Model));
    }

    // ---- values ---------------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void AKelvinIsATemperatureDifference()
    {
        // D-172: `dt = 20 K` is what language 1 writes `dt=20 dK`; there, `20 K` is FS1303.
        var result = Clean("""
            fluidscript 2
            circuit "c":
              fluid = water
              HE1 heat_exchanger  power = 30 kW  dt = 20 K
              N1 - HE1 - N1
            """);

        var dt = Stated(result, "HE1", "dt");
        Assert.Equal(Dimension.TemperatureDelta, dt.Dimension);
        Assert.Equal(20, dt.SiValue, 12);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AListsUnitBelongsToEachItem()
    {
        var result = Clean("""
            fluidscript 2
            project "p":
              cases = [winter, mild]
            circuit "c":
              fluid = water
              S1 inlet   t = [85, -5] C   p = 300 kPa
              S2 outlet  p = 100 kPa
              S1 - PU1 - S2
              PU1 pump
            """);

        var t = Component(result, "S1").Parameters["t"];
        Assert.Equal(2, t.Scenarios.Length);
        Assert.Equal(358.15, t.Value!.Value.SiValue, 9);
        Assert.Equal("winter", result.Model.Project.DesignScenario);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PrimaryAndSecondaryAreTheExchangersTwoSides()
    {
        var result = Clean("""
            fluidscript 2
            circuit "c":
              fluid = water
              HX1 heat_exchanger:
                primary.in.t    = 85 C
                primary.out.t   = 45 C
                secondary.in.t  = 40 C
                secondary.out.t = 60 C
              N1 - HX1.primary.in
              HX1.primary.out - N2
              N3 - HX1.secondary.in
              HX1.secondary.out - N4
            """);

        var exchanger = Component(result, "HX1");
        Assert.Equal(358.15, exchanger.Parameters["in"].Value!.Value.SiValue, 9);
        Assert.Equal(333.15, exchanger.Parameters["out2"].Value!.Value.SiValue, 9);
        Assert.Contains(result.Model.Connections, static c => c.To is { Component: "HX1", Port: "in2" });
        Assert.Contains(result.Model.Connections, static c => c.From is { Component: "HX1", Port: "out2" });
    }

    // ---- connections ----------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void APipeAtTheEndOfItsLinkHasALengthAndASize()
    {
        var result = Clean("""
            fluidscript 2
            circuit "c":
              fluid = water
              PU1 pump
              N1 - PU1   12 m  DN25
              PU1 - N1
            """);

        var pipe = Assert.Single(result.Model.Components, static c => c.Kind?.Keyword == "pipe");
        Assert.Equal(12, pipe.Parameters["length"].Value!.Value.SiValue, 12);
        Assert.Equal(25, pipe.Parameters["dn"].Value!.Value.SiValue, 12);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AWordThatIsNotADnIsFS1813() =>
        Only("""
            fluidscript 2
            circuit "c":
              fluid = water
              PU1 pump
              N1 - PU1   12 m  NPS1
              PU1 - N1
            """, "FS1813");

    [Fact]
    [Trait("Category", "Unit")]
    public void ASensorInAChainObservesANodeWhereItSits()
    {
        var result = Clean("""
            fluidscript 2
            circuit "c":
              fluid = water
              SP  pump
              TE1 temperature_sensor
              RAD heat_exchanger  power = -10 kW
              N1 - SP - TE1 - RAD - N1
            """);

        Assert.Equal("TE1_node", Component(result, "TE1").AttachedTo);
        Assert.Contains(result.Model.Connections, static c => c.From.Component == "SP" && c.To.Component == "TE1_node");
        Assert.Contains(result.Model.Connections, static c => c.From.Component == "TE1_node" && c.To.Component == "RAD");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASensorBothPlacedAndChainedIsFS1814() =>
        Only("""
            fluidscript 2
            circuit "c":
              fluid = water
              SP  pump
              TE1 temperature_sensor at N1
              N1 - SP - TE1 - N1
            """, "FS1814");

    // ---- ports by flow direction ----------------------------------------------------------------

    /// <summary>The port the model connects at one end of the link between two components, through the node <c>I2</c> inserts between them if it did.</summary>
    private static string? PortAt(BindResult result, string from, string to, string at)
    {
        var inserted = $"{from}__{to}";
        return result.Model.Connections
            .Where(c => at == from
                ? c.From.Component == from && (c.To.Component == to || c.To.Component == inserted)
                : c.To.Component == to && (c.From.Component == from || c.From.Component == inserted))
            .Select(c => at == from ? c.From.Port : c.To.Port)
            .Single();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TwoStreamsInMakeAMixingValveAndTheFirstWrittenIsA()
    {
        var result = Bind("""
            fluidscript 2
            circuit "c":
              fluid = water
              PU1 pump
              RAD heat_exchanger  power = -10 kW
              TV1 valve3
              N1 - PU1 - N2 - RAD - TV1 - N1
              N2 - TV1
            """);

        Assert.Equal("a", PortAt(result, "RAD", "TV1", "TV1"));
        Assert.Equal("b", PortAt(result, "N2", "TV1", "TV1"));
        Assert.Equal("ab", PortAt(result, "TV1", "N1", "TV1"));

        var wired = Assert.Single(result.Diagnostics, static d => d.Code == "FS1815");
        Assert.Equal("'TV1' is wired as a mixing valve: a from RAD, ab to N1, b from N2.", wired.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OneStreamInAndTwoOutMakeADivertingValve()
    {
        var result = Clean("""
            fluidscript 2
            circuit "c":
              fluid = water
              PU1 pump
              TV1 valve3
              N1 - PU1 - TV1 - N2 - N1
              TV1 - N2
            """);

        Assert.Equal("ab", PortAt(result, "PU1", "TV1", "TV1"));
        Assert.Contains(result.Model.Connections, static c => c.From is { Component: "TV1", Port: "a" });
        Assert.Contains(result.Model.Connections, static c => c.From is { Component: "TV1", Port: "b" });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AMixingValveWiredToDivertIsFS1805()
    {
        var diagnostic = Only("""
            fluidscript 2
            circuit "c":
              fluid = water
              PU1 pump
              TV1 mixing_valve
              N1 - PU1 - TV1 - N2 - N1
              TV1 - N2
            """, "FS1805");

        Assert.Equal("'TV1' is written as a mixing valve, and its connections make it diverting: 1 in and 2 out.", diagnostic.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AValveWithOneStreamInAndOneOutSaysNeitherAndIsFS1804()
    {
        var diagnostic = Only("""
            fluidscript 2
            circuit "c":
              fluid = water
              PU1 pump
              TV1 valve3
              N1 - PU1 - TV1 - N1
            """, "FS1804");

        Assert.Contains("1 in and 1 out", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'TV1.a'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AValveWithThreeInflowsIsFS1804() =>
        Only("""
            fluidscript 2
            circuit "c":
              fluid = water
              TV1 valve3
              N1 - TV1
              N2 - TV1
              N3 - TV1
              TV1 - N4
            """, "FS1804");

    [Fact]
    [Trait("Category", "Unit")]
    public void AnAssertedFunctionSettlesAValveWithOneLegWired()
    {
        var result = Clean("""
            fluidscript 2
            circuit "c":
              fluid = water
              PU1 pump
              TV1 diverting_valve
              N1 - PU1 - TV1 - N1
            """);

        Assert.Equal("ab", PortAt(result, "PU1", "TV1", "TV1"));
        Assert.Equal("a", PortAt(result, "TV1", "N1", "TV1"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnExchangersSideInItsOwnCircuitIsPrimaryAndTheOtherSecondary()
    {
        var result = Bind("""
            fluidscript 2
            circuit "source":
              fluid = water
              PP  pump
              N1 - PP - N2
            circuit "load":
              fluid = water
              HX1 heat_exchanger  power = 50 kW
              SP  pump
              N3 - SP - HX1 - N3
            circuit "link":
              fluid = water
              N2 - HX1 - N1
            """);

        Assert.Equal("in", PortAt(result, "SP", "HX1", "HX1"));
        Assert.Equal("out", PortAt(result, "HX1", "N3", "HX1"));
        Assert.Equal("in2", PortAt(result, "N2", "HX1", "HX1"));
        Assert.Equal("out2", PortAt(result, "HX1", "N1", "HX1"));

        var wired = Assert.Single(result.Diagnostics, static d => d.Code == "FS1815");
        Assert.Equal("'HX1' is wired as primary from SP to N3, secondary from N2 to N1.", wired.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ANamedSideLeavesTheOtherToTheUnnamedPass()
    {
        var result = Clean("""
            fluidscript 2
            circuit "c":
              fluid = water
              HX1 heat_exchanger  power = 50 kW
              PU1 pump
              PU2 pump
              N1 - PU1 - HX1 - N1
              N2 - PU2 - HX1.primary.in
              HX1.primary.out - N2
            """);

        Assert.Equal("in", PortAt(result, "PU2", "HX1", "HX1"));
        Assert.Equal("in2", PortAt(result, "PU1", "HX1", "HX1"));
        Assert.Equal("out2", PortAt(result, "HX1", "N1", "HX1"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AThirdPassThroughAnExchangerIsFS1804() =>
        Only("""
            fluidscript 2
            circuit "c":
              fluid = water
              HX1 heat_exchanger  power = 50 kW
              N1 - HX1 - N2
              N3 - HX1 - N4
              N5 - HX1 - N6
            """, "FS1804");

    [Fact]
    [Trait("Category", "Unit")]
    public void ATanksStreamsTakeItsPortsInTheOrderWritten()
    {
        var result = Bind("""
            fluidscript 2
            circuit "c":
              fluid = water
              TK1 tank
              PU1 pump
              PU2 pump
              N1 - TK1 - PU1 - N1
              N2 - TK1 - PU2 - N2
            """);

        Assert.Equal("in1", PortAt(result, "N1", "TK1", "TK1"));
        Assert.Equal("out1", PortAt(result, "TK1", "PU1", "TK1"));
        Assert.Equal("in2", PortAt(result, "N2", "TK1", "TK1"));
        Assert.Equal("out2", PortAt(result, "TK1", "PU2", "TK1"));
        Assert.Contains(result.Diagnostics, static d => d.Code == "FS1815");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASecondStreamIntoAPumpIsFS1804()
    {
        var diagnostic = Only("""
            fluidscript 2
            circuit "c":
              fluid = water
              PU1 pump
              N1 - PU1 - N2 - N1
              N2 - PU1
            """, "FS1804");

        Assert.Equal("'PU1' cannot take this connection: a pump has one inlet, and it is already connected. Name the port, such as 'PU1.in'.", diagnostic.Message);
    }

    // ---- cases and drivers ----------------------------------------------------------------------

    /// <summary>A parameter's value in every case, in case order.</summary>
    private static double[] Cases(BindResult result, string component, string parameter) =>
        [.. Component(result, component).Parameters[parameter].Scenarios.Select(static e => e.Value!.Value.SiValue)];

    private const string Substation = """
        fluidscript 2
        project "p":
          cases = [winter, mild]

        let outdoor = [-26, 5] C

        curve district_supply: outdoor
          -26   85
           18   65

        curve heat_demand: outdoor
          -26   150
           18     0

        circuit "c":
          fluid = water
          NPS inlet     t = district_supply  p = 600 kPa
          NPR outlet    p = 350 kPa
          RAD radiator  power = heat_demand
          NPS - RAD
          RAD - NPR
        """;

    /// <summary><c>19</c>'s worked example: at 5 °C outside the supply is 85 − 31 × 20/44 and the demand 150 − 31 × 150/44.</summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveOfADriverIsReadAtEachCasesValue()
    {
        var result = Clean(Substation);

        var supply = Cases(result, "NPS", "t");
        Assert.Equal(85 + 273.15, supply[0], 9);
        Assert.Equal(85 - (31 * 20.0 / 44) + 273.15, supply[1], 9);
        Assert.Equal(70.9, supply[1] - 273.15, 1);

        var demand = Cases(result, "RAD", "power");
        Assert.Equal(150_000, demand[0], 6);
        Assert.Equal(44.3, demand[1] / 1000, 1);

        // The design case is the parameter's own value, and what reads no driver carries no cases.
        Assert.Equal(358.15, Stated(result, "NPS", "t").SiValue, 9);
        Assert.Empty(Component(result, "NPS").Parameters["p"].Scenarios);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ProjectingOntoACaseGivesThatCasesValue()
    {
        var model = ScenarioProjection.Project(Clean(Substation).Model, 1);
        var radiator = Assert.Single(model.Components, static c => c.Name == "RAD");

        Assert.Equal(44.3, radiator.Parameters["power"].Value!.Value.SiValue / 1000, 1);
    }

    /// <summary>The rows are read in the unit the driver is written in: 2 m³/h is row 2, not 0.56 l/s clamped to the first.</summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void ADriversRowsAreInTheUnitItIsWrittenIn()
    {
        var result = Clean("""
            fluidscript 2
            project "p":
              cases = [low, high]
            let production = [2, 3] m3/h
            curve lift: production
              2   10
              4   20
            circuit "c":
              fluid = water
              PU1 pump  head = lift
              N1 - PU1 - N1
            """);

        Assert.Equal([10, 15], Cases(result, "PU1", "head"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ALetThatReadsADriverVariesWithIt()
    {
        var result = Clean("""
            fluidscript 2
            project "p":
              cases = [winter, mild]
            let rise   = [10, 20] K
            let double = rise * 2
            circuit "c":
              fluid = water
              HE1 heat_exchanger  power = 30  dt = double
              PU1 pump
              N1 - PU1 - HE1 - N1
            """);

        Assert.Equal([20, 40], Cases(result, "HE1", "dt"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AMistakeInEveryCaseIsReportedOnce() =>
        Only("""
            fluidscript 2
            project "p":
              cases = [winter, mild]
            let rise = [-5, -5] K
            circuit "c":
              fluid = water
              HE1 heat_exchanger  power = 30  dt = rise
              PU1 pump
              N1 - PU1 - HE1 - N1
            """, "FS1307");

    [Fact]
    [Trait("Category", "Unit")]
    public void ADriverWithTheWrongNumberOfCasesIsFS1540() =>
        Only("""
            fluidscript 2
            project "p":
              cases = [winter, mild]
            let outdoor = [-26, 5, 10] C
            curve heat_demand: outdoor
              -26   150
               18     0
            circuit "c":
              fluid = water
              RAD radiator  power = heat_demand
              PU1 pump
              N1 - PU1 - RAD - N1
            """, "FS1540");

    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveDrivenByNeitherALetNorTimeIsFS1811()
    {
        var diagnostic = Only("""
            fluidscript 2
            curve heat_demand: tout
              -26   150
               18     0
            circuit "c":
              fluid = water
              N1 - N2
            """, "FS1811");

        Assert.Equal("'heat_demand' is driven by 'tout', which is not a let. Write 'let tout = [...]' with one value per case, or drive it by time.", diagnostic.Message);
    }

    // ---- controllers ----------------------------------------------------------------------------

    /// <summary>A mixing loop held on its supply temperature, with the controller written as <c>19</c> writes it.</summary>
    private static string Controlled(string settings, string cases = "", string setpoint = "60 C") => $$"""
        fluidscript 2
        {{cases}}
        circuit "Heating":
          fluid = water
          SP   pump
          TV1  valve3  stroke = 90 s
          TE1  temperature_sensor
          RAD  radiator  power = 150 kW
          TC1  controller:
            moves    = TV1
            reads    = TE1
            setpoint = {{setpoint}}
        {{settings}}
          N1 - TV1 - SP - TE1 - RAD - NR
          NR - TV1
          NR - N1
        """;

    [Fact]
    [Trait("Category", "Unit")]
    public void TheReferenceScriptBindsWithNoError() =>
        Clean(FluidScript.Core.Tests.Language.Syntax.Parsing.FluidScript2ParserTests.Reference);

    [Fact]
    [Trait("Category", "Unit")]
    public void AControllerIsOneDeclarationThatMovesAndReads()
    {
        var result = Clean(Controlled("""
                type     = PI
                band     = 20 K
                ti       = 120 s
                output   = 10..100 %
            """));

        var binding = Assert.Single(result.Model.ControlBindings);
        Assert.Equal(new PropertyReference("TV1", "position"), binding.Actuator);
        Assert.Equal(new PropertyReference("TE1", "t"), binding.Measurement);
        Assert.Equal(333.15, binding.Setpoint!.Value.SiValue, 9);
        Assert.Equal(Dimension.TemperatureDelta, binding.Band!.Value.Dimension);
        Assert.Equal(20, binding.Band.Value.SiValue, 9);
        Assert.Equal(0.1, binding.OutputLow!.Value.SiValue, 9);
        Assert.Equal(1.0, binding.OutputHigh!.Value.SiValue, 9);

        Assert.Equal(120, Stated(result, "TC1", "ti").SiValue, 9);
        Assert.Equal(90, Stated(result, "TV1", "stroke").SiValue, 9);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Code == "FS1810");
    }

    /// <summary><c>19</c>'s worked example: the setpoint follows <c>supply_temp</c>, 60 − 31 × 30/44 = 38.9 °C at 5 °C outside.</summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void ASetpointOnACurveOfADriverHoldsEachCasesValue()
    {
        var result = Clean(Controlled(
            string.Empty,
            """
            project "p":
              cases = [winter, mild]
            let outdoor = [-26, 5] C
            curve supply_temp: outdoor
              -26   60
               18   30
            """,
            "supply_temp"));

        var binding = Assert.Single(result.Model.ControlBindings);
        Assert.Equal(2, binding.Setpoints.Length);
        Assert.Equal(333.15, binding.Setpoints[0]!.Value.SiValue, 9);
        Assert.Equal(60 - (31 * 30.0 / 44), binding.Setpoints[1]!.Value.SiValue - 273.15, 9);

        var mild = Assert.Single(ScenarioProjection.Project(result.Model, 1).ControlBindings);
        Assert.Equal(38.9, mild.Setpoint!.Value.SiValue - 273.15, 1);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("P", "band = 20 K")]
    [InlineData("PI", "ti = 120 s")]
    [InlineData("PID", "td = 30 s")]
    [InlineData("onoff", "differential = 2 K")]
    public void AControllerOfEachTypeBinds(string type, string tuning)
    {
        var result = Clean(Controlled($"""
                type = {type}
                {tuning}
            """));

        Assert.Equal(type, Component(result, "TC1").Parameters["type"].Symbol);
        Assert.Single(result.Model.ControlBindings);
        Assert.Equal(type == "PI" ? 0 : 1, result.Diagnostics.Count(static d => d.Code == "FS1810"));
    }

    /// <summary>A <c>curve</c> controller follows its curve open loop, so it has no setpoint; reading the clock, it binds as a declaration.</summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveControllerReadingTheClockBindsAsADeclaration()
    {
        var result = Bind("""
            fluidscript 2
            curve opening: time
              2026-01-15 06:00   0.2
              2026-01-15 12:00   0.8
            circuit "c":
              fluid = water
              PU1 pump
              CV1 valve
              TC1 controller:
                type  = curve
                moves = CV1
                reads = time
                curve = opening
              N1 - PU1 - CV1 - N1
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("curve", Component(result, "TC1").Parameters["type"].Symbol);
        Assert.Empty(result.Model.ControlBindings);
        Assert.Contains(result.Diagnostics, static d => d.Code == "FS1810");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASettingItsTypeDoesNotHaveIsFS1808()
    {
        var diagnostic = Only(Controlled("""
                type = PI
                td   = 30 s
            """), "FS1808");

        Assert.Equal("'TC1' is a PI controller, which has no 'td'. A PI controller takes: type, moves, reads, setpoint, output, action, band, kp, ti.", diagnostic.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ABandOnAnOnOffControllerIsFS1808() =>
        Only(Controlled("""
                type = onoff
                band = 20 K
            """), "FS1808");

    [Fact]
    [Trait("Category", "Unit")]
    public void BothBandAndGainIsFS1809() =>
        Only(Controlled("""
                band = 20 K
                kp   = 0.05
            """), "FS1809");

    [Fact]
    [Trait("Category", "Unit")]
    public void ATypeTheSolverDoesNotRunYetIsFS1810()
    {
        var diagnostic = Only(Controlled("""
                type = PID
                td   = 30 s
            """), "FS1810");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ABandIsADifferenceInWhatIsMeasured() =>
        Only(Controlled("""
                band = 20 C
            """), "FS1304");

    [Fact]
    [Trait("Category", "Unit")]
    public void ASetpointInTheWrongDimensionIsRefused() =>
        Only(Controlled(string.Empty, setpoint: "20 kPa"), "FS1304");

    [Fact]
    [Trait("Category", "Unit")]
    public void AnOutputOutsideTheActuatorsRangeIsRefused() =>
        Only(Controlled("""
                output = 10..150 %
            """), "FS2105");

    [Fact]
    [Trait("Category", "Unit")]
    public void AControllerThatMovesNothingIsFS1521() =>
        Only("""
            fluidscript 2
            circuit "c":
              fluid = water
              TE1 temperature_sensor at N1
              TC1 controller:
                reads    = TE1
                setpoint = 60 C
              PU1 pump
              N1 - PU1 - N1
            """, "FS1521");

    [Fact]
    [Trait("Category", "Unit")]
    public void ALanguage1GainIsNotALanguage2Setting()
    {
        var diagnostic = Only(Controlled("""
                ki = 0.01
            """), "FS1503");

        Assert.Contains("band, kp, ti, td", diagnostic.Message, StringComparison.Ordinal);
    }

    // ---- names ----------------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ANearMissParameterBindsNothingAndOffersTheFix()
    {
        // D-170: language 1 reads `haed=15` as `head` under an information notice (FS1512).
        var diagnostic = Only("""
            fluidscript 2
            circuit "c":
              fluid = water
              PU1 pump  haed = 15 m
              N1 - PU1 - N1
            """, "FS1503");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("head", diagnostic.Suggestion?.Replacement);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ANearMissKindBindsNothingAndOffersTheFix()
    {
        var diagnostic = Only("""
            fluidscript 2
            circuit "c":
              fluid = water
              PU1 pmp
              N1 - PU1 - N1
            """, "FS1502");

        Assert.Equal("pump", diagnostic.Suggestion?.Replacement);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACaseVariantOfAParameterBindsInLanguage1Too()
    {
        // D-15's first stage, which the binder skipped for parameters: `HEAD=5` was FS1503 in language 1.
        var result = new Binder(ComponentRegistry.Default).Bind(
            FluidScriptParser.Parse(new SourceText("fluidscript 1\ncircuit c\nfluid water\nPU1 pump HEAD=5\nconnections\nN1 - PU1 - N1\n")),
            "script");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Code is "FS1503" or "FS1512");
        Assert.Equal(Dimension.Head, Stated(result, "PU1", "head").Dimension);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AliasesAndCaseStillBind()
    {
        var result = Clean("""
            fluidscript 2
            circuit "c":
              fluid = water
              TV1 valve3
              PU1 Pump  HEAD = 5 m
              RAD heat_exchanger  power = -10 kW
              N1 - PU1 - N2 - RAD - TV1 - N1
              N2 - TV1
            """);

        Assert.Equal("three_way_valve", Component(result, "TV1").Kind?.Keyword);
        Assert.Equal(Dimension.Head, Stated(result, "PU1", "head").Dimension);
    }

    // ---- circuits -------------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ACircuitIsItsTitleNumberFluidAndRole()
    {
        var result = Clean("""
            fluidscript 2
            circuit "District primary":
              fluid  = water
              number = 300
              role   = district
              PU1 pump
              N1 - PU1 - N1
            """);

        var circuit = Assert.Single(result.Model.Circuits);
        Assert.Equal("District primary", circuit.Name);
        Assert.Equal(300, circuit.Number);
        Assert.Equal("water", circuit.Substance);
        Assert.Equal("district", circuit.Role.CanonicalName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ATitleIsNotARole()
    {
        // Language 1 reads a role from the circuit's name and says so when it cannot (FS1519); a quoted title
        // is free text, so a circuit that states no role is neutral without a word.
        var result = Clean("""
            fluidscript 2
            circuit "Anything at all":
              fluid = water
              PU1 pump
              N1 - PU1 - N1
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Code == "FS1519");
        Assert.Equal(CircuitRoleRegistry.Neutral, Assert.Single(result.Model.Circuits).Role);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnUnknownCircuitSettingIsFS1503() =>
        Only("""
            fluidscript 2
            circuit "c":
              fluid = water
              colour = red
              PU1 pump
              N1 - PU1 - N1
            """, "FS1503");

    [Fact]
    [Trait("Category", "Unit")]
    public void ACircuitsStyleOverridesOnlyTheKeysItStates()
    {
        var result = Clean("""
            fluidscript 2
            project "p":
              style:
                colour = navy
                width  = 3
            circuit "c":
              fluid = water
              style:
                colour = crimson
              PU1 pump
              N1 - PU1 - N1
            """);

        var style = Component(result, "PU1").Style;
        Assert.NotNull(style);
        Assert.Equal(3, style.StrokeWidth);
        Assert.NotEqual(Component(result, "PU1").Style, result.Model.Style.Default);
    }

    // ---- the version line -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void Language2IsASupportedNewerMajor()
    {
        var result = ScriptCompatibility.Inspect(new SourceText("fluidscript 2\nproject \"p\":\n  catalog = steel_en10255@2026.1\n"));

        Assert.Equal(CompatibilityDisposition.SupportedNewer, result.Disposition);
        Assert.Contains(CompatibilityAction.Compile, result.AllowedActions);
        Assert.DoesNotContain(CompatibilityAction.PreviewMigration, result.AllowedActions);
        Assert.Equal("steel_en10255", result.Catalog?.Id);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheTranslatedTreeIsNeverTheOnePrinted()
    {
        // The printer prints the language 2 tree; the translation's tokens exist for the binder alone.
        const string Text = "fluidscript 2\ncircuit \"c\":\n  fluid = water\n  PU1 pump\n  N1 - PU1   12 m  DN25\n";
        var parse = FluidScript2Parser.Parse(new SourceText(Text));

        Assert.Equal(Text, SyntaxPrinter.Print(parse));
    }
}
