using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Compatibility;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Language.Translation;

namespace FluidScript.Core.Tests.Language.Translation;

/// <summary>
/// A language 2 file's diagnostics in language 2's words (<c>plan/10-language/19-fluidscript-2.md</c> §Diagnostics):
/// the second template on a descriptor, the respelled exchanger sides, and the shared defects the audit fixed.
/// </summary>
public sealed class Language2WordingTests
{
    /// <summary>Binds as the pipeline does, with the parser's and the translation's diagnostics ahead of the binder's.</summary>
    private static BindResult Bind(string text)
    {
        var source = new SourceText(text);
        var major = ScriptCompatibility.Inspect(source).DetectedMajor;
        var parse = MajorParser.Parse(source, major, ComponentRegistry.Default);
        var bound = new Binder(ComponentRegistry.Default).Bind(parse, "script");
        return bound with { Diagnostics = [.. parse.Diagnostics, .. bound.Diagnostics] };
    }

    private static string Describe(BindResult result) =>
        string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}"));

    private static Diagnostic[] All(string text, string code) =>
        [.. Bind(text).Diagnostics.Where(d => d.Code == code)];

    private static Diagnostic Only(string text, string code)
    {
        var result = Bind(text);
        var matching = result.Diagnostics.Where(d => d.Code == code).ToArray();
        Assert.True(matching.Length == 1, $"Expected exactly one {code}; got: {Describe(result)}");
        return matching[0];
    }

    // ---- the descriptor ---------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ASecondWordingMayNotNameAnArgumentTheFirstDoesNotHave()
    {
        // Both wordings render from the one argument list the emitting stage supplied; a placeholder only the second
        // names would have no value in a language 1 file's list and none in a language 2 file's either.
        Assert.Throws<ArgumentException>(() => new DiagnosticDescriptor(
            "FS1302", DiagnosticSeverity.Error, "Cannot add {left}.", language2Template: "Cannot add {right}."));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACodeWithOneWordingRendersItInBothLanguages()
    {
        var descriptor = new DiagnosticDescriptor("FS1302", DiagnosticSeverity.Error, "Cannot add {left}.");
        var argument = new DiagnosticArgument("left", "two temperatures");

        Assert.Equal(descriptor.Render(argument), descriptor.RenderLanguage2(argument));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheWordingIsRenderedFromTheArgumentsSoApplyingItTwiceChangesNothing()
    {
        var once = Language2Wording.Apply(Diagnostic.Create(
            BinderDiagnostics.ScenarioListWithoutScenarios, new TextSpan(0, 0), new DiagnosticArgument("param", "t")));

        Assert.Equal(once, Language2Wording.Apply(once));
        Assert.Contains("'cases = [<name>, <name>]'", once.Message, StringComparison.Ordinal);
    }

    // ---- through the pipeline ---------------------------------------------------------------

    private const string ListWithoutCases = """
        circuit "loop":
          fluid = water
          S1 inlet   t = [85, 70] C   p = 300 kPa
          S2 outlet  p = 100 kPa
          PU1 pump
          S1 - PU1 - S2
        """;

    [Fact]
    [Trait("Category", "Unit")]
    public void ALanguage2FileIsToldToDeclareItsCasesInTheProjectBlock()
    {
        var diagnostic = Only("fluidscript 2\n" + ListWithoutCases, "FS1541");

        Assert.EndsWith("Add 'cases = [<name>, <name>]' to the project block.", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ALanguage1FileKeepsLanguage1sWording()
    {
        var diagnostic = Only("""
            circuit loop
            fluid water
            S1 inlet   t=[85 C, 70 C]   p=300 kPa
            S2 outlet  p=100 kPa
            PU1 pump
            connections
            S1 - PU1 - S2
            """, "FS1541");

        Assert.Contains("scenarios", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnExchangersSecondSideIsNamedAsLanguage2WritesIt()
    {
        var diagnostics = All("""
            fluidscript 2
            circuit "loop":
              fluid = water
              HX1  heat_exchanger  power = 150 kW:
                primary.in.t    = 60 C
                primary.out.t   = 40 C
                secondary.in.t  = 30 C
                secondary.out.t = 50 C
              PU1  pump
              PU2  pump
              N1 - PU1 - HX1 - N1
              M1 - PU2 - HX1.secondary.in
              HX1.secondary.out - M1
            """, "FS2119");

        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, static d => d.Message.Contains(
            "means the primary side gains heat, but in.t = 60 °C and out.t = 40 °C", StringComparison.Ordinal));
        Assert.Contains(diagnostics, static d => d.Message.Contains(
            "means the secondary side loses heat, but secondary.in.t = 30 °C and secondary.out.t = 50 °C", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnExchangersPortsAreListedAsLanguage2WritesThem()
    {
        var diagnostic = Only("""
            fluidscript 2
            circuit "loop":
              fluid = water
              HE1  heat_exchanger  power = 30 kW  in.t = 20 C  out.t = 50 C
              PU1  pump
              N1 - PU1 - N2 - HE1 - N1
              N5 - HE1.in[3]
            """, "FS1505");

        Assert.EndsWith("Ports: in, out, secondary.in, secondary.out.", diagnostic.Message, StringComparison.Ordinal);
    }

    // ---- the shared defects the audit fixed -------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void AStreamToAPortAlreadyTakenIsReportedOnceAsFS1506()
    {
        // The chain gave PU1.in to N1; `N6 - PU1.in` claims it again. The stream is handed the port it named so the
        // binder reports the double connection, rather than FS1804 saying the pump has no free inlet as well.
        var result = Bind("""
            fluidscript 2
            circuit "loop":
              fluid = water
              PU1  pump
              HE1  heat_exchanger  power = 30 kW  in.t = 20 C  out.t = 50 C
              N1 - PU1 - N2 - HE1 - N1
              N6 - PU1.in
            """);

        Assert.Single(result.Diagnostics, static d => d.Code == "FS1506");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Code == "FS1804");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveDrivenByAnUnknownNameIsReportedOnce()
    {
        var result = Bind("""
            fluidscript 2
            curve c: foo
              -26 85
              18 65
            circuit "loop":
              fluid = water
              PU1 pump head = c
              N1 - PU1 - N1
            """);

        Assert.Single(result.Diagnostics, static d => d.Code == "FS1811");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Code == "FS1528");
    }

    [Theory]
    [InlineData("moves = HE1\n    reads = N3.t", "'HE1.power'")]
    [InlineData("moves = HE1.power\n    reads = N3", "'N3.t'")]
    [Trait("Category", "Unit")]
    public void ABareEndpointIsShownTheQualifiedFormAnEngineerWouldWrite(string lines, string example)
    {
        // A heat exchanger can move more than its power, and a node has more than its temperature; the example is
        // the one a heating engineer means by far most often (`19` §Controllers).
        var diagnostic = Only($"""
            fluidscript 2
            circuit "loop":
              fluid = water
              HE1  heat_exchanger  power = 30 kW  in.t = 20 C  out.t = 50 C
              LOAD heat_exchanger  power = -30 kW  dp = 0
              PU1  pump
              N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - N1
              TC1 controller:
                type = PI
                {lines}
                setpoint = 50 C
            """, "FS1531");

        Assert.EndsWith($"such as {example}.", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AKindWithNoParametersSaysSo()
    {
        var kind = ComponentRegistry.Default.Kinds.First(static kind => kind.Parameters.Count == 0).Keyword;
        var diagnostic = Only($"""
            fluidscript 2
            circuit "loop":
              fluid = water
              PU1 pump
              X1 {kind}  depth = 3
              N1 - PU1 - X1 - N1
            """, "FS1503");

        Assert.EndsWith("It accepts: no parameters.", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACornerOtherThanSharpOrFilletIsFS1514()
    {
        var diagnostic = Only("""
            fluidscript 2
            project "p":
              style:
                corner = round
            circuit "loop":
              fluid = water
              PU1 pump
              N1 - PU1 - N1
            """, "FS1514");

        Assert.Equal("'corner' accepts sharp or fillet; 'round' is none of them.", diagnostic.Message);
    }
}
