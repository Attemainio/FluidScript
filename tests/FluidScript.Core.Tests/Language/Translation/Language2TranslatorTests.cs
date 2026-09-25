using System.Globalization;
using System.Text;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
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

        foreach (var connection in model.Connections)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{connection.From.Component}.{connection.From.Port} - {connection.To.Component}.{connection.To.Port}");
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
              N1 - PU1 - TV1 - N1
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
