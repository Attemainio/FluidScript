using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Printing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Tests.Language.Syntax.Parsing;

/// <summary>The three forms of <c>style</c> (<c>D-104</c>): definition, application, and the anonymous token list.</summary>
[Trait("Category", "Unit")]
public sealed class StyleDirectiveTests
{
    private const string Script = """
        fluidscript 1
        style hot = "#c0392b" 2px -
        style cold = "#2f6f9f" 2px - fill="#e8f1f8"
        style trace = 1px --

        circuit heating 100
        style hot
        HS1  heat_exchanger power=54 kW out.t=60

        circuit ahu 101
        style cold
        TV  three_way_valve style=trace
        PU  pump
        style gray 1px
        HE  load in.t=50 out.t=30 power=24 kW

        connections
        HS1 - TV.a
        TV.ab - PU - HE - HS1
        """;

    [Fact]
    public void ADefinitionKeepsItsNameAndTheAssignment()
    {
        var parse = FluidScriptParser.Parse(new SourceText(Script));
        var definitions = parse.Root.Statements.OfType<StyleDirectiveSyntax>().Where(static s => s.IsDefinition).ToList();

        Assert.Empty(parse.Diagnostics);
        Assert.Equal(["hot", "cold", "trace"], definitions.Select(static d => d.Name!.Text));
        Assert.Equal(["style", "cold", "=", "\"#2f6f9f\"", "2px", "-", "fill", "=", "\"#e8f1f8\""], definitions[1].Tokens.Select(static t => t.Text));
        Assert.Equal(StyleTokenKind.Keyed, definitions[1].Parts[^1].Kind);
        Assert.Equal("fill=\"#e8f1f8\"", definitions[1].Parts[^1].Text);
    }

    [Fact]
    public void TheScriptPrintsBackByteForByte()
    {
        var parse = FluidScriptParser.Parse(new SourceText(Script));
        Assert.Equal(Script, SyntaxPrinter.Print(parse));
    }

    [Fact]
    public void TheBinderResolvesDefinitionsApplicationsAndOverrides()
    {
        var parse = FluidScriptParser.Parse(new SourceText(Script));
        var bind = new FluidScript.Core.Language.Binding.Binder(ComponentRegistry.Default).Bind(parse, "styles");
        var components = bind.Model.Components.ToDictionary(static c => c.Name);

        Assert.DoesNotContain(bind.Diagnostics, static d => d.Code.StartsWith("FS12", StringComparison.Ordinal));
        Assert.Equal(3, bind.Model.Style.Definitions.Count);
        Assert.Equal("#c0392b", components["HS1"].Style!.Stroke);
        Assert.Equal(2, components["HS1"].Style!.StrokeWidth);
        Assert.Equal("#2f6f9f", components["PU"].Style!.Stroke);
        Assert.Equal("#e8f1f8", components["PU"].Style!.Fill);
        Assert.Equal("dashed", components["TV"].Style!.Pattern);
        Assert.Equal(1, components["TV"].Style!.StrokeWidth);
        Assert.Equal("#2f6f9f", components["TV"].Style!.Stroke);
        Assert.Equal("#808080", components["HE"].Style!.Stroke);
        Assert.Equal("#e8f1f8", components["HE"].Style!.Fill);
        Assert.True(bind.Model.Style.Default.IsEmpty);
    }

    [Theory]
    [InlineData("style warm", "FS1204")]
    [InlineData("style hot = red\nstyle hot = blue", "FS1205")]
    [InlineData("style purplish 2px", "FS1201")]
    [InlineData("style red blue", "FS1202")]
    [InlineData("P1 pipe length=1 dn=25 style=nope", "FS1204")]
    public void TheStyleDiagnosticsFire(string line, string code)
    {
        var parse = FluidScriptParser.Parse(new SourceText("fluidscript 1\n" + line + "\n"));
        var bind = new FluidScript.Core.Language.Binding.Binder(ComponentRegistry.Default).Bind(parse, "styles");

        Assert.Contains(bind.Diagnostics, d => d.Code == code);
    }
}
