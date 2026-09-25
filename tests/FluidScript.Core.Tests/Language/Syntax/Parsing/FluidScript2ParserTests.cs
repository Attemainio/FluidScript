using System.Text;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Printing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Language.Syntax.Parsing;

/// <summary>
/// Language 2's parser against <c>plan/10-language/19-fluidscript-2.md</c>: the reference script, one test
/// per statement shape, one per diagnostic the parser raises, and the invariants language 1's parser keeps —
/// every token in the tree, printed back byte for byte, and no input that throws.
/// </summary>
public sealed class FluidScript2ParserTests
{
    /// <summary><c>19</c>'s reference script, verbatim.</summary>
    public const string Reference = """
        fluidscript 2

        project "Substation 12":
          cases   = [winter, mild]
          catalog = steel_en10255@2026.1
          show    = temperature
          style:
            colour = "#2f6f9f"
            width  = 2

        let outdoor = [-26, 5] C

        curve district_supply: outdoor
          -26   85
           18   65

        curve heat_demand: outdoor
          -26   150
           18     0

        curve supply_temp: outdoor
          -26   60
           18   30

        curve weather_jan: time
          2026-01-15 06:00   -18
          2026-01-15 12:00    -9

        circuit "District primary":
          fluid  = water
          number = 100
          style:
            colour = crimson

          NPS  inlet      t = district_supply   p = 600 kPa
          NPR  outlet     p = 350 kPa
          PCV  valve
          HX1  exchanger:
            primary.out.t   = 45 C              # the network's required return
            secondary.in.t  = 40 C
            secondary.out.t = 60 C

          NPS - PCV
          PCV - HX1                  12 m  DN25
          HX1 - NPR

        circuit "Heating":
          fluid  = water
          number = 200

          SP   pump
          TV1  valve3      stroke = 90 s
          TE1  temperature_sensor
          RAD  radiator    power = heat_demand
          TC1  controller:
            type     = PI
            moves    = TV1
            reads    = TE1
            setpoint = supply_temp
            band     = 20 K
            ti       = 120 s

          HX1 - TV1                  30 m  DN32
          TV1 - SP - TE1 - RAD - NR
          NR - TV1
          NR - HX1                   30 m  DN32

        run "Cold morning":
          from     = winter
          start    = 2026-01-15 06:00
          duration = 2 h
          frame    = 10 s

          outdoor  = weather_jan
          at   10 min       RAD.power    = 100 kW
          over 30..40 min   NPS.t        = 85..75 C
          at   1 h          TC1.setpoint = 55 C
        """;

    private static ParseResult Parse(string text) => FluidScript2Parser.Parse(new SourceText(text));

    private static string Describe(ParseResult result) =>
        string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}"));

    private static ParseResult Clean(string text)
    {
        var result = Parse(text);
        Assert.True(result.Diagnostics.IsEmpty, Describe(result));
        return result;
    }

    private static Diagnostic Only(string text, string code)
    {
        var result = Parse(text);
        var matching = result.Diagnostics.Where(d => d.Code == code).ToArray();
        Assert.True(matching.Length == 1, $"Expected exactly one {code}; got: {Describe(result)}");
        return matching[0];
    }

    private static BlockSyntax Block(ParseResult result, int index) =>
        Assert.IsType<BlockSyntax>(result.Root.Statements[index]);

    // ---- the reference script ---------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void TheReferenceScriptParsesWithoutADiagnostic() => Clean(Reference);

    [Fact]
    [Trait("Category", "Unit")]
    public void TheReferenceScriptPrintsBackByteForByte()
    {
        var result = Parse(Reference);
        Assert.Equal(Reference, SyntaxPrinter.Print(result));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheReferenceScriptHasTheTopLevelItsBlocksSay()
    {
        var result = Clean(Reference);

        Assert.Equal(
            [
                nameof(VersionDirectiveSyntax), nameof(BlockSyntax), nameof(LetBindingSyntax),
                nameof(BlockSyntax), nameof(BlockSyntax), nameof(BlockSyntax), nameof(BlockSyntax),
                nameof(BlockSyntax), nameof(BlockSyntax), nameof(BlockSyntax),
            ],
            result.Root.Statements.Select(static statement => statement.GetType().Name));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACircuitBlockHoldsItsSettingsDeclarationsAndChains()
    {
        var circuit = Block(Clean(Reference), 7);

        var head = Assert.IsType<CircuitHeadSyntax>(circuit.Head);
        Assert.Equal("\"District primary\"", head.Title!.Text);
        Assert.NotNull(circuit.Colon);

        Assert.Equal(
            [
                nameof(SettingLineSyntax), nameof(SettingLineSyntax), nameof(BlockSyntax),
                nameof(ComponentDeclarationSyntax), nameof(ComponentDeclarationSyntax),
                nameof(ComponentDeclarationSyntax), nameof(BlockSyntax),
                nameof(ConnectionSyntax), nameof(PipedConnectionSyntax), nameof(ConnectionSyntax),
            ],
            circuit.Body.Select(static statement => statement.GetType().Name));

        var exchanger = Assert.IsType<BlockSyntax>(circuit.Body[6]);
        var declaration = Assert.IsType<ComponentDeclarationSyntax>(exchanger.Head);
        Assert.Equal("HX1", declaration.Name.Text);
        Assert.Equal(3, exchanger.Body.Length);
        Assert.All(exchanger.Body, static line => Assert.IsType<SettingLineSyntax>(line));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APipeIsRecognisedByItsForm()
    {
        var circuit = Block(Clean(Reference), 7);
        var pipe = Assert.IsType<PipedConnectionSyntax>(circuit.Body[8]);

        Assert.Single(pipe.Connection.Links);
        Assert.Collection(
            pipe.Properties,
            static length => Assert.Equal("12 m", Assert.IsType<QuantityLiteralSyntax>(length).Token.Text),
            static size => Assert.Equal("DN25", Assert.IsType<IdentifierSyntax>(size).Text));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheDriverIsAListWithOneUnit()
    {
        var let = Assert.IsType<LetBindingSyntax>(Clean(Reference).Root.Statements[2]);

        var list = Assert.IsType<UnitListSyntax>(let.Value);
        Assert.Equal("C", list.UnitText);
        Assert.Equal(2, list.List.Elements.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveNamesItsDriverAndHoldsItsRows()
    {
        var curve = Block(Clean(Reference), 6);

        var head = Assert.IsType<DriverCurveHeadSyntax>(curve.Head);
        Assert.Equal("weather_jan", head.Name.Text);
        Assert.Equal("time", head.Driver.Text);
        Assert.Null(curve.Colon);
        Assert.Equal(2, curve.Body.Length);
        Assert.All(curve.Body, static row => Assert.IsType<CurveRowSyntax>(row));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ARunHoldsItsSettingsAnOverrideAndItsEvents()
    {
        var run = Block(Clean(Reference), 9);

        Assert.Equal("\"Cold morning\"", Assert.IsType<RunHeadSyntax>(run.Head).Title!.Text);

        var start = Assert.IsType<SettingLineSyntax>(run.Body[1]);
        Assert.IsType<DateLiteralSyntax>(Assert.Single(start.Assignments).Value);

        var ramp = Assert.IsType<DisturbanceSyntax>(run.Body[6]);
        Assert.Equal("over", ramp.Keyword.Text);
        Assert.IsType<RangeSyntax>(ramp.When);
        Assert.IsType<RangeSyntax>(ramp.Value);
        Assert.Equal("NPS", ramp.Target.Component.Text);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheProjectHoldsItsCasesCatalogueAndStyle()
    {
        var project = Block(Clean(Reference), 1);

        var catalog = Assert.IsType<SettingLineSyntax>(project.Body[1]);
        var pin = Assert.IsType<CatalogReferenceSyntax>(Assert.Single(catalog.Assignments).Value);
        Assert.Equal("steel_en10255", pin.Catalog.Text);
        Assert.Equal("2026.1", pin.Version.Text);

        var style = Assert.IsType<BlockSyntax>(project.Body[3]);
        Assert.IsType<StyleHeadSyntax>(style.Head);
        Assert.Equal(2, style.Body.Length);
    }

    // ---- shapes -----------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void SeveralPairsMayShareALine()
    {
        var result = Clean("""
            circuit "c":
              S1 inlet
              HX1 exchanger:
                primary.out.t = 45 C   secondary.out.t = [60, 50] C
            """);

        var exchanger = Assert.IsType<BlockSyntax>(Block(result, 0).Body[1]);
        var line = Assert.IsType<SettingLineSyntax>(Assert.Single(exchanger.Body));

        Assert.Equal(2, line.Assignments.Length);
        Assert.IsType<UnitListSyntax>(line.Assignments[1].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AControllerOutputIsARange()
    {
        var result = Clean("""
            circuit "c":
              TC1 controller:
                output = 10..100 %
            """);

        var controller = Assert.IsType<BlockSyntax>(Assert.Single(Block(result, 0).Body));
        var line = Assert.IsType<SettingLineSyntax>(Assert.Single(controller.Body));
        Assert.IsType<RangeExpressionSyntax>(Assert.Single(line.Assignments).Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnExplicitPortChainIsAConnection()
    {
        var result = Clean("""
            circuit "c":
              HX1.secondary.out - TV1.a
            """);

        var chain = Assert.IsType<ConnectionSyntax>(Assert.Single(Block(result, 0).Body));
        Assert.Equal("secondary.out", SyntaxPrinter.Print(result.Source, chain.First.Port!).Trim());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CommentsAndBlankLinesNeverEndABlock()
    {
        var result = Clean("""
            circuit "c":
              P1 pump

            # a note at the margin
              P2 pump
            """);

        Assert.Equal(2, Block(result, 0).Body.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ATabIndentedFileIsConsistentOnItsOwnTerms() =>
        Clean("circuit \"c\":\n\tP1 pump\n\tP2 pump\n");

    [Fact]
    [Trait("Category", "Unit")]
    public void AtIsANameOutsideARun()
    {
        var result = Clean("""
            circuit "c":
              TE1 temperature_sensor at N2
            """);

        var sensor = Assert.IsType<ComponentDeclarationSyntax>(Assert.Single(Block(result, 0).Body));
        Assert.Equal("N2", sensor.AttachedTo!.Text);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AClockTimeIsAnEventTime()
    {
        var result = Clean("""
            run "r":
              at 06:30 RAD.power = 100 kW
            """);

        var step = Assert.IsType<DisturbanceSyntax>(Assert.Single(Block(result, 0).Body));
        Assert.IsType<DateLiteralSyntax>(Assert.IsType<PointSyntax>(step.When).Value);
    }

    // ---- diagnostics ------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ALineBetweenTwoLevelsIsFS1801()
    {
        // Between the circuit's head and its body: inside the circuit, and indented unlike its other lines.
        // A line deeper than a declaration is not this case; that is the declaration's missing colon.
        var diagnostic = Only("""
            circuit "c":
                P1 pump
              P2 pump
            """, "FS1801");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CurveRowsMayBeIndentedToAlignTheirColumns()
    {
        var result = Clean("curve c: outdoor\n  -26   85\n   18   65\n");
        Assert.Equal(2, Block(result, 0).Body.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MixingTabsAndSpacesInOneBodyIsFS1801() =>
        Only("circuit \"c\":\n  P1 pump\n\tP2 pump\n", "FS1801");

    [Theory]
    [InlineData("fluid = water", "A setting belongs inside the block it sets")]
    [InlineData("circuit \"c\":\n  run \"r\":\n    duration = 1 h", "A 'run' block belongs at the top level")]
    [InlineData("P1 pump", "A component declaration belongs inside a circuit block")]
    [InlineData("circuit \"c\":\n  at 10 min P1.speed = 50 %", "An event belongs inside a run block")]
    [InlineData("run \"r\":\n  style:\n    width = 2", "A 'style:' block belongs inside the project block or a circuit's")]
    [Trait("Category", "Unit")]
    public void AStatementOutsideItsBlockIsFS1802(string text, string message)
    {
        var diagnostic = Only(text, "FS1802");
        Assert.StartsWith(message, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AMisplacedStatementIsStillParsedAsWhatItIs()
    {
        var result = Parse("fluid = water");
        Assert.IsType<SettingLineSyntax>(Assert.Single(result.Root.Statements));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APipeOnAChainIsFS1803AndTheChainStillParses()
    {
        const string Text = """
            circuit "c":
              A - B - C   25 m  DN25
            """;

        var diagnostic = Only(Text, "FS1803");
        Assert.Contains("this line has 2 links", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'A - B 25 m  DN25'", diagnostic.Message, StringComparison.Ordinal);

        var pipe = Assert.IsType<PipedConnectionSyntax>(Assert.Single(Block(Parse(Text), 0).Body));
        Assert.Equal(2, pipe.Connection.Links.Length);
    }

    [Theory]
    [InlineData("circuit \"c\":\n  connections", "connections")]
    [InlineData("circuit \"c\":\n  control TV1 with TE1 by PID1", "control")]
    [InlineData("scenarios winter mild", "scenarios")]
    [InlineData("design winter", "design")]
    [InlineData("circuit \"c\":\n  schedule", "schedule")]
    [InlineData("project dynamic plant", "project")]
    [InlineData("circuit heating number=100", "circuit")]
    [InlineData("curve heat tout", "curve")]
    [InlineData("circuit \"c\":\n  fluid water", "fluid")]
    [InlineData("show temperature", "show")]
    [InlineData("project \"p\":\n  style gray 1px sharp", "style")]
    [InlineData("spacing 0.75", "spacing")]
    [InlineData("catalog steel_en10255", "catalog")]
    [InlineData("circuit \"c\":\n  inlet N1", "inlet")]
    [InlineData("circuit \"c\":\n  outlet N3", "outlet")]
    [Trait("Category", "Unit")]
    public void ALanguageOneStatementIsFS1806(string text, string word)
    {
        var diagnostic = Only(text, "FS1806");
        Assert.StartsWith($"'{word}' is language 1. In language 2, ", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ARampWithOneValueIsFS1807AndIsKept()
    {
        const string Text = """
            run "r":
              over 30..40 min NPS.t = 75 C
            """;

        var diagnostic = Only(Text, "FS1807");
        Assert.Contains("'NPS.t = 30..45'", diagnostic.Message, StringComparison.Ordinal);
        Assert.IsType<DisturbanceSyntax>(Assert.Single(Block(Parse(Text), 0).Body));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ARampWithOneTimeIsFS1807AboutItsTime()
    {
        var diagnostic = Only("run \"r\":\n  over 30 min NPS.t = 70..75 C", "FS1807");

        Assert.Equal("A ramp needs both ends of its time, such as 'over 30..40 min NPS.t = 70..75 C'. For a step, write 'at'.", diagnostic.Message);
    }

    [Theory]
    [InlineData("curve heating")]
    [InlineData("curve heating:")]
    [Trait("Category", "Unit")]
    public void ACurveWithoutItsDriverIsFS1116(string head)
    {
        var diagnostic = Only($"{head}\n  -26 85\n  18 65", "FS1116");

        Assert.Equal("heating", Assert.Single(diagnostic.Arguments).Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AHyphenatedNameInAChainIsFS1108AndNothingElse()
    {
        var result = Parse("circuit \"c\":\n  N1 - HX-1 - N2");

        Assert.Equal("FS1108", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnUnquotedColourSettingIsFS1203AndNothingElse()
    {
        var result = Parse("project \"p\":\n  style:\n    colour = #ff0000");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("FS1203", diagnostic.Code);
        Assert.Equal("#ff0000", diagnostic.Arguments.Single(static a => a.Name == "hex").Value);
    }

    [Theory]
    [InlineData("project \"p\"\n  cases = [a, b]", "project")]
    [InlineData("circuit \"c\"\n  P1 pump", "circuit")]
    [InlineData("run \"r\"\n  duration = 1 h", "run")]
    [Trait("Category", "Unit")]
    public void AHeadWithoutItsColonIsFS1812AndStillOpensItsBlock(string text, string head)
    {
        var diagnostic = Only(text, "FS1812");
        Assert.Equal($"A {head} line opens a block and ends with ':'.", diagnostic.Message);

        var result = Parse(text);
        Assert.Single(Assert.IsType<BlockSyntax>(Assert.Single(result.Root.Statements)).Body);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADeclarationWithIndentedLinesButNoColonIsFS1812Once()
    {
        const string Text = """
            circuit "c":
              HX1 exchanger
                primary.out.t  = 45 C
                secondary.in.t = 40 C
            """;

        var result = Parse(Text);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("FS1812", diagnostic.Code);

        var exchanger = Assert.IsType<BlockSyntax>(Assert.Single(Block(result, 0).Body));
        Assert.IsType<ComponentDeclarationSyntax>(exchanger.Head);
        Assert.Null(exchanger.Colon);
        Assert.Equal(2, exchanger.Body.Length);
        Assert.Equal(Text, SyntaxPrinter.Print(result));
    }

    [Theory]
    [InlineData("run pump")]
    [InlineData("circuit \"c\":\n  N1 - circuit")]
    [InlineData("let = 5")]
    [Trait("Category", "Unit")]
    public void AStatementWordAsANameIsFS1004(string text) => Only(text, "FS1004");

    [Fact]
    [Trait("Category", "Unit")]
    public void AnUnreadableLineCostsOnlyItself()
    {
        var result = Parse("""
            circuit "c":
              P1 pump
              ! ? !
              P2 pump
            """);

        var body = Block(result, 0).Body;
        Assert.Equal(3, body.Length);
        Assert.IsType<MalformedStatementSyntax>(body[1]);
        Assert.IsType<ComponentDeclarationSyntax>(body[2]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryLanguage2BlockParsesWithNoUnexpectedDiagnostic()
    {
        // Language 1's corpus check (`ParserTests`), for the blocks whose fence says `lang=2`.
        var blocks = ScriptCorpus.InLanguage(2);
        Assert.NotEmpty(blocks);

        var offenders = new List<string>();
        foreach (var script in blocks)
        {
            var produced = Parse(script.Text).Diagnostics.Select(static d => d.Code).ToHashSet(StringComparer.Ordinal);
            if (!produced.SetEquals(script.Expected))
            {
                offenders.Add(
                    $"{script.Name}: expected [{string.Join(", ", script.Expected)}], "
                    + $"got [{string.Join(", ", produced.Order(StringComparer.Ordinal))}]");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    // ---- invariants -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Property")]
    public void DeletingAnyOneCharacterOfTheReferenceStillYieldsALosslessTree()
    {
        for (var i = 0; i < Reference.Length; i++)
        {
            AssertLossless($"less character {i}", Reference.Remove(i, 1));
        }
    }

    [Fact]
    [Trait("Category", "Property")]
    public void RandomMutationsNeverThrowAndStayLossless()
    {
        string[] interesting = [":", " ", "\n", "\t", "  ", "-", "=", "[", "]", "..", "@", "\"", "#", "2026-01-", "06:", "run", "at "];
        var random = new Random(20260925);

        for (var iteration = 0; iteration < 3_000; iteration++)
        {
            var builder = new StringBuilder(Reference);
            for (var edit = random.Next(1, 6); edit > 0 && builder.Length > 0; edit--)
            {
                var at = random.Next(builder.Length);
                if (random.Next(2) == 0)
                {
                    builder.Remove(at, 1);
                }
                else
                {
                    builder.Insert(at, interesting[random.Next(interesting.Length)]);
                }
            }

            AssertLossless($"mutation {iteration}", builder.ToString());
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnEmptyFileIsAnEmptyTree()
    {
        var result = Clean(string.Empty);
        Assert.Empty(result.Root.Statements);
    }

    private static void AssertLossless(string name, string text)
    {
        var result = Parse(text);
        Assert.True(text == SyntaxPrinter.Print(result), $"{name}: the tree does not print back.");

        var tokens = result.Root.Tokens;
        for (var i = 1; i < tokens.Length; i++)
        {
            Assert.True(tokens[i - 1].Span.End <= tokens[i].Span.Start, $"{name}: tokens out of order at {tokens[i]}.");
        }
    }
}
